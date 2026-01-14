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
            Console.WriteLine($"Searching for: '{searchTerm}'\n");

            var actions = ActionFactory.GetAvailableActions();
            var matchedActions = actions.Where(a =>
                a.ActionName.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0 ||
                a.Description.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (a.Arguments != null && a.Arguments.Any(arg => arg.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0))
            ).ToList();

            if (matchedActions.Count == 0)
            {
                Logger.Warning($"No actions found matching '{searchTerm}'");
                Console.WriteLine("\nUse -h or --help to see all available actions.");
                return;
            }

            Console.WriteLine($"Found {matchedActions.Count} matching action(s):\n");

            foreach (var action in matchedActions)
            {
                Console.WriteLine($"{action.ActionName} - {action.Description}");

                if (action.Arguments != null && action.Arguments.Any())
                {
                    foreach (var arg in action.Arguments)
                    {
                        Console.WriteLine($"\t> {arg}");
                    }
                }

                Console.WriteLine();
            }
        }

        /// <summary>
        /// Displays concise usage + action list (no args).
        /// </summary>
        public static void ShowQuickStart()
        {
            Console.WriteLine("Usage: <host> -c <credential> <action> [options]");
            Console.WriteLine();
            Console.WriteLine("\tExample: localhost -c token whoami");
            Console.WriteLine("\tUse -h for full help");
            Console.WriteLine();

            // Show all actions grouped by category
            var actions = ActionFactory.GetAvailableActions();
            
            // Group by category using dictionary to consolidate
            var groupedActions = new Dictionary<string, List<string>>();

            foreach (var action in actions)
            {
                if (!groupedActions.ContainsKey(action.Category))
                {
                    groupedActions[action.Category] = new List<string>();
                }
                groupedActions[action.Category].Add(action.ActionName);
            }

            Console.WriteLine("Available actions:");
            foreach (var group in groupedActions)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"[{group.Key}] ");
                Console.ResetColor();
                Console.WriteLine(string.Join(", ", group.Value));
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Displays full help message with CLI arguments and credential types (-h).
        /// </summary>
        public static void Show()
        {
            MarkdownFormatter formatter = new MarkdownFormatter();

            Console.WriteLine("Usage: <host> [options] <action> [action-options]\n");
            Console.WriteLine("Examples");
            Console.WriteLine("\tlocalhost -c token whoami");
            Console.WriteLine("\tlocalhost@clients -c token tables --name 'invoice' --columns");
            Console.WriteLine("\tSQL02,61433 -c local -u sa -p 'admin' search 'password' --all");
            Console.WriteLine();

            Console.WriteLine("Help options");
            Console.WriteLine("\t-h, --help           Show this full help");
            Console.WriteLine("\t-h <keyword>         Search actions by keyword");
            Console.WriteLine("\t<host> <action> -h   Show help for specific action");
            Console.WriteLine("\t(no args)            Quick start with action list");

            Console.WriteLine();
            Console.WriteLine("CLI arguments");
            Console.WriteLine(formatter.ConvertDataTable(getArguments()));

            Console.WriteLine();
            Console.WriteLine("Credential types");
            Console.WriteLine(formatter.ConvertDataTable(getCredentialTypes()));

            Console.WriteLine();
            Console.WriteLine("Discovery utilities (no database connection)");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[Discovery]");
            Console.ResetColor();
            Console.WriteLine("\t-findsql [domain]                 Find SQL Servers via SPNs in a domain");
            Console.WriteLine("\t-findsql [domain] --global-catalog Query entire forest (Global Catalog, preferred)");
            Console.WriteLine("\t-findsql [domain] -gc              (short form for --global-catalog)");
            Console.WriteLine("\t-findsql [domain] --forest/-forest  (legacy, supported for compatibility)");
            Console.WriteLine("\t<host> -browse                      Query SQL Browser for instances/ports");
            Console.WriteLine("\t<host> -portscan [--all]            Scan for SQL Server ports (TDS validation)");
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
            MarkdownFormatter formatter = new MarkdownFormatter();
            Console.WriteLine("Available credential types:\n");
            Console.WriteLine(formatter.ConvertDataTable(getCredentialTypes()));
            Console.WriteLine("Usage: <host> -c <type> [auth-options] <action>");
            Console.WriteLine("Example: localhost -c local -u sa -p 'password' whoami");
            Console.WriteLine();
        }

        private static DataTable getCredentialTypes()
        {
            // Build a DataTable for credentials
            DataTable credentialsTable = new();
            credentialsTable.Columns.Add("Type", typeof(string));
            credentialsTable.Columns.Add("Description", typeof(string));
            credentialsTable.Columns.Add("Required Arguments", typeof(string));
            credentialsTable.Columns.Add("Optional Arguments", typeof(string));

            // Use CredentialsFactory to get all available credentials
            var credentials = Services.Credentials.CredentialsFactory.GetAvailableCredentials();
            foreach (var credential in credentials.Values)
            {
                string requiredArgs = credential.RequiredArguments.Any()
                    ? string.Join(", ", credential.RequiredArguments)
                    : "None";
                
                string optionalArgs = credential.OptionalArguments.Any()
                    ? string.Join(", ", credential.OptionalArguments)
                    : "-";
                
                credentialsTable.Rows.Add(credential.Name, credential.Description, requiredArgs, optionalArgs);
            }

            return credentialsTable;
        }


        private static DataTable getArguments()
        {
            // Build a DataTable for arguments
            DataTable argumentsTable = new();
            argumentsTable.Columns.Add("Argument", typeof(string));
            argumentsTable.Columns.Add("Description", typeof(string));

            // Positional Arguments
            argumentsTable.Rows.Add("<host>", "[Positional] Target SQL Server. Format: server,port (port defaults to 1433).");
            argumentsTable.Rows.Add("<action>", "[Positional] Action to execute.");
            
            // Authentication
            argumentsTable.Rows.Add("-c, --credentials", "[Auth] Credential type (mandatory).");
            argumentsTable.Rows.Add("-u, --username", "[Auth] Username (if required by credential type).");
            argumentsTable.Rows.Add("-p, --password", "[Auth] Password (if required by credential type).");
            argumentsTable.Rows.Add("-d, --domain", "[Auth] Domain (if required by credential type).");
            
            // Connection Settings
            argumentsTable.Rows.Add("--timeout", "[Connection] Connection timeout in seconds (default: 5).");
            argumentsTable.Rows.Add("--app-name", "[Connection] SQL application name (default: DataFactory).");
            argumentsTable.Rows.Add("--workstation-id", "[Connection] SQL workstation ID (default: datafactory-runX).");
            argumentsTable.Rows.Add("--packet-size", "[Connection] Network packet size in bytes (default: 8192).");
            argumentsTable.Rows.Add("--no-encrypt", "[Connection] Disable connection encryption (default: enabled).");
            argumentsTable.Rows.Add("--no-trust-cert", "[Connection] Disable server certificate trust (default: trusted).");
            
            // Server Options
            argumentsTable.Rows.Add("-l, --links", "[Server] Linked server chain. Format: server1:user1,server2:user2,...");
            
            // Output & Logging
            argumentsTable.Rows.Add("--output-format, --format", "[Output] Output format: table (default), csv, json, markdown.");
            argumentsTable.Rows.Add("--silent", "[Output] Enable silent mode. No logging, only results.");
            argumentsTable.Rows.Add("--debug", "[Output] Enable debug mode for detailed logs.");
            argumentsTable.Rows.Add("--trace", "[Output] Enable trace mode for verbose debugging logs.");

            // Help
            argumentsTable.Rows.Add("-h, --help", "[Help] Display help. Use with action for action-specific help.");
            argumentsTable.Rows.Add("--version", "[Help] Display version information.");

            // Discovery
            argumentsTable.Rows.Add("[Discovery]", "");
            argumentsTable.Rows.Add("-findsql [domain] [--global-catalog|--gc]", "[Discovery] Find SQL Servers via AD SPNs. Use --global-catalog or -gc for forest-wide search (Global Catalog). --forest/-forest also supported for compatibility.");
            argumentsTable.Rows.Add("<host> -browse", "[Discovery] Query SQL Browser service (UDP 1434) for instances and ports.");
            argumentsTable.Rows.Add("<host> -portscan [--all]", "[Discovery] Scan for SQL ports via TDS. Stops on first hit unless --all.");

            return argumentsTable;
        }
    }
}
