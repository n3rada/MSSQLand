using System;
using System.Data;
using System.Linq;

namespace MSSQLand.Utilities
{
    internal class Helper
    {
        /// <summary>
        /// Displays the help message with available actions, credentials, and argument usage.
        /// </summary>
        public static void Show()
        {
            Logger.Banner("Command-Line Arguments");
            ShowArguments();

            Logger.Banner("Credential Types");
            ShowCredentialTypes();

            Logger.Banner("Available Actions");
            ShowActions();

            Logger.Banner("Available Enumerations");
            ShowEnumerations();
        }

        /// <summary>
        /// Displays available actions in a table format.
        /// </summary>
        private static void ShowActions()
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

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(actionsTable));
        }

        private static void ShowEnumerations()
        {
            var enumerations = ActionFactory.GetAvailableEnumerations();

            DataTable enumerationTable = new();
            enumerationTable.Columns.Add("Enumeration", typeof(string));
            enumerationTable.Columns.Add("Description", typeof(string));

            foreach (var (name, description) in enumerations)
            {
                enumerationTable.Rows.Add(name, description);
            }

            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(enumerationTable));
        }


        /// <summary>
        /// Displays credential types and their required arguments in a table format.
        /// </summary>
        private static void ShowCredentialTypes()
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

            // Use MarkdownFormatter to display the table
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(credentialsTable));
        }

        /// <summary>
        /// Displays command-line argument usage in a table format.
        /// </summary>
        private static void ShowArguments()
        {
            // Build a DataTable for arguments
            DataTable argumentsTable = new();
            argumentsTable.Columns.Add("Argument", typeof(string));
            argumentsTable.Columns.Add("Description", typeof(string));

            
            argumentsTable.Rows.Add("/t or /target", "Specify the target SQL Server (mandatory).");
            argumentsTable.Rows.Add("/c or /credentials", "Specify the credential type (mandatory).");
            argumentsTable.Rows.Add("/u or /username", "Provide the username (if required by credential type).");
            argumentsTable.Rows.Add("/p or /password", "Provide the password (if required by credential type).");
            argumentsTable.Rows.Add("/d or /domain", "Provide the domain (if required by credential type).");
            argumentsTable.Rows.Add("/a or /action", "Specify the action to execute (default: 'info').");
            argumentsTable.Rows.Add("/l or /links", "Specify linked server chain for multi-hop connections.");
            argumentsTable.Rows.Add("/db", "Specify the target database (optional).");
            argumentsTable.Rows.Add("/e or /enum", "Execute tasks related to enumeration.");
            argumentsTable.Rows.Add("/silent", "Enable silent mode (minimal output).");
            argumentsTable.Rows.Add("/debug", "Enable debug mode for detailed logs.");
            argumentsTable.Rows.Add("/help", "Display this help message and exit.");

            // Use MarkdownFormatter to display the table
            Console.WriteLine(MarkdownFormatter.ConvertDataTableToMarkdownTable(argumentsTable));
        }
    }
}
