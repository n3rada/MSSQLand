using System;
using System.Data;
using System.Linq;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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
            markdownContent.AppendLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(getActions()));

            markdownContent.AppendLine();
            markdownContent.AppendLine("## 🔎 Available Enumerations");
            markdownContent.AppendLine();
            markdownContent.AppendLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(getEnumerations()));

            // Write to file
            File.WriteAllText(filePath, markdownContent.ToString());
            Logger.Success($"Command documentation saved to {filePath}");
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

            DataTable actions = getActions();

            foreach (DataRow row in actions.Rows)
            {
                // "Action" column
                string actionName = row["Action"]?.ToString() ?? "";
                // "Description" column
                string description = row["Description"]?.ToString() ?? "";
                // "Arguments" column (comma-separated, typically)
                string arguments = row["Arguments"]?.ToString() ?? "";

                // Print the main line: "actionName - description"
                Console.WriteLine($"{actionName} - {description}");

                // If we have arguments, parse them properly
                if (!string.IsNullOrWhiteSpace(arguments))
                {
                    var argumentList = ParseArguments(arguments);

                    foreach (var arg in argumentList)
                    {
                        Console.WriteLine($"  > {arg}");
                    }
                }

                // Blank line after each action to visually separate them
                Console.WriteLine();
            }

            Console.WriteLine("Available Enumerations:");
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(getEnumerations()));
        }

        /// <summary>
        /// Parses the arguments string, properly handling commas within brackets and parentheses.
        /// </summary>
        /// <param name="arguments">The arguments string to parse.</param>
        /// <returns>A list of parsed argument strings.</returns>
        private static List<string> ParseArguments(string arguments)
        {
            var argumentList = new List<string>();

            // Match argument patterns: "name (type [options], options)"
            // This regex handles nested brackets and parentheses properly
            var matches = Regex.Matches(arguments, @"(\w+)\s*\(([^)]+)\)");

            foreach (Match match in matches)
            {
                if (match.Success && match.Groups.Count >= 3)
                {
                    string argName = match.Groups[1].Value;
                    string argDetails = match.Groups[2].Value;
                    argumentList.Add($"{argName} ({argDetails})");
                }
            }

            return argumentList;
        }



        private static DataTable getActions()
        {
            var actions = ActionFactory.GetAvailableActions();

            DataTable actionsTable = new();
            actionsTable.Columns.Add("Action", typeof(string));
            actionsTable.Columns.Add("Description", typeof(string));
            actionsTable.Columns.Add("Arguments", typeof(string));

            foreach (var (ActionName, Description, Arguments) in actions)
            {
                actionsTable.Rows.Add(ActionName, Description, Arguments);
            }

            return actionsTable;

        }

        private static DataTable getEnumerations()
        {
            var enumerations = ActionFactory.GetAvailableEnumerations();

            DataTable enumerationTable = new();
            enumerationTable.Columns.Add("Enumeration", typeof(string));
            enumerationTable.Columns.Add("Description", typeof(string));

            foreach (var (name, description) in enumerations)
            {
                enumerationTable.Rows.Add(name, description);
            }

            return enumerationTable;
        }



        private static DataTable getCredentialTypes()
        {
            // Build a DataTable for credentials
            DataTable credentialsTable = new();
            credentialsTable.Columns.Add("Type", typeof(string));
            credentialsTable.Columns.Add("Required Arguments", typeof(string));

            foreach (var credential in CommandParser.CredentialArgumentGroups)
            {
                string requiredArgs = credential.Value.Any()
                    ? string.Join(", ", credential.Value)
                    : "None";
                credentialsTable.Rows.Add(credential.Key, requiredArgs);
            }

            return credentialsTable;
        }


        private static DataTable getArguments()
        {
            // Build a DataTable for arguments
            DataTable argumentsTable = new();
            argumentsTable.Columns.Add("Argument", typeof(string));
            argumentsTable.Columns.Add("Description", typeof(string));


            argumentsTable.Rows.Add("/h or /host", "Specify the target SQL Server (mandatory).");
            argumentsTable.Rows.Add("/c or /credentials", "Specify the credential type (mandatory).");
            argumentsTable.Rows.Add("/u or /username", "Provide the username (if required by credential type).");
            argumentsTable.Rows.Add("/p or /password", "Provide the password (if required by credential type).");
            argumentsTable.Rows.Add("/d or /domain", "Provide the domain (if required by credential type).");
            argumentsTable.Rows.Add("/a or /action", "Specify the action to execute (mandatory).");
            argumentsTable.Rows.Add("/l or /links", "Specify linked server chain for multi-hop connections.");
            argumentsTable.Rows.Add("/db", "Specify the target database (optional).");
            argumentsTable.Rows.Add("/e or /enum", "Execute tasks related to enumeration.");
            argumentsTable.Rows.Add("/silent or /s", "Enable silent mode (minimal output).");
            argumentsTable.Rows.Add("/debug", "Enable debug mode for detailed logs.");
            argumentsTable.Rows.Add("/help", "Display this help message and exit.");
            argumentsTable.Rows.Add("/printhelp", "Save commands to COMMANDS.md file.");

            return argumentsTable;
        }
    }
}