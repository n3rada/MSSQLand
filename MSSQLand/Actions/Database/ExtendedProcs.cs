// MSSQLand/Actions/Database/ExtendedProcs.cs

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
        // Descriptions for common extended procedures (xp_*)
        [ExcludeFromArguments]
        private static readonly Dictionary<string, string> XpDescriptions = new()
        {
            // Command Execution & System
            { "xp_cmdshell", "Execute OS commands (requires xp_cmdshell enabled)" },
            { "xp_servicecontrol", "Start/stop Windows services" },
            { "xp_terminate_process", "Terminate a Windows process by PID" },
            
            // File System Operations
            { "xp_dirtree", "List directory tree structure (depth, files)" },
            { "xp_subdirs", "List subdirectories only" },
            { "xp_fileexist", "Check if a file/directory exists" },
            { "xp_fixeddrives", "List drives with free space (MB)" },
            { "xp_availablemedia", "List available backup media devices" },
            { "xp_get_tape_devices", "List tape backup devices" },
            { "xp_create_subdir", "Create a subdirectory" },
            { "xp_delete_file", "Delete backup/log files" },
            { "xp_copy_file", "Copy a file" },
            { "xp_getfiledetails", "Get file size, dates, attributes" },
            
            // Registry Operations
            { "xp_regread", "Read registry value" },
            { "xp_regwrite", "Write registry value" },
            { "xp_regdeletekey", "Delete registry key" },
            { "xp_regdeletevalue", "Delete registry value" },
            { "xp_regenumkeys", "Enumerate registry subkeys" },
            { "xp_regenumvalues", "Enumerate registry values" },
            { "xp_regaddmultistring", "Add to REG_MULTI_SZ value" },
            { "xp_regremovemultistring", "Remove from REG_MULTI_SZ value" },
            
            // Instance-specific Registry
            { "xp_instance_regread", "Read instance registry value" },
            { "xp_instance_regwrite", "Write instance registry value" },
            { "xp_instance_regdeletekey", "Delete instance registry key" },
            { "xp_instance_regdeletevalue", "Delete instance registry value" },
            { "xp_instance_regenumkeys", "Enumerate instance registry subkeys" },
            { "xp_instance_regenumvalues", "Enumerate instance registry values" },
            { "xp_instance_regaddmultistring", "Add to instance REG_MULTI_SZ" },
            { "xp_instance_regremovemultistring", "Remove from instance REG_MULTI_SZ" },
            
            // Network & SMB
            { "xp_getnetname", "Get server network name" },
            { "xp_ntsec_enumdomains", "Enumerate trusted domains" },
            { "xp_enumdsn", "Enumerate ODBC data sources" },
            { "xp_enumgroups", "Enumerate Windows local groups" },
            { "xp_logininfo", "Get Windows login info from AD" },
            { "xp_grantlogin", "Grant Windows login access" },
            { "xp_revokelogin", "Revoke Windows login access" },
            
            // SQL Server Information
            { "xp_msver", "Get SQL Server version info" },
            { "xp_loginconfig", "Get login/auth configuration" },
            { "xp_readerrorlog", "Read SQL Server error log" },
            { "xp_enumerrorlogs", "List available error logs" },
            { "xp_logevent", "Write to Windows Event Log" },
            
            // SQL Agent
            { "xp_sqlagent_enum_jobs", "List SQL Agent jobs" },
            { "xp_sqlagent_is_starting", "Check if SQL Agent starting" },
            { "xp_sqlagent_monitor", "Monitor SQL Agent status" },
            { "xp_sqlagent_notify", "Send SQL Agent notification" },
            { "xp_sqlagent_param", "Get SQL Agent parameters" },
            { "xp_sqlmaint", "Run maintenance operations" },
            
            // Database Mail
            { "xp_sysmail_activate", "Activate Database Mail" },
            { "xp_sysmail_attachment_load", "Load email attachment" },
            { "xp_sysmail_format_query", "Format query for email" },
            { "xp_sendmail", "Send email (legacy, use sp_send_dbmail)" },
            { "xp_smtp_sendmail", "Send email via SMTP" },
            
            // OLE DB Providers
            { "xp_enum_oledb_providers", "List installed OLE DB providers" },
            { "xp_prop_oledb_provider", "Get OLE DB provider properties" },
            
            // String/Utility
            { "xp_sprintf", "Format string (C-style sprintf)" },
            { "xp_sscanf", "Parse string (C-style sscanf)" },
            { "xp_qv", "Internal query processor" },
            
            // Replication
            { "xp_replposteor", "Replication end-of-record" },
            { "xp_repl_convert_lsn", "Convert replication LSN" },
            
            // Misc
            { "xp_msx_enlist", "Enlist in multiserver admin" },
            { "xp_passAgentInfo", "Pass info to SQL Agent" },
        };

        // Descriptions for OLE Automation procedures (sp_OA*)
        [ExcludeFromArguments]
        private static readonly Dictionary<string, string> OleDescriptions = new()
        {
            { "sp_OACreate", "Create COM/OLE object instance" },
            { "sp_OAMethod", "Call method on COM object" },
            { "sp_OAGetProperty", "Get property from COM object" },
            { "sp_OASetProperty", "Set property on COM object" },
            { "sp_OAGetErrorInfo", "Get last OLE error info" },
            { "sp_OADestroy", "Destroy COM object instance" },
            { "sp_OAStop", "Stop OLE Automation environment" },
        };

        // Descriptions for other useful system procedures
        [ExcludeFromArguments]
        private static readonly Dictionary<string, string> SystemProcDescriptions = new()
        {
            // External Scripts (R/Python/Java)
            { "sp_execute_external_script", "Execute R/Python/Java scripts (requires external scripts enabled)" },
            
            // Bulk Operations
            { "sp_addextendedproc", "Register an extended stored procedure DLL" },
            { "sp_dropextendedproc", "Unregister an extended stored procedure" },
            
            // Linked Servers
            { "sp_addlinkedserver", "Create a linked server" },
            { "sp_addlinkedsrvlogin", "Map login to linked server" },
            { "sp_dropserver", "Drop a linked server" },
            { "sp_linkedservers", "List linked servers" },
            { "sp_testlinkedserver", "Test linked server connectivity" },
            { "sp_catalogs", "List catalogs on linked server" },
            { "sp_tables_ex", "List tables on linked server" },
            { "sp_columns_ex", "List columns on linked server table" },
            { "sp_primarykeys", "List primary keys on linked server" },
            { "sp_foreignkeys", "List foreign keys on linked server" },
            
            // SQL Agent Jobs
            { "sp_add_job", "Create SQL Agent job" },
            { "sp_add_jobstep", "Add step to SQL Agent job" },
            { "sp_add_jobschedule", "Add schedule to SQL Agent job" },
            { "sp_start_job", "Start SQL Agent job" },
            { "sp_stop_job", "Stop SQL Agent job" },
            { "sp_delete_job", "Delete SQL Agent job" },
            { "sp_help_job", "Get SQL Agent job info" },
            
            // Credentials & Proxies
            { "sp_add_credential", "Create a credential" },
            { "sp_add_proxy", "Create SQL Agent proxy" },
            { "sp_grant_proxy_to_subsystem", "Grant proxy to subsystem" },
        };

        public override object Execute(DatabaseContext databaseContext)
        {
            // First check if user is sysadmin (can execute everything)
            bool isSysadmin = databaseContext.UserService.IsAdmin();

            // ═══════════════════════════════════════════════════════════════════════════════
            // SECTION 1: Extended Stored Procedures (xp_*)
            // ═══════════════════════════════════════════════════════════════════════════════
            Logger.Task("Enumerating extended stored procedures (xp_*)");

            string xpQuery = $@"
                SELECT 
                    o.name AS [Procedure],
                    CASE 
                        WHEN {(isSysadmin ? "1" : "0")} = 1 THEN 'Yes (sysadmin)'
                        WHEN HAS_PERMS_BY_NAME('master.dbo.' + o.name, 'OBJECT', 'EXECUTE') = 1 THEN 'Yes'
                        ELSE 'No'
                    END AS [Execute],
                    o.create_date AS [Created],
                    o.modify_date AS [Modified]
                FROM master.sys.all_objects o
                WHERE o.type = 'X' 
                    AND o.name LIKE 'xp[_]%'
                ORDER BY o.name;";

            try
            {
                DataTable xpTable = databaseContext.QueryService.ExecuteTable(xpQuery);

                if (xpTable != null && xpTable.Rows.Count > 0)
                {
                    // Add Description column
                    xpTable.Columns.Add("Description", typeof(string));

                    foreach (DataRow row in xpTable.Rows)
                    {
                        string procName = row["Procedure"].ToString();
                        row["Description"] = XpDescriptions.ContainsKey(procName)
                            ? XpDescriptions[procName]
                            : "";
                    }

                    // Reorder columns
                    xpTable.Columns["Description"].SetOrdinal(2);

                    // Sort: executable first, then by name
                    DataTable sortedXp = xpTable.AsEnumerable()
                        .OrderByDescending(row => row.Field<string>("Execute").StartsWith("Yes", StringComparison.OrdinalIgnoreCase))
                        .ThenBy(row => row.Field<string>("Procedure"))
                        .CopyToDataTable();

                    int executableCount = sortedXp.AsEnumerable().Count(r => r.Field<string>("Execute").StartsWith("Yes", StringComparison.OrdinalIgnoreCase));
                    
                    Console.WriteLine(OutputFormatter.ConvertDataTable(sortedXp));
                    Logger.Success($"Found {sortedXp.Rows.Count} extended procedures ({executableCount} executable)");
                }
                else
                {
                    Logger.Warning("No extended stored procedures found or access denied.");
                }

                // ═══════════════════════════════════════════════════════════════════════════════
                // SECTION 2: OLE Automation Procedures (sp_OA*)
                // ═══════════════════════════════════════════════════════════════════════════════
                Logger.NewLine();
                Logger.Task("Enumerating OLE Automation procedures (sp_OA*)");

                // Check if Ole Automation is enabled
                int oleStatus = databaseContext.ConfigService.GetConfigurationStatus("Ole Automation Procedures");
                if (oleStatus == 1)
                {
                    Logger.SuccessNested("Ole Automation Procedures: Enabled");
                }
                else
                {
                    Logger.WarningNested("Ole Automation Procedures: Disabled (sp_OA* won't work)");
                }

                string oleQuery = $@"
                    SELECT 
                        o.name AS [Procedure],
                        CASE 
                            WHEN {(isSysadmin ? "1" : "0")} = 1 THEN 'Yes (sysadmin)'
                            WHEN HAS_PERMS_BY_NAME('master.dbo.' + o.name, 'OBJECT', 'EXECUTE') = 1 THEN 'Yes'
                            ELSE 'No'
                        END AS [Execute],
                        o.create_date AS [Created],
                        o.modify_date AS [Modified]
                    FROM master.sys.all_objects o
                    WHERE o.type = 'X' 
                        AND o.name LIKE 'sp[_]OA%'
                    ORDER BY o.name;";

                DataTable oleTable = databaseContext.QueryService.ExecuteTable(oleQuery);

                if (oleTable != null && oleTable.Rows.Count > 0)
                {
                    oleTable.Columns.Add("Description", typeof(string));

                    foreach (DataRow row in oleTable.Rows)
                    {
                        string procName = row["Procedure"].ToString();
                        row["Description"] = OleDescriptions.ContainsKey(procName)
                            ? OleDescriptions[procName]
                            : "";
                    }

                    oleTable.Columns["Description"].SetOrdinal(2);

                    DataTable sortedOle = oleTable.AsEnumerable()
                        .OrderByDescending(row => row.Field<string>("Execute").StartsWith("Yes", StringComparison.OrdinalIgnoreCase))
                        .ThenBy(row => row.Field<string>("Procedure"))
                        .CopyToDataTable();
                    
                    Console.WriteLine(OutputFormatter.ConvertDataTable(sortedOle));
                    Logger.Success($"Found {sortedOle.Rows.Count} OLE Automation procedures");

                }

                // ═══════════════════════════════════════════════════════════════════════════════
                // SECTION 3: Other Useful System Procedures
                // ═══════════════════════════════════════════════════════════════════════════════
                Logger.NewLine();
                Logger.Task("Checking other useful system procedures");

                // Check key configuration options
                int externalScripts = databaseContext.ConfigService.GetConfigurationStatus("external scripts enabled");
                int clrEnabled = databaseContext.ConfigService.GetConfigurationStatus("clr enabled");
                int adHocQueries = databaseContext.ConfigService.GetConfigurationStatus("Ad Hoc Distributed Queries");

                Logger.InfoNested($"External Scripts (R/Python): {(externalScripts == 1 ? "Enabled" : "Disabled")}");
                Logger.InfoNested($"CLR Integration: {(clrEnabled == 1 ? "Enabled" : "Disabled")}");
                Logger.InfoNested($"Ad Hoc Distributed Queries (OPENROWSET/OPENDATASOURCE): {(adHocQueries == 1 ? "Enabled" : "Disabled")}");

                // Check for key procedures
                string systemQuery = $@"
                    SELECT 
                        SCHEMA_NAME(o.schema_id) + '.' + o.name AS [Procedure],
                        CASE 
                            WHEN {(isSysadmin ? "1" : "0")} = 1 THEN 'Yes (sysadmin)'
                            WHEN HAS_PERMS_BY_NAME(QUOTENAME(DB_NAME()) + '.' + QUOTENAME(SCHEMA_NAME(o.schema_id)) + '.' + QUOTENAME(o.name), 'OBJECT', 'EXECUTE') = 1 THEN 'Yes'
                            ELSE 'No'
                        END AS [Execute],
                        o.type_desc AS [Type]
                    FROM sys.all_objects o
                    WHERE o.name IN (
                        'sp_execute_external_script',
                        'sp_addextendedproc', 'sp_dropextendedproc',
                        'sp_addlinkedserver', 'sp_addlinkedsrvlogin', 'sp_dropserver', 
                        'sp_linkedservers', 'sp_testlinkedserver',
                        'sp_add_job', 'sp_add_jobstep', 'sp_start_job', 'sp_delete_job',
                        'sp_add_credential', 'sp_add_proxy'
                    )
                    ORDER BY o.name;";

                DataTable systemTable = databaseContext.QueryService.ExecuteTable(systemQuery);

                if (systemTable != null && systemTable.Rows.Count > 0)
                {
                    systemTable.Columns.Add("Description", typeof(string));

                    foreach (DataRow row in systemTable.Rows)
                    {
                        string procName = row["Procedure"].ToString();
                        // Extract just the procedure name without schema
                        string simpleName = procName.Contains(".") ? procName.Substring(procName.LastIndexOf('.') + 1) : procName;
                        row["Description"] = SystemProcDescriptions.ContainsKey(simpleName)
                            ? SystemProcDescriptions[simpleName]
                            : "";
                    }

                    systemTable.Columns["Description"].SetOrdinal(2);

                    DataTable sortedSystem = systemTable.AsEnumerable()
                        .OrderByDescending(row => row.Field<string>("Execute").StartsWith("Yes", StringComparison.OrdinalIgnoreCase))
                        .ThenBy(row => row.Field<string>("Procedure"))
                        .CopyToDataTable();

                    Console.WriteLine(OutputFormatter.ConvertDataTable(sortedSystem));
                    Logger.Success($"Found {sortedSystem.Rows.Count} system procedures");

                }

                return xpTable;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to enumerate procedures: {ex.Message}");
                return null;
            }
        }
    }
}
