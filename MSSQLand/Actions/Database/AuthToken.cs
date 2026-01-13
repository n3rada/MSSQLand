// MSSQLand/Actions/Database/AuthToken.cs

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
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Retrieving Windows authentication token groups");

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
                        lt.name,
                        lt.type,
                        lt.usage,
                        lt.principal_id,
                        sp.name AS sql_principal_name
                    FROM sys.login_token lt
                    LEFT JOIN master.sys.server_principals sp ON lt.principal_id = sp.principal_id
                    WHERE lt.type = 'WINDOWS GROUP'
                    ORDER BY lt.name;";

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
                    string typeDesc = row["type"].ToString();
                    string usage = row["usage"].ToString();
                    int principalId = Convert.ToInt32(row["principal_id"]);

                    // Determine group category
                    string category = DetermineGroupCategory(groupName);

                    // Get SQL Server principal name from the joined query
                    string sqlPrincipal = row["sql_principal_name"] == DBNull.Value ? "-" : row["sql_principal_name"].ToString();

                    groups.Add(new Dictionary<string, string>
                    {
                        { "Group Name", groupName },
                        { "Category", category },
                        { "Type", typeDesc },
                        { "Usage", usage },
                        { "SQL Principal", sqlPrincipal }
                    });
                }

                // Display results
                DataTable resultTable = new DataTable();
                resultTable.Columns.Add("Group Name", typeof(string));
                resultTable.Columns.Add("Category", typeof(string));
                resultTable.Columns.Add("Type", typeof(string));
                resultTable.Columns.Add("Usage", typeof(string));
                resultTable.Columns.Add("SQL Principal", typeof(string));

                foreach (var group in groups)
                {
                    resultTable.Rows.Add(
                        group["Group Name"],
                        group["Category"],
                        group["Type"],
                        group["Usage"],
                        group["SQL Principal"]
                    );
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));
                
                Logger.Success($"Retrieved {groups.Count} group membership(s) from authentication token");

                return groups;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to retrieve authentication token: {e.Message}");
                Logger.TraceNested($"Stack trace: {e.StackTrace}");
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
