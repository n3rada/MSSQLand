// MSSQLand/Utilities/Helper.cs

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

using MSSQLand.Actions;
using MSSQLand.Services.Credentials;

namespace MSSQLand.Utilities
{
    internal class Helper
    {

        /// <summary>
        /// Displays help message for a specific help topic.
        /// </summary>
        /// <param name="topic">The help topic: "actions", "credentials", or any action name/alias.</param>
        public static void ShowFilteredHelp(string topic)
        {
            if (topic.Equals("actions", StringComparison.OrdinalIgnoreCase))
            {
                ShowAllActions();
                return;
            }

            if (topic.Equals("credentials", StringComparison.OrdinalIgnoreCase) ||
                topic.Equals("creds", StringComparison.OrdinalIgnoreCase))
            {
                ShowCredentialTypes();
                return;
            }

            // Try as an action name or alias
            var actions = ActionFactory.GetAvailableActions();
            var match = actions.FirstOrDefault(a =>
                a.ActionName.Equals(topic, StringComparison.OrdinalIgnoreCase) ||
                (a.Aliases != null && a.Aliases.Any(al => al.Equals(topic, StringComparison.OrdinalIgnoreCase))));

            if (match != default)
            {
                ShowActionHelp(topic);
                return;
            }

            Logger.Error($"Unknown help topic: '{topic}'");
            Logger.ErrorNested("Available topics: actions, credentials, <action-name>");
        }

