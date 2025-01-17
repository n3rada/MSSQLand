using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MSSQLand.Models;
using MSSQLand.Services;
using MSSQLand.Utilities;


namespace MSSQLand
{
    [ComVisible(true)]
    internal class Program
    {
        private static readonly string BuildTimestamp = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
        
        [STAThread]
        static void Main(string[] args)
        {

            Stopwatch stopwatch = Stopwatch.StartNew();
            DateTime startTime = DateTime.UtcNow;

            try
            {
                CommandParser parser = new();

                CommandArgs parsedArgs = parser.Parse(args);

                Logger.Info($"Build timestamp: {BuildTimestamp}");
                Logger.NewLine();

                int bannerWidth = Logger.Banner($"Start at {startTime:yyyy-MM-dd HH:mm:ss} UTC");
                

                using AuthenticationService authService = new(parsedArgs.Target);

                // Authenticate with the provided credentials
                if (!authService.Authenticate(
                    credentialsType: parsedArgs.CredentialType,
                    sqlServer: $"{parsedArgs.Target.Hostname},{parsedArgs.Target.Port}",
                    database: parsedArgs.Target.Database, username: parsedArgs.Username,
                    password: parsedArgs.Password,
                    domain: parsedArgs.Domain
                 ))
                {
                    Logger.Error("Failed to authenticate with the provided credentials.");
                    return;
                }

                DatabaseContext databaseContext = new(authService);

                // If LinkedServers variable exists and has valid server names
                if (parsedArgs.LinkedServers?.ServerNames != null && parsedArgs.LinkedServers.ServerNames.Length > 0)
                {
                    databaseContext.QueryService.LinkedServers = parsedArgs.LinkedServers;

                    Logger.Info($"Server chain: {parsedArgs.Target.Hostname} -> " + string.Join(" -> ", parsedArgs.LinkedServers.ServerNames));
                }

                databaseContext.UserService.GetInfo();

                Logger.Task($"Executing action: {parsedArgs.Action.GetName()}");

                parsedArgs.Action.Execute(databaseContext);

                stopwatch.Stop();
                DateTime endTime = DateTime.UtcNow;
                Logger.Banner($"End at {endTime:yyyy-MM-dd HH:mm:ss} UTC\nTotal duration: {stopwatch.Elapsed.TotalSeconds:F2} seconds", totalWidth: bannerWidth);

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
