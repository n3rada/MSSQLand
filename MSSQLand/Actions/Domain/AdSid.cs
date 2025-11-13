using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Security.Principal;

namespace MSSQLand.Actions.Domain
{
    /// <summary>
    /// Retrieves the current user's SID using SUSER_SID() function.
    /// </summary>
    internal class AdSid : BaseAction
    {
        public override void ValidateArguments(string additionalArguments)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Retrieving current user's SID");

            try
            {
                string userName = databaseContext.UserService.UserName;
                string systemUser = databaseContext.UserService.SystemUser;
                Logger.Info($"System User: {systemUser}");

                // Get the user's SID using SUSER_SID()
                var dtSid = databaseContext.QueryService.ExecuteTable($"SELECT SUSER_SID('{systemUser}');");
                
                if (dtSid.Rows.Count == 0 || dtSid.Rows[0][0] == DBNull.Value)
                {
                    Logger.Error("Could not obtain user SID via SUSER_SID().");
                    return null;
                }

                // Extract the binary SID from the query result
                object rawSidObj = dtSid.Rows[0][0];
                
                // Parse the binary SID
                string AdSidString = SidParser.ParseSid(rawSidObj);
                
                if (string.IsNullOrEmpty(AdSidString))
                {
                    Logger.Error("Unable to parse user SID from SUSER_SID() result.");
                    return null;
                }

                // Extract domain SID if it's a domain account
                string AdDomain = null;
                string rid = null;
                
                if (SidParser.IsAdDomain(AdSidString))
                {
                    AdDomain = SidParser.GetAdDomain(AdSidString);
                    rid = SidParser.GetRid(AdSidString);
                }

                Logger.NewLine();
                Logger.Success("User SID information retrieved");

                // Create result dictionary
                var result = new Dictionary<string, string>
                {
                    { "User Name", userName },
                    { "System User", systemUser },
                    { "User SID", AdSidString }
                };

                if (!string.IsNullOrEmpty(AdDomain))
                {
                    result.Add("Domain SID", AdDomain);
                    result.Add("RID", rid);
                }
                else
                {
                    result.Add("Type", "Local or Built-in Account");
                }

                // Display as markdown table
                Console.WriteLine(MarkdownFormatter.ConvertDictionaryToMarkdownTable(result, "Property", "Value"));

                return result;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to retrieve user SID: {e.Message}");
                if (Logger.IsDebugEnabled)
                {
                    Logger.DebugNested($"Stack trace: {e.StackTrace}");
                }
                return null;
            }
        }
    }
}
