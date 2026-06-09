// MSSQLand/Actions/Remote/Links.cs

using System;
using System.Collections.Generic;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;


namespace MSSQLand.Actions.Remote
{
    internal class Links : BaseAction
    {
        /// <summary>
        /// Executes the query action using the provided ConnectionManager.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager for executing the query.</param>
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.Task($"Retrieving Linked SQL Servers");

            DataTable resultTable = GetLinkedServers(databaseContext);

            if (resultTable.Rows.Count == 0)
            {
                Logger.Warning("No linked servers found.");
            }
            else
            {
                Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));

                Logger.Success($"Found {resultTable.Rows.Count} linked server(s).");

                bool hasPassThrough = false;
                foreach (DataRow row in resultTable.Rows)
                {
                    string access = row["Access"].ToString();
                    if (access == "Pass-through" || access == "Pass-through (catch-all)")
                    {
                        hasPassThrough = true;
                        break;
                    }
                }

                if (hasPassThrough)
                {
                    Logger.Warning("Pass-through entries use the caller's Windows identity (Kerberos delegation required for network hops).");
                    Logger.WarningNested("SQL Authentication logins cannot use these mappings.");
                }
            }

            Logger.Warning("Only returns the linked servers that user has visibility into.");

            return resultTable;
        }


        /// <summary>
        /// Retrieves linked servers and login mappings.
        /// The raw columns from SQL Server are:
        ///   - [Local Login]: the specific local login mapped, NULL for catch-all or no row
        ///   - [Remote Login]: the remote credential, NULL for pass-through or denied
        ///   - [Uses Self]: 1=pass-through, 0=mapped/denied, NULL=no linked_logins row visible
        ///   - [Is Default]: 1 if local_principal_id=0 (catch-all rule), 0 otherwise
        ///
        /// C# display logic interprets these into the [Access] column shown to the user:
        ///   - "No visibility"            : no linked_logins row; user lacks permission to see mappings
        ///   - "Denied (catch-all)"       : catch-all rule explicitly blocks unmapped logins
        ///   - "Pass-through (catch-all)" : catch-all rule passes caller's Windows identity
        ///   - "Catch-all → {remote}"     : catch-all rule maps everyone to a fixed remote login
        ///   - "Pass-through"             : specific login passes through as itself
        ///   - "→ {remote}"              : specific login is mapped to a remote credential
        /// </summary>
        public static DataTable GetLinkedServers(DatabaseContext databaseContext)
        {
            string query = @"
                SELECT
                    srv.modify_date AS [Last Modified],
                    srv.name AS [Link],
                    srv.provider AS [Provider],
                    srv.data_source AS [Data Source],
                    prin.name AS [Local Login],
                    ll.remote_name AS [Remote Login],
                    ll.uses_self_credential AS [Uses Self],
                    CASE WHEN ll.server_id IS NOT NULL AND ll.local_principal_id = 0 THEN 1 ELSE 0 END AS [Is Default],
                    srv.is_rpc_out_enabled AS [RPC Out],
                    srv.is_data_access_enabled AS [OPENQUERY],
                    srv.is_collation_compatible AS [Collation]
                FROM master.sys.servers srv
                LEFT JOIN master.sys.linked_logins ll ON srv.server_id = ll.server_id
                LEFT JOIN master.sys.server_principals prin ON ll.local_principal_id = prin.principal_id
                WHERE srv.is_linked = 1
                ORDER BY srv.provider, srv.modify_date DESC;";

            DataTable raw = databaseContext.QueryService.ExecuteTable(query);

            // Add computed Access column for display
            raw.Columns.Add("Access", typeof(string));
            foreach (DataRow row in raw.Rows)
            {
                bool hasRow = row["Uses Self"] != DBNull.Value;
                bool usesSelf = hasRow && Convert.ToInt32(row["Uses Self"]) == 1;
                bool isDefault = Convert.ToInt32(row["Is Default"]) == 1;
                string remoteLogin = row["Remote Login"] == DBNull.Value ? null : row["Remote Login"].ToString();

                string access;
                if (!hasRow)
                    access = "No visibility";
                else if (isDefault && usesSelf)
                    access = "Pass-through (catch-all)";
                else if (isDefault && remoteLogin != null)
                    access = "Mapped (catch-all)";
                else if (isDefault)
                    access = "Denied (catch-all)";
                else if (usesSelf)
                    access = "Pass-through";
                else if (remoteLogin != null)
                    access = "Mapped";
                else
                    access = "Denied";

                row["Access"] = access;
            }

            // Remove internal columns not useful for display
            raw.Columns.Remove("Uses Self");
            raw.Columns.Remove("Is Default");

            // Remove "Denied (catch-all)" rows when specific mappings exist for the same link.
            // The deny is implied; only keep it when it's the sole row (no visible mappings).
            var linksWithMappings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow row in raw.Rows)
            {
                string access = row["Access"].ToString();
                if (access != "Denied (catch-all)" && access != "No visibility")
                    linksWithMappings.Add(row["Link"].ToString());
            }

            for (int i = raw.Rows.Count - 1; i >= 0; i--)
            {
                if (raw.Rows[i]["Access"].ToString() == "Denied (catch-all)"
                    && linksWithMappings.Contains(raw.Rows[i]["Link"].ToString()))
                {
                    raw.Rows.RemoveAt(i);
                }
            }

            return raw;
        }
    }
}
