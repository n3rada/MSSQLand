using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;


namespace MSSQLand.Actions.Network
{
    /// <summary>
    /// Retrieves database-scoped credentials used by External Data Sources.
    /// 
    /// Database-scoped credentials store authentication information for:
    /// - External Data Sources (Elastic Query, PolyBase)
    /// - Azure Blob Storage access
    /// - Cross-database authentication in Azure SQL Database
    /// 
    /// Attack Surface:
    /// - Credential names may reveal system architecture
    /// - Identity field shows the username used for authentication
    /// - With sufficient privileges, credentials can be used to create malicious external data sources
    /// - May indicate high-value targets (production databases, storage accounts)
    /// </summary>
    internal class ExternalCredentials : BaseAction
    {


        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }


        /// <summary>
        /// Executes the external credentials action to retrieve database-scoped credentials.
        /// </summary>
        /// <param name="databaseContext">The DatabaseContext for executing the query.</param>
        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Retrieving Database-Scoped Credentials");
            
            // Check if sys.database_scoped_credentials exists
            string checkQuery = @"
                SELECT COUNT(*) 
                FROM sys.all_objects 
                WHERE object_id = OBJECT_ID('sys.database_scoped_credentials') 
                AND type = 'V'";
            
            int viewExists = databaseContext.QueryService.ExecuteScalar<int>(checkQuery);
            
            if (viewExists == 0)
            {
                Logger.Warning("Database-scoped credentials are not available on this SQL Server instance.");
                Logger.InfoNested("This feature requires SQL Server 2016+ or Azure SQL Database.");
                Logger.NewLine();
                return null;
            }

            DataTable resultTable = GetDatabaseScopedCredentials(databaseContext);

            if (resultTable.Rows.Count == 0)
            {
                Logger.Warning("No database-scoped credentials found in the current database.");
                Logger.InfoNested("Database-scoped credentials are used by External Data Sources for authentication.");
            }
            else
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));
                Logger.NewLine();
                Logger.Info("Security Note:");
                Logger.InfoNested("Credential secrets are not visible, but names and identities reveal authentication targets.");
                Logger.InfoNested("With CREATE EXTERNAL DATA SOURCE permission, these can be leveraged for attacks.");
            }

            return resultTable;
        }


        /// <summary>
        /// Retrieves database-scoped credentials from sys.database_scoped_credentials.
        /// </summary>
        private static DataTable GetDatabaseScopedCredentials(DatabaseContext databaseContext)
        {
            string query = @"
                SELECT
                    dsc.credential_id AS [ID],
                    dsc.name AS [Credential Name],
                    dsc.credential_identity AS [Identity],
                    dsc.create_date AS [Created],
                    dsc.modify_date AS [Modified],
                    CASE 
                        WHEN EXISTS (
                            SELECT 1 
                            FROM sys.external_data_sources eds 
                            WHERE eds.credential_name = dsc.name
                        ) THEN 'Yes'
                        ELSE 'No'
                    END AS [In Use]
                FROM sys.database_scoped_credentials dsc
                ORDER BY dsc.create_date DESC;";

            return databaseContext.QueryService.ExecuteTable(query);
        }
    }
}
