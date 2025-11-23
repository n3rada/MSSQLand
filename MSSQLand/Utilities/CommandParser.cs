using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using MSSQLand.Actions;
using MSSQLand.Models;
using MSSQLand.Services.Credentials;
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
            if (arg.StartsWith($"--{longName}:", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith($"--{longName}=", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals($"--{longName}", StringComparison.OrdinalIgnoreCase))
                return true;
            
            // Check short form: -s:value, -s=value, or -s (for space-separated)
            if (shortName != null && 
                (arg.StartsWith($"-{shortName}:", StringComparison.OrdinalIgnoreCase) ||
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

        public (ParseResultType, CommandArgs?) Parse(string[] args)
        {
            if (args.Length == 0)
            {
                Helper.Show();
                return (ParseResultType.ShowHelp, null);
            }

            CommandArgs parsedArgs = new();
            string username = null, password = null, domain = null;
            int? connectionTimeout = null;
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
                    Console.WriteLine(version);
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
                    string adDomain = args.Length > 1 
                        ? args[1] 
                        : throw new ArgumentException("FindSQLServers requires a domain argument. Example: -findsql corp.com");
                    
                    Logger.Info("FindSQLServers utility mode - no database connection required");
                    FindSQLServers.Execute(adDomain);
                    return (ParseResultType.UtilityMode, null);
                }

                // Check for global flags (-debug, -silent)
                while (currentIndex < args.Length)
                {
                    string arg = args[currentIndex];

                    if (arg == "--debug")
                    {
                        Logger.IsDebugEnabled = true;
                        currentIndex++;
                        continue;
                    }

                    if (arg == "-s" || arg == "--silent")
                    {
                        Logger.IsSilentModeEnabled = true;
                        currentIndex++;
                        continue;
                    }

                    break; // Stop when we hit a non-global flag
                }

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

                // Parse global flags until we hit the action (non-flag positional arg)
                while (currentIndex < args.Length && !actionFound)
                {
                    string arg = args[currentIndex];

                    // If it's not a flag, it's the action
                    if (!IsFlag(arg))
                    {
                        actionName = arg;
                        actionFound = true;
                        currentIndex++;
                        
                        // Check for action-specific help
                        if (currentIndex < args.Length && 
                            (args[currentIndex] == "-h" || args[currentIndex] == "--help"))
                        {
                            Helper.ShowActionHelp(actionName);
                            return (ParseResultType.ShowHelp, null);
                        }
                        break;
                    }

                    // Parse global flags
                    if (IsGlobalArgument(arg, "credentials", "c"))
                    {
                        parsedArgs.CredentialType = ExtractFlagValue(arg, args, ref currentIndex);
                    }
                    else if (IsGlobalArgument(arg, "links", "l"))
                    {
                        parsedArgs.LinkedServers = new LinkedServers(ExtractFlagValue(arg, args, ref currentIndex));
                    }
                    else if (IsGlobalArgument(arg, "output", "o"))
                    {
                        string outputFormat = ExtractFlagValue(arg, args, ref currentIndex);
                        try
                        {
                            OutputFormatter.SetFormat(outputFormat);
                        }
                        catch (ArgumentException ex)
                        {
                            var availableFormats = string.Join(", ", OutputFormatter.GetAvailableFormats());
                            throw new ArgumentException($"{ex.Message}. Available formats: {availableFormats}");
                        }
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
                    else
                    {
                        Logger.Error($"Unknown global argument: {arg}");
                        Logger.NewLine();
                        Logger.Info("Available global arguments:");
                        Logger.InfoNested("-c, --credentials: Credential type for authentication");
                        Logger.InfoNested("-l, --links: Linked server chain");
                        Logger.InfoNested("-o, --output: Output format (table, csv, json, markdown)");
                        Logger.InfoNested("--timeout: Connection timeout in seconds");
                        Logger.InfoNested("-u, --username: Username for authentication");
                        Logger.InfoNested("-p, --password: Password for authentication");
                        Logger.InfoNested("-d, --domain: Domain for authentication");
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
                    Helper.ShowAllActions();
                    return (ParseResultType.ShowHelp, null);
                }

                // Check if credential type is provided
                if (string.IsNullOrWhiteSpace(parsedArgs.CredentialType))
                {
                Logger.Error("Missing required argument: -c or --credentials.");
                Logger.NewLine();
                DataTable credentialsTable = new();
                credentialsTable.Columns.Add("Type", typeof(string));
                credentialsTable.Columns.Add("Description", typeof(string));
                credentialsTable.Columns.Add("Required Arguments", typeof(string));
                credentialsTable.Columns.Add("Optional Arguments", typeof(string));

                var credentials = CredentialsFactory.GetAvailableCredentials();
                foreach (var credential in credentials.Values)
                {
                    string requiredArgs = credential.RequiredArguments.Count > 0
                        ? string.Join(", ", credential.RequiredArguments)
                        : "None";
                    
                    string optionalArgs = credential.OptionalArguments.Count > 0
                        ? string.Join(", ", credential.OptionalArguments)
                        : "-";
                    
                    credentialsTable.Rows.Add(credential.Name, credential.Description, requiredArgs, optionalArgs);
                }                    Console.WriteLine(OutputFormatter.ConvertDataTable(credentialsTable));
                    return (ParseResultType.InvalidInput, null);
                }

                if (connectionTimeout.HasValue)
                {
                    parsedArgs.ConnectionTimeout = connectionTimeout.Value;
                }

                // Validate the provided arguments against the selected credential type
                ValidateCredentialArguments(parsedArgs.CredentialType, username, password, domain);

                // Assign optional arguments to parsedArgs
                parsedArgs.Username = username;
                parsedArgs.Password = password;
                parsedArgs.Domain = domain;

                // Get the action from the factory and pass action arguments
                parsedArgs.Action = ActionFactory.GetAction(actionName, actionArgs.ToArray());

                // Show parsed arguments only in debug mode
                if (Logger.IsDebugEnabled)
                {
                    Logger.Debug("Parsed arguments (argparse-style)");
                    Logger.DebugNested($"Host (positional): {parsedArgs.Host.Hostname}:{parsedArgs.Host.Port}");
                    Logger.DebugNested($"Credential Type: {parsedArgs.CredentialType}");
                    Logger.DebugNested($"Connection Timeout: {parsedArgs.ConnectionTimeout} seconds");
                    
                    if (!string.IsNullOrEmpty(parsedArgs.Host.Database))
                    {
                        Logger.DebugNested($"Database: {parsedArgs.Host.Database}");
                    }

                    if (parsedArgs.LinkedServers?.ServerNames != null && parsedArgs.LinkedServers.ServerNames.Length > 0)
                    {
                        Logger.DebugNested("Server Chain:");
                        foreach (var server in parsedArgs.LinkedServers.ServerNames)
                        {
                            Logger.DebugNested($"{server}", 1, "-");
                        }
                    }

                    Logger.DebugNested($"Action (positional): {parsedArgs.Action.GetName()}");
                    
                    if (actionArgs.Count > 0)
                    {
                        Logger.DebugNested("Action Arguments:");
                        for (int i = 0; i < actionArgs.Count; i++)
                        {
                            Logger.DebugNested($"[{i}] {actionArgs[i]}", 1, "-");
                        }
                    }

                    Logger.NewLine();
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
                throw new ArgumentException($"Unknown credential type: {credentialType}. Available types: {availableTypes}");
            }

            // Get the required arguments for this credential type from CredentialsFactory
            var metadata = CredentialsFactory.GetCredentialMetadata(credentialType);
            var requiredArgs = metadata.RequiredArguments;

            // Validate arguments
            if (requiredArgs.Contains("username") && string.IsNullOrEmpty(username))
            {
                throw new ArgumentException($"{credentialType} credentials require -u (username).");
            }

            if (requiredArgs.Contains("password") && string.IsNullOrEmpty(password))
            {
                throw new ArgumentException($"{credentialType} credentials require -p (password).");
            }

            if (requiredArgs.Contains("domain") && string.IsNullOrEmpty(domain))
            {
                throw new ArgumentException($"{credentialType} credentials require -d (domain).");
            }

            // Ensure no extra arguments are provided
            if (requiredArgs.Count == 0 && (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password) || !string.IsNullOrEmpty(domain)))
            {
                throw new ArgumentException($"Extra arguments provided for {credentialType} credentials, which do not require additional parameters.");
            }
        }

    }
}
