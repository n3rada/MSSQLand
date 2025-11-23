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
            Console.WriteLine("Examples:");
            Console.WriteLine("  localhost -c token whoami");
            Console.WriteLine("\nQuick Help:");
            Console.WriteLine("  -h, --help           - Show this basic help");
            Console.WriteLine("  -h <keyword>         - Search for actions matching keyword");
            Console.WriteLine("  <host> <action> -h   - Show help for specific action");
            Console.WriteLine("  (no action)          - List all available actions");

            // Provide a quick reference of top-level arguments or usage
            Console.WriteLine();
            Console.WriteLine("CLI arguments:");
            Console.WriteLine(formatter.ConvertDataTable(getArguments()));

            // Provide credential types
            Console.WriteLine();
            Console.WriteLine("Credential types:");
            Console.WriteLine(formatter.ConvertDataTable(getCredentialTypes()));

            // Add Utilities Section
            Console.WriteLine();
            Console.WriteLine("Standalones (no database connection needed):");
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
            
            // Group actions by category
            var groupedActions = actions.GroupBy(a => a.Category)
                                       .OrderBy(g => g.Key);

            int totalCount = 0;
            foreach (var group in groupedActions)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[{group.Key}]");
                Console.ResetColor();
                
                foreach (var action in group.OrderBy(a => a.ActionName))
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
                Console.WriteLine("Arguments:");
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

            argumentsTable.Rows.Add("<host>", "[Positional] Target SQL Server. Format: server,port (port defaults to 1433).");
            argumentsTable.Rows.Add("<action>", "[Positional] Action to execute (see list below).");
            argumentsTable.Rows.Add("-c, --credentials", "Credential type (mandatory). See credential types below.");
            argumentsTable.Rows.Add("-u, --username", "Username (if required by credential type).");
            argumentsTable.Rows.Add("-p, --password", "Password (if required by credential type).");
            argumentsTable.Rows.Add("-d, --domain", "Domain (if required by credential type).");
            argumentsTable.Rows.Add("-l, --links", "Linked server chain. Format: server1:user1,server2:user2,...");
            argumentsTable.Rows.Add("-o, --output", "Output format: table (default), csv, json, markdown.");
            argumentsTable.Rows.Add("--timeout", "Connection timeout in seconds (default: 15).");
            argumentsTable.Rows.Add("-s, --silent", "Enable silent mode. No logging, only results.");
            argumentsTable.Rows.Add("--debug", "Enable debug mode for detailed logs.");
            argumentsTable.Rows.Add("-h, --help", "Display help. Use with action for action-specific help.");
            argumentsTable.Rows.Add("--version", "Display version information.");
            argumentsTable.Rows.Add("--findsql <domain>", "Find SQL Servers in Active Directory (standalone utility).");

            return argumentsTable;
        }
    }
}
