// MSSQLand/Utilities/SidParser.cs

using System;
using System.Security.Principal;

namespace MSSQLand.Utilities
{
    /// <summary>
    /// Utility class for parsing Security Identifiers (SIDs) from various formats.
    /// </summary>
    public static class SidParser
    {
        /// <summary>
        /// Parses a SID from various formats (binary, hex string, or S-1-... string) to standard S-1-... format.
        /// </summary>
        /// <param name="rawSidObj">The SID object from SQL query (byte[], string, etc.)</param>
        /// <returns>SID in S-1-... format, or null if parsing fails.</returns>
        public static string ParseSid(object rawSidObj)
        {
            if (rawSidObj == null || rawSidObj == DBNull.Value)
            {
                return null;
            }

            try
            {
                if (rawSidObj is byte[] sidBytes)
                {
                    // Convert binary SID to S-1-... format
                    var sid = new SecurityIdentifier(sidBytes, 0);
                    return sid.Value;
                }
                else
                {
                    // Handle string-based SID formats
                    string maybeSid = rawSidObj.ToString();
                    
                    if (maybeSid.StartsWith("S-", StringComparison.OrdinalIgnoreCase))
                    {
                        return maybeSid;
                    }
                    else
                    {
                        // Try parsing hex format (0x...)
                        string hex = maybeSid.StartsWith("0x", StringComparison.OrdinalIgnoreCase) 
                            ? maybeSid.Substring(2) 
                            : maybeSid;
                        
                        var bytes = Misc.HexStringToBytes(hex);
                        var sid = new SecurityIdentifier(bytes, 0);
                        return sid.Value;
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts the domain SID from a user/group SID by removing the RID (last component).
        /// </summary>
        /// <param name="fullSid">Full SID in S-1-... format</param>
        /// <returns>Domain SID without the trailing RID, or null if invalid format.</returns>
        public static string GetAdDomain(string fullSid)
        {
            if (string.IsNullOrEmpty(fullSid))
            {
                return null;
            }

            int lastDash = fullSid.LastIndexOf('-');
            if (lastDash <= 0)
            {
                return null;
            }

            return fullSid.Substring(0, lastDash);
        }

        /// <summary>
        /// Extracts the RID (Relative Identifier) from a SID.
        /// </summary>
        /// <param name="fullSid">Full SID in S-1-... format</param>
        /// <returns>RID as string, or null if invalid format.</returns>
        public static string GetRid(string fullSid)
        {
            if (string.IsNullOrEmpty(fullSid))
            {
                return null;
            }

            int lastDash = fullSid.LastIndexOf('-');
            if (lastDash <= 0 || lastDash >= fullSid.Length - 1)
            {
                return null;
            }

            return fullSid.Substring(lastDash + 1);
        }

        /// <summary>
        /// Checks if a SID is a domain account (S-1-5-21-...).
        /// </summary>
        /// <param name="sid">SID in S-1-... format</param>
        /// <returns>True if it's a domain account SID.</returns>
        public static bool IsAdDomain(string sid)
        {
            return !string.IsNullOrEmpty(sid) && sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase);
        }
    }
}
