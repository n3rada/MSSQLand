// MSSQLand/Utilities/Helper.cs

using System;
using System.Data;
using System.Linq;
using System.Collections.Generic;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Utilities
{
    internal class Helper
    {

        /// <summary>
        /// Displays help message filtered by a search term.
        /// </summary>
        /// <param name="searchTerm">The term to search for in actions, descriptions, and arguments.</param>
        public static void ShowFilteredHelp(string searchTerm)
        {
            // Special keywords for detailed help sections
            if (searchTerm.Equals("actions", StringComparison.OrdinalIgnoreCase))
            {
                ShowAllActions();
                return;
            }

            if (searchTerm.Equals("credentials", StringComparison.OrdinalIgnoreCase) || 
                searchTerm.Equals("creds", StringComparison.OrdinalIgnoreCase))
            {
                ShowCredentialTypes();
                return;
            }

            // Search actions by keyword
            Console.WriteLine($"Searching actions for: '{searchTerm}'\n");

            var actions = ActionFactory.GetAvailableActions();
            var matchedActions = actions.Where(a =>
                a.ActionName.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0 ||
                a.Description.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (a.Arguments != null && a.Arguments.Any(arg => arg.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0))
            ).ToList();

            if (matchedActions.Count == 0)
            {
                Logger.Warning($"No actions found matching '{searchTerm}'");
                Logger.WarningNested("Use -h actions to see all available actions.");
                return;
            }

            Console.WriteLine($"Found {matchedActions.Count} matching action(s):\n");

            foreach (var action in matchedActions)
            {
                Console.Write($"  {action.ActionName}");
                
                if (action.Aliases != null && action.Aliases.Length > 0)
                {
                    Console.Write($"  (aliases: {string.Join(", ", action.Aliases)})");
                }
                
                Console.WriteLine();
                Console.WriteLine($"\t{action.Description}");
            }
            Console.WriteLine();
            Console.WriteLine("For action details:  <action> -h ");
        }

        /// <summary>
        /// Displays all available actions grouped by category.
        /// </summary>
        public static void ShowAllActions()
        {
            var actions = ActionFactory.GetAvailableActions();
            var groupedActions = new Dictionary<string, List<string>>();

            foreach (var action in actions)
            {
                if (!groupedActions.ContainsKey(action.Category))
                {
                    groupedActions[action.Category] = new List<string>();
                }
                groupedActions[action.Category].Add(action.ActionName);
            }

            Console.WriteLine("Available Actions\n");
            
            foreach (var group in groupedActions.OrderBy(g => g.Key))
            {
                Console.WriteLine($"  [{group.Key}]");
                
                // Display max 6 actions per line
                var actionsList = group.Value.OrderBy(a => a).ToList();
                for (int i = 0; i < actionsList.Count; i += 6)
                {
                    var chunk = actionsList.Skip(i).Take(6);
                    Console.WriteLine("\t" + string.Join(", ", chunk));
                }
                Console.WriteLine();
            }

            Console.WriteLine("For action details:  <action> -h  or  -h <action>");
        }

        /// <summary>
        /// Displays concise help message (argparse-style).
        /// </summary>
        public static void Show()
        {
            Console.WriteLine("Usage: <host> -c <cred> [options] <action> [action-args]\n");

            Console.WriteLine("--version                Show version information");

            Console.WriteLine("\nPositional arguments:");
            Console.WriteLine("\t<host>                 Target SQL Server (format: server,port or server\\instance)");
            Console.WriteLine("\t<action>               Action to execute (use -h actions for full list)\n");

            Console.WriteLine("Authentication (required):");
            Console.WriteLine("\t-c, --credentials      Credential type: probe, token, local, windows, domain, entraid");
            Console.WriteLine("\t-u, --username         Username (if required by credential type)");
            Console.WriteLine("\t-p, --password         Password (if required by credential type)");
            Console.WriteLine("\t-d, --domain           Domain (if required by credential type)\n");

            Console.WriteLine("Connection options:");
            Console.WriteLine("\t-l, --links            Linked server chain (server1:user1,server2:user2,...)");
            Console.WriteLine("\t--timeout              Connection timeout in seconds (default: 5)");
            Console.WriteLine("\t--workstation-id       Workstation ID (default: target server name)");
            Console.WriteLine("\t--app-name             Application name (default: SQLAgent - TSQL JobStep)");
            Console.WriteLine("\t--packet-size          Network packet size in bytes (default: 8192)");
            Console.WriteLine("\t--no-encrypt           Disable connection encryption");
            Console.WriteLine("\t--no-trust-cert        Disable server certificate trust\n");

            Console.WriteLine("Output options:");
            Console.WriteLine("\t--format               Output format: table (default), csv, json, markdown");
            Console.WriteLine("\t--silent               Silent mode (results only, no logging)");
            Console.WriteLine("\t--debug                Enable debug logging");
            Console.WriteLine("\t--trace                Enable trace logging\n");

            Console.WriteLine("Discovery (no authentication required):");
            Console.WriteLine("\t-findsql [domain]      Find SQL Servers via AD SPNs (add --gc for Global Catalog)");
            Console.WriteLine("\t<host> -browse         Query SQL Browser service (UDP 1434)");
            Console.WriteLine("\t<host> -portscan       Scan for SQL Server ports with TDS validation\n");

            Console.WriteLine("Help:");
            Console.WriteLine("\t-h, --help             Show this help");
            Console.WriteLine("\t-h <keyword>           Search actions matching keyword");
            Console.WriteLine("\t-h actions             List all available actions by category");
            Console.WriteLine("\t-h credentials         Show credential types and requirements");
            Console.WriteLine("\t<action> -h            Show help for specific action");
            Console.WriteLine();
        } 

        /// <summary>
        /// Displays help for a specific action.
        /// </summary>
        /// <param name="actionName">The name of the action to display help for.</param>
        public static void ShowActionHelp(string actionName)
        {
            var actions = ActionFactory.GetAvailableActions();
            var action = actions.FirstOrDefault(a => a.ActionName.Equals(actionName, StringComparison.OrdinalIgnoreCase));

            if (action == default)
            {
                Logger.Error($"Action '{actionName}' not found.");
                Console.WriteLine();
                Console.WriteLine("Use -h or --help to see all available actions.");
                return;
            }

            Console.WriteLine($"\nAction: {action.ActionName}");
            if (action.Aliases != null && action.Aliases.Length > 0)
            {
                Console.WriteLine($"Aliases: {string.Join(", ", action.Aliases)}");
            }
            Console.WriteLine($"Description: {action.Description}");
            Console.WriteLine();

            if (action.Arguments != null && action.Arguments.Any())
            {
                Console.WriteLine("Arguments");
                foreach (var arg in action.Arguments)
                {
                    Console.WriteLine($"\t> {arg}");
                }
            }
            else
            {
                Console.WriteLine("No additional arguments required.");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Displays available credential types.
        /// </summary>
        public static void ShowCredentialTypes()
        {
            Console.WriteLine("Available Credential Types\n");

            var credentials = Services.Credentials.CredentialsFactory.GetAvailableCredentials();
            
            foreach (var credential in credentials.Values.OrderBy(c => c.Name))
            {
                Console.WriteLine($"  [{credential.Name}]");
                Console.WriteLine($"\t{credential.Description}");
                
                if (credential.RequiredArguments.Any())
                {
                    Console.WriteLine($"\tRequired: {string.Join(", ", credential.RequiredArguments)}");
                }
                else
                {
                    Console.WriteLine("\tRequired: None");
                }

                if (credential.OptionalArguments.Any())
                {
                    Console.WriteLine($"\tOptional: {string.Join(", ", credential.OptionalArguments)}");
                }

                Console.WriteLine();
            }

            Console.WriteLine("For credential usage:  <host> -c <type> -h");
        }

        /// <summary>
        /// Displays a quick start banner with minimal usage info.
        /// </summary>
        public static void ShowQuickStart()
        {
            Console.WriteLine("Usage: <host> -c <cred> [options] <action> [action-options]\n");
            Console.WriteLine("For detailed help:   -h or --help");
            Console.WriteLine("For actions list:    -h actions");
            Console.WriteLine("For credentials:     -h credentials");
            Console.WriteLine("Search actions:      -h <keyword>");
        }
    }
}
