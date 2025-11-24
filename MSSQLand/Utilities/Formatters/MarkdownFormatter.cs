using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;

namespace MSSQLand.Utilities.Formatters
{
    /// <summary>
    /// Formats data into Markdown-friendly table format.
    /// </summary>
    internal class MarkdownFormatter : IOutputFormatter
    {
        public string FormatName => "markdown";

        /// <summary>
        /// Converts a byte array to a hexadecimal string representation.
        /// </summary>
        private static string ByteArrayToHexString(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return "";
            }

            StringBuilder hex = new(bytes.Length * 2);
            hex.Append("0x");
            foreach (byte b in bytes)
            {
                hex.AppendFormat("{0:X2}", b);
            }
            return hex.ToString();
        }

        public string ConvertDictionary(Dictionary<string, string> dictionary, string columnOneHeader, string columnTwoHeader)
        {
            if (dictionary == null || dictionary.Count == 0)
            {
                return "";
            }

            StringBuilder sqlStringBuilder = new();

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

            return "\n" + sqlStringBuilder.ToString() + "\n";
        }

        public string ConvertSqlDataReader(SqlDataReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader), "SqlDataReader cannot be null.");
            }

            using (reader)
            {
                if (!reader.HasRows)
                {
                    return "No data available.";
                }

                StringBuilder sqlStringBuilder = new();
                var columnWidths = new List<int>();
                var rows = new List<string[]>();

                int columnCount = reader.FieldCount;

                for (int i = 0; i < columnCount; i++)
                {
                    columnWidths.Add(reader.GetName(i).Length);
                }

                while (reader.Read())
                {
                    var row = new string[columnCount];
                    for (int i = 0; i < columnCount; i++)
                    {
                        object value = reader.GetValue(i);
                        string cellValue = value is byte[] byteArray
                            ? ByteArrayToHexString(byteArray)
                            : value?.ToString() ?? "";

                        row[i] = cellValue;
                        columnWidths[i] = Math.Max(columnWidths[i], cellValue.Length);
                    }
                    rows.Add(row);
                }

                for (int i = 0; i < columnCount; i++)
                {
                    string columnName = string.IsNullOrEmpty(reader.GetName(i)) ? $"column{i}" : reader.GetName(i);
                    sqlStringBuilder.Append("| ").Append(columnName.PadRight(columnWidths[i])).Append(" ");
                }
                sqlStringBuilder.AppendLine("|");

                for (int i = 0; i < columnCount; i++)
                {
                    sqlStringBuilder.Append("| ").Append(new string('-', columnWidths[i])).Append(" ");
                }
                sqlStringBuilder.AppendLine("|");

                foreach (var row in rows)
                {
                    for (int i = 0; i < columnCount; i++)
                    {
                        sqlStringBuilder.Append("| ").Append(row[i].PadRight(columnWidths[i])).Append(" ");
                    }
                    sqlStringBuilder.AppendLine("|");
                }

                return "\n" + sqlStringBuilder.ToString() + "\n";
            }
        }

        public string ConvertList(List<string> list, string columnName)
        {
            if (list == null || list.Count == 0)
            {
                return "";
            }

            StringBuilder sqlStringBuilder = new();
            int columnWidth = Math.Max(columnName.Length, list.Max(item => item.Length));

            sqlStringBuilder.Append("| ").Append(columnName.PadRight(columnWidth)).Append(" |").AppendLine();
            sqlStringBuilder.Append("| ").Append(new string('-', columnWidth)).Append(" |").AppendLine();

            foreach (string item in list)
            {
                sqlStringBuilder.Append("| ").Append(item.PadRight(columnWidth)).Append(" |").AppendLine();
            }

            return "\n" + sqlStringBuilder.ToString() + "\n";
        }

        public string ConvertDataTable(DataTable table)
        {
            if (table == null || table.Columns.Count == 0 || table.Rows.Count == 0)
            {
                return "";
            }

            StringBuilder sqlStringBuilder = new();
            var columnWidths = new int[table.Columns.Count];

            for (int i = 0; i < table.Columns.Count; i++)
            {
                columnWidths[i] = table.Columns[i].ColumnName.Length;

                foreach (DataRow row in table.Rows)
                {
                    object value = row[i];
                    string cellValue = value is byte[] byteArray
                        ? ByteArrayToHexString(byteArray)
                        : value?.ToString() ?? "";

                    columnWidths[i] = Math.Max(columnWidths[i], cellValue.Length);
                }
            }

            for (int i = 0; i < table.Columns.Count; i++)
            {
                sqlStringBuilder.Append("| ").Append(table.Columns[i].ColumnName.PadRight(columnWidths[i])).Append(" ");
            }
            sqlStringBuilder.AppendLine("|");

            foreach (var width in columnWidths)
            {
                sqlStringBuilder.Append("| ").Append(new string('-', width)).Append(" ");
            }
            sqlStringBuilder.AppendLine("|");

            foreach (DataRow row in table.Rows)
            {
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    object value = row[i];
                    string cellValue = value is byte[] byteArray
                        ? ByteArrayToHexString(byteArray)
                        : value?.ToString() ?? "";

                    sqlStringBuilder.Append("| ").Append(cellValue.PadRight(columnWidths[i])).Append(" ");
                }
                sqlStringBuilder.AppendLine("|");
            }

            return sqlStringBuilder.ToString();
        }
    }
}
