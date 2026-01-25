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
                Console.WriteLine($"\t{action.ActionName}");
                Console.WriteLine($"\t  {action.Description}");

                if (action.Aliases != null && action.Aliases.Length > 0)
                {
                    Console.WriteLine($"\t  Aliases: {string.Join(", ", action.Aliases)}");
                }

                if (action.Arguments != null && action.Arguments.Any())
                {
                    Console.WriteLine("\t  Arguments:");
                    foreach (var arg in action.Arguments)
                    {
                        Console.WriteLine($"\t    {arg}");
                    }
                }

                Console.WriteLine();
            }
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

            Console.WriteLine("Available actions by category:\n");
            
            foreach (var group in groupedActions.OrderBy(g => g.Key))
            {
                Console.WriteLine($"[{group.Key}]");
                
                // Display max 6 actions per line
                var actionsList = group.Value.OrderBy(a => a).ToList();
                for (int i = 0; i < actionsList.Count; i += 6)
                {
                    var chunk = actionsList.Skip(i).Take(6);
                    Console.WriteLine("\t" + string.Join(", ", chunk));
                }
                Console.WriteLine();
            }

            Console.WriteLine("Use '<action> -h' or '-h <action>' for detailed help on a specific action.");
            Console.WriteLine();
        }

        /// <summary>
        /// Displays concise help message (argparse-style).
        /// </summary>
        public static void Show()
        {
            Console.WriteLine("Usage: <host> -c <cred> [options] <action> [action-args]\n");

            Console.WriteLine("Positional arguments:");
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
            Console.WriteLine("\t-w, --workstation-id   Workstation ID (default: target server name)");
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
            Console.WriteLine("\t<host> <action> -h     Show help for specific action");
            Console.WriteLine("\t--version              Show version information\n");

            Console.WriteLine("Examples:");
            Console.WriteLine("\tMSSQLand.exe localhost -c token whoami");
            Console.WriteLine("\tMSSQLand.exe sql.corp.local -c local -u sa -p Pass123 databases");
            Console.WriteLine("\tMSSQLand.exe 10.0.0.5,1433 -c domain -u admin -p Pass -d CORP info");
            Console.WriteLine("\tMSSQLand.exe sqlprod -c token -l sqldev:sa exec \"whoami\"");
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
            Console.WriteLine("Available credential types:\n");

            var credentials = Services.Credentials.CredentialsFactory.GetAvailableCredentials();
            
            foreach (var credential in credentials.Values.OrderBy(c => c.Name))
            {
                Console.WriteLine($"\t{credential.Name}");
                Console.WriteLine($"\t  {credential.Description}");
                
                if (credential.RequiredArguments.Any())
                {
                    Console.WriteLine($"\t  Required: {string.Join(", ", credential.RequiredArguments)}");
                }
                else
                {
                    Console.WriteLine("\t  Required: None");
                }

                if (credential.OptionalArguments.Any())
                {
                    Console.WriteLine($"\t  Optional: {string.Join(", ", credential.OptionalArguments)}");
                }

                Console.WriteLine();
            }

            Console.WriteLine("Examples:");
            Console.WriteLine("\tMSSQLand.exe localhost -c token whoami");
            Console.WriteLine("\tMSSQLand.exe sqlserver -c local -u sa -p Pass123 info");
            Console.WriteLine("\tMSSQLand.exe sqlserver -c domain -u admin -p Pass -d CORP whoami");
            Console.WriteLine("\tMSSQLand.exe database.windows.net -c entraid -u user -p Pass -d tenant.com databases");
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
