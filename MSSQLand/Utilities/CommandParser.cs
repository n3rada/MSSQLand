using System;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;
using MSSQLand.Actions;
using MSSQLand.Models;
using MSSQLand.Services.Credentials;

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

        public (ParseResultType, CommandArgs?) Parse(string[] args)
        {
            CommandArgs parsedArgs = new();

            string username = null, password = null, domain = null;
            int? port = null;
            string actionType = null;
            string additionalArguments = "";

            try {
                foreach (var arg in args)
                {
                    
                    if (arg.Contains("--"))
                    {
                        continue; // Skip any argument containing "--" (sliver thing)
                    }

                    switch (arg)
                    {
                        case "/debug":
                            Logger.IsDebugEnabled = true;
                            continue;
                        case "/s":
                        case "/silent":
                            Logger.IsSilentModeEnabled = true;
                            continue;
                        case "/help":
                            // Check if this is action-specific help (action was already specified)
                            if (!string.IsNullOrWhiteSpace(actionType))
                            {
                                Helper.ShowActionHelp(actionType);
                                return (ParseResultType.ShowHelp, null);
                            }
                            // Otherwise show general help
                            Helper.Show();
                            return (ParseResultType.ShowHelp, null);
                        case "/printhelp":
                            Helper.SaveCommandsToFile();
                            return (ParseResultType.ShowHelp, null);
                        case "/findsql":
                            // Standalone utility - find SQL Servers in Active Directory
                            Logger.Info("FindSQLServers utility mode - no database connection required");
                            
                            // Get the domain argument (next argument after /findsql)
                            string adDomain = args.Length > Array.IndexOf(args, arg) + 1 
                                ? args[Array.IndexOf(args, arg) + 1] 
                                : throw new ArgumentException("FindSQLServers requires a domain argument. Example: /findsql corp.com");
                            

                            FindSQLServers.Execute(adDomain);
                            return (ParseResultType.UtilityMode, null);
                    }

                    
                    if (arg.StartsWith("/c:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/credentials:", StringComparison.OrdinalIgnoreCase))
                    {
                        parsedArgs.CredentialType = ExtractValue(arg, "/c:", "/credentials:");
                    }
                    else if (arg.StartsWith("/h:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/host:", StringComparison.OrdinalIgnoreCase))
                    {
                        parsedArgs.Host = Server.ParseServer(ExtractValue(arg, "/h:", "/host:"));
                    }
                    else if (arg.StartsWith("/l:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/links:", StringComparison.OrdinalIgnoreCase))
                    {
                        parsedArgs.LinkedServers = new LinkedServers(ExtractValue(arg, "/l:", "/links:"));
                    }
                    else if (arg.StartsWith("/a:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/action:", StringComparison.OrdinalIgnoreCase))
                    {
                        actionType = ExtractValue(arg, "/a:", "/action:");
                    }
                    else if (arg.StartsWith("/port:", StringComparison.OrdinalIgnoreCase))
                    {
                        port = int.Parse(ExtractValue(arg, "/port:"));
                    }
                    else if (arg.StartsWith("/db:", StringComparison.OrdinalIgnoreCase))
                    {
                        parsedArgs.Host.Database = ExtractValue(arg, "/db:");
                    }
                    else if ((arg.StartsWith("/u:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/username:", StringComparison.OrdinalIgnoreCase)) && username == null)
                    {
                        // Only set global username if not already set (first occurrence)
                        username = ExtractValue(arg, "/u:", "/username:");
                    }
                    else if ((arg.StartsWith("/p:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/password:", StringComparison.OrdinalIgnoreCase)) && password == null)
                    {
                        // Only set global password if not already set (first occurrence)
                        password = ExtractValue(arg, "/p:", "/password:");
                    }
                    else if ((arg.StartsWith("/d:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/domain:", StringComparison.OrdinalIgnoreCase)) && domain == null)
                    {
                        // Only set global domain if not already set (first occurrence)
                        domain = ExtractValue(arg, "/d:", "/domain:");
                    }
                    else if (!arg.StartsWith("/"))
                    {
                        // Positional argument (doesn't start with /)
                        additionalArguments += $"{arg}{CommandParser.AdditionalArgumentsSeparator}";
                    }
                    else
                    {
                        // Unknown argument starting with / - could be an action-specific named argument
                        // Pass it through to the action for validation
                        additionalArguments += $"{arg}{CommandParser.AdditionalArgumentsSeparator}";
                    }

                }

                // Remove trailing pipe
                parsedArgs.AdditionalArguments = Regex.Replace(additionalArguments, $"{Regex.Escape(CommandParser.AdditionalArgumentsSeparator)}$", "");


                if (parsedArgs.Host == null)
                {
                    Logger.Error("Missing required argument: /h or /host.");
                    return (ParseResultType.InvalidInput, null);
                }


                if (port.HasValue)
                {
                    parsedArgs.Host.Port = port.Value;
                }

                // Check if action was provided or is empty
                if (string.IsNullOrWhiteSpace(actionType))
                {
                    // Check if credentials are provided (user is trying to connect)
                    if (!string.IsNullOrEmpty(parsedArgs.CredentialType))
                    {
                        // User has credentials but no action - show available actions
                        Logger.Error("Missing required argument: /a or /action.");
                        Logger.NewLine();
                        Logger.Info("Available actions you can perform:");
                        
                        var actions = ActionFactory.GetAvailableActions();
                        foreach (var (ActionName, Description, Arguments) in actions)
                        {
                            Logger.TaskNested($"{ActionName} - {Description}");
                        }
                        
                        Logger.NewLine();
                        Logger.Info("Example: /a:whoami");
                    }
                    else
                    {
                        // No credentials and no action - show full help
                        Logger.Error("Missing required argument: /a or /action.");
                        Logger.NewLine();
                        Helper.Show();
                    }
                    return (ParseResultType.InvalidInput, null);
                }

                // Get the action from the factory
                parsedArgs.Action = ActionFactory.GetAction(actionType, parsedArgs.AdditionalArguments);

                // Check if credential type is empty or null
                if (string.IsNullOrWhiteSpace(parsedArgs.CredentialType))
                {
                    Logger.Error("Missing required argument: /c or /credentials.");
                    Logger.NewLine();
                    Logger.Info("Available credential types:");
                    
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
                    
                    Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(credentialsTable));
                    return (ParseResultType.InvalidInput, null);
                }

                // Validate the provided arguments against the selected credential type
                ValidateCredentialArguments(parsedArgs.CredentialType, username, password, domain);

                // Assign optional arguments to parsedArgs
                parsedArgs.Username = username;
                parsedArgs.Password = password;
                parsedArgs.Domain = domain;

                // Show parsed arguments only in debug mode
                if (Logger.IsDebugEnabled)
                {
                    Logger.Debug("Parsed arguments");
                    Logger.DebugNested($"Credential Type: {parsedArgs.CredentialType}");
                    Logger.DebugNested($"Target: {parsedArgs.Host.Hostname}:{parsedArgs.Host.Port}");
                    
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
                        Logger.DebugNested($"Additional Arguments: {parsedArgs.AdditionalArguments}");
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
