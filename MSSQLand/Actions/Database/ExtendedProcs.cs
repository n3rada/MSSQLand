using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// Enumerates extended stored procedures available on the SQL Server instance.
    /// </summary>
    internal class ExtendedProcs : BaseAction
    {
        // Descriptions for common extended procedures
        [ExcludeFromArguments]
        private static readonly Dictionary<string, string> ProcedureDescriptions = new()
        {
            { "xp_cmdshell", "Command execution" },
            { "xp_dirtree", "Displays directory tree structure" },
            { "xp_fileexist", "Checks if a file exists" },
            { "xp_fixeddrives", "Lists fixed drives and their free space" },
            { "xp_regread", "Reads registry values" },
            { "xp_regwrite", "Writes registry values" },
            { "xp_regdeletekey", "Deletes registry keys" },
            { "xp_regdeletevalue", "Deletes registry values" },
            { "xp_regenumkeys", "Enumerates registry keys" },
            { "xp_regenumvalues", "Enumerates registry values" },
            { "xp_regaddmultistring", "Adds multistring registry values" },
            { "xp_regremovemultistring", "Removes multistring registry values" },
            { "xp_instance_regread", "Reads instance-specific registry values" },
            { "xp_instance_regwrite", "Writes instance-specific registry values" },
            { "xp_instance_regdeletekey", "Deletes instance-specific registry keys" },
            { "xp_instance_regdeletevalue", "Deletes instance-specific registry values" },
            { "xp_instance_regenumkeys", "Enumerates instance-specific registry keys" },
            { "xp_instance_regenumvalues", "Enumerates instance-specific registry values" },
            { "xp_instance_regaddmultistring", "Adds instance-specific multistring registry values" },
            { "xp_instance_regremovemultistring", "Removes instance-specific multistring registry values" },
            { "xp_servicecontrol", "Starts/stops SQL Server services" },
            { "xp_subdirs", "Lists subdirectories" },
            { "xp_create_subdir", "Creates a subdirectory" },
            { "xp_delete_file", "Deletes a file" },
            { "xp_delete_files", "Deletes multiple files" },
            { "xp_copy_file", "Copies a file" },
            { "xp_copy_files", "Copies multiple files" },
            { "xp_getnetname", "Returns the network name of the server" },
            { "xp_msver", "Returns SQL Server version information" },
            { "xp_loginconfig", "Returns login configuration information" },
            { "xp_logevent", "Logs events to Windows Event Log" },
            { "xp_sprintf", "Formats strings (similar to C sprintf)" },
            { "xp_sscanf", "Parses strings (similar to C sscanf)" },
            { "xp_enum_oledb_providers", "Lists OLE DB providers" },
            { "xp_prop_oledb_provider", "Returns OLE DB provider properties" },
            { "xp_readerrorlog", "Reads SQL Server error log" },
            { "xp_enumerrorlogs", "Lists SQL Server error logs" },
            { "xp_enumgroups", "Lists Windows groups" },
            { "xp_availablemedia", "Lists available backup media" },
            { "xp_get_tape_devices", "Lists tape devices" },
            { "xp_sqlagent_enum_jobs", "Lists SQL Agent jobs" },
            { "xp_sqlagent_is_starting", "Checks if SQL Agent is starting" },
            { "xp_sqlagent_monitor", "Monitors SQL Agent" },
            { "xp_sqlagent_notify", "Sends notifications via SQL Agent" },
            { "xp_sqlagent_param", "Gets SQL Agent parameters" },
            { "xp_sqlmaint", "Maintenance utility" },
            { "xp_sysmail_activate", "Activates Database Mail" },
            { "xp_sysmail_attachment_load", "Loads email attachments" },
            { "xp_sysmail_format_query", "Formats query results for email" },
            { "xp_replposteor", "Replication-related procedure" },
            { "xp_passAgentInfo", "Passes information to SQL Agent" },
            { "xp_msx_enlist", "Enlists server in multiserver environment" },
            { "xp_qv", "Internal query processor procedure" }
        };

        public override void ValidateArguments(string[] args)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Enumerating extended stored procedures");

            // First check if user is sysadmin (can execute everything)
            bool isSysadmin = databaseContext.UserService.IsAdmin();

            // Single query to get all extended procedures with their permissions
            string query = $@"
                SELECT 
                    o.name AS [Procedure Name],
                    CASE 
                        WHEN {(isSysadmin ? "1" : "0")} = 1 THEN 'Yes (sysadmin)'
                        WHEN HAS_PERMS_BY_NAME('master.dbo.' + o.name, 'OBJECT', 'EXECUTE') = 1 THEN 'Yes'
                        ELSE 'No'
                    END AS [Execute],
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

                // Add Description column
                resultTable.Columns.Add("Description", typeof(string));

                // Populate descriptions
                foreach (DataRow row in resultTable.Rows)
                {
                    string procName = row["Procedure Name"].ToString();
                    row["Description"] = ProcedureDescriptions.ContainsKey(procName)
                        ? ProcedureDescriptions[procName]
                        : "";
                }

                // Reorder columns: Procedure Name, Execute, Description, Created Date, Modified Date
                resultTable.Columns["Description"].SetOrdinal(2);

                // Sort in memory: Execute DESC (Yes before No), then Procedure Name ASC
                DataTable sortedTable = resultTable.AsEnumerable()
                    .OrderByDescending(row => row.Field<string>("Execute"))
                    .ThenBy(row => row.Field<string>("Procedure Name"))
                    .CopyToDataTable();

                Logger.Success($"Found {sortedTable.Rows.Count} extended stored procedures.");
                Console.WriteLine(OutputFormatter.ConvertDataTable(sortedTable));

                return sortedTable;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to enumerate extended stored procedures: {ex.Message}");
                return null;
            }
        }
    }
}
