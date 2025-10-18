using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Security.Principal;

namespace MSSQLand.Actions.Domain
{
    /// <summary>
    /// Retrieves the domain SID using SUSER_SID and DEFAULT_DOMAIN functions.
    /// </summary>
    internal class DomainSid : BaseAction
    {
        public override void ValidateArguments(string additionalArguments)
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

                // Parse the binary SID
                string domainSidString;
                string domainSidPrefix;
                object rawSidObj = dtSid.Rows[0][0];

                if (rawSidObj is byte[] sidBytes)
                {
                    // Convert binary SID to S-1-... format
                    var sid = new SecurityIdentifier(sidBytes, 0);
                    domainSidString = sid.Value;
                }
                else
                {
                    // Handle string-based SID formats
                    string maybeSid = rawSidObj.ToString();
                    if (maybeSid.StartsWith("S-"))
                    {
                        domainSidString = maybeSid;
                    }
                    else
                    {
                        // Try parsing hex format (0x...)
                        string hex = maybeSid.StartsWith("0x", StringComparison.OrdinalIgnoreCase) 
                            ? maybeSid.Substring(2) 
                            : maybeSid;
                        try
                        {
                            var bytes = Misc.HexStringToBytes(hex);
                            var sid = new SecurityIdentifier(bytes, 0);
                            domainSidString = sid.Value;
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Unable to parse domain SID: {ex.Message}");
                            return null;
                        }
                    }
                }

                // Strip the trailing RID to get the domain SID prefix (S-1-5-21-XXXXXXXXX-XXXXXXXXX-XXXXXXXXX)
                int lastDash = domainSidString.LastIndexOf('-');
                if (lastDash <= 0)
                {
                    Logger.Error($"Unexpected SID format: {domainSidString}");
                    return null;
                }
                
                domainSidPrefix = domainSidString.Substring(0, lastDash);

                Logger.NewLine();
                Logger.Success("Domain SID information retrieved");

                // Create result dictionary
                var result = new Dictionary<string, string>
                {
                    { "Domain", domain },
                    { "Full SID (Domain Admins)", domainSidString },
                    { "Domain SID", domainSidPrefix }
                };

                // Display as markdown table
                Console.WriteLine(MarkdownFormatter.ConvertDictionaryToMarkdownTable(result, "Property", "Value"));

                return result;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to retrieve domain SID: {e.Message}");
                if (Logger.IsDebugEnabled)
                {
                    Logger.DebugNested($"Stack trace: {e.StackTrace}");
                }
                return null;
            }
        }
    }
}
