using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using MSSQLand.Models;
using MSSQLand.Services;
using MSSQLand.Utilities;


namespace MSSQLand
{
    [ComVisible(true)]
    internal class Program
    {

        [STAThread]
        static int Main(string[] args)
        {

            Stopwatch stopwatch = Stopwatch.StartNew();
            DateTime startTime = DateTime.UtcNow;

            // Get time zone details
            DateTime localTime = DateTime.Now;
            TimeZoneInfo localTimeZone = TimeZoneInfo.Local;
            string timeZoneId = localTimeZone.Id;
            TimeSpan offset = localTimeZone.BaseUtcOffset;
            string formattedOffset = $"{(offset.Hours >= 0 ? "+" : "-")}{Math.Abs(offset.Hours)}:{Math.Abs(offset.Minutes):D2}";

            try
            {
                CommandParser parser = new();
                (CommandParser.ParseResultType result, CommandArgs? arguments) = parser.Parse(args);

                switch (result)
                {
                    case CommandParser.ParseResultType.ShowHelp:
                        return 0;
                    case CommandParser.ParseResultType.InvalidInput:
                        return 1;
                    case CommandParser.ParseResultType.UtilityMode:
                        return 0;
                    case CommandParser.ParseResultType.Success:
                        break; // Proceed with execution
                }

                // Ensure arguments are valid (just in case)
                if (arguments == null || arguments.Host == null)
                {
                    Logger.Error("Invalid command arguments.");
                    return 1;
                }


                // Show banner only when executing an action
                int bannerWidth = Logger.Banner($"Executing from: {Environment.MachineName}\nTime Zone ID: {timeZoneId}\nLocal Time: {localTime:HH:mm:ss}, UTC Offset: {formattedOffset}");
                Logger.NewLine();

                Logger.Banner($"Start at {startTime:yyyy-MM-dd HH:mm:ss:fffff} UTC", totalWidth: bannerWidth);
                Logger.NewLine();

                using AuthenticationService authService = new(arguments.Host);

                // Authenticate with the provided credentials
                if (!authService.Authenticate(
                    credentialsType: arguments.CredentialType,
                    sqlServer: $"{arguments.Host.Hostname},{arguments.Host.Port}",
                    database: arguments.Host.Database ?? "master",
                    username: arguments.Username,
                    password: arguments.Password,
                    domain: arguments.Domain,
                    connectionTimeout: arguments.ConnectionTimeout
                 ))
                {
                    Logger.Error("Failed to authenticate with the provided credentials.");
                    return 1;
                }

                DatabaseContext databaseContext;
                try
                {
                    databaseContext = new DatabaseContext(authService);
                }
                catch (Exception ex)
                {
                    Logger.Error($"DatabaseContext initialization failed: {ex.Message}");
                    return 1;
                }

                string userName, systemUser;
                try
                {
                    (userName, systemUser) = databaseContext.UserService.GetInfo();
                    databaseContext.Server.MappedUser = userName;
                    databaseContext.Server.SystemUser = systemUser;

                    Logger.Info($"Logged in on {databaseContext.Server.Hostname} as {systemUser}");
                    Logger.InfoNested($"Mapped to the user {userName}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to retrieve user information: {ex.Message}");
                    return 1;
                }

                // Compute effective user and source principal (handles group-based access via AD groups)
                // Only works for Windows Integrated authentication on on-premises SQL Server
                // Does not work for: SQL auth, Azure AD auth, LocalDB, or linked servers
                if (arguments.CredentialType == "windows" && databaseContext.UserService.IsDomainUser)
                {
                    databaseContext.UserService.ComputeEffectiveUserAndSource();
                    
                    string effectiveUser = databaseContext.UserService.EffectiveUser;
                    string sourcePrincipal = databaseContext.UserService.SourcePrincipal;
                    
                    if (!effectiveUser.Equals(userName, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.InfoNested($"Effective database user: {effectiveUser}");
                        if (!sourcePrincipal.Equals(systemUser, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.InfoNested($"Access granted via: {sourcePrincipal}");
                        }
                    }
                }

                // If LinkedServers variable exists and has valid server names
                if (arguments.LinkedServers?.ServerNames != null && arguments.LinkedServers.ServerNames.Length > 0)
                {
                    databaseContext.QueryService.LinkedServers = arguments.LinkedServers;

                    Logger.Info($"Server chain: {arguments.Host.Hostname} -> " + string.Join(" -> ", arguments.LinkedServers.ServerNames));
                    
                    try
                    {
                        (userName, systemUser) = databaseContext.UserService.GetInfo();

                        Logger.Info($"Logged in on {databaseContext.QueryService.ExecutionServer} as {systemUser}");
                        Logger.InfoNested($"Mapped to the user {userName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to retrieve user information on linked server: {ex.Message}");
                        return 1;
                    }
                }

                // Compute and display the final execution context
                databaseContext.QueryService.ComputeExecutionDatabase();
                Logger.InfoNested($"Execution database: {databaseContext.QueryService.ExecutionDatabase}");

                // Detect Azure SQL on the final execution server
                databaseContext.QueryService.IsAzureSQL();

                Logger.NewLine();
                Logger.Task($"Executing action '{arguments.Action.GetName()}' against {databaseContext.QueryService.ExecutionServer}");
                arguments.Action.Execute(databaseContext);

                stopwatch.Stop();
                DateTime endTime = DateTime.UtcNow;
                Logger.Banner($"End at {endTime:yyyy-MM-dd HH:mm:ss:fffff} UTC\nTotal duration: {stopwatch.Elapsed.TotalSeconds:F2} seconds", totalWidth: bannerWidth);
                
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
                Logger.Debug($"Stack Trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    Logger.Error("Inner Exception:");
                    Logger.Error($"Message: {ex.InnerException.Message}");
                    Logger.Debug($"Stack Trace: {ex.InnerException.StackTrace}");
                }

                return 1;
            }

        }

    }
}