        /// <summary>
        /// Displays all available actions grouped by category.
        /// </summary>
        public static void ShowAllActions()
        {
            var actions = ActionFactory.GetAvailableActions();
            var groupedActions = new Dictionary<string, List<(string ActionName, string Description, string[] Aliases)>>();

            foreach (var action in actions)
            {
                if (!groupedActions.ContainsKey(action.Category))
                {
                    groupedActions[action.Category] = new List<(string, string, string[])>();
                }
                groupedActions[action.Category].Add((action.ActionName, action.Description, action.Aliases));
            }

            // Compute alignment width from the longest name+aliases label
            int maxWidth = 0;
            foreach (var group in groupedActions.Values)
            {
                foreach (var (name, _, aliases) in group)
                {
                    string label = aliases != null && aliases.Length > 0
                        ? $"{name}, {string.Join(", ", aliases)}"
                        : name;
                    if (label.Length > maxWidth) maxWidth = label.Length;
                }
            }
            int columnWidth = maxWidth + 4;

            Console.WriteLine("Available Actions");

            foreach (var group in groupedActions.OrderBy(g => g.Key))
            {
                Console.WriteLine($"  [{group.Key}]");

                foreach (var (name, description, aliases) in group.Value.OrderBy(a => a.ActionName))
                {
                    string label = aliases != null && aliases.Length > 0
                        ? $"{name}, {string.Join(", ", aliases)}"
                        : name;
                    Console.WriteLine($"    {label.PadRight(columnWidth)}{description}");
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
            Console.WriteLine("\nUsage: <host> -c <cred> [options] <action> [action-args]\n");

            Console.WriteLine("Positional Arguments:");
            Console.WriteLine("\t<host>                 Target SQL Server");
            Console.WriteLine("\t                         server[:port][/user][@database]");
            Console.WriteLine("\t                         :port     - Port number (default: 1433)");
            Console.WriteLine("\t                         /user     - Impersonate login (EXECUTE AS LOGIN)");
            Console.WriteLine("\t                                     Cascading: /user1/user2/user3");
            Console.WriteLine("\t                         @database - Database context");
            Console.WriteLine("\t<action>               Action to execute (omit for connection test only)\n");

            Console.WriteLine("Authentication (Required):");
            Console.WriteLine("\t-c, --credentials      Credential type:");
            WriteCredentialTypeLines("\t                         ");
            Console.WriteLine("\t--probe                Connectivity check only (no credentials; default when -c is omitted)");
            Console.WriteLine("\t-u, --username         Username (if required by credential type)");
            Console.WriteLine("\t-p, --password         Password (if required by credential type)");
            Console.WriteLine("\t-d, --domain           Domain (if required by credential type)\n");

            Console.WriteLine("Routing:");
            Console.WriteLine("\t-l, --links            Linked server chain (semicolon-separated)");
            Console.WriteLine("\t                         -l SQL01;SQL02/user;SQL03@database");
            Console.WriteLine("\t                         /user     - Impersonate login on that hop");
            Console.WriteLine("\t                                     Cascading: /user1/user2");
            Console.WriteLine("\t                         @database - Database context on that hop");
            Console.WriteLine("\t                         [name]    - Bracket server names containing ; or @\n");

            Console.WriteLine("Connection Options:");
            Console.WriteLine("\t--timeout              Connection timeout in seconds");
            Console.WriteLine("\t--workstation-id       Workstation ID");
            Console.WriteLine("\t--app-name             Application name");
            Console.WriteLine("\t--packet-size          Network packet size in bytes (default: 8192)");
            Console.WriteLine("\t--no-encrypt           Disable connection encryption");
            Console.WriteLine("\t--no-trust-cert        Disable server certificate trust\n");

            Console.WriteLine("Output Options:");
            Console.WriteLine("\t--version, -v          Print version and author");
            Console.WriteLine("\t--format               Output format: markdown (default), csv");
            Console.WriteLine("\t--silent               Silent mode (results only, no logging)");
            Console.WriteLine("\t--debug                Enable debug logging");
            Console.WriteLine("\t--trace                Enable trace logging\n");

            Console.WriteLine("Discovery (No Authentication Required):");
            Console.WriteLine("\t--findsql [domain]     Find SQL Servers via LDAP query (add --gc for Global Catalog)");
            Console.WriteLine("\t--broadcast            Broadcast for SQL Servers on local network (UDP 1434)");
            Console.WriteLine("\t<host> --browse        Query SQL Browser service (UDP 1434)");
            Console.WriteLine("\t<host> --portscan      Scan for SQL Server ports with TDS validation\n");

            Console.WriteLine("Getting Help:");
            Console.WriteLine("\t-h actions             List all available actions");
            Console.WriteLine("\t-h credentials         Show credential types and required flags");
            Console.WriteLine("\t-h <action>            Show help for a specific action");
            Console.WriteLine("\t--help <action>        Same as above");
            Console.WriteLine("\t<action> -h            Same as above");
        }

        /// <summary>
        /// Displays help for a specific action in argparse style.
        /// </summary>
        /// <param name="actionName">The name of the action to display help for.</param>
        public static void ShowActionHelp(string actionName)
        {
            var actions = ActionFactory.GetAvailableActions();
            var action = actions.FirstOrDefault(a =>
                a.ActionName.Equals(actionName, StringComparison.OrdinalIgnoreCase) ||
                (a.Aliases != null && a.Aliases.Any(alias => alias.Equals(actionName, StringComparison.OrdinalIgnoreCase))));

            if (action == default)
            {
                Logger.Error($"Action '{actionName}' not found.");
                Logger.ErrorNested("Use -h actions to see all available actions.");
                return;
            }

            Type actionType = ActionFactory.GetActionType(action.ActionName);
            BaseAction actionInstance = (BaseAction)Activator.CreateInstance(actionType);
            var descriptors = actionInstance.GetArgumentDescriptors();

            var positional = descriptors.Where(d => d.Position >= 0).OrderBy(d => d.Position).ToList();
            var named      = descriptors.Where(d => d.Position < 0).ToList();

            // Build usage synopsis: <action> <positionals> [named-flags]
            var usageParts = new List<string> { action.ActionName };
            foreach (var d in positional)
                usageParts.Add(PositionalLabel(d));
            foreach (var d in named)
                usageParts.Add($"[{ActionArgUsageToken(d)}]");

            Console.WriteLine($"\nusage: {string.Join(" ", usageParts)}");

            if (action.Aliases != null && action.Aliases.Length > 0)
                Console.WriteLine($"aliases: {string.Join(", ", action.Aliases)}");

            Console.WriteLine();
            Console.WriteLine(action.Description);

            if (!descriptors.Any())
            {
                Console.WriteLine();
                return;
            }

            // Compute column width for alignment
            int colWidth = positional.Select(d => $"  {PositionalLabel(d)}".Length)
                .Concat(named.Select(d => $"  {ActionArgLabel(d)}".Length))
                .DefaultIfEmpty(0).Max() + 3;

            Console.WriteLine("\noptions:");

            foreach (var d in positional)
            {
                string left = $"  {PositionalLabel(d)}";
                Console.WriteLine($"{left.PadRight(colWidth)}{ActionArgDescription(d)}");
            }

            foreach (var d in named)
            {
                string left = $"  {ActionArgLabel(d)}";
                Console.WriteLine($"{left.PadRight(colWidth)}{ActionArgDescription(d)}");
            }

            Console.WriteLine();
        }

        private static string ActionArgUsageToken(ArgumentDescriptor d)
        {
            if (d.IsFlag)
            {
                if (d.ShortName != null && d.LongName != null)
                    return $"-{d.ShortName}, --{d.LongName}";
                return d.ShortName != null ? $"-{d.ShortName}" : $"--{d.LongName ?? d.FieldName}";
            }
            string valueName = (d.LongName ?? d.FieldName).ToUpper();
            if (d.ShortName != null && d.LongName != null)
                return $"-{d.ShortName}, --{d.LongName} {valueName}";
            return d.ShortName != null ? $"-{d.ShortName} {valueName}" : $"--{d.LongName ?? d.FieldName} {valueName}";
        }

        private static string ActionArgLabel(ArgumentDescriptor d)
        {
            if (d.IsFlag)
            {
                if (d.ShortName != null && d.LongName != null)
                    return $"-{d.ShortName}, --{d.LongName}";
                return d.ShortName != null ? $"-{d.ShortName}" : $"--{d.LongName ?? d.FieldName}";
            }
            string valueName = (d.LongName ?? d.FieldName).ToUpper();
            if (d.ShortName != null && d.LongName != null)
                return $"-{d.ShortName}, --{d.LongName} {valueName}";
            return d.ShortName != null ? $"-{d.ShortName} {valueName}" : $"--{d.LongName ?? d.FieldName} {valueName}";
        }

        private static string PositionalLabel(ArgumentDescriptor d)
        {
            string core = d.Required ? $"<{d.FieldName}>" : $"[{d.FieldName}]";
            return d.Remainder ? core + "..." : core;
        }

        private static string ActionArgDescription(ArgumentDescriptor d)
        {
            string desc = d.Description ?? string.Empty;
            string remainderNote = d.Remainder ? "; no quoting needed, all remaining words joined as-is" : string.Empty;
            if (d.Required)
                return string.IsNullOrEmpty(desc) ? $"(required{remainderNote})" : $"{desc} (required{remainderNote})";
            // Suppress default: False for plain bool flags and default: "" for empty strings
            bool isEmptyStringDefault = d.DefaultValue is string s && s == string.Empty;
            bool isFalseBoolDefault   = d.IsFlag && d.DefaultValue is bool b && !b;
            if (d.DefaultValue != null && !isEmptyStringDefault && !isFalseBoolDefault)
                return string.IsNullOrEmpty(desc) ? $"(default: {d.DefaultValue}{remainderNote})" : $"{desc} (default: {d.DefaultValue}{remainderNote})";
            return string.IsNullOrEmpty(remainderNote) ? desc : $"{desc}{remainderNote}";
        }

        /// <summary>
        /// Displays available credential types in argparse style.
        /// </summary>
        public static void ShowCredentialTypes()
        {
            var allCreds = Services.Credentials.CredentialsFactory.GetAvailableCredentials();
            var names = string.Join(",", new[] { "token", "windows", "local", "entraid" }
                .Where(n => allCreds.ContainsKey(n)));

            Console.WriteLine($"\nusage: <host> -c <type> [auth-flags] [options] <action>\n");
            Console.WriteLine($"  -c, --credentials {{{names}}}\n");
            WriteCredentialTypeLines("    ");
            Console.WriteLine();
            Console.WriteLine("  auth flags:  -u, --username  /  -p, --password  /  -d, --domain");
            Console.WriteLine();
        }

        /// <summary>
        /// Writes one line per credential type to stdout, indented by <paramref name="indent"/>.
        /// Shared by Show() and ShowCredentialTypes().
        /// </summary>
        private static void WriteCredentialTypeLines(string indent)
        {
            var allCreds = Services.Credentials.CredentialsFactory.GetAvailableCredentials();
            var preferredOrder = new[] { "token", "windows", "local", "entraid" };
            var orderedCreds = preferredOrder
                .Where(n => allCreds.ContainsKey(n))
                .Select(n => allCreds[n])
                .Concat(allCreds.Values.Where(c => !preferredOrder.Any(n => n.Equals(c.Name, StringComparison.OrdinalIgnoreCase))))
                .ToList();

            var argToShort = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "username", "-u" }, { "password", "-p" }, { "domain", "-d" }
            };

            string FlagsHint(CredentialMetadata c)
            {
                var req = c.RequiredArguments.Select(a => argToShort.TryGetValue(a, out var f) ? f : $"--{a}");
                var opt = c.OptionalArguments.Select(a => argToShort.TryGetValue(a, out var f) ? $"[{f}]" : $"[--{a}]");
                return string.Join(" ", req.Concat(opt));
            }

            int nameWidth  = orderedCreds.Max(c => c.Name.Length) + 2;
            int flagsWidth = orderedCreds.Max(c => FlagsHint(c).Length) + 3;

            foreach (var cred in orderedCreds)
            {
                string flags = FlagsHint(cred);
                Console.WriteLine($"{indent}{cred.Name.PadRight(nameWidth)}{flags.PadRight(flagsWidth)}{cred.Description}");

                if (cred.Aliases != null && cred.Aliases.Count > 0)
                {
                    string pad = indent + new string(' ', nameWidth + flagsWidth);
                    Console.WriteLine($"{pad}alias: {string.Join(", ", cred.Aliases)}");
                }
            }
        }

        /// <summary>
        /// Displays a quick start banner with minimal usage info.
        /// </summary>
        public static void ShowQuickStart()
        {
            Console.WriteLine("\nUsage: <host> -c <cred> [options] <action> [action-options]\n");
            Console.WriteLine("For full help: -h or --help\n");
        }
    }
}
