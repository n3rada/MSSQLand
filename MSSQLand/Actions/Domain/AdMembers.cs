// MSSQLand/Actions/Domain/AdMembers.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Domain
{
    /// <summary>
    /// Retrieves members of a specific Active Directory group using multiple methods.
    /// </summary>
    internal class AdMembers : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "AD group name")]
        private string _groupName = "";

        [ArgumentMetadata(LongName = "openquery", Description = "Use OPENQUERY method with ADSI fallback")]
        private bool _useOpenQuery = false;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (string.IsNullOrWhiteSpace(_groupName))
            {
                throw new ArgumentException("Group name is required");
            }

            // Back-compat: allow positional "openquery" as second argument
            if (args != null && args.Length > 1)
            {
                if (args.Length > 2)
                {
                    throw new ArgumentException("Too many arguments. Usage: ad-members <DOMAIN\\Group> [openquery]");
                }

                if (args[1].Trim().Equals("openquery", StringComparison.OrdinalIgnoreCase))
                {
                    _useOpenQuery = true;
                }
                else
                {
                    throw new ArgumentException("Invalid argument. Use 'openquery' as the optional second argument.");
                }
            }

            // Ensure the group name contains a backslash (domain separator)
            if (!_groupName.Contains("\\"))
            {
                throw new ArgumentException("Group name must be in format: DOMAIN\\GroupName");
            }
        }

        public override object Execute(DatabaseContext databaseContext)
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
                    "SELECT * FROM master.sys.all_objects WHERE name = 'xp_logininfo' AND type = 'X';"
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
                Console.WriteLine(OutputFormatter.ConvertDataTable(membersTable));

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
                Console.WriteLine(OutputFormatter.ConvertDataTable(membersTable));

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
