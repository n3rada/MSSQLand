using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Data;

namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// Retrieves all group memberships from the Windows authentication token.
    /// This includes AD groups, BUILTIN groups, NT AUTHORITY groups, and other Windows security principals.
    /// Only available for Windows authenticated connections (not available through linked servers).
    /// </summary>
    internal class AuthToken : BaseAction
    {
        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.Task("Retrieving Windows authentication token groups");

            try
            {
                // Check if it's a domain user
                if (!databaseContext.UserService.IsDomainUser)
                {
                    Logger.Warning("Current user is not a Windows domain user.");
                    return null;
                }

                // Query sys.login_token for all groups
                string tokenQuery = @"
                    SELECT DISTINCT 
                        name,
                        type_desc,
                        usage_desc,
                        principal_id
                    FROM sys.login_token
                    WHERE type = 'WINDOWS GROUP'
                    ORDER BY name;";

                var tokenTable = databaseContext.QueryService.ExecuteTable(tokenQuery);

                if (tokenTable.Rows.Count == 0)
                {
                    Logger.Warning("No groups found in authentication token.");
                    return null;
                }

                var groups = new List<Dictionary<string, string>>();

                foreach (System.Data.DataRow row in tokenTable.Rows)
                {
                    string groupName = row["name"].ToString();
                    string typeDesc = row["type_desc"].ToString();
                    string usageDesc = row["usage_desc"].ToString();
                    int principalId = Convert.ToInt32(row["principal_id"]);

                    // Determine group category
                    string category = DetermineGroupCategory(groupName);

                    groups.Add(new Dictionary<string, string>
                    {
                        { "Group Name", groupName },
                        { "Category", category },
                        { "Type", typeDesc },
                        { "Usage", usageDesc },
                        { "Has SQL Principal", principalId > 0 ? "Yes" : "No" }
                    });
                }

                // Display results
                DataTable resultTable = new DataTable();
                resultTable.Columns.Add("Group Name", typeof(string));
                resultTable.Columns.Add("Category", typeof(string));
                resultTable.Columns.Add("Type", typeof(string));
                resultTable.Columns.Add("Usage", typeof(string));
                resultTable.Columns.Add("Has SQL Principal", typeof(string));

                foreach (var group in groups)
                {
                    resultTable.Rows.Add(
                        group["Group Name"],
                        group["Category"],
                        group["Type"],
                        group["Usage"],
                        group["Has SQL Principal"]
                    );
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));

                return groups;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to retrieve authentication token: {e.Message}");
                if (Logger.IsDebugEnabled)
                {
                    Logger.DebugNested($"Stack trace: {e.StackTrace}");
                }
                return null;
            }
        }

        /// <summary>
        /// Determines the category of a Windows group based on its name prefix.
        /// </summary>
        private string DetermineGroupCategory(string groupName)
        {
            if (groupName.StartsWith("BUILTIN\\", StringComparison.OrdinalIgnoreCase))
                return "Built-in";
            
            if (groupName.StartsWith("NT AUTHORITY\\", StringComparison.OrdinalIgnoreCase))
                return "Well-known SID";
            
            if (groupName.StartsWith("NT SERVICE\\", StringComparison.OrdinalIgnoreCase))
                return "Service";
            
            if (groupName.Contains("\\"))
                return "Active Directory";
            
            return "Other";
        }
    }
}
