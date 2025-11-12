using System;
using System.Data;
using System.Linq;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace MSSQLand.Utilities
{
    internal class Helper
    {
        /// <summary>
        /// Saves the command-line help details to a Markdown file.
        /// </summary>
        public static void SaveCommandsToFile(string filePath = "COMMANDS.md")
        {
            StringBuilder markdownContent = new();

            markdownContent.AppendLine("# MSSQLand Command Reference");
            markdownContent.AppendLine();
            markdownContent.AppendLine("## 📌 Command-Line Arguments");
            markdownContent.AppendLine();
            markdownContent.AppendLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(getArguments()));

            markdownContent.AppendLine();
            markdownContent.AppendLine("## 🔑 Credential Types");
            markdownContent.AppendLine();
            markdownContent.AppendLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(getCredentialTypes()));

            markdownContent.AppendLine();
            markdownContent.AppendLine("## 🛠 Available Actions");
            markdownContent.AppendLine();

            // Get all actions and group them by namespace (category)
            var actions = ActionFactory.GetAvailableActions();
            
            // Group actions by their namespace/category
            var groupedActions = new Dictionary<string, List<(string ActionName, string Description, List<string> Arguments)>>
            {
                { "Administration", new List<(string, string, List<string>)>() },
                { "Database", new List<(string, string, List<string>)>() },
                { "Domain", new List<(string, string, List<string>)>() },
                { "Execution", new List<(string, string, List<string>)>() },
                { "FileSystem", new List<(string, string, List<string>)>() },
                { "Network", new List<(string, string, List<string>)>() }
            };

            // Categorize each action based on the action type's namespace
            foreach (var action in actions)
            {
                // Get the action instance to determine its namespace
                var actionType = ActionFactory.GetActionType(action.ActionName);
                string category = DetermineCategory(actionType);
                
                if (groupedActions.ContainsKey(category))
                {
                    groupedActions[category].Add(action);
                }
            }

            // Output each category in order
            string[] categoryOrder = { "Administration", "Database", "Domain", "Execution", "FileSystem", "Network" };
            
            foreach (string category in categoryOrder)
            {
                if (groupedActions[category].Count == 0) continue;

                markdownContent.AppendLine($"### {category} Actions");
                markdownContent.AppendLine();

                foreach (var (ActionName, Description, Arguments) in groupedActions[category])
                {
                    markdownContent.AppendLine($"#### `{ActionName}`");
                    markdownContent.AppendLine($"**Description:** {Description}");
                    markdownContent.AppendLine();

                    if (Arguments != null && Arguments.Any())
                    {
                        markdownContent.AppendLine("**Arguments:**");
                        foreach (var arg in Arguments)
                        {
                            markdownContent.AppendLine($"- {arg}");
                        }
                    }
                    else
                    {
                        markdownContent.AppendLine("**Arguments:** None");
                    }
                    
                    markdownContent.AppendLine();
                }
            }

            // Write to file
            File.WriteAllText(filePath, markdownContent.ToString());
            Logger.Success($"Command documentation saved to {filePath}");
        }

        /// <summary>
        /// Determines the category of an action based on its namespace.
        /// </summary>
        private static string DetermineCategory(Type actionType)
        {
            string fullName = actionType.FullName ?? actionType.Name;
            // Return last segment of namespace as category
            return fullName.Split('.').Reverse().Skip(1).FirstOrDefault() ?? "Unknown";
        }

        /// <summary>
        /// Displays the help message with available actions, credentials, and argument usage.
        /// </summary>
        public static void Show()
        {

            // Usage instruction
            Console.WriteLine("Usage: MSSQLand.exe /h:localhost /c:token /a:whoami\n");

            // Provide a quick reference of top-level arguments or usage
            Console.WriteLine("CLI arguments:");
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(getArguments()));

            // Provide credential types
            Console.WriteLine();
            Console.WriteLine("Credential types:");
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(getCredentialTypes()));

            // Provide actions
            Console.WriteLine();
            Console.WriteLine("Available Actions:");
            Console.WriteLine();

            var actions = ActionFactory.GetAvailableActions();

            foreach (var (ActionName, Description, Arguments) in actions)
            {
                // Print the main line: "actionName - description"
                Console.WriteLine($"{ActionName} - {Description}");

                // Display arguments directly from the list
                if (Arguments != null && Arguments.Any())
                {
                    foreach (var arg in Arguments)
                    {
                        Console.WriteLine($"  > {arg}");
                    }
                }

                // Blank line after each action to visually separate them
                Console.WriteLine();
            }


            // Add Utilities Section
            Console.WriteLine();
            Console.WriteLine("Standalones (no database connection needed):");
            Console.WriteLine();
            Console.WriteLine("  findsql <domain>     - Search for MS SQL Servers in Active Directory.");
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

            argumentsTable.Rows.Add("/findsql <domain>", "Find SQL Servers in Active Directory (no database connection needed).");
            argumentsTable.Rows.Add("/h or /host", "Specify the target SQL Server hostname. Format: hostname:impersonationUser");
            argumentsTable.Rows.Add("/port", "Specify the SQL Server port (default: 1433).");
            argumentsTable.Rows.Add("/c or /credentials", "Specify the credential type (mandatory for actions).");
            argumentsTable.Rows.Add("/u or /username", "Provide the username (if required by credential type).");
            argumentsTable.Rows.Add("/p or /password", "Provide the password (if required by credential type).");
            argumentsTable.Rows.Add("/d or /domain", "Provide the domain (if required by credential type).");
            argumentsTable.Rows.Add("/a or /action", "Specify the action to execute (mandatory for actions).");
            argumentsTable.Rows.Add("/l or /links", "Specify linked server chain. Format: server1:user1,server2:user2,...");
            argumentsTable.Rows.Add("/db", "Specify the target database (default: master).");
            argumentsTable.Rows.Add("/silent or /s", "Enable silent mode (minimal output).");
            argumentsTable.Rows.Add("/debug", "Enable debug mode for detailed logs.");
            argumentsTable.Rows.Add("/help", "Display this help message and exit.");
            argumentsTable.Rows.Add("/printhelp", "Save commands to COMMANDS.md file.");

            return argumentsTable;
        }
    }
}
