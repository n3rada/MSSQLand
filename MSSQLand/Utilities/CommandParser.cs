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
            ShowHelp,       // The user requested help (/help or /printHelp).
            InvalidInput,   // User input is incorrect or missing required fields.
            UtilityMode     // Utility mode detected, executed separately (no database connection needed).
        }

        public const string AdditionalArgumentsSeparator = "/|/";
        
        // Compiled regex patterns for better performance
        private static readonly Regex TrailingSeparatorRegex = new Regex($"{Regex.Escape(AdditionalArgumentsSeparator)}$", RegexOptions.Compiled);

        // Global arguments dictionary: Key = long name, Value = (short name, description)
        private static readonly Dictionary<string, (string Short, string Description)> GlobalArguments = new()
        {
            { "credentials", ("c", "Credential type for authentication") },
            { "host", ("h", "Target SQL Server hostname") },
            { "links", ("l", "Linked server chain") },
            { "output", ("o", "Output format (table, csv, json, markdown)") },
            { "timeout", (null, "Connection timeout in seconds") },
            { "username", ("u", "Username for authentication") },
            { "password", ("p", "Password for authentication") },
            { "domain", ("d", "Domain for authentication") },
            { "action", ("a", "Action to execute") }
        };

        /// <summary>
        /// Checks if an argument matches a global argument (by short or long name).
        /// Automatically looks up the short name from the GlobalArguments dictionary.
        /// </summary>
        private static bool IsGlobalArgument(string arg, string longName)
        {
            if (arg.StartsWith($"/{longName}:", StringComparison.OrdinalIgnoreCase))
                return true;
            
            // Look up short name from dictionary
            if (GlobalArguments.TryGetValue(longName, out var metadata) && metadata.Short != null)
            {
                if (arg.StartsWith($"/{metadata.Short}:", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            
            return false;
        }

        public (ParseResultType, CommandArgs?) Parse(string[] args)
        {
            CommandArgs parsedArgs = new();

            string username = null, password = null, domain = null;
            int? connectionTimeout = null;
            string actionType = null;
            StringBuilder additionalArgumentsBuilder = new StringBuilder();
            bool actionFound = false; // Track if we've encountered the action argument
            HashSet<int> processedIndices = new HashSet<int>(); // Track which args were already processed

            try {
                for (int i = 0; i < args.Length; i++)
                {
                    if (processedIndices.Contains(i))
                    {
                        continue; // Skip already processed arguments
                    }

                    var arg = args[i];
                    
                    if (arg.Contains("--"))
                    {
                        continue; // Skip any argument containing "--" (sliver thing)
                    }

                    switch (arg)
                    {
                        case "/version":
                            // Show version information
                            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                            var version = assembly.GetName().Version;
                            var compileDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(version.Build - 1).Date;
                            
                            Logger.Info($"MSSQLand Version: {version}");
                            Logger.Info($"Compile Date: {compileDate:yyyy-MM-dd}");
                            return (ParseResultType.ShowHelp, null);
                        case "/debug":
                            Logger.IsDebugEnabled = true;
                            continue;
                        case "/s":
                        case "/silent":
                            Logger.IsSilentModeEnabled = true;
                            continue;
                        case "/help":
                            // Check if next argument is a search term (not starting with /)
                            if (i + 1 < args.Length && !args[i + 1].StartsWith("/"))
                            {
                                string searchTerm = args[i + 1];
                                processedIndices.Add(i + 1); // Mark search term as processed
                                Helper.ShowFilteredHelp(searchTerm);
                                return (ParseResultType.ShowHelp, null);
                            }
                            
                            // Check if next argument is an action specification
                            if (i + 1 < args.Length && 
                                (args[i + 1].StartsWith("/a:", StringComparison.OrdinalIgnoreCase) ||
                                 args[i + 1].StartsWith("/action:", StringComparison.OrdinalIgnoreCase)))
                            {
                                string action = ExtractValue(args[i + 1], "/a:", "/action:");
                                processedIndices.Add(i + 1); // Mark action arg as processed
                                Helper.ShowActionHelp(action);
                                return (ParseResultType.ShowHelp, null);
                            }
                            
                            // Otherwise show general help
                            Helper.Show();
                            return (ParseResultType.ShowHelp, null);
                        case "/findsql":
                            // Standalone utility - find SQL Servers in Active Directory
                            Logger.Info("FindSQLServers utility mode - no database connection required");
                            
                            // Get the domain argument (next argument after /findsql)
                            string adDomain = i + 1 < args.Length
                                ? args[i + 1] 
                                : throw new ArgumentException("FindSQLServers requires a domain argument. Example: /findsql corp.com");
                            
                            processedIndices.Add(i + 1); // Mark domain as processed
                            FindSQLServers.Execute(adDomain);
                            return (ParseResultType.UtilityMode, null);
                    }

                    
                    // Check if this is the action argument
                    if (arg.StartsWith("/a:", StringComparison.OrdinalIgnoreCase) ||
                        arg.StartsWith("/action:", StringComparison.OrdinalIgnoreCase))
                    {
                        actionType = ExtractValue(arg, "/a:", "/action:");
                        actionFound = true; // Mark that action has been found
                        
                        // Check if next argument is /help for action-specific help
                        if (i + 1 < args.Length && args[i + 1] == "/help")
                        {
                            processedIndices.Add(i + 1); // Mark /help as processed
                            Helper.ShowActionHelp(actionType);
                            return (ParseResultType.ShowHelp, null);
                        }
                    }
                    // Process global/connection arguments (before action)
                    else if (!actionFound)
                    {
                        if (IsGlobalArgument(arg, "credentials"))
                        {
                            parsedArgs.CredentialType = ExtractValue(arg, "/c:", "/credentials:");
                        }
                        else if (IsGlobalArgument(arg, "host"))
                        {
                            parsedArgs.Host = Server.ParseServer(ExtractValue(arg, "/h:", "/host:"));
                        }
                        else if (IsGlobalArgument(arg, "links"))
                        {
                            parsedArgs.LinkedServers = new LinkedServers(ExtractValue(arg, "/l:", "/links:"));
                        }
                        else if (IsGlobalArgument(arg, "output"))
                        {
                            string outputFormat = ExtractValue(arg, "/o:", "/output:");
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
                        else if (IsGlobalArgument(arg, "timeout"))
                        {
                            if (connectionTimeout.HasValue)
                            {
                                Logger.Warning($"/timeout: specified multiple times. Using last value.");
                            }
                            string timeoutValue = ExtractValue(arg, "/timeout:");
                            if (!int.TryParse(timeoutValue, out int parsedTimeout) || parsedTimeout <= 0)
                            {
                                throw new ArgumentException($"Invalid timeout value: {timeoutValue}. Timeout must be a positive integer (seconds).");
                            }
                            connectionTimeout = parsedTimeout;
                        }
                        else if (IsGlobalArgument(arg, "username") && username == null)
                        {
                            username = ExtractValue(arg, "/u:", "/username:");
                        }
                        else if (IsGlobalArgument(arg, "password") && password == null)
                        {
                            password = ExtractValue(arg, "/p:", "/password:");
                        }
                        else if (IsGlobalArgument(arg, "domain") && domain == null)
                        {
                            domain = ExtractValue(arg, "/d:", "/domain:");
                        }
                        else
                        {
                            // Unknown argument before action - show available global arguments
                            Logger.Error($"Unknown global argument: {arg}");
                            Logger.NewLine();
                            Logger.Info("Available global arguments:");
                            foreach (var globalArg in GlobalArguments)
                            {
                                string shortForm = globalArg.Value.Short != null ? $"/{globalArg.Value.Short}:" : "";
                                string longForm = $"/{globalArg.Key}:";
                                string aliases = globalArg.Value.Short != null ? $"{shortForm} or {longForm}" : longForm;
                                Logger.InfoNested($"{aliases} - {globalArg.Value.Description}");
                            }
                            throw new ArgumentException($"Unknown global argument: {arg}");
                        }
                    }
                    // Process action-specific arguments (after action found)
                    else
                    {
                        // Everything after the action goes to additional arguments
                        if (Logger.IsDebugEnabled)
                        {
                            Logger.DebugNested($"Action argument: {arg}");
                        }
                        additionalArgumentsBuilder.Append(arg).Append(CommandParser.AdditionalArgumentsSeparator);
                    }

                }

                // Remove trailing separator and convert to string
                string additionalArguments = additionalArgumentsBuilder.ToString();
                parsedArgs.AdditionalArguments = TrailingSeparatorRegex.Replace(additionalArguments, "");

                if (parsedArgs.Host == null)
                {
                    Logger.Error("Missing required argument: /h or /host.");
                    return (ParseResultType.InvalidInput, null);
                }

                if (connectionTimeout.HasValue)
                {
                    parsedArgs.ConnectionTimeout = connectionTimeout.Value;
                }

                // Check if action was provided or is empty
                if (string.IsNullOrWhiteSpace(actionType))
                {
                    // User wants to see all available actions
                    Helper.ShowAllActions();
                    return (ParseResultType.ShowHelp, null);
                }

                // Check if credential type is empty or null
                if (string.IsNullOrWhiteSpace(parsedArgs.CredentialType))
                {
                    Logger.Error("Missing required argument: /c or /credentials.");
                    Logger.NewLine();
                    DataTable credentialsTable = new();
                    credentialsTable.Columns.Add("Type", typeof(string));
                    credentialsTable.Columns.Add("Description", typeof(string));
                    credentialsTable.Columns.Add("Required Arguments", typeof(string));

                    // Use CredentialsFactory to get all available credentials
                    var credentials = CredentialsFactory.GetAvailableCredentials();
                    foreach (var credential in credentials.Values)
                    {
                        string requiredArgs = credential.RequiredArguments.Count > 0
                            ? string.Join(", ", credential.RequiredArguments)
                            : "None";
                        credentialsTable.Rows.Add(credential.Name, credential.Description, requiredArgs);
                    }
                    
                    Console.WriteLine(OutputFormatter.ConvertDataTable(credentialsTable));
                    return (ParseResultType.InvalidInput, null);
                }

                // Validate the provided arguments against the selected credential type
                ValidateCredentialArguments(parsedArgs.CredentialType, username, password, domain);

                // Assign optional arguments to parsedArgs
                parsedArgs.Username = username;
                parsedArgs.Password = password;
                parsedArgs.Domain = domain;

                // Get the action from the factory (after validation)
                parsedArgs.Action = ActionFactory.GetAction(actionType, parsedArgs.AdditionalArguments);

                // Show parsed arguments only in debug mode
                if (Logger.IsDebugEnabled)
                {
                    Logger.Debug("Parsed arguments");
                    Logger.DebugNested($"Credential Type: {parsedArgs.CredentialType}");
                    Logger.DebugNested($"Target: {parsedArgs.Host.Hostname}:{parsedArgs.Host.Port}");
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

                    Logger.DebugNested($"Action: {parsedArgs.Action.GetName()}");
                    
                    if (!string.IsNullOrEmpty(parsedArgs.AdditionalArguments))
                    {
                        Logger.DebugNested($"Action Arguments (raw): {parsedArgs.AdditionalArguments}");
                        
                        // Show parsed action arguments
                        string[] actionArgs = parsedArgs.AdditionalArguments.Split(new[] { AdditionalArgumentsSeparator }, StringSplitOptions.RemoveEmptyEntries);
                        if (actionArgs.Length > 0)
                        {
                            Logger.DebugNested("Action Arguments (parsed):");
                            for (int i = 0; i < actionArgs.Length; i++)
                            {
                                Logger.DebugNested($"[{i}] {actionArgs[i]}", 1, "-");
                            }
                        }
                    }

                    Logger.NewLine();
                }

                return (ParseResultType.Success, parsedArgs);
            } catch (Exception ex) {
                Logger.Error($"Parsing error: {ex.Message}");
                return (ParseResultType.InvalidInput, null);
            }
        }

        private void ValidateCredentialArguments(string credentialType, string username, string password, string domain)
        {
            if (string.IsNullOrEmpty(credentialType))
            {
                throw new ArgumentException("Credential type (/c or /credentials) is required.");
            }

            // Check if credential type exists using CredentialsFactory
            if (!CredentialsFactory.IsValidCredentialType(credentialType))
            {
                var availableTypes = string.Join(", ", CredentialsFactory.GetCredentialTypeNames());
                throw new ArgumentException($"Unknown credential type: {credentialType}. Available types: {availableTypes}");
            }

            // Get the required arguments for this credential type from CredentialsFactory
            var metadata = CredentialsFactory.GetCredentialMetadata(credentialType);
            var requiredArgs = metadata.RequiredArguments;

            // Validate arguments
            if (requiredArgs.Contains("username") && string.IsNullOrEmpty(username))
            {
                throw new ArgumentException($"{credentialType} credentials require /u (username).");
            }

            if (requiredArgs.Contains("password") && string.IsNullOrEmpty(password))
            {
                throw new ArgumentException($"{credentialType} credentials require /p (password).");
            }

            if (requiredArgs.Contains("domain") && string.IsNullOrEmpty(domain))
            {
                throw new ArgumentException($"{credentialType} credentials require /d (domain).");
            }

            // Ensure no extra arguments are provided
            if (requiredArgs.Count == 0 && (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password) || !string.IsNullOrEmpty(domain)))
            {
                throw new ArgumentException($"Extra arguments provided for {credentialType} credentials, which do not require additional parameters.");
            }
        }

        private string ExtractValue(string arg, string shortVersion, string longVersion = null)
        {
            if (arg.StartsWith(shortVersion, StringComparison.OrdinalIgnoreCase))
                return arg.Substring(shortVersion.Length);

            if (longVersion  != null)
            {
                if (arg.StartsWith(longVersion, StringComparison.OrdinalIgnoreCase))
                    return arg.Substring(longVersion.Length);
            }
            
            throw new ArgumentException($"Invalid argument format: {arg}");
        }

    }
}
