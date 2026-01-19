using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Text;
using MSSQLand.Models;
using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Exceptions;


namespace MSSQLand
{
    internal class Program
    {
        static int Main(string[] args)
        {
            // Force UTF-8 output encoding for consistent cross-platform handling
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            // Parse command-line arguments
            CommandArgs arguments;
            try
            {
                CommandParser parser = new();
                (CommandParser.ParseResultType result, CommandArgs parsedArgs) = parser.Parse(args);

                switch (result)
                {
                    case CommandParser.ParseResultType.ShowHelp:
                        return 0;
                    case CommandParser.ParseResultType.InvalidInput:
                        return 1;
                    case CommandParser.ParseResultType.UtilityMode:
                        return 0;
                }

                if (parsedArgs == null || parsedArgs.Host == null)
                {
                    Logger.Error("Invalid command arguments.");
                    return 1;
                }

                arguments = parsedArgs;
            }
            catch (Exception ex)
            {
                Logger.Error($"Parsing error: {ex.Message}");
                return 1;
            }

            // Authenticate
            AuthenticationService authService;
            string connectionTarget = arguments.Host.GetConnectionTarget();
            try
            {
                authService = new AuthenticationService(arguments.Host);
                authService.Authenticate(
                    credentialsType: arguments.CredentialType,
                    sqlServer: connectionTarget,
                    database: arguments.Host.Database,
                    username: arguments.Username,
                    password: arguments.Password,
                    domain: arguments.Domain,
                    connectionTimeout: arguments.ConnectionTimeout,
                    appName: arguments.AppName,
                    workstationId: arguments.WorkstationId,
                    packetSize: arguments.PacketSize,
                    enableEncryption: arguments.EnableEncryption,
                    trustServerCertificate: arguments.TrustServerCertificate
                );

                if (authService.Connection == null)
                {
                    return 1;
                }
            }
            catch (AuthenticationFailedException ex)
            {
                Logger.Error($"Authentication failed: {ex.Message}");
                return 1;
            }
            catch (SqlException sqlEx) when (sqlEx.Number == -2 || sqlEx.Number == -1)
            {
                Logger.Error($"Connection timeout to {connectionTarget}");
                Logger.ErrorNested($"Server did not respond within {arguments.ConnectionTimeout} seconds.");
                return 1;
            }
            catch (SqlException sqlEx)
            {
                Logger.Error($"Connection error: {sqlEx.Message}");
                return 1;
            }

            // Main execution
            using (authService)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                DateTime startTime = DateTime.UtcNow;
                int bannerWidth = 0;
                bool executionStarted = false;

                try
                {
                    // Get time zone details
                    TimeZoneInfo localTimeZone = TimeZoneInfo.Local;
                    TimeSpan offset = localTimeZone.BaseUtcOffset;
                    string formattedOffset = $"{(offset.Hours >= 0 ? "+" : "-")}{Math.Abs(offset.Hours)}:{Math.Abs(offset.Minutes):D2}";

                    // Build target info
                    string targetInfo = arguments.Host.Hostname;
                    if (arguments.ResolvedIpAddress != null)
                    {
                        targetInfo = $"{arguments.Host.Hostname} ({arguments.ResolvedIpAddress})";
                    }

                    // Pre-calculate all banner content to determine max width
                    string headerContent = $"From: {Environment.MachineName}\n{DateTime.Now:HH:mm:ss} UTC{formattedOffset} ({localTimeZone.Id})\nTo: {targetInfo}";
                    string startContent = $"Start at {startTime:yyyy-MM-dd HH:mm:ss:fffff} UTC";
                    
                    // Find the longest line across all banners
                    int maxLineLength = 0;
                    foreach (string line in headerContent.Split('\n'))
                    {
                        if (line.Length > maxLineLength) maxLineLength = line.Length;
                    }
                    if (startContent.Length > maxLineLength) maxLineLength = startContent.Length;
                    
                    // Add padding (4 spaces on each side) + 2 for border chars
                    bannerWidth = maxLineLength + 8;

                    Logger.NewLine();
                    Logger.Banner(headerContent, totalWidth: bannerWidth);
                    Logger.NewLine();

                    Logger.Banner(startContent, totalWidth: bannerWidth);
                    Logger.NewLine();
                    
                    executionStarted = true;

                    // Log connection information
                    SqlConnection connection = authService.Connection;
                    Logger.Success($"Connection opened successfully");
                    Logger.SuccessNested($"Server: {connection.DataSource}");
                    Logger.SuccessNested($"Database: {connection.Database}");
                    Logger.SuccessNested($"Server Version: {connection.ServerVersion}");
                    Logger.SuccessNested($"Client Workstation ID: {authService.Credentials.WorkstationId}");
                    Logger.SuccessNested($"Client Application Name: {authService.Credentials.AppName}");
                    Logger.SuccessNested($"Client Connection ID: {connection.ClientConnectionId}");
                    Logger.NewLine();

                    if (authService.Server.IsLegacy)
                    {
                        Logger.Warning("Connected to a legacy SQL Server version (2016 or earlier).");
                        Logger.NewLine();
                    }

                    DatabaseContext databaseContext = new(authService);

                    string userName, systemUser;
                    (userName, systemUser) = databaseContext.UserService.GetInfo();
                    databaseContext.Server.MappedUser = userName;
                    databaseContext.Server.SystemUser = systemUser;

                    Logger.Info($"Logged in on {databaseContext.Server.Hostname} as {systemUser}");
                    Logger.InfoNested($"Mapped to the user {userName}");

                    // Compute effective user (domain users only)
                    if (databaseContext.UserService.IsDomainUser)
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
                        else
                        {
                            Logger.InfoNested($"Domain user '{systemUser}' is directly mapped");
                        }
                    }

                    // Handle linked servers
                    if (arguments.LinkedServers?.ServerNames != null && arguments.LinkedServers.ServerNames.Length > 0)
                    {
                        databaseContext.QueryService.LinkedServers = arguments.LinkedServers;

                        // Get display chain (shows alias [actual] when they differ)
                        var chainParts = databaseContext.QueryService.LinkedServers.GetDisplayChainParts();

                        Logger.NewLine();
                        Logger.Info($"Server chain: {arguments.Host.Hostname} -> " + string.Join(" -> ", chainParts));
                        Logger.NewLine();

                        (userName, systemUser) = databaseContext.UserService.GetInfo();

                        Logger.Info($"Logged in on {databaseContext.QueryService.ExecutionServer.Hostname} as {systemUser}");
                        Logger.InfoNested($"Mapped to the user {userName}");
                        Logger.InfoNested($"Execution database: {databaseContext.QueryService.ExecutionServer.Database}");

                        if (databaseContext.QueryService.ExecutionServer.IsLegacy)
                        {
                            Logger.NewLine();
                            Logger.Warning($"Execution server '{databaseContext.QueryService.ExecutionServer.Hostname}' is running legacy SQL Server (version {databaseContext.QueryService.ExecutionServer.MajorVersion}).");
                        }
                    }

                    databaseContext.QueryService.IsAzureSQL();

                    // Execute action
                    if (arguments.Action != null)
                    {
                        Logger.NewLine();
                        Logger.Task($"Executing action '{arguments.Action.GetName()}' against {databaseContext.QueryService.ExecutionServer.Hostname}");
                        arguments.Action.Execute(databaseContext);
                    }
                    else
                    {
                        Logger.NewLine();
                        Logger.Success("Connection test successful. No action specified.");
                    }

                    return 0;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Execution error: {ex.Message}");
                    Logger.Trace($"Stack Trace:\n{ex.StackTrace}");

                    if (ex.InnerException != null)
                    {
                        Logger.Error("Inner Exception");
                        Logger.ErrorNested($"Message: {ex.InnerException.Message}");
                        Logger.Trace($"Stack Trace:\n{ex.InnerException.StackTrace}");
                    }

                    return 1;
                }
                finally
                {
                    if (executionStarted)
                    {
                        stopwatch.Stop();
                        DateTime endTime = DateTime.UtcNow;
                        Logger.NewLine();
                        Logger.Banner($"End at {endTime:yyyy-MM-dd HH:mm:ss:fffff} UTC\nTotal duration: {stopwatch.Elapsed.TotalSeconds:F2} seconds", totalWidth: bannerWidth);
                    }
                }
            }
        }
    }
}
