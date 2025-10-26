using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;

namespace MSSQLand.Utilities
{
    /// <summary>
    /// Provides methods to format data into Markdown-friendly table formats.
    /// </summary>
    internal static class MarkdownFormatter
    {
        /// <summary>
        /// Converts a byte array to a hexadecimal string representation.
        /// </summary>
        private static string ByteArrayToHexString(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return "";
            }

            StringBuilder hex = new (bytes.Length * 2);
            hex.Append("0x");
            foreach (byte b in bytes)
            {
                hex.AppendFormat("{0:X2}", b);
            }
            return hex.ToString();
        }

        /// <summary>
        /// Converts a dictionary into a Markdown-friendly table format.
        /// </summary>
        internal static string ConvertDictionaryToMarkdownTable(Dictionary<string, string> dictionary, string columnOneHeader, string columnTwoHeader)
        {
            // Check if the dictionary is null or empty
            if (dictionary == null || dictionary.Count == 0)
            {
                return "";
            }

            StringBuilder sqlStringBuilder = new("\n");

            if (dictionary.Count > 0)
            {
                dictionary.Add(columnOneHeader, columnTwoHeader);

                int columnOneWidth = dictionary.Max(t => t.Key.Length);
                int columnTwoWidth = dictionary.Max(t => t.Value.Length);

                sqlStringBuilder.Append("| ").Append(columnOneHeader.PadRight(columnOneWidth)).Append(" ");
                sqlStringBuilder.Append("| ").Append(columnTwoHeader.PadRight(columnTwoWidth)).Append(" |");
                sqlStringBuilder.AppendLine();

                sqlStringBuilder.Append("| ").Append(new string('-', columnOneWidth)).Append(" ");
                sqlStringBuilder.Append("| ").Append(new string('-', columnTwoWidth)).Append(" |");
                sqlStringBuilder.AppendLine();

                foreach (var item in dictionary.Take(dictionary.Count - 1))
                {
                    sqlStringBuilder.Append("| ").Append(item.Key.PadRight(columnOneWidth)).Append(" ");
                    sqlStringBuilder.Append("| ").Append(item.Value.PadRight(columnTwoWidth)).Append(" |");
                    sqlStringBuilder.AppendLine();
                }
            }

            return sqlStringBuilder.ToString();
        }

