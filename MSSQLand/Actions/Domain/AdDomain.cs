using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;
using System.Security.Principal;

namespace MSSQLand.Actions.Domain
{
    /// <summary>
    /// Retrieves the domain SID using SUSER_SID and DEFAULT_DOMAIN functions.
    /// </summary>
    internal class AdDomain : BaseAction
    {
        public override void ValidateArguments(string[] args)
        {
            // No additional arguments needed
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Retrieving domain SID");

            try
            {
                // 1) Get the default domain
                var dtDomain = databaseContext.QueryService.ExecuteTable("SELECT DEFAULT_DOMAIN();");
                if (dtDomain.Rows.Count == 0 || dtDomain.Rows[0][0] == DBNull.Value)
                {
                    Logger.Error("Could not determine DEFAULT_DOMAIN(). The server may not be domain-joined.");
                    return null;
                }
                
                string domain = dtDomain.Rows[0][0].ToString();
                Logger.Info($"Domain: {domain}");

                // 2) Obtain the domain SID by querying a known group (Domain Admins)
                var dtSid = databaseContext.QueryService.ExecuteTable($"SELECT SUSER_SID('{domain}\\Domain Admins');");
                if (dtSid.Rows.Count == 0 || dtSid.Rows[0][0] == DBNull.Value)
                {
                    Logger.Error("Could not obtain domain SID via SUSER_SID(). Ensure the server has access to the domain.");
                    return null;
                }

                // Extract the binary SID from the query result
                object rawSidObj = dtSid.Rows[0][0];
                
                // Parse the binary SID
                string AdDomainString = SidParser.ParseSid(rawSidObj);
                
                if (string.IsNullOrEmpty(AdDomainString))
                {
                    Logger.Error("Unable to parse domain SID from SUSER_SID() result.");
                    return null;
                }

                // Strip the trailing RID to get the domain SID prefix
                string AdDomainPrefix = SidParser.GetAdDomain(AdDomainString);
                
                if (string.IsNullOrEmpty(AdDomainPrefix))
                {
                    Logger.Error($"Unexpected SID format: {AdDomainString}");
                    return null;
                }

                Logger.Success("Domain SID information retrieved");

                // Create result dictionary
                var result = new Dictionary<string, string>
                {
                    { "Domain", domain },
                    { "Full SID (Domain Admins)", AdDomainString },
                    { "Domain SID", AdDomainPrefix }
                };

                // Display as markdown table
                Console.WriteLine(OutputFormatter.ConvertDictionary(result, "Property", "Value"));

                return result;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to retrieve domain SID: {e.Message}");
                if (Logger.MinimumLogLevel <= LogLevel.Debug)
                {
                    Logger.DebugNested($"Stack trace: {e.StackTrace}");
                }
                return null;
            }
        }
    }
}
