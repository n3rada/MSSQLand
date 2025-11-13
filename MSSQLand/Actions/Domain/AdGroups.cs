using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;

namespace MSSQLand.Actions.Domain
{
    /// <summary>
    /// Retrieves Active Directory group memberships for the current user using xp_logininfo.
    /// </summary>
    internal class AdGroups : BaseAction
    {
        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Retrieving Active Directory group memberships");

            try
            {
                // Get the current system user
                (string userName, string systemUser) = databaseContext.UserService.GetInfo();

                if (string.IsNullOrEmpty(systemUser))
                {
                    Logger.Error("Could not determine current system user.");
                    return null;
                }

                Logger.Info($"System User: {systemUser}");

                // Check if xp_logininfo is available
                var xprocCheck = databaseContext.QueryService.ExecuteTable(
                    "SELECT * FROM master.sys.all_objects WHERE name = 'xp_logininfo' AND type = 'X';"
                );

                if (xprocCheck.Rows.Count == 0)
                {
                    Logger.Error("xp_logininfo extended procedure is not available.");
                    return null;
                }

                // Get group memberships using xp_logininfo
                string query = $"EXEC master.dbo.xp_logininfo @acctname = '{systemUser}', @option = 'all';";
                DataTable groupsTable = databaseContext.QueryService.ExecuteTable(query);

                if (groupsTable == null || groupsTable.Rows.Count == 0)
                {
                    Logger.Warning("No group memberships found or user is not a domain account.");
                    return null;
                }

                // Parse the results
                var groups = new List<Dictionary<string, string>>();
                
                foreach (DataRow row in groupsTable.Rows)
                {
                    string accountName = row["account name"]?.ToString();
                    string type = row["type"]?.ToString();
                    string privilege = row["privilege"]?.ToString();
                    string mappedLoginName = row["mapped login name"]?.ToString();
                    string permissionPath = row["permission path"]?.ToString();

                    // Filter to show only groups (not the user itself)
                    if (!string.IsNullOrEmpty(type) && 
                        type.IndexOf("group", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        groups.Add(new Dictionary<string, string>
                        {
                            { "Group Name", permissionPath ?? accountName },
                            { "Type", type },
                            { "Privilege", privilege ?? "N/A" }
                        });
                    }
                }

                if (groups.Count == 0)
                {
                    Logger.Warning("User is not a member of any domain groups.");
                    return null;
                }

                Logger.NewLine();
                Logger.Success($"Found {groups.Count} AD group membership(s)");

                // Display as markdown table
                DataTable resultTable = new DataTable();
                resultTable.Columns.Add("Group Name", typeof(string));
                resultTable.Columns.Add("Type", typeof(string));
                resultTable.Columns.Add("Privilege", typeof(string));

                foreach (var group in groups)
                {
                    resultTable.Rows.Add(
                        group["Group Name"],
                        group["Type"],
                        group["Privilege"]
                    );
                }

                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));

                return groups;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to retrieve AD group memberships: {e.Message}");
                if (Logger.IsDebugEnabled)
                {
                    Logger.DebugNested($"Stack trace: {e.StackTrace}");
                }
                return null;
            }
        }
    }
}
