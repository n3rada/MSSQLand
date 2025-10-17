using System;
using System.Collections.Generic;

namespace MSSQLand.Utilities
{
    /// <summary>
    /// Factory for standalone utilities that don't require database connections.
    /// </summary>
    public static class UtilityFactory
    {
        // Define available utilities with their descriptions
        private static readonly Dictionary<string, (Func<string, int> Execute, string Description)> UtilityMetadata = new()
        {
            { "findsql", (FindSQLServers.Execute, "Search for MS SQL Servers in Active Directory.") },
        };

        /// <summary>
        /// Executes a standalone utility command.
        /// </summary>
        /// <param name="utilityName">The name of the utility to execute</param>
        /// <param name="arguments">Arguments to pass to the utility</param>
        public static void ExecuteUtility(string utilityName, string arguments)
        {
            if (!UtilityMetadata.TryGetValue(utilityName.ToLower(), out var utility))
            {
                throw new ArgumentException($"Unknown utility: {utilityName}");
            }

            Logger.Task($"Executing utility: {utilityName}");
            int result = utility.Execute(arguments);
            Logger.Debug($"Utility returned: {result}");
        }

        /// <summary>
        /// Gets a list of all available utilities with their descriptions.
        /// </summary>
        public static List<(string UtilityName, string Description)> GetAvailableUtilities()
        {
            var result = new List<(string UtilityName, string Description)>();

            foreach (var utility in UtilityMetadata)
            {
                result.Add((utility.Key, utility.Value.Description));
            }

            return result;
        }
    }
}
