// MSSQLand/Utilities/CommandParser.cs

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using MSSQLand.Exceptions;
using MSSQLand.Models;
using MSSQLand.Services.Credentials;
using MSSQLand.Utilities.Discovery;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Utilities
{
    public class CommandParser
    {
        public enum ParseResultType
        {
            Success,        // Parsing succeeded, return valid arguments.
            ShowHelp,       // The user requested help.
            InvalidInput,   // User input is incorrect or missing required fields.
            UtilityMode     // Utility mode detected, executed separately (no database connection needed).
        }

        /// <summary>
        /// Checks if an argument is a flag (starts with -).
        /// </summary>
        private static bool IsFlag(string arg)
        {
            return arg.StartsWith("-");
        }

        /// <summary>
        /// Checks if an argument matches a global argument (by short or long name).
        /// Supports both - and -- prefixes, and : or = separators, or space-separated values.
        /// </summary>
        private static bool IsGlobalArgument(string arg, string longName, string shortName = null)
        {
            // Check long form: --longname:value, --longname=value, or --longname (for space-separated)
            if (arg.StartsWith($"--{longName}", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith($"--{longName}=", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals($"--{longName}", StringComparison.OrdinalIgnoreCase))
                return true;
            
            // Check short form: -s:value, -s=value, or -s (for space-separated)
            if (shortName != null && 
                (arg.StartsWith($"-{shortName}", StringComparison.OrdinalIgnoreCase) ||
                 arg.StartsWith($"-{shortName}=", StringComparison.OrdinalIgnoreCase) ||
                 arg.Equals($"-{shortName}", StringComparison.OrdinalIgnoreCase)))
                return true;
            
            return false;
        }

        /// <summary>
        /// Extracts value from a flag argument or returns null if value should be next arg.
        /// Supports: -flag:value, -flag=value, or null (indicating next arg is value).
        /// </summary>
        private static string ExtractFlagValue(string arg, string[] args, ref int currentIndex)
        {
            int separatorIndex = arg.IndexOfAny(new[] { ':', '=' });
            
            // Has inline separator
            if (separatorIndex > 0)
            {
                if (separatorIndex >= arg.Length - 1)
                {
                    throw new ArgumentException($"Invalid flag format: {arg}. Value cannot be empty after separator.");
                }
                return arg.Substring(separatorIndex + 1);
            }
            
            // Space-separated value: next argument should be the value
            if (currentIndex + 1 < args.Length && !IsFlag(args[currentIndex + 1]))
            {
                currentIndex++;
                return args[currentIndex];
            }
            
            throw new ArgumentException($"Flag {arg} requires a value. Use: {arg}:value, {arg}=value, or {arg} value");
        }

        /// <summary>
        /// Parses port specification: single port, range (start-end), or comma-separated list.
        /// Examples: "65184", "65180-65190", "1433,5000,65184"
        /// </summary>
        private static int[] ParsePortSpec(string spec)
        {
            try
            {
                // Range: 65180-65190
                if (spec.Contains("-") && !spec.Contains(","))
                {
                    var parts = spec.Split('-');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int start) && int.TryParse(parts[1], out int end))
                    {
                        if (start > 0 && end <= 65535 && start <= end)
                        {
                            return Enumerable.Range(start, end - start + 1).ToArray();
                        }
                    }
                    return null;
                }

                // Comma-separated list: 1433,5000,65184
                if (spec.Contains(","))
                {
                    var ports = spec.Split(',')
                        .Select(p => p.Trim())
                        .Where(p => int.TryParse(p, out int port) && port > 0 && port <= 65535)
                        .Select(p => int.Parse(p))
                        .Distinct()
                        .OrderBy(p => p)
                        .ToArray();
                    return ports.Length > 0 ? ports : null;
                }

                // Single port: 65184
                if (int.TryParse(spec, out int singlePort) && singlePort > 0 && singlePort <= 65535)
                {
                    return new[] { singlePort };
                }
            }
            catch { }

            return null;
        }

        public (ParseResultType, CommandArgs?) Parse(string[] args)
        {
            if (args.Length == 0)
            {
                Helper.ShowQuickStart();
                return (ParseResultType.ShowHelp, null);
            }

            CommandArgs parsedArgs = new();
            string username = null, password = null, domain = null;
            int? connectionTimeout = null;
            string appName = null, workstationId = null;
            int? packetSize = null;
            bool? enableEncryption = null, trustServerCertificate = null;
            string hostArg = null;
            string actionName = null;
            List<string> actionArgs = new List<string>();
            
            int currentIndex = 0;
            bool actionFound = false;

            try
            {
                // Handle special standalone commands first
                if (args[0] == "--version")
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    var version = assembly.GetName().Version;
                    Console.WriteLine(version.ToString());
                    return (ParseResultType.ShowHelp, null);
                }

                if (args[0] == "-h" || args[0] == "--help")
                {
                    // Check if next argument is a search term (not a flag)
                    if (args.Length > 1 && !IsFlag(args[1]))
                    {
                        Helper.ShowFilteredHelp(args[1]);
                        return (ParseResultType.ShowHelp, null);
                    }
                    Helper.Show();
                    return (ParseResultType.ShowHelp, null);
                }

                if (args[0] == "-findsql" || args[0] == "--findsql")
                {
                    string adDomain = null;
                    bool useGlobalCatalog = false;
                    
                    // Parse optional arguments
                    for (int i = 1; i < args.Length; i++)
                    {
                        if (args[i] == "--global-catalog" || args[i] == "-gc" || args[i] == "--gc")
                        {
                            useGlobalCatalog = true;
                        }
                        else if (!IsFlag(args[i]) && adDomain == null)
                        {
                            adDomain = args[i];
                        }
                    }
                    
                    // If no domain specified, try to get the current domain or forest root
                    if (string.IsNullOrEmpty(adDomain))
                    {
                        try
                        {
                            var currentDomain = System.DirectoryServices.ActiveDirectory.Domain.GetCurrentDomain();
                            if (useGlobalCatalog)
                            {
                                // For forest-wide search, use the forest root domain
                                adDomain = currentDomain.Forest.RootDomain.Name;
                            }
                            else
                            {
                                adDomain = currentDomain.Name;
                            }
                        }
                        catch
                        {
                            throw new ArgumentException("FindSqlServers requires a domain argument (not domain-joined). Example: -findsql corp.com [--forest]");
                        }
                    }
                    
                    FindSqlServers.Execute(adDomain, useGlobalCatalog);
                    return (ParseResultType.UtilityMode, null);
                }

                // First pass: extract --trace, --debug, --silent, and --format flags from anywhere in the arguments
                var filteredArgs = new List<string>();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--trace")
                    {
                        Logger.MinimumLogLevel = LogLevel.Trace;
                    }
                    else if (args[i] == "--debug")
                    {
                        Logger.MinimumLogLevel = LogLevel.Debug;
                    }
                    else if (args[i] == "--silent")
                    {
                        Logger.IsSilentModeEnabled = true;
                    }
                    else if (args[i].StartsWith("--output-format", StringComparison.OrdinalIgnoreCase) ||
                             args[i].StartsWith("--output=", StringComparison.OrdinalIgnoreCase) ||
                             args[i].StartsWith("--format", StringComparison.OrdinalIgnoreCase))
                    {
                        string formatValue = null;
                        int sepIndex = args[i].IndexOfAny(new[] { ':', '=' });
                        if (sepIndex > 0 && sepIndex < args[i].Length - 1)
                        {
                            formatValue = args[i].Substring(sepIndex + 1);
                        }
                        else if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        {
                            formatValue = args[++i];
                        }
                        
                        if (!string.IsNullOrEmpty(formatValue))
                        {
                            try
                            {
                                OutputFormatter.SetFormat(formatValue);
                            }
                            catch (ArgumentException ex)
                            {
                                var availableFormats = string.Join(", ", OutputFormatter.GetAvailableFormats());
                                throw new ArgumentException($"{ex.Message}. Available formats: {availableFormats}");
                            }
                        }
                    }
                    else
                    {
                        filteredArgs.Add(args[i]);
                    }
                }

                // Continue parsing with filtered arguments (without --trace, --debug and --silent)
                args = filteredArgs.ToArray();
                currentIndex = 0;

                // First positional argument: HOST (mandatory)
                if (currentIndex >= args.Length || IsFlag(args[currentIndex]))
                {
                    Logger.Error("Missing required positional argument: HOST");
                    Logger.Info("Usage: <host> [options] <action> [action-options]");
                    Logger.Info("Example: localhost -c token info");
                    return (ParseResultType.InvalidInput, null);
                }

                hostArg = args[currentIndex++];
                parsedArgs.Host = Server.ParseServer(hostArg);
                
                // Validate DNS resolution for the target server (skip for named pipes)
                IPAddress resolvedIp = null;
                if (!parsedArgs.Host.UsesNamedPipe)
                {
                    try
                    {
                        var addresses = Misc.ValidateDnsResolution(parsedArgs.Host.Hostname, throwOnFailure: true);
                        // Prefer IPv4
                        resolvedIp = addresses?.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) 
                                   ?? addresses?.First();                        parsedArgs.ResolvedIpAddress = resolvedIp;                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"DNS resolution failed: {ex.Message}");
                        return (ParseResultType.InvalidInput, null);
                    }
                }

                // Check for utility modes that work on a specific host
                if (currentIndex < args.Length)
                {
                    string nextArg = args[currentIndex];
                    
                    if (nextArg == "-browse" || nextArg == "--browse" || nextArg == "-browser" || nextArg == "--browser")
                    {
                        Logger.Info($"Querying SQL Browser service on {hostArg} (UDP 1434)");                        
                        var instances = SqlBrowser.Query(resolvedIp, hostArg);
                        SqlBrowser.LogInstances(hostArg, instances);
                        
                        return (ParseResultType.UtilityMode, null);
                    }
                    
                    if (nextArg == "-portscan" || nextArg == "--portscan")
                    {
                        // Check for port specification or flags
                        int[] customPorts = null;
                        bool scanAll = false;
                        
                        if (currentIndex + 1 < args.Length)
                        {
                            string nextVal = args[currentIndex + 1];
                            if (nextVal == "--all" || nextVal == "-a")
                            {
                                scanAll = true;
                            }
                            else if (!nextVal.StartsWith("-"))
                            {
                                // Parse port specification: single, range (49152-65535), or list (1433,5000,65184)
                                customPorts = ParsePortSpec(nextVal);
                                if (customPorts == null || customPorts.Length == 0)
                                {
                                    Logger.Error($"Invalid port specification: {nextVal}");
                                    Logger.InfoNested("Examples: 65184, 65180-65190, 1433,5000,65184");
                                    return (ParseResultType.UtilityMode, null);
                                }
                            }
                        }
                        
                        if (customPorts != null)
                        {
                            Logger.Info($"Scanning {hostArg} for SQL Server on {customPorts.Length} port(s)");
                            PortScanner.ScanPorts(resolvedIp, hostArg, customPorts);
                        }
                        else
                        {
                            Logger.Info($"Scanning {hostArg} for SQL Server ports (TDS validation)");
                            if (scanAll)
                            {
                                Logger.InfoNested("Find all instances (full ephemeral range)");
                            }
                            else
                            {
                                Logger.InfoNested("Stop on first hit (use --all to find all)");
                            }                            
                            PortScanner.Scan(resolvedIp, hostArg, stopOnFirst: !scanAll);
                        }
                        
                        return (ParseResultType.UtilityMode, null);
                    }
                }

                // Parse global flags until we hit the action (non-flag positional arg)
                while (currentIndex < args.Length && !actionFound)
                {
                    string arg = args[currentIndex];

                    // Check for help flag
                    if (arg == "-h" || arg == "--help")
                    {
                        Helper.Show();
                        return (ParseResultType.ShowHelp, null);
                    }

                    // If it's not a flag, it's the action
                    if (!IsFlag(arg))
                    {
                        actionName = arg;
                        actionFound = true;
                        currentIndex++;
                        
                        // Check for action-specific help anywhere in remaining arguments
                        for (int i = currentIndex; i < args.Length; i++)
                        {
                            if (args[i] == "-h" || args[i] == "--help")
                            {
                                Helper.ShowActionHelp(actionName);
                                return (ParseResultType.ShowHelp, null);
                            }
                        }
                        break;
                    }

                    // Parse global flags
                    if (IsGlobalArgument(arg, "credentials", "c"))
                    {
                        // Check if -c is used without a value - show available credential types
                        int separatorIndex = arg.IndexOfAny(new[] { ':', '=' });
                        bool hasInlineValue = separatorIndex > 0 && separatorIndex < arg.Length - 1;
                        bool hasNextValue = currentIndex + 1 < args.Length && !IsFlag(args[currentIndex + 1]);
                        
                        if (!hasInlineValue && !hasNextValue)
                        {
                            Helper.ShowCredentialTypes();
                            return (ParseResultType.ShowHelp, null);
                        }
                        
                        parsedArgs.CredentialType = ExtractFlagValue(arg, args, ref currentIndex);
                    }
                    else if (IsGlobalArgument(arg, "links", "l"))
                    {
                        parsedArgs.LinkedServers = new LinkedServers(ExtractFlagValue(arg, args, ref currentIndex));
                    }
                    else if (IsGlobalArgument(arg, "timeout", null))
                    {
                        if (connectionTimeout.HasValue)
                        {
                            Logger.Warning($"--timeout specified multiple times. Using last value.");
                        }
                        string timeoutValue = ExtractFlagValue(arg, args, ref currentIndex);
                        if (!int.TryParse(timeoutValue, out int parsedTimeout) || parsedTimeout <= 0)
                        {
                            throw new ArgumentException($"Invalid timeout value: {timeoutValue}. Timeout must be a positive integer (seconds).");
                        }
                        connectionTimeout = parsedTimeout;
                    }
                    else if (IsGlobalArgument(arg, "username", "u"))
                    {
                        if (username != null)
                        {
                            Logger.Warning($"-u/--username specified multiple times. Using last value.");
                        }
                        username = ExtractFlagValue(arg, args, ref currentIndex);
                    }
                    else if (IsGlobalArgument(arg, "password", "p"))
                    {
                        if (password != null)
                        {
                            Logger.Warning($"-p/--password specified multiple times. Using last value.");
                        }
                        password = ExtractFlagValue(arg, args, ref currentIndex);
                    }
                    else if (IsGlobalArgument(arg, "domain", "d"))
                    {
                        if (domain != null)
                        {
                            Logger.Warning($"-d/--domain specified multiple times. Using last value.");
                        }
                        domain = ExtractFlagValue(arg, args, ref currentIndex);
                    }
                    else if (IsGlobalArgument(arg, "app-name", null))
                    {
                        if (appName != null)
                        {
                            Logger.Warning($"--app-name specified multiple times. Using last value.");
                        }
                        appName = ExtractFlagValue(arg, args, ref currentIndex);
                    }
                    else if (IsGlobalArgument(arg, "workstation-id", null))
                    {
                        if (workstationId != null)
                        {
                            Logger.Warning($"--workstation-id specified multiple times. Using last value.");
                        }
                        workstationId = ExtractFlagValue(arg, args, ref currentIndex);
                    }
                    else if (IsGlobalArgument(arg, "packet-size", null))
                    {
                        if (packetSize.HasValue)
                        {
                            Logger.Warning($"--packet-size specified multiple times. Using last value.");
                        }
                        string packetSizeValue = ExtractFlagValue(arg, args, ref currentIndex);
                        if (!int.TryParse(packetSizeValue, out int parsedPacketSize) || parsedPacketSize <= 0)
                        {
                            throw new ArgumentException($"Invalid packet-size value: {packetSizeValue}. Must be a positive integer (bytes).");
                        }
                        packetSize = parsedPacketSize;
                    }
                    else if (arg == "--no-encrypt" || arg == "--disable-encrypt")
                    {
                        enableEncryption = false;
                    }
                    else if (arg == "--no-trust-cert" || arg == "--disable-trust-cert")
                    {
                        trustServerCertificate = false;
                    }
                    else
                    {
                        Logger.Error($"Unknown global argument: {arg}");
                        Logger.Info("Available global arguments");
                        Logger.InfoNested("-c, --credentials: Credential type for authentication");
                        Logger.InfoNested("-l, --links: Linked server chain");
                        Logger.InfoNested("--timeout: Connection timeout in seconds");
                        Logger.InfoNested("-u, --username: Username for authentication");
                        Logger.InfoNested("-p, --password: Password for authentication");
                        Logger.InfoNested("-d, --domain: Domain for authentication");
                        Logger.InfoNested("--app-name: SQL connection application name (default: DataFactory)");
                        Logger.InfoNested("--workstation-id: SQL connection workstation ID (default: datafactory-runX)");
                        Logger.InfoNested("--packet-size: Network packet size in bytes (default: 8192)");
                        Logger.InfoNested("--no-encrypt: Disable connection encryption");
                        Logger.InfoNested("--no-trust-cert: Disable server certificate trust");
                        Logger.Info("Available discovery arguments (no database connection)");
                        Logger.InfoNested("-findsql [domain] [--global-catalog|--gc]: Find SQL Servers via AD SPNs");
                        Logger.InfoNested("-browse: Query SQL Browser service for instances/ports");
                        Logger.InfoNested("-portscan [--all]: Scan for SQL Server ports with TDS validation");
                        throw new ArgumentException($"Unknown global argument: {arg}");
                    }

                    currentIndex++;
                }

                // Collect remaining arguments as action arguments
                while (currentIndex < args.Length)
                {
                    actionArgs.Add(args[currentIndex++]);
                }

                // Check if action was provided
                if (string.IsNullOrWhiteSpace(actionName))
                {
                    // Connection test mode
                    parsedArgs.Action = null;
                }

                // Check if credential type is provided
                if (string.IsNullOrWhiteSpace(parsedArgs.CredentialType))
                {
                    // No credentials specified - use probe mode to just test connectivity
                    parsedArgs.CredentialType = "probe";
                }

                if (connectionTimeout.HasValue)
                {
                    parsedArgs.ConnectionTimeout = connectionTimeout.Value;
                }

                // Assign connection string customization parameters
                parsedArgs.AppName = appName;
                parsedArgs.WorkstationId = workstationId;
                if (packetSize.HasValue)
                {
                    parsedArgs.PacketSize = packetSize.Value;
                }
                
                // Assign connection string boolean overrides
                parsedArgs.EnableEncryption = enableEncryption;
                parsedArgs.TrustServerCertificate = trustServerCertificate;

                // Validate the provided arguments against the selected credential type
                ValidateCredentialArguments(parsedArgs.CredentialType, username, password, domain);

                // Assign optional arguments to parsedArgs
                parsedArgs.Username = username;
                parsedArgs.Password = password;
                parsedArgs.Domain = domain;

                // Get the action from the factory and pass action arguments (only if action was specified)
                if (!string.IsNullOrWhiteSpace(actionName))
                {
                    try
                    {
                        parsedArgs.Action = ActionFactory.GetAction(actionName, actionArgs.ToArray());
                    }
                    catch (ActionNotFoundException ex)
                    {
                        // Try to find actions that start with the given name
                        var matches = ActionFactory.GetActionsByPrefix(ex.ActionName);
                        
                        if (matches.Count > 0)
                        {
                            Logger.Error($"Action '{ex.ActionName}' not found. Did you mean one of these?");
                            
                            DataTable matchTable = new();
                            matchTable.Columns.Add("Action", typeof(string));
                            matchTable.Columns.Add("Description", typeof(string));
                            
                            foreach (var match in matches)
                            {
                                matchTable.Rows.Add(match.ActionName, match.Description);
                            }
                            
                            Console.WriteLine(OutputFormatter.ConvertDataTable(matchTable));
                        }
                        else
                        {
                            Logger.Error($"Action '{ex.ActionName}' not found.");
                            Logger.ErrorNested("Use -h or --help to see all available actions.");
                        }
                        
                        return (ParseResultType.InvalidInput, null);
                    }
                }

                return (ParseResultType.Success, parsedArgs);
            }
            catch (Exception ex)
            {
                Logger.Error($"Parsing error: {ex.Message}");
                return (ParseResultType.InvalidInput, null);
            }
        }

        private void ValidateCredentialArguments(string credentialType, string username, string password, string domain)
        {
            if (string.IsNullOrEmpty(credentialType))
            {
                throw new ArgumentException("Credential type (-c or --credentials) is required.");
            }

            // Check if credential type exists using CredentialsFactory
            if (!CredentialsFactory.IsValidCredentialType(credentialType))
            {
                Logger.Error($"Unknown credential type: {credentialType}");
                Logger.NewLine();
                
                DataTable credentialsTable = new();
                credentialsTable.Columns.Add("Type", typeof(string));
                credentialsTable.Columns.Add("Description", typeof(string));
                credentialsTable.Columns.Add("Required Arguments", typeof(string));
                credentialsTable.Columns.Add("Optional Arguments", typeof(string));

                var credentials = CredentialsFactory.GetAvailableCredentials();
                foreach (var credential in credentials.Values)
                {
                    string requiredArgsDisplay = credential.RequiredArguments.Count > 0
                        ? string.Join(", ", credential.RequiredArguments)
                        : "None";
                    
                    string optionalArgsDisplay = credential.OptionalArguments.Count > 0
                        ? string.Join(", ", credential.OptionalArguments)
                        : "-";
                    
                    credentialsTable.Rows.Add(credential.Name, credential.Description, requiredArgsDisplay, optionalArgsDisplay);
                }
                
                Console.WriteLine(OutputFormatter.ConvertDataTable(credentialsTable));
                
                var availableTypes = string.Join(", ", CredentialsFactory.GetCredentialTypeNames());
                throw new InvalidCredentialException(credentialType, $"Unknown credential type: {credentialType}. Available types: {availableTypes}");
            }

            // Get the required arguments for this credential type from CredentialsFactory
            var metadata = CredentialsFactory.GetCredentialMetadata(credentialType);
            var requiredArgs = metadata.RequiredArguments;

            // Validate arguments
            if (requiredArgs.Contains("username") && string.IsNullOrEmpty(username))
            {
                throw new MissingRequiredArgumentException("username", $"{credentialType} credentials");
            }

            if (requiredArgs.Contains("password") && string.IsNullOrEmpty(password))
            {
                throw new MissingRequiredArgumentException("password", $"{credentialType} credentials");
            }

            if (requiredArgs.Contains("domain") && string.IsNullOrEmpty(domain))
            {
                throw new MissingRequiredArgumentException("domain", $"{credentialType} credentials");
            }

            // Ensure no extra arguments are provided
            if (requiredArgs.Count == 0 && (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password) || !string.IsNullOrEmpty(domain)))
            {
                throw new ArgumentException($"Extra arguments provided for {credentialType} credentials, which do not require additional parameters.");
            }
        }

    }
}
