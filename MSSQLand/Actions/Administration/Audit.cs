// MSSQLand/Actions/Administration/Audit.cs

using System;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Administration
{
    /// <summary>
    /// Enumerate SQL Server audit configuration from sys.server_audits,
    /// sys.server_audit_specifications, and sys.server_audit_specification_details.
    ///
    /// Useful before any noisy action:
    ///   - Is auditing active at all? Which audit objects are enabled?
    ///   - What event groups are captured (failed logins, schema access, object changes)?
    ///   - Where do logs go (file, Windows Event Log, Application Log)?
    ///   - Is ON_FAILURE = SHUTDOWN set? (SQL service dies if the audit log fills up.)
    ///
    /// Requires VIEW SERVER STATE (held by the public role by default).
    /// No privileges are needed to read sys.server_audits.
    /// </summary>
    internal class Audit : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.Task("Enumerating SQL Server audit configuration");

            // 1. Audit objects
            string auditsQuery = @"
SELECT a.name, a.type_desc, a.is_state_enabled, a.on_failure_desc, a.queue_delay
FROM sys.server_audits a ORDER BY a.name;";

            DataTable audits = databaseContext.QueryService.ExecuteTable(auditsQuery);

            if (audits.Rows.Count == 0)
            {
                Logger.Warning("Auditing is not configured");
                return null;
            }

            // Post-process into a display table
            var display = new DataTable();
            display.Columns.Add("Audit Name");
            display.Columns.Add("Destination");
            display.Columns.Add("Enabled");
            display.Columns.Add("On Failure");
            display.Columns.Add("Queue Delay");

            foreach (DataRow r in audits.Rows)
            {
                string queueDelay = r["queue_delay"]?.ToString() == "0"
                    ? "Synchronous"
                    : r["queue_delay"] + " ms";

                display.Rows.Add(
                    r["name"],
                    r["type_desc"],
                    r["is_state_enabled"]?.ToString() == "1" ? "Yes" : "No",
                    r["on_failure_desc"],
                    queueDelay
                );
            }

            Logger.TaskNested($"Found {audits.Rows.Count} audit object(s)");
            Console.WriteLine(OutputFormatter.ConvertDataTable(display));

            // 2. Audit specifications + event groups
            string specsQuery = @"
SELECT a.name AS audit, s.name AS spec, s.is_state_enabled, d.audit_action_name
FROM sys.server_audit_specifications s
JOIN sys.server_audits a ON a.audit_guid = s.audit_guid
JOIN sys.server_audit_specification_details d ON d.server_specification_id = s.server_specification_id
ORDER BY a.name, s.name, d.audit_action_name;";

            DataTable specs = databaseContext.QueryService.ExecuteTable(specsQuery);

            if (specs.Rows.Count == 0)
            {
                Logger.Warning("No audit specifications configured");
                Logger.WarningNested("Audit objects exist but capture nothing");
                return null;
            }

            var specsDisplay = new DataTable();
            specsDisplay.Columns.Add("Audit");
            specsDisplay.Columns.Add("Specification");
            specsDisplay.Columns.Add("Enabled");
            specsDisplay.Columns.Add("Event Group");

            foreach (DataRow r in specs.Rows)
            {
                specsDisplay.Rows.Add(
                    r["audit"],
                    r["spec"],
                    r["is_state_enabled"]?.ToString() == "1" ? "Yes" : "No",
                    r["audit_action_name"]
                );
            }

            Logger.TaskNested($"Found {specs.Rows.Count} audited event group(s)");
            Console.WriteLine(OutputFormatter.ConvertDataTable(specsDisplay));

            return specsDisplay;
        }
    }
}
