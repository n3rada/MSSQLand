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
                        Console.WriteLine($"  > {arg}");
                    }
                }

                Console.WriteLine();
            }
        }

        /// <summary>
        /// Displays basic help message with CLI arguments and credential types.
        /// </summary>
        public static void Show()
        {
            MarkdownFormatter formatter = new MarkdownFormatter();

            // Usage instruction
            Console.WriteLine("Usage: <host> [options] <action> [action-options]\n");
            Console.WriteLine("Examples");
            Console.WriteLine("  localhost -c token whoami");
            Console.WriteLine("\nQuick Help");
            Console.WriteLine("  -h, --help           - Show this basic help");
            Console.WriteLine("  -h <keyword>         - Search for actions matching keyword");
            Console.WriteLine("  <host> <action> -h   - Show help for specific action");
            Console.WriteLine("  (no action)          - List all available actions");

            // Provide a quick reference of top-level arguments or usage
            Console.WriteLine();
            Console.WriteLine("CLI arguments");
            Console.WriteLine(formatter.ConvertDataTable(getArguments()));

            // Provide credential types
            Console.WriteLine();
            Console.WriteLine("Credential types");
            Console.WriteLine(formatter.ConvertDataTable(getCredentialTypes()));

            // Add Utilities Section
            Console.WriteLine();
            Console.WriteLine("Standalones (no database connection needed)");
            Console.WriteLine("  --findsql <domain>   - Search for MS SQL Servers in Active Directory.");
            Console.WriteLine();
            Console.WriteLine("For a complete list of actions, run without specifying an action.");
            Console.WriteLine("For detailed help on a specific action, use: <host> <action> -h");
            Console.WriteLine();
        } 

        /// <summary>
        /// Displays all available actions grouped by category.
        /// </summary>
        public static void ShowAllActions()
        {
            Console.WriteLine("Available Actions:\n");

            var actions = ActionFactory.GetAvailableActions();
            
            // Group actions by category, preserving insertion order
            var groupedActions = new List<(string Category, List<(string ActionName, string Description)>)>();
            string currentCategory = null;
            List<(string ActionName, string Description)> currentActions = null;

            foreach (var action in actions)
            {
                if (action.Category != currentCategory)
                {
                    if (currentActions != null)
                    {
                        groupedActions.Add((currentCategory, currentActions));
                    }
                    currentCategory = action.Category;
                    currentActions = new List<(string ActionName, string Description)>();
                }
                currentActions.Add((action.ActionName, action.Description));
            }
            
            // Add the last group
            if (currentActions != null && currentActions.Count > 0)
            {
                groupedActions.Add((currentCategory, currentActions));
            }

            int totalCount = 0;
            foreach (var group in groupedActions)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[{group.Category}]");
                Console.ResetColor();
                
                foreach (var action in group.Item2)
                {
                    Console.WriteLine($"  {action.ActionName}");
                    totalCount++;
                }
                Console.WriteLine();
            }

            Console.WriteLine();
            Console.WriteLine("For detailed information about a specific action, use: <host> <action> -h");
            Console.WriteLine("Example: localhost -c token createuser -h");
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
                    Console.WriteLine($"  > {arg}");
                }
            }
            else
            {
                Console.WriteLine("No additional arguments required.");
            }

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
            
            // Output & Logging            argumentsTable.Rows.Add("--output-format, --format", "[Output] Output format: table (default), csv, json, markdown.");
            argumentsTable.Rows.Add("--silent", "[Output] Enable silent mode. No logging, only results.");
            argumentsTable.Rows.Add("--debug", "[Output] Enable debug mode for detailed logs.");
            argumentsTable.Rows.Add("--trace", "[Output] Enable trace mode for verbose debugging logs.");
            
            // Help & Utilities
            argumentsTable.Rows.Add("-h, --help", "[Help] Display help. Use with action for action-specific help.");
            argumentsTable.Rows.Add("--version", "[Help] Display version information.");
            argumentsTable.Rows.Add("--findsql <domain>", "[Utility] Find SQL Servers in Active Directory (standalone).");

            return argumentsTable;
        }
    }
}
