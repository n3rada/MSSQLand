using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
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
                // Check if it's a domain user
                if (!databaseContext.UserService.IsDomainUser)
                {
                    Logger.Warning("Current user is not a Windows domain user.");
                    return null;
                }

                var groupNames = new List<string>();

                // Try sys.login_token first (direct connections)
                try
                {
                    string tokenQuery = @"
                        SELECT DISTINCT name
                        FROM sys.login_token
                        WHERE type = 'WINDOWS GROUP'
                        ORDER BY name;";

                    var tokenTable = databaseContext.QueryService.ExecuteTable(tokenQuery);

                    if (tokenTable.Rows.Count > 0)
                    {
                        Logger.Info("Retrieving groups from Windows Authentication Token");
                        Logger.InfoNested("These are ALL groups in your Windows token (including those without SQL Server access)");
                        
                        foreach (System.Data.DataRow row in tokenTable.Rows)
                        {
                            string groupName = row["name"].ToString();
                            groupNames.Add(groupName);
                        }
                    }
                    else
                    {
                        // Linked server or token unavailable - fall back to IS_MEMBER
                        Logger.Info("Retrieving groups via IS_MEMBER (linked server or token unavailable)");
                        Logger.InfoNested("Only groups with SQL Server principals will be shown");
                        
                        string groupsQuery = @"
                            SELECT name
                            FROM master.sys.server_principals
                            WHERE type = 'G'
                            AND name LIKE '%\%'
                            AND name NOT LIKE '##%'
                            ORDER BY name;";

                        var serverGroups = databaseContext.QueryService.ExecuteTable(groupsQuery);

                        foreach (System.Data.DataRow row in serverGroups.Rows)
                        {
                            string groupName = row["name"].ToString();

                            try
                            {
                                string memberCheckQuery = $"SELECT IS_MEMBER('{groupName.Replace("'", "''")}');";
                                object result = databaseContext.QueryService.ExecuteScalar(memberCheckQuery);

                                if (result != null && result != DBNull.Value && Convert.ToInt32(result) == 1)
                                {
                                    groupNames.Add(groupName);
                                }
                            }
                            catch
                            {
                                // IS_MEMBER might fail for some groups
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error retrieving AD groups: {ex.Message}");
                    return null;
                }

                if (groupNames.Count == 0)
                {
                    Logger.Warning("User is not a member of any domain groups.");
                    return null;
                }

                Logger.NewLine();
                Logger.Success($"Found {groupNames.Count} group membership(s)");

                // Query additional details for each group
                var groups = new List<Dictionary<string, string>>();
                
                foreach (string groupName in groupNames)
                {
                    try
                    {
                        string detailsQuery = $@"
                            SELECT type_desc, is_disabled
                            FROM master.sys.server_principals
                            WHERE name = '{groupName.Replace("'", "''")}';";
                        
                        var details = databaseContext.QueryService.ExecuteTable(detailsQuery);
                        
                        if (details.Rows.Count > 0)
                        {
                            groups.Add(new Dictionary<string, string>
                            {
                                { "Group Name", groupName },
                                { "Type", details.Rows[0]["type_desc"]?.ToString() ?? "Windows Group" },
                                { "Is Disabled", details.Rows[0]["is_disabled"]?.ToString() ?? "Unknown" }
                            });
                        }
                        else
                        {
                            groups.Add(new Dictionary<string, string>
                            {
                                { "Group Name", groupName },
                                { "Type", "Windows Group" },
                                { "Is Disabled", "Unknown" }
                            });
                        }
                    }
                    catch
                    {
                        groups.Add(new Dictionary<string, string>
                        {
                            { "Group Name", groupName },
                            { "Type", "Windows Group" },
                            { "Is Disabled", "Unknown" }
                        });
                    }
                }

                // Display results
                DataTable resultTable = new DataTable();
                resultTable.Columns.Add("Group Name", typeof(string));
                resultTable.Columns.Add("Type", typeof(string));
                resultTable.Columns.Add("Is Disabled", typeof(string));

                foreach (var group in groups)
                {
                    resultTable.Rows.Add(
                        group["Group Name"],
                        group["Type"],
                        group["Is Disabled"]
                    );
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));

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
