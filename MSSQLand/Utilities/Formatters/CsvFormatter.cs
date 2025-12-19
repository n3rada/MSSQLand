// MSSQLand/Utilities/Formatters/CsvFormatter.cs

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;

namespace MSSQLand.Utilities.Formatters
{
    /// <summary>
    /// Formats data into CSV (Comma-Separated Values) format.
    /// Uses semicolon (;) as separator for better compatibility with European Excel versions.
    /// </summary>
    internal class CsvFormatter : IOutputFormatter
    {
        public string FormatName => "csv";

        private const char Separator = ';';

        /// <summary>
        /// Escapes CSV values by wrapping in quotes if they contain separators, quotes, or newlines.
        /// </summary>
        private static string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            if (value.Contains(Separator) || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

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

            StringBuilder csvBuilder = new();

            // Header
            csvBuilder.AppendLine($"{EscapeCsvValue(columnOneHeader)}{Separator}{EscapeCsvValue(columnTwoHeader)}");

            // Rows
            foreach (var item in dictionary)
            {
                csvBuilder.AppendLine($"{EscapeCsvValue(item.Key)}{Separator}{EscapeCsvValue(item.Value)}");
            }

            return csvBuilder.ToString();
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

                StringBuilder csvBuilder = new();
                int columnCount = reader.FieldCount;

                // Header
                var headers = new List<string>();
                for (int i = 0; i < columnCount; i++)
                {
                    string columnName = string.IsNullOrEmpty(reader.GetName(i)) ? $"column{i}" : reader.GetName(i);
                    headers.Add(EscapeCsvValue(columnName));
                }
                csvBuilder.AppendLine(string.Join(Separator.ToString(), headers));

                // Rows
                while (reader.Read())
                {
                    var values = new List<string>();
                    for (int i = 0; i < columnCount; i++)
                    {
                        object value = reader.GetValue(i);
                        string cellValue = value is byte[] byteArray
                            ? ByteArrayToHexString(byteArray)
                            : value?.ToString() ?? "";

                        values.Add(EscapeCsvValue(cellValue));
                    }
                    csvBuilder.AppendLine(string.Join(Separator.ToString(), values));
                }

                return csvBuilder.ToString();
            }
        }

        public string ConvertList(List<string> list, string columnName)
        {
            if (list == null || list.Count == 0)
            {
                return "";
            }

            StringBuilder csvBuilder = new();

            // Header
            csvBuilder.AppendLine(EscapeCsvValue(columnName));

            // Rows
            foreach (string item in list)
            {
                csvBuilder.AppendLine(EscapeCsvValue(item));
            }

            return csvBuilder.ToString();
        }

        public string ConvertDataTable(DataTable table)
        {
            if (table == null || table.Columns.Count == 0 || table.Rows.Count == 0)
            {
                return "";
            }

            StringBuilder csvBuilder = new();

            // Header
            var headers = new List<string>();
            foreach (DataColumn column in table.Columns)
            {
                headers.Add(EscapeCsvValue(column.ColumnName));
            }
            csvBuilder.AppendLine(string.Join(Separator.ToString(), headers));

            // Rows
            foreach (DataRow row in table.Rows)
            {
                var values = new List<string>();
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    object value = row[i];
                    string cellValue = value is byte[] byteArray
                        ? ByteArrayToHexString(byteArray)
                        : value?.ToString() ?? "";

                    values.Add(EscapeCsvValue(cellValue));
                }
                csvBuilder.AppendLine(string.Join(Separator.ToString(), values));
            }

            return csvBuilder.ToString();
        }
    }
}
