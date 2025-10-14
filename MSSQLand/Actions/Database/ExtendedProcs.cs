using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;

namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// Enumerates extended stored procedures available on the SQL Server instance.
    /// </summary>
    internal class ExtendedProcs : BaseAction
    {
        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.Info("Enumerating extended stored procedures...");

            // First check if user is sysadmin (can execute everything)
            bool isSysadmin = databaseContext.UserService.IsAdmin();

            // Query to get extended stored procedures with additional information
            string query = @"
                SELECT 
                    o.name AS [Procedure Name],
                    o.create_date AS [Created Date],
                    o.modify_date AS [Modified Date]
                FROM master.sys.all_objects o
                WHERE o.type = 'X' 
                    AND o.name LIKE 'xp_%'
                ORDER BY o.name;";

            try
            {
                DataTable resultTable = databaseContext.QueryService.ExecuteTable(query);

                if (resultTable == null || resultTable.Rows.Count == 0)
                {
                    Logger.Warning("No extended stored procedures found or access denied.");
                    return null;
                }

                // Add Execute Permission column
                resultTable.Columns.Add("Execute Permission", typeof(string));

                // If sysadmin, all procedures are executable
                if (isSysadmin)
                {
                    foreach (DataRow row in resultTable.Rows)
                    {
                        row["Execute Permission"] = "Yes (sysadmin)";
                    }
                }
                else
                {
                    // Check each procedure individually by attempting to get metadata
                    foreach (DataRow row in resultTable.Rows)
                    {
                        string procName = row["Procedure Name"].ToString();
                        bool canExecute = CheckExecutePermission(databaseContext, procName);
                        row["Execute Permission"] = canExecute ? "Yes" : "No";
                    }
                }

                // Reorder columns
                resultTable.Columns["Execute Permission"].SetOrdinal(1);

                Logger.Success($"Found {resultTable.Rows.Count} extended stored procedures.");
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));

                return resultTable;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to enumerate extended stored procedures: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Checks if the current user can execute a specific extended stored procedure.
        /// Uses HAS_PERMS_BY_NAME to check actual executable permissions considering all role memberships.
        /// </summary>
        private bool CheckExecutePermission(DatabaseContext databaseContext, string procedureName)
        {
            try
            {
                string checkQuery = $@"
                    SELECT HAS_PERMS_BY_NAME(
                        'master.dbo.{procedureName}',
                        'OBJECT',
                        'EXECUTE'
                    ) AS CanExecute;";

                object result = databaseContext.QueryService.ExecuteScalar(checkQuery);
                return result != null && Convert.ToInt32(result) == 1;
            }
            catch
            {
                // If we can't check, assume we can't execute
                return false;
            }
        }
    }
}
