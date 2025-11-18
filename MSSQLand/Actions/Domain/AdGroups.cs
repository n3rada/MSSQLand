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
            Logger.Task("Retrieving Active Directory group memberships");

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

                // Check if it's a domain user
                if (!databaseContext.UserService.IsDomainUser)
                {
                    Logger.Warning("Current user is not a Windows domain user.");
                    return null;
                }

                // Try xp_logininfo first (most detailed, but requires elevated privileges)
                DataTable groupsTable = null;
                bool useXpLogininfo = false;

                try
                {
                    Logger.TaskNested("Attempting to use xp_logininfo...");
                    string query = $"EXEC master.dbo.xp_logininfo @acctname = '{systemUser}', @option = 'all';";
                    groupsTable = databaseContext.QueryService.ExecuteTable(query);
                    
                    if (groupsTable != null && groupsTable.Rows.Count > 0)
                    {
                        useXpLogininfo = true;
                        Logger.Success("Retrieved group memberships via xp_logininfo");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"xp_logininfo not available or insufficient permissions: {ex.Message}");
                }

                // Fallback: Use IS_MEMBER() to check all Windows groups
                if (!useXpLogininfo)
                {
                    Logger.TaskNested("Using fallback method: IS_MEMBER() checks");
                    
                    string groupsQuery = @"
                        SELECT name, type_desc, is_disabled, create_date
                        FROM master.sys.server_principals
                        WHERE type = 'G'
                        AND name LIKE '%\%'
                        AND name NOT LIKE '##%'
                        ORDER BY name;";

                    DataTable windowsGroups = databaseContext.QueryService.ExecuteTable(groupsQuery);

                    if (windowsGroups.Rows.Count == 0)
                    {
                        Logger.Warning("No Windows groups found in SQL Server principals.");
                        return null;
                    }

                    Logger.TaskNested($"Checking membership in {windowsGroups.Rows.Count} Windows group(s)...");

                    var memberGroups = new List<Dictionary<string, string>>();

                    foreach (DataRow row in windowsGroups.Rows)
                    {
                        string groupName = row["name"].ToString();
                        bool isDisabled = Convert.ToBoolean(row["is_disabled"]);

                        try
                        {
                            string memberCheckQuery = $"SELECT IS_MEMBER('{groupName.Replace("'", "''")}') AS IsMember;";
                            DataTable memberCheck = databaseContext.QueryService.ExecuteTable(memberCheckQuery);

                            if (memberCheck.Rows.Count > 0 && memberCheck.Rows[0][0] != DBNull.Value)
                            {
                                int isMember = Convert.ToInt32(memberCheck.Rows[0][0]);

                                if (isMember == 1)
                                {
                                    memberGroups.Add(new Dictionary<string, string>
                                    {
                                        { "Group Name", groupName },
                                        { "Type", "Windows Group" },
                                        { "Is Disabled", isDisabled.ToString() }
                                    });
                                }
                            }
                        }
                        catch
                        {
                            // IS_MEMBER might fail for some groups
                        }
                    }

                    if (memberGroups.Count == 0)
                    {
                        Logger.Warning("You are not a member of any Windows groups in SQL Server.");
                        return null;
                    }

                    Logger.NewLine();
                    Logger.Success($"Found {memberGroups.Count} group membership(s)");

                    DataTable resultTable = new DataTable();
                    resultTable.Columns.Add("Group Name", typeof(string));
                    resultTable.Columns.Add("Type", typeof(string));
                    resultTable.Columns.Add("Is Disabled", typeof(string));

                    foreach (var group in memberGroups)
                    {
                        resultTable.Rows.Add(
                            group["Group Name"],
                            group["Type"],
                            group["Is Disabled"]
                        );
                    }

                    Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(resultTable));
                    return memberGroups;
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
