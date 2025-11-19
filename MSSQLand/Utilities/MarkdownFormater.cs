using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Utilities
{
    /// <summary>
    /// Provides methods to format data into various output formats.
    /// This class is a wrapper around OutputFormatter for backward compatibility.
    /// Use OutputFormatter directly for new code.
    /// </summary>
    internal static class MarkdownFormatter
    {
        /// <summary>
        /// Converts a dictionary into a formatted table.
        /// </summary>
        internal static string ConvertDictionaryToMarkdownTable(Dictionary<string, string> dictionary, string columnOneHeader, string columnTwoHeader)
        {
            return OutputFormatter.ConvertDictionary(dictionary, columnOneHeader, columnTwoHeader);
        }

        /// <summary>
        /// Converts a SqlDataReader into a formatted table.
        /// </summary>
        internal static string ConvertSqlDataReaderToMarkdownTable(SqlDataReader reader)
        {
            return OutputFormatter.ConvertSqlDataReader(reader);
        }

        /// <summary>
        /// Converts a list into a formatted table with a specified column name.
        /// </summary>
        internal static string ConvertListToMarkdownTable(List<string> list, string columnName)
        {
            return OutputFormatter.ConvertList(list, columnName);
        }

        /// <summary>
        /// Converts a DataTable into a formatted table.
        /// </summary>
        internal static string ConvertDataTableToMarkdownTable(DataTable table)
        {
            return OutputFormatter.ConvertDataTable(table);
        }
    }
}
