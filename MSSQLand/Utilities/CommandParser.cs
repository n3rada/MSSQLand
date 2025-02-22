using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MSSQLand.Actions;
using MSSQLand.Models;

namespace MSSQLand.Utilities
{
    public class CommandParser
    {
        public static readonly Dictionary<string, List<string>> CredentialArgumentGroups = new()
        {
            { "token", new List<string>() },
            { "domain", new List<string> { "username", "password", "domain" } },
            { "local", new List<string> { "username", "password" } },
            { "entraid", new List<string> { "username", "password" } },
            { "azure", new List<string> { "username", "password" } }
        };

        public const string AdditionalArgumentsSeparator = "/|/";

        public CommandArgs Parse(string[] args)
        {
            CommandArgs parsedArgs = new();


            string username = null, password = null, domain = null;
            int? port = null;
            string actionType = "info";
            string enumType = null;
            string additionalArguments = "";

            try {
                foreach (var arg in args)
                {
                    if (arg.Contains("--"))
                    {
                        continue; // Skip any argument containing "--" (sliver thing)
                    }

                    if (arg.Equals("/debug", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.IsDebugEnabled = true;
                    }
                    else if (arg.StartsWith("/s", StringComparison.OrdinalIgnoreCase) || arg.Equals("/silent", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.IsSilentModeEnabled = true;
                    }
                    else if (arg.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
                    {
                        Helper.Show();
                        Environment.Exit(0);
                    }
                    else if (arg.StartsWith("/printHelp", StringComparison.OrdinalIgnoreCase))
                    {
                        Helper.SaveCommandsToFile();
                        Environment.Exit(0);
                    }
                    else if (arg.StartsWith("/c:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/credentials:", StringComparison.OrdinalIgnoreCase))
                    {
                        parsedArgs.CredentialType = ExtractValue(arg, "/c:", "/credentials:");
                    }
                    else if (arg.StartsWith("/u:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/username:", StringComparison.OrdinalIgnoreCase))
                    {
                        username = ExtractValue(arg, "/u:", "/username:");
                    }
                    else if (arg.StartsWith("/p:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/password:", StringComparison.OrdinalIgnoreCase))
                    {
                        password = ExtractValue(arg, "/p:", "/password:");
                    }
                    else if (arg.StartsWith("/d:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/domain:", StringComparison.OrdinalIgnoreCase))
                    {
                        domain = ExtractValue(arg, "/d:", "/domain:");
                    }
                    else if (arg.StartsWith("/h:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/host:", StringComparison.OrdinalIgnoreCase))
                    {
                        parsedArgs.Host = ParseServer(ExtractValue(arg, "/h:", "/host:"));
                    }
                    else if (arg.StartsWith("/l:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/links:", StringComparison.OrdinalIgnoreCase))
                    {
                        parsedArgs.LinkedServers = new LinkedServers(ParseServerChain(ExtractValue(arg, "/l:", "/links:")));
                    }
                    else if (arg.StartsWith("/a:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/action:", StringComparison.OrdinalIgnoreCase))
                    {
                        actionType = ExtractValue(arg, "/a:", "/action:");
                    }
                    else if (arg.StartsWith("/e:", StringComparison.OrdinalIgnoreCase) ||
                             arg.StartsWith("/enum:", StringComparison.OrdinalIgnoreCase))
                    {
                        enumType = ExtractValue(arg, "/e:", "/enum:");
                    }
                    else if (arg.StartsWith("/port:", StringComparison.OrdinalIgnoreCase))
                    {
                        port = int.Parse(ExtractValue(arg, "/port:"));
                    }
                    else if (arg.StartsWith("/db:", StringComparison.OrdinalIgnoreCase))
                    {
                        parsedArgs.Host.Database = ExtractValue(arg, "/db:");
                    }
                    else if (!arg.StartsWith("/"))
                    {
                        additionalArguments += $"{arg}{CommandParser.AdditionalArgumentsSeparator}";
                    }
                    else
                    {
                        throw new ArgumentException($"Unrecognized argument: {arg}");
                    }

                }

                // Remove trailing pipe
                parsedArgs.AdditionalArguments = Regex.Replace(additionalArguments, $"{Regex.Escape(CommandParser.AdditionalArgumentsSeparator)}$", "");


                // Verify if in enumeration mode
                if (!string.IsNullOrEmpty(enumType))
                {
                    Logger.Info("Enumeration mode");
                    BaseAction action = ActionFactory.GetEnumeration(enumType, parsedArgs.AdditionalArguments);
                    Logger.Task($"Executing action: {action.GetName()}");
                    action.Execute();
                    Environment.Exit(0);
                }


                if (parsedArgs.Host == null)
                {
                    throw new ArgumentException("Targeted server (/h or /host) is mandatory. Use /help for more information");
                }
                else
                {
                    if (port.HasValue)
                    {
                        parsedArgs.Host.Port = port.Value;
                    }
                }


                // Get the action from the factory
                parsedArgs.Action = ActionFactory.GetAction(actionType, parsedArgs.AdditionalArguments);

                // Validate the provided arguments against the selected credential type
                ValidateCredentialArguments(parsedArgs.CredentialType, username, password, domain);

                // Assign optional arguments to parsedArgs
                parsedArgs.Username = username;
                parsedArgs.Password = password;
                parsedArgs.Domain = domain;


                if (Logger.IsDebugEnabled)
                {
                    Logger.Debug("Parsed arguments");
                    Logger.DebugNested($"Credential Type: {parsedArgs.CredentialType}");
                    Logger.DebugNested($"Target: {parsedArgs.Host}");

                    if (parsedArgs.LinkedServers?.ServerNames != null && parsedArgs.LinkedServers.ServerNames.Length > 0)
                    {
                        Logger.DebugNested("Server Chain");
                        foreach (var server in parsedArgs.LinkedServers.ServerNames)
                        {
                            Logger.DebugNested($"{server}", 1, "-");
                        }
                    }

                    Logger.DebugNested($"Action: {parsedArgs.Action}");
                    Logger.DebugNested($"Additional Arguments: {parsedArgs.AdditionalArguments}");
                }
            } catch (Exception ex) {
                Logger.Error($"Parsing error occured: {ex.Message}");
                Environment.Exit(0);
            }

            return parsedArgs;
        }

        private void ValidateCredentialArguments(string credentialType, string username, string password, string domain)
        {

            if (string.IsNullOrEmpty(credentialType))
            {
                throw new ArgumentException("Credential type (/c or /credentials) is required.");
            }

            if (!CredentialArgumentGroups.ContainsKey(credentialType.ToLower()))
            {
                throw new ArgumentException($"Unknown credential type: {credentialType}");
            }

            // Get the required arguments for this credential type
            var requiredArgs = CredentialArgumentGroups[credentialType.ToLower()];

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

        private Server ParseServer(string serverInput)
        {
            var parts = serverInput.Split(':');

            if (parts.Length < 1 || parts.Length > 2)
                throw new ArgumentException($"Invalid target format: {serverInput}");

            Server server = new()
            {
                Hostname = parts[0],
                ImpersonationUser = parts.Length > 1 ? parts[1] : null
            };

            return server;
        }


        private Server[] ParseServerChain(string chainInput)
        {
            // Split the input string by commas and parse each server
            var serverList = chainInput.Split(',');

            var servers = new List<Server>();

            foreach (var link in serverList)
            {
                servers.Add(ParseServer(link.Trim()));
            }

            return servers.ToArray();
        }


    }
}
