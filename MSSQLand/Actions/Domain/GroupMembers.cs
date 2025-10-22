using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;

namespace MSSQLand.Actions.Domain
{
    /// <summary>
    /// Retrieves members of a specific Active Directory group using multiple methods.
    /// </summary>
    internal class GroupMembers : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "AD group name (e.g., DOMAIN\\Domain Admins)")]
        private string _groupName;

        [ExcludeFromArguments]
        private bool _useOpenQuery = false;

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrWhiteSpace(additionalArguments))
            {
                throw new ArgumentException("Group name is required. Example: DOMAIN\\IT or DOMAIN\\Domain Admins");
            }

            string[] parts = SplitArguments(additionalArguments);
            _groupName = parts[0].Trim();

            // Check for openquery flag
            if (parts.Length > 1 && parts[1].Trim().Equals("openquery", StringComparison.OrdinalIgnoreCase))
            {
                _useOpenQuery = true;
            }

            // Ensure the group name contains a backslash (domain separator)
            if (!_groupName.Contains("\\"))
            {
                throw new ArgumentException("Group name must be in format: DOMAIN\\GroupName");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Retrieving members of AD group: {_groupName}");

            // Try xp_logininfo first (most common method)
            DataTable result = TryXpLoginInfo(databaseContext);
            
            if (result != null)
            {
                return result;
            }

            // If xp_logininfo fails and openquery flag is set, try OPENQUERY with ADSI
            if (_useOpenQuery)
            {
                Logger.Info("Attempting OPENQUERY method with ADSI...");
                result = TryOpenQueryAdsi(databaseContext);
                
                if (result != null)
                {
                    return result;
                }
            }

            Logger.Error("All enumeration methods failed.");

            return null;
        }

        /// <summary>
        /// Tries to enumerate group members using xp_logininfo (default method).
        /// </summary>
        private DataTable TryXpLoginInfo(DatabaseContext databaseContext)
        {
            try
            {
                Logger.Info("Trying xp_logininfo method...");

                // Check if xp_logininfo is available
                var xprocCheck = databaseContext.QueryService.ExecuteTable(
                    "SELECT * FROM sys.all_objects WHERE name = 'xp_logininfo' AND type = 'X';"
                );

                if (xprocCheck.Rows.Count == 0)
                {
                    Logger.Warning("xp_logininfo extended stored procedure is not available.");
                    return null;
                }

                // Query group members using xp_logininfo
                string query = $"EXEC xp_logininfo @acctname = '{_groupName}', @option = 'members';";
                DataTable membersTable = databaseContext.QueryService.ExecuteTable(query);

                if (membersTable.Rows.Count == 0)
                {
                    Logger.Warning($"No members found for group '{_groupName}'. Verify the group name and permissions.");
                    return null;
                }

                Logger.NewLine();
                Logger.Success($"Found {membersTable.Rows.Count} member(s) using xp_logininfo");

                // Display the results
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(membersTable));

                return membersTable;
            }
            catch (Exception ex)
            {
                Logger.Warning($"xp_logininfo method failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Tries to enumerate group members using OPENQUERY with ADSI.
        /// Requires 'Ad Hoc Distributed Queries' to be enabled.
        /// </summary>
        private DataTable TryOpenQueryAdsi(DatabaseContext databaseContext)
        {
            try
            {
                // Extract domain and group name
                string[] parts = _groupName.Split('\\');
                if (parts.Length != 2)
                {
                    Logger.Warning("Invalid group name format for OPENQUERY method.");
                    return null;
                }

                string domain = parts[0];
                string groupName = parts[1];

                // Build LDAP query
                string query = $@"
                    SELECT *
                    FROM OPENQUERY(
                        ADSI,
                        'SELECT cn, sAMAccountName, distinguishedName
                         FROM ''LDAP://{domain}''
                         WHERE objectClass = ''user''
                         AND memberOf = ''CN={groupName},CN=Users,DC={domain.Replace(".", ",DC=")}'''
                    );";

                DataTable membersTable = databaseContext.QueryService.ExecuteTable(query);

                if (membersTable.Rows.Count == 0)
                {
                    Logger.Warning($"No members found using OPENQUERY method.");
                    return null;
                }

                Logger.NewLine();
                Logger.Success($"Found {membersTable.Rows.Count} member(s) using OPENQUERY/ADSI");
                Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(membersTable));

                return membersTable;
            }
            catch (Exception ex)
            {
                Logger.Warning($"OPENQUERY/ADSI method failed: {ex.Message}");
                
                return null;
            }
        }
    }
}
