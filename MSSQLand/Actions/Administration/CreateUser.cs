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

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrWhiteSpace(additionalArguments))
            {
                return;
            }

            // Parse both named and positional arguments
            var (named, positional) = ParseArguments(additionalArguments);

            // Priority 1: Named arguments (most explicit)
            _username = GetNamedArgument(named, "u", null) ?? 
                       GetNamedArgument(named, "username", null) ?? 
                       _username;
            
            _password = GetNamedArgument(named, "p", null) ?? 
                       GetNamedArgument(named, "password", null) ?? 
                       _password;
            
            _role = GetNamedArgument(named, "r", null) ?? 
                   GetNamedArgument(named, "role", null) ?? 
                   _role;

            // Priority 2: Positional arguments (fallback)
            // Only use positional args if named args weren't provided
            if (!named.ContainsKey("u") && !named.ContainsKey("username"))
            {
                _username = GetPositionalArgument(positional, 0, _username);
            }

            if (!named.ContainsKey("p") && !named.ContainsKey("password"))
            {
                if (positional.Count > 1)
                {
                    _password = GetPositionalArgument(positional, 1, _password);
                }
            }

            if (!named.ContainsKey("r") && !named.ContainsKey("role"))
            {
                if (positional.Count > 2)
                {
                    _role = GetPositionalArgument(positional, 2, _role);
                }
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
            Logger.TaskNested($"Creating SQL login '{_username}' with '{_role}' role");

            try
            {
                // Create the SQL login
                Logger.TaskNested($"Creating SQL login '{_username}'");
                
                // Escape single quotes in password
                string escapedPassword = _password.Replace("'", "''");
                
                string createLoginQuery = $@"
                    CREATE LOGIN [{_username}] 
                    WITH PASSWORD = '{escapedPassword}', 
                    CHECK_POLICY = OFF, 
                    CHECK_EXPIRATION = OFF;";

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

                if (Logger.IsDebugEnabled)
                {
                    Logger.DebugNested($"Stack trace: {ex.StackTrace}");
                }

                return false;
            }
        }
    }
}
