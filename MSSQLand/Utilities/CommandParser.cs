// MSSQLand/Utilities/CommandParser.cs

using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using MSSQLand.Actions;
using MSSQLand.Exceptions;
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
                    string adDomain = args.Length > 1 
                        ? args[1] 
                        : throw new ArgumentException("FindSQLServers requires a domain argument. Example: -findsql corp.com");
                    
                    Logger.Info("FindSQLServers utility mode - no database connection required");
                    FindSQLServers.Execute(adDomain);
                    return (ParseResultType.UtilityMode, null);
                }

                // First pass: extract --trace, --debug and --silent flags from anywhere in the arguments
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
                    else if (args[i] == "-s" || args[i] == "--silent")
                    {
                        Logger.IsSilentModeEnabled = true;
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

                // Parse global flags until we hit the action (non-flag positional arg)
                while (currentIndex < args.Length && !actionFound)
                {
                    string arg = args[currentIndex];

                    // Check for help flag
                    if (arg == "-h" || arg == "--help")
                    {
                        Helper.ShowAllActions();
                        return (ParseResultType.ShowHelp, null);
                    }

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
                    else if (IsGlobalArgument(arg, "output-format", "o"))
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
                        Logger.Info("Available global arguments:");
                        Logger.InfoNested("-c, --credentials: Credential type for authentication");
                        Logger.InfoNested("-l, --links: Linked server chain");
                        Logger.InfoNested("-o, --output-format: Output format (table, csv, json, markdown)");
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
                    // Connection test mode
                    parsedArgs.Action = null;
                }

                // Check if credential type is provided
                if (string.IsNullOrWhiteSpace(parsedArgs.CredentialType))
                {
                Logger.Error("Missing required argument: -c or --credentials.");
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
                            Logger.NewLine();
                            
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