        /// <summary>
        /// Converts a SqlDataReader into a Markdown-friendly table format.
        /// Ensures the reader is properly closed after use.
        /// </summary>
        internal static string ConvertSqlDataReaderToMarkdownTable(SqlDataReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader), "SqlDataReader cannot be null.");
            }

            using (reader) // Ensure the reader is disposed of properly
            {
                if (!reader.HasRows)
                {
                    return "No data available.";
                }

                StringBuilder sqlStringBuilder = new("\n");
                var columnWidths = new List<int>();
                var rows = new List<string[]>();

                int columnCount = reader.FieldCount;

                // Initialize column widths
                for (int i = 0; i < columnCount; i++)
                {
                    columnWidths.Add(reader.GetName(i).Length);
                }

                // Read rows and calculate column widths
                while (reader.Read())
                {
                    var row = new string[columnCount];
                    for (int i = 0; i < columnCount; i++)
                    {
                        object value = reader.GetValue(i);
                        string cellValue;
                        
                        // Handle byte arrays specially
                        if (value is byte[] byteArray)
                        {
                            cellValue = ByteArrayToHexString(byteArray);
                        }
                        else
                        {
                            cellValue = value?.ToString() ?? "";
                        }
                        
                        row[i] = cellValue;
                        columnWidths[i] = Math.Max(columnWidths[i], cellValue.Length);
                    }
                    rows.Add(row);
                }

                // Add column headers
                for (int i = 0; i < columnCount; i++)
                {
                    string columnName = string.IsNullOrEmpty(reader.GetName(i)) ? $"column{i}" : reader.GetName(i);
                    sqlStringBuilder.Append("| ").Append(columnName.PadRight(columnWidths[i])).Append(" ");
                }
                sqlStringBuilder.AppendLine("|");

                // Add separator row
                for (int i = 0; i < columnCount; i++)
                {
                    sqlStringBuilder.Append("| ").Append(new string('-', columnWidths[i])).Append(" ");
                }
                sqlStringBuilder.AppendLine("|");

                // Add rows
                foreach (var row in rows)
                {
                    for (int i = 0; i < columnCount; i++)
                    {
                        sqlStringBuilder.Append("| ").Append(row[i].PadRight(columnWidths[i])).Append(" ");
                    }
                    sqlStringBuilder.AppendLine("|");
                }

                return sqlStringBuilder.ToString();
            }
        }


        /// <summary>
        /// Converts a list into a Markdown-friendly table format with a specified column name.
        /// </summary>
        /// <param name="list">The list of strings to convert.</param>
        /// <param name="columnName">The name of the column.</param>
        /// <returns>A string containing the Markdown-formatted table.</returns>
        internal static string ConvertListToMarkdownTable(List<string> list, string columnName)
        {
            if (list == null || list.Count == 0)
            {
                return "";
            }

            StringBuilder sqlStringBuilder = new("\n");

            // Determine the max width for the column
            int columnWidth = Math.Max(columnName.Length, list.Max(item => item.Length));

            // Add header row
            sqlStringBuilder.Append("| ").Append(columnName.PadRight(columnWidth)).Append(" |").AppendLine();

            // Add separator row
            sqlStringBuilder.Append("| ").Append(new string('-', columnWidth)).Append(" |").AppendLine();

            // Add data rows
            foreach (string item in list)
            {
                sqlStringBuilder.Append("| ").Append(item.PadRight(columnWidth)).Append(" |").AppendLine();
            }

            return sqlStringBuilder.ToString();
        }



        /// <summary>
        /// Converts a DataTable into a Markdown-friendly table format.
        /// </summary>
        /// <param name="table">The DataTable to convert.</param>
        /// <returns>A string containing the Markdown-formatted table.</returns>
        internal static string ConvertDataTableToMarkdownTable(DataTable table)
        {
            // Check if the DataTable is null or has no rows/columns
            if (table == null || table.Columns.Count == 0 || table.Rows.Count == 0)
            {
                return "";
            }

            StringBuilder sqlStringBuilder = new("\n");
            var columnWidths = new int[table.Columns.Count];

            // Determine column widths
            for (int i = 0; i < table.Columns.Count; i++)
            {
                columnWidths[i] = table.Columns[i].ColumnName.Length;

                foreach (DataRow row in table.Rows)
                {
                    object value = row[i];
                    string cellValue;
                    
                    // Handle byte arrays specially
                    if (value is byte[] byteArray)
                    {
                        cellValue = ByteArrayToHexString(byteArray);
                    }
                    else
                    {
                        cellValue = value?.ToString() ?? "";
                    }
                    
                    columnWidths[i] = Math.Max(columnWidths[i], cellValue.Length);
                }
            }

            // Add header row
            for (int i = 0; i < table.Columns.Count; i++)
            {
                sqlStringBuilder.Append("| ").Append(table.Columns[i].ColumnName.PadRight(columnWidths[i])).Append(" ");
            }
            sqlStringBuilder.AppendLine("|");

            // Add separator row
            foreach (var width in columnWidths)
            {
                sqlStringBuilder.Append("| ").Append(new string('-', width)).Append(" ");
            }
            sqlStringBuilder.AppendLine("|");

            // Add data rows
            foreach (DataRow row in table.Rows)
            {
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    object value = row[i];
                    string cellValue;
                    
                    // Handle byte arrays specially
                    if (value is byte[] byteArray)
                    {
                        cellValue = ByteArrayToHexString(byteArray);
                    }
                    else
                    {
                        cellValue = value?.ToString() ?? "";
                    }
                    
                    sqlStringBuilder.Append("| ").Append(cellValue.PadRight(columnWidths[i])).Append(" ");
                }
                sqlStringBuilder.AppendLine("|");
            }

            return sqlStringBuilder.ToString();
        }
    }
}
