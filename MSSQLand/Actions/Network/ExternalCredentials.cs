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

            DataTable resultTable = GetDatabaseScopedCredentials(databaseContext);

            if (resultTable.Rows.Count == 0)
            {
                Logger.Warning("No database-scoped credentials found in the current database.");
                Logger.WarningNested("Database-scoped credentials are used by External Data Sources for authentication.");
            }
            else
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));
            }

            return resultTable;
        }


        /// <summary>
        /// Retrieves database-scoped credentials from sys.database_scoped_credentials.
        /// </summary>
        private static DataTable GetDatabaseScopedCredentials(DatabaseContext databaseContext)
        {
            // Select all columns - different SQL Server versions may have different column sets
            string credQuery = "SELECT * FROM sys.database_scoped_credentials ORDER BY create_date DESC;";
            DataTable rawCreds = databaseContext.QueryService.ExecuteTable(credQuery);

            if (rawCreds == null || rawCreds.Rows.Count == 0)
            {
                return rawCreds;
            }

            // Get external data sources to check credential usage
            string edsQuery = "SELECT credential_id FROM sys.external_data_sources WHERE credential_id IS NOT NULL;";
            DataTable usedCreds = null;
            System.Collections.Generic.HashSet<int> usedCredIds = new System.Collections.Generic.HashSet<int>();

            try
            {
                usedCreds = databaseContext.QueryService.ExecuteTable(edsQuery);
                if (usedCreds != null)
                {
                    foreach (DataRow row in usedCreds.Rows)
                    {
                        if (row["credential_id"] != DBNull.Value)
                        {
                            usedCredIds.Add(Convert.ToInt32(row["credential_id"]));
                        }
                    }
                }
            }
            catch
            {
                // sys.external_data_sources may not exist on older versions
            }

            // Create formatted output table
            DataTable result = new DataTable();
            result.Columns.Add("ID", typeof(int));
            result.Columns.Add("Credential Name", typeof(string));
            result.Columns.Add("Identity", typeof(string));
            result.Columns.Add("Created", typeof(DateTime));
            result.Columns.Add("Modified", typeof(DateTime));
            result.Columns.Add("In Use", typeof(string));

            foreach (DataRow row in rawCreds.Rows)
            {
                DataRow newRow = result.NewRow();

                // Essential columns (always present)
                newRow["ID"] = Convert.ToInt32(row["credential_id"]);
                newRow["Credential Name"] = row["name"]?.ToString() ?? "";
                newRow["Identity"] = row["credential_identity"]?.ToString() ?? "";
                newRow["Created"] = row["create_date"];
                newRow["Modified"] = row["modify_date"];

                // Check if credential is in use by external data sources
                int credId = Convert.ToInt32(row["credential_id"]);
                newRow["In Use"] = usedCredIds.Contains(credId) ? "Yes" : "No";

                result.Rows.Add(newRow);
            }

            return result;
        }
    }
}
