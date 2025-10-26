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
                // Combined query: check if login exists AND if role exists in one go
                string validationQuery = $@"
                    SELECT 
                        (SELECT COUNT(*) FROM sys.server_principals WHERE name = '{_username.Replace("'", "''")}') AS LoginExists,
                        (SELECT COUNT(*) FROM sys.server_principals WHERE type_desc = 'SERVER_ROLE' AND name = '{_role.Replace("'", "''")}') AS RoleExists;";
                
                var validationTable = databaseContext.QueryService.ExecuteTable(validationQuery);
                
                if (validationTable.Rows.Count == 0)
                {
                    Logger.Error("Failed to validate login and role existence.");
                    return false;
                }

                int loginExists = Convert.ToInt32(validationTable.Rows[0]["LoginExists"]);
                int roleExists = Convert.ToInt32(validationTable.Rows[0]["RoleExists"]);

                // Check if role exists
                if (roleExists == 0)
                {
                    Logger.Error($"Server role '{_role}' does not exist on this SQL Server instance.");
                    Logger.Info("To see available server roles, use: SELECT name FROM sys.server_principals WHERE type_desc = 'SERVER_ROLE';");
                    return false;
                }

                // Check if login already exists
                if (loginExists > 0)
                {
                    Logger.Warning($"Login '{_username}' already exists.");
                    Logger.Info($"Attempting to add {_role} role if not already assigned...");

                    // Try to add specified role even if login exists
                    string addRoleQuery = $"ALTER SERVER ROLE [{_role}] ADD MEMBER [{_username}];";
                    
                    try
                    {
                        databaseContext.QueryService.ExecuteNonProcessing(addRoleQuery);
                        Logger.Success($"Added '{_username}' to {_role} role.");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("already a member"))
                        {
                            Logger.Info($"'{_username}' is already a member of {_role} role.");
                        }
                        else
                        {
                            Logger.Error($"Failed to add {_role} role: {ex.Message}");
                        }
                    }

                    return true;
                }

                // Create the SQL login
                Logger.Info($"Creating SQL login '{_username}'...");
                
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
                Logger.Info($"Adding '{_username}' to {_role} server role...");
                
                string addRoleToNewUserQuery = $"ALTER SERVER ROLE [{_role}] ADD MEMBER [{_username}];";
                databaseContext.QueryService.ExecuteNonProcessing(addRoleToNewUserQuery);
                
                Logger.Success($"'{_username}' added to {_role} role successfully.");

                Logger.NewLine();
                Logger.Success($"SQL login created and configured:");
                Console.WriteLine(MarkdownFormatter.ConvertDictionaryToMarkdownTable(
                    new System.Collections.Generic.Dictionary<string, string>
                    {
                        { "Username", _username },
                        { "Password", _password },
                        { "Server Role", _role },
                        { "Check Policy", "OFF" },
                        { "Check Expiration", "OFF" }
                    },
                    "Property",
                    "Value"
                ));

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
