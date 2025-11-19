using System;
using System.Data;
using System.Linq;
using System.Collections.Generic;

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
                Console.WriteLine("\nUse /help to see all available actions.");
                return;
            }

            Console.WriteLine($"Found {matchedActions.Count} matching action(s):\n");

            foreach ((string ActionName, string Description, List<string> Arguments) in matchedActions)
            {
                Console.WriteLine($"{ActionName} - {Description}");

                if (Arguments != null && Arguments.Any())
                {
                    foreach (var arg in Arguments)
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
            // Usage instruction
            Console.WriteLine("Usage: /h:localhost /c:token /a:whoami\n");
            Console.WriteLine("Quick Help:");
            Console.WriteLine("  /help                - Show this basic help");
            Console.WriteLine("  /help <keyword>      - Search for actions matching keyword");
            Console.WriteLine("  /a: or /a            - List all available actions");
            Console.WriteLine("  /a:actionname        - Execute specific action");

            // Provide a quick reference of top-level arguments or usage
            Console.WriteLine();
            Console.WriteLine("CLI arguments:");
            Console.WriteLine(MarkdownFormater.ConvertDataTableToMarkdownTable(getArguments()));

            // Provide credential types
            Console.WriteLine();
            Console.WriteLine("Credential types:");
            Console.WriteLine(MarkdownFormater.ConvertDataTableToMarkdownTable(getCredentialTypes()));

            // Add Utilities Section
            Console.WriteLine();
            Console.WriteLine("Standalones (no database connection needed):");
            Console.WriteLine("  findsql <domain>     - Search for MS SQL Servers in Active Directory.");
            Console.WriteLine();
            Console.WriteLine("For a complete list of actions, use: /a: or /a");
            Console.WriteLine();
        } 

        /// <summary>
        /// Displays all available actions grouped by category.
        /// </summary>
        public static void ShowAllActions()
        {
            Console.WriteLine("Available Actions:\n");

            var actions = ActionFactory.GetAvailableActions();

            foreach ((string ActionName, string Description, List<string> Arguments) in actions)
            {
                Console.WriteLine($"{ActionName} - {Description}");

                if (Arguments != null && Arguments.Any())
                {
                    foreach (var arg in Arguments)
                    {
                        Console.WriteLine($"  > {arg}");
                    }
                }

                Console.WriteLine();
            }
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
                Console.WriteLine("Use /help to see all available actions.");
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

            // Use CredentialsFactory to get all available credentials
            var credentials = Services.Credentials.CredentialsFactory.GetAvailableCredentials();
            foreach (var credential in credentials.Values)
            {
                string requiredArgs = credential.RequiredArguments.Any()
                    ? string.Join(", ", credential.RequiredArguments)
                    : "None";
                credentialsTable.Rows.Add(credential.Name, credential.Description, requiredArgs);
            }

            return credentialsTable;
        }


        private static DataTable getArguments()
        {
            // Build a DataTable for arguments
            DataTable argumentsTable = new();
            argumentsTable.Columns.Add("Argument", typeof(string));
            argumentsTable.Columns.Add("Description", typeof(string));

            argumentsTable.Rows.Add("/h or /host", "Specify the target SQL Server hostname. Format: SQL01:user01");
            argumentsTable.Rows.Add("/port", "Specify the SQL Server port (default: 1433).");
            argumentsTable.Rows.Add("/timeout", "Specify the connection timeout in seconds (default: 15).");
            argumentsTable.Rows.Add("/c or /credentials", "Specify the credential type (mandatory).");
            argumentsTable.Rows.Add("/u or /username", "Provide the username (if required by credential type).");
            argumentsTable.Rows.Add("/p or /password", "Provide the password (if required by credential type).");
            argumentsTable.Rows.Add("/d or /domain", "Provide the domain (if required by credential type).");
            argumentsTable.Rows.Add("/db", "Specify the target database (default: master).");
            argumentsTable.Rows.Add("/l or /links", "Specify linked server chain. Format: server1:user1,server2:user2,...");
            argumentsTable.Rows.Add("/a or /action", "Specify the action to execute.");
            argumentsTable.Rows.Add("/o or /output", "Specify output format: markdown (default), csv.");
            argumentsTable.Rows.Add("/silent or /s", "Enable silent mode. No logging, only results.");
            argumentsTable.Rows.Add("/debug", "Enable debug mode for detailed logs.");
            argumentsTable.Rows.Add("/help", "Display the helper.");
            argumentsTable.Rows.Add("/findsql <domain>", "Find SQL Servers in Active Directory (no database connection needed).");

            return argumentsTable;
        }
    }
}
