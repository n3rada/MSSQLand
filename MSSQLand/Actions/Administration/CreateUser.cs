using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Administration
{
    /// <summary>
    /// Creates a new SQL Server login with specified server role privileges.
    /// </summary>
    internal class CreateUser : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "u", LongName = "username", Description = "SQL login username")]
        private string _username = "backup_usr";

        [ArgumentMetadata(Position = 1, ShortName = "p", LongName = "password", Description = "SQL login password")]
        private string _password = "$ap3rlip0pe//e";

        [ArgumentMetadata(Position = 2, ShortName = "r", LongName = "role", Description = "Server role to assign")]
        private string _role = "sysadmin";

        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return;
            }

            // Parse both named and positional arguments using modern argparse-style
            var (named, positional) = ParseActionArguments(args);

            // Priority 1: Named arguments (most explicit)
            // Support both short (-u) and long (--username) forms
            if (named.TryGetValue("u", out string username) || named.TryGetValue("username", out username))
            {
                _username = username;
            }
            
            if (named.TryGetValue("p", out string password) || named.TryGetValue("password", out password))
            {
                _password = password;
            }
            
            if (named.TryGetValue("r", out string role) || named.TryGetValue("role", out role))
            {
                _role = role;
            }

            // Priority 2: Positional arguments (fallback if no named args)
            if (!named.ContainsKey("u") && !named.ContainsKey("username") && positional.Count > 0)
            {
                _username = positional[0];
            }

            if (!named.ContainsKey("p") && !named.ContainsKey("password") && positional.Count > 1)
            {
                _password = positional[1];
            }

            if (!named.ContainsKey("r") && !named.ContainsKey("role") && positional.Count > 2)
            {
                _role = positional[2];
            }

            // Validate inputs
            if (string.IsNullOrWhiteSpace(_username))
            {
                throw new ArgumentException("Username cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(_password))
            {
                throw new ArgumentException("Password cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(_role))
            {
                throw new ArgumentException("Role cannot be empty.");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Creating SQL login '{_username}'");
            Logger.TaskNested($"Password: '{_password}'");
            Logger.TaskNested($"Role: '{_role}'");

            try
            {

                // Escape single quotes in password
                string escapedPassword = _password.Replace("'", "''");
                
                string createLoginQuery = $@"CREATE LOGIN [{_username}]  WITH PASSWORD = '{escapedPassword}',  CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF;";

                databaseContext.QueryService.ExecuteNonProcessing(createLoginQuery);
                Logger.Success($"SQL login '{_username}' created successfully.");

                // Add the login to the specified server role
                Logger.TaskNested($"Adding '{_username}' to {_role} server role.");
                
                string addRoleToNewUserQuery = $"ALTER SERVER ROLE [{_role}] ADD MEMBER [{_username}];";
                databaseContext.QueryService.ExecuteNonProcessing(addRoleToNewUserQuery);
                
                Logger.Success($"'{_username}' added to {_role} role successfully.");

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to create SQL login: {ex.Message}");
                
                if (ex.Message.Contains("permission"))
                {
                    Logger.Warning("You may not have sufficient privileges to create logins or assign server roles.");
                }

                return false;
            }
        }
    }
}
