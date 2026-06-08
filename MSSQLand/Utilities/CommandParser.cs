// MSSQLand/Utilities/CommandParser.cs

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;

using MSSQLand.Actions;
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
        /// Boolean-only flags that consume no following value.
        /// Used by FindEarlyActionName to skip flag values correctly.
        /// </summary>
        private static readonly HashSet<string> BooleanFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--probe", "--trace", "--debug", "--silent", "--no-banner",
            "--no-encrypt", "--disable-encrypt", "--no-trust-cert", "--disable-trust-cert"
        };

        /// <summary>
        /// Checks if an argument matches a global argument (by short or long name).
        /// Supports both - and -- prefixes, and : or = separators, or space-separated values.
        /// </summary>
        private static bool IsGlobalArgument(string arg, string longName, string shortName = null)
        {
            // Check long form: --longname:value, --longname=value, or --longname (for space-separated)
            string longFlag = $"--{longName}";
            if (arg.Equals(longFlag, StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith($"{longFlag}:", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith($"{longFlag}=", StringComparison.OrdinalIgnoreCase))
                return true;

            // Check short form: -s:value, -s=value, or -s (for space-separated)
            if (shortName != null)
            {
                string shortFlag = $"-{shortName}";
                if (arg.Equals(shortFlag, StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith($"{shortFlag}:", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith($"{shortFlag}=", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

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
            // Scan for display flags and early-exit flags before any work.
            bool helpRequested = false;
            foreach (string arg in args)
            {
                if (arg == "--no-banner") Logger.IsBannerSuppressed = true;
                else if (arg == "--silent") Logger.IsSilentModeEnabled = true;
                else if (arg == "-h" || arg == "--help") helpRequested = true;
            }

            if (!Logger.IsBannerSuppressed && !Logger.IsSilentModeEnabled)
                Banner.Show();

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

            try
            {
                // Short-circuit for help before any parsing work (avoids JIT overhead on host/links parsing).
                if (helpRequested)
                {
                    string earlyAction = FindEarlyActionName(args);
                    if (earlyAction != null)
                    {
                        Helper.ShowActionHelp(earlyAction);
                        return (ParseResultType.ShowHelp, null);
                    }

                    for (int i = 0; i < args.Length - 1; i++)
                    {
                        if ((args[i] == "-h" || args[i] == "--help") && !IsFlag(args[i + 1]))
                        {
                            Helper.ShowFilteredHelp(args[i + 1]);
                            return (ParseResultType.ShowHelp, null);
                        }
                    }

                    Helper.Show();
                    return (ParseResultType.ShowHelp, null);
                }

                if (args[0] == "--findsql")
                {
                    string adDomain = null;
                    bool useGlobalCatalog = false;

                    // Parse optional arguments
                    for (int i = 1; i < args.Length; i++)
                    {
                        if (args[i] == "--global-catalog" || args[i] == "--gc")
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
                        catch (Exception ex)
                        {
                            throw new ArgumentException($"Failed to auto-detect domain: {ex.Message}.");

                        }
                    }

                    FindSqlServers.Execute(adDomain, useGlobalCatalog);
                    return (ParseResultType.UtilityMode, null);
                }

                if (args[0] == "--broadcast")
                {
                    int timeoutMs = 3000;

                    // Parse optional arguments
                    for (int i = 1; i < args.Length; i++)
                    {
                        if (args[i] == "--timeout" || args[i] == "-t")
                        {
                            if (i + 1 < args.Length && int.TryParse(args[i + 1], out int t) && t > 0)
                            {
                                timeoutMs = t * 1000;
                                i++;
                            }
                        }
                    }

                    SqlBrowser.Broadcast(timeoutMs);
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
                    else if (args[i] == "--no-banner")
                    {
                        Logger.IsBannerSuppressed = true;
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

                // Continue parsing with filtered arguments
                args = filteredArgs.ToArray();
                currentIndex = 0;

                // First positional argument: HOST (mandatory)
                if (currentIndex >= args.Length || IsFlag(args[currentIndex]))
                {
                    Logger.Error("Missing Host positional argument.");
                    Logger.ErrorNested("Usage: <host> [options] <action> [action-options]");
                    return (ParseResultType.InvalidInput, null);
                }

                hostArg = args[currentIndex++];
                parsedArgs.Host = Server.ParseServer(hostArg);

                // DNS resolution is deferred to SqlConnection for normal auth
                // Only resolve for utility modes that need the IP address upfront
                IPAddress resolvedIp = null;

                // Check for utility modes that work on a specific host
                if (currentIndex < args.Length)
                {
                    string nextArg = args[currentIndex];

                    if (nextArg == "--browse" || nextArg == "--browser")
                    {
                        // Resolve DNS for utility mode
                        if (!parsedArgs.Host.UsesNamedPipe)
                        {
                            try
                            {
                                resolvedIp = ResolveDnsIfNeeded(parsedArgs.Host.Hostname);
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"DNS resolution failed: {ex.Message}");
                                return (ParseResultType.InvalidInput, null);
                            }
                        }

                        Logger.Task($"Querying SQL Browser service on {hostArg} (UDP 1434)");
                        var instances = SqlBrowser.Query(resolvedIp, hostArg);
                        SqlBrowser.LogInstances(hostArg, instances);

                        return (ParseResultType.UtilityMode, null);
                    }

                    if (nextArg == "--portscan")
                    {
                        // Resolve DNS for utility mode
                        if (!parsedArgs.Host.UsesNamedPipe)
                        {
                            try
                            {
                                resolvedIp = ResolveDnsIfNeeded(parsedArgs.Host.Hostname);
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"DNS resolution failed: {ex.Message}");
                                return (ParseResultType.InvalidInput, null);
                            }
                        }

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
                                    Logger.ErrorNested("Examples: 65184, 65180-65190, 1433,5000,65184");
                                    return (ParseResultType.UtilityMode, null);
                                }
                            }
                        }

                        if (customPorts != null)
                        {
                            Logger.Task($"Scanning {hostArg} for SQL Server on {customPorts.Length} port(s)");
                            PortScanner.ScanPorts(resolvedIp, hostArg, customPorts);
                        }
                        else
                        {
                            Logger.Task($"Scanning {hostArg} for SQL Server ports (TDS validation)");
                            if (scanAll)
                            {
                                Logger.TaskNested("Find all instances (full ephemeral range)");
                            }
                            else
                            {
                                Logger.TaskNested("Stop on first hit (use --all to find all)");
                            }
                            PortScanner.Scan(resolvedIp, hostArg, stopOnFirst: !scanAll);
                        }

                        return (ParseResultType.UtilityMode, null);
                    }
                }

                // Parse global flags until we hit the action (non-flag positional arg)
                while (currentIndex < args.Length)
                {
                    string arg = args[currentIndex];

                    // If it's not a flag, it's the action
                    if (!IsFlag(arg))
                    {
                        actionName = arg;
                        currentIndex++;
                        break;
                    }

                    if (arg.Equals("--probe", StringComparison.OrdinalIgnoreCase))
                    {
                        parsedArgs.CredentialType = "probe";
                        currentIndex++;
                        continue;
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
                        Logger.ErrorNested("Use -h or --help to see available options");
                        return (ParseResultType.InvalidInput, null);
                    }

                    currentIndex++;
                }

                // Collect remaining arguments as action arguments
                while (currentIndex < args.Length)
                {
                    actionArgs.Add(args[currentIndex++]);
                }

                // No credentials specified — default to probe (connectivity test only)
                if (string.IsNullOrWhiteSpace(parsedArgs.CredentialType))
                {
                    parsedArgs.CredentialType = "probe";
                }

                // Probe is connectivity-check only; actions and linked server traversal both require real auth
                if (parsedArgs.CredentialType.Equals("probe", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(actionName))
                    {
                        Logger.Error($"Action '{actionName}' cannot be used with probe mode.");
                        Logger.ErrorNested("Probe only tests connectivity. Use a real credential type (e.g., -c token) to execute actions.");
                        return (ParseResultType.InvalidInput, null);
                    }

                    if (parsedArgs.LinkedServers != null && !parsedArgs.LinkedServers.IsEmpty)
                    {
                        Logger.Error("Linked server chains cannot be used with probe mode.");
                        Logger.ErrorNested("Probe only tests connectivity to the target host. Use a real credential type (e.g., -c token) to traverse linked servers.");
                        return (ParseResultType.InvalidInput, null);
                    }
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

                // Parse embedded domain from username (DOMAIN\user or user@domain)
                if (!string.IsNullOrEmpty(username))
                {
                    var (parsedUsername, embeddedDomain) = NetworkHelper.ParseUsernameWithDomain(username);
                    if (embeddedDomain != null)
                    {
                        if (!string.IsNullOrEmpty(domain))
                        {
                            // Explicit -d takes precedence, warn about ignored embedded domain
                            Logger.Warning($"Domain '{embeddedDomain}' in username ignored. Using explicit -d value: {domain}");
                        }
                        else
                        {
                            domain = embeddedDomain;
                            Logger.Debug($"Parsed domain '{domain}' from username");
                        }
                        username = parsedUsername;
                    }
                }

                // Probe is a mode, not a credential type — skip credential validation
                if (parsedArgs.CredentialType.Equals("probe", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password) || !string.IsNullOrEmpty(domain))
                        Logger.Warning("Probe mode does not use credentials. -u/-p/-d flags are ignored.");
                }
                else
                {
                    ValidateCredentialArguments(parsedArgs.CredentialType, username, password, domain);
                }

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
                            Logger.ErrorNested("Use -h actions to see all available actions.");
                        }

                        return (ParseResultType.InvalidInput, null);
                    }
                }

                return (ParseResultType.Success, parsedArgs);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error parsing command line arguments: {ex.Message}");
                return (ParseResultType.InvalidInput, null);
            }
        }

        /// <summary>
        /// Resolves DNS for a hostname, with special handling for localhost/loopback addresses.
        /// Returns the resolved IP address.
        /// </summary>
        private static IPAddress ResolveDnsIfNeeded(string hostname)
        {
            // Skip DNS resolution for localhost/loopback addresses
            if (hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                hostname.Equals("127.0.0.1") ||
                hostname.Equals("::1"))
            {
                Logger.Trace($"Using loopback address: {hostname}");
                return hostname.Equals("::1")
                    ? IPAddress.IPv6Loopback
                    : IPAddress.Loopback;
            }

            // Resolve hostname to IP
            var addresses = NetworkHelper.ValidateDnsResolution(hostname, throwOnFailure: true);

            // Prefer IPv4
            var resolvedIp = addresses?.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                           ?? addresses?.First();

            Logger.Trace($"Resolved {hostname} to {resolvedIp}");
            return resolvedIp;
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

                throw new InvalidCredentialException(credentialType, $"Unknown credential type '{credentialType}'.");
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

        /// <summary>
        /// Scans <paramref name="args"/> to find the action name that appears before the first
        /// <c>-h</c> / <c>--help</c> flag. Flag values are skipped so they are not mistaken for positional args.
        ///
        /// This supports both of these forms:
        ///   <action> -h
        ///   <host> <action> -h
        /// </summary>
        private static string FindEarlyActionName(string[] args)
        {
            int positionals = 0;
            string lastPositional = null;
            bool skipNext = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg == "-h" || arg == "--help") break;

                if (skipNext) { skipNext = false; continue; }

                if (IsFlag(arg))
                {
                    bool hasInlineValue = arg.IndexOfAny(new[] { ':', '=' }) > 0;
                    if (!hasInlineValue && !BooleanFlags.Contains(arg))
                        skipNext = true;
                }
                else
                {
                    positionals++;
                    lastPositional = arg;
                    if (positionals == 2)
                        return arg; // host = 1st, action = 2nd
                }
            }

            if (positionals == 1 && lastPositional != null)
            {
                // Allow action-specific help without a host, but only if the lone positional is a known action.
                return ActionFactory.GetActionType(lastPositional) != null ? lastPositional : null;
            }

            return null;
        }

    }
}
