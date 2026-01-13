// MSSQLand/Actions/Domain/AdSid.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
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
        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested("Retrieving current user's SID");

            try
            {
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
                
                // Convert binary SID to hex string
                string hexSid = null;
                string hexDomainSid = null;
                string hexRid = null;
                if (rawSidObj is byte[] sidBytes)
                {
                    hexSid = "0x" + BitConverter.ToString(sidBytes).Replace("-", "");
                    
                    // Domain SID is the user SID without the last 4 bytes (RID)
                    if (sidBytes.Length > 4)
                    {
                        byte[] domainSidBytes = new byte[sidBytes.Length - 4];
                        Array.Copy(sidBytes, 0, domainSidBytes, 0, domainSidBytes.Length);
                        hexDomainSid = "0x" + BitConverter.ToString(domainSidBytes).Replace("-", "");
                        
                        // Extract the last 4 bytes as the RID
                        byte[] ridBytes = new byte[4];
                        Array.Copy(sidBytes, sidBytes.Length - 4, ridBytes, 0, 4);
                        hexRid = "0x" + BitConverter.ToString(ridBytes).Replace("-", "");
                    }
                }
                
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
                    { "System User", systemUser },
                    { "User SID", AdSidString }
                };

                if (!string.IsNullOrEmpty(hexSid))
                {
                    result.Add("User Hex SID", hexSid);
                }

                if (!string.IsNullOrEmpty(AdDomain))
                {
                    result.Add("Domain SID", AdDomain);
                    if (!string.IsNullOrEmpty(hexDomainSid))
                    {
                        result.Add("Domain Hex SID", hexDomainSid);
                    }
                    result.Add("RID", rid);
                    if (!string.IsNullOrEmpty(hexRid))
                    {
                        result.Add("Hex RID", hexRid);
                    }
                }
                else
                {
                    result.Add("Type", "Local or Built-in Account");
                }

                // Display as markdown table
                Console.WriteLine(OutputFormatter.ConvertDictionary(result, "Property", "Value"));

                return result;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to retrieve user SID: {e.Message}");
                Logger.TraceNested($"Stack trace: {e.StackTrace}");
                return null;
            }
        }
    }
}
