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
        [STAThread]
        static void Main(string[] args)
        {

            var stopwatch = Stopwatch.StartNew();
            var startTime = DateTime.Now;

            try
            {
                CommandParser parser = new();

                CommandArgs parsedArgs = parser.Parse(args);

                Logger.Banner($"Start at {startTime:yyyy-MM-dd HH:mm:ss}");
                

                using AuthenticationService authService = new(parsedArgs.Target);

                // Authenticate with the provided credentials
                if (!authService.Authenticate(
                    credentialsType: parsedArgs.CredentialType,
                    sqlServer: $"{parsedArgs.Target.Hostname},{parsedArgs.Target.Port}",
                    database: "master", username: parsedArgs.Username,
                    password: parsedArgs.Password,
                    domain: parsedArgs.Domain
                 ))
                {
                    Logger.Error("Failed to authenticate with the provided credentials.");
                    return;
                }

                DatabaseContext connectionManager = new(authService);

                // If LinkedServers variable exists and has valid server names
                if (parsedArgs.LinkedServers?.ServerNames != null && parsedArgs.LinkedServers.ServerNames.Length > 0)
                {
                    connectionManager.QueryService.LinkedServers = parsedArgs.LinkedServers;

                    Logger.Info($"Server chain: {parsedArgs.Target.Hostname} -> " + string.Join(" -> ", parsedArgs.LinkedServers.ServerNames));

                    connectionManager.UserService.GetInfo();
                }

                Logger.Task($"Executing action: {parsedArgs.Action.GetName()}");

                parsedArgs.Action.Execute(connectionManager);

                stopwatch.Stop();
                var endTime = DateTime.Now;
                Logger.Banner($"End at {endTime:yyyy-MM-dd HH:mm:ss}\nTotal duration: {stopwatch.Elapsed.TotalSeconds:F2} seconds");

            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
                Logger.Debug($"Stack Trace:\n{ex.StackTrace}");
            }
        }

    }
}
