﻿using System;
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

        public static readonly Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
        public static readonly DateTime compileDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(currentVersion.Build - 1).Date; // Strips the time portion


        [STAThread]
        static void Main(string[] args)
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

                CommandArgs arguments = parser.Parse(args);

                Logger.Banner($"Version: {currentVersion}\nCompile date: {compileDate:yyyy-MM-dd}", borderChar: '*');

                Logger.NewLine();

                int bannerWidth = Logger.Banner($"Executing from: {Environment.MachineName}\nTime Zone ID: {timeZoneId}\nLocal Time: {localTime:HH:mm:ss}, UTC Offset: {formattedOffset}");

                Logger.NewLine();

                Logger.Banner($"Start at {startTime:yyyy-MM-dd HH:mm:ss:fffff} UTC", totalWidth: bannerWidth);
                
                Logger.NewLine();

                using AuthenticationService authService = new(arguments.Host);

                // Authenticate with the provided credentials
                if (!authService.Authenticate(
                    credentialsType: arguments.CredentialType,
                    sqlServer: $"{arguments.Host.Hostname},{arguments.Host.Port}",
                    database: arguments.Host.Database, username: arguments.Username,
                    password: arguments.Password,
                    domain: arguments.Domain
                 ))
                {
                    Logger.Error("Failed to authenticate with the provided credentials.");
                    return;
                }

                DatabaseContext databaseContext = new(authService);

                // If LinkedServers variable exists and has valid server names
                if (arguments.LinkedServers?.ServerNames != null && arguments.LinkedServers.ServerNames.Length > 0)
                {
                    databaseContext.QueryService.LinkedServers = arguments.LinkedServers;

                    Logger.Info($"Server chain: {arguments.Host.Hostname} -> " + string.Join(" -> ", arguments.LinkedServers.ServerNames));
                    
                    (string userName, string systemUser) = databaseContext.UserService.GetInfo();

                    Logger.Info($"Logged in on {databaseContext.QueryService.ExecutionServer} as {systemUser}");
                    Logger.InfoNested($"Mapped to the user {userName} ");
                }

                Logger.NewLine();
                Logger.Task($"Executing action '{arguments.Action.GetName()}' against {databaseContext.QueryService.ExecutionServer}");

                arguments.Action.Execute(databaseContext);

                stopwatch.Stop();
                DateTime endTime = DateTime.UtcNow;
                Logger.NewLine();
                Logger.Banner($"End at {endTime:yyyy-MM-dd HH:mm:ss:fffff} UTC\nTotal duration: {stopwatch.Elapsed.TotalSeconds:F2} seconds", totalWidth: bannerWidth);

            }
            catch (Exception ex)
            {
                Logger.Error("An unhandled exception occurred.");
                Logger.Error($"Message: {ex.Message}");
                Logger.Error($"Stack Trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    Logger.Error("Inner Exception:");
                    Logger.Error($"Message: {ex.InnerException.Message}");
                    Logger.Error($"Stack Trace: {ex.InnerException.StackTrace}");
                }

                Environment.Exit(1);
            }

        }

    }
}
