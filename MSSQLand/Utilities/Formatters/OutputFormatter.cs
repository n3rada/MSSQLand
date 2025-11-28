using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace MSSQLand.Utilities.Formatters
{
    /// <summary>
    /// Main output formatter that delegates to the appropriate formatter based on user selection.
    /// Provides a static interface for formatting data structures.
    /// </summary>
    internal static class OutputFormatter
    {
        private static IOutputFormatter _currentFormatter = new MarkdownFormatter();

        /// <summary>
        /// Gets the current output format name.
        /// </summary>
        public static string CurrentFormat => _currentFormatter.FormatName;

        /// <summary>
        /// Sets the output format based on format name.
        /// </summary>
        /// <param name="formatName">Format name (e.g., "markdown", "csv")</param>
        public static void SetFormat(string formatName)
        {
            if (string.IsNullOrWhiteSpace(formatName))
            {
                throw new ArgumentException("Format name cannot be null or empty.", nameof(formatName));
            }

            _currentFormatter = formatName.ToLower() switch
            {
                "markdown" or "md" => new MarkdownFormatter(),
                "csv" => new CsvFormatter(),
                _ => throw new ArgumentException($"Unknown output format: {formatName}. Available formats: markdown, csv")
            };

            Logger.Debug($"Output format set to: {_currentFormatter.FormatName}");
        }

        /// <summary>
        /// Gets a list of available format names.
        /// </summary>
        public static List<string> GetAvailableFormats()
        {
            return new List<string> { "markdown", "csv" };
        }

        /// <summary>
        /// Converts a dictionary into the current output format.
        /// </summary>
        public static string ConvertDictionary(Dictionary<string, string> dictionary, string columnOneHeader, string columnTwoHeader)
        {
            string result = _currentFormatter.ConvertDictionary(dictionary, columnOneHeader, columnTwoHeader);
            return string.IsNullOrEmpty(result) ? result : "\n" + result + "\n";
        }

        /// <summary>
        /// Converts a SqlDataReader into the current output format.
        /// </summary>
        public static string ConvertSqlDataReader(SqlDataReader reader)
        {
            string result = _currentFormatter.ConvertSqlDataReader(reader);
            return string.IsNullOrEmpty(result) ? result : "\n" + result + "\n";
        }

        /// <summary>
        /// Converts a list into the current output format with a specified column name.
        /// </summary>
        public static string ConvertList(List<string> list, string columnName)
        {
            string result = _currentFormatter.ConvertList(list, columnName);
            return string.IsNullOrEmpty(result) ? result : "\n" + result + "\n";
        }

        /// <summary>
        /// Converts a DataTable into the current output format.
        /// </summary>
        public static string ConvertDataTable(DataTable table)
        {
            string result = _currentFormatter.ConvertDataTable(table);
            return string.IsNullOrEmpty(result) ? result : "\n" + result + "\n";
        }
    }
}
