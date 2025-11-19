using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace MSSQLand.Utilities.Formatters
{
    /// <summary>
    /// Interface for output formatters that convert data structures to specific formats.
    /// </summary>
    internal interface IOutputFormatter
    {
        /// <summary>
        /// Gets the name of the formatter (e.g., "markdown", "csv").
        /// </summary>
        string FormatName { get; }

        /// <summary>
        /// Converts a dictionary into a formatted table.
        /// </summary>
        string ConvertDictionary(Dictionary<string, string> dictionary, string columnOneHeader, string columnTwoHeader);

        /// <summary>
        /// Converts a SqlDataReader into a formatted table.
        /// </summary>
        string ConvertSqlDataReader(SqlDataReader reader);

        /// <summary>
        /// Converts a list into a formatted table with a specified column name.
        /// </summary>
        string ConvertList(List<string> list, string columnName);

        /// <summary>
        /// Converts a DataTable into a formatted table.
        /// </summary>
        string ConvertDataTable(DataTable table);
    }
}
