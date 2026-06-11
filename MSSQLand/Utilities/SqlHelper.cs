// MSSQLand/Utilities/SqlHelper.cs

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MSSQLand.Utilities
{
    internal static class SqlHelper
    {
        private static readonly char[] _bracketDelimiters = { ':', '/', '@', ';' };

        /// <summary>
        /// Wraps SQL Server identifier in brackets if it contains separator characters.
        /// Only brackets if the name contains delimiters used in our syntax: : / @ ;
        ///
        /// This is used for linked server chains where hostnames may contain these
        /// characters and need protection from being interpreted as delimiters.
        /// </summary>
        /// <param name="name">The identifier name to potentially bracket.</param>
        /// <returns>Bracketed identifier if separators present, otherwise unchanged.</returns>
        /// <example>
        /// BracketIdentifier("SQL01") => "SQL01"
        /// BracketIdentifier("SQL02;PROD") => "[SQL02;PROD]"
        /// BracketIdentifier("SQL03/TEST") => "[SQL03/TEST]"
        /// BracketIdentifier("SQL04@INST") => "[SQL04@INST]"
        /// BracketIdentifier("SQL05:8080") => "[SQL05:8080]"
        /// </example>
        public static string BracketIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            // Only bracket if name contains our special delimiter characters
            return name.IndexOfAny(_bracketDelimiters) >= 0 ? $"[{name}]" : name;
        }

        /// <summary>
        /// Parses a fully qualified table name into its components (database, schema, table).
        /// Handles bracketed identifiers correctly, including names with embedded dots.
        /// </summary>
        /// <param name="fqtn">The fully qualified table name to parse.</param>
        /// <returns>Tuple of (database, schema, table) - database and schema may be null.</returns>
        /// <example>
        /// ParseQualifiedTableName("Users") => (null, null, "Users")
        /// ParseQualifiedTableName("dbo.Users") => (null, "dbo", "Users")
        /// ParseQualifiedTableName("master.dbo.Users") => ("master", "dbo", "Users")
        /// ParseQualifiedTableName("[my.db].[dbo].[table]") => ("my.db", "dbo", "table")
        /// ParseQualifiedTableName("[CM_PSC]..[v_R_System]") => ("CM_PSC", null, "v_R_System")
        /// </example>
        public static (string Database, string Schema, string Table) ParseQualifiedTableName(string fqtn)
        {
            if (string.IsNullOrWhiteSpace(fqtn))
                throw new ArgumentException("Table name cannot be null or empty.", nameof(fqtn));

            List<string> parts = new();
            StringBuilder current = new();
            bool inBrackets = false;

            for (int i = 0; i < fqtn.Length; i++)
            {
                char c = fqtn[i];

                if (c == '[' && !inBrackets)
                {
                    inBrackets = true;
                }
                else if (c == ']' && inBrackets)
                {
                    // Check for escaped bracket ]]
                    if (i + 1 < fqtn.Length && fqtn[i + 1] == ']')
                    {
                        current.Append(']');
                        i++; // Skip next bracket
                    }
                    else
                    {
                        inBrackets = false;
                    }
                }
                else if (c == '.' && !inBrackets)
                {
                    // Separator found - add current part and reset
                    parts.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            // Add final part
            parts.Add(current.ToString());

            // Validate and return based on part count
            return parts.Count switch
            {
                1 => (null, null, NullIfEmpty(parts[0])),
                2 => (null, NullIfEmpty(parts[0]), NullIfEmpty(parts[1])),
                3 => (NullIfEmpty(parts[0]), NullIfEmpty(parts[1]), NullIfEmpty(parts[2])),
                _ => throw new ArgumentException($"Invalid table name format: '{fqtn}'. Expected [table], [schema.table], or [database.schema.table].")
            };
        }

        /// <summary>
        /// Builds a fully qualified table name in SQL Server format.
        /// Handles empty schema by using the double-dot notation (database..table).
        /// Automatically handles identifiers that may already be bracketed.
        /// </summary>
        /// <param name="database">Database name (required).</param>
        /// <param name="schema">Schema name (optional - if null/empty, uses double-dot notation).</param>
        /// <param name="table">Table name (required).</param>
        /// <returns>Fully qualified table name with proper bracket escaping.</returns>
        /// <example>
        /// BuildQualifiedTableName("CM_PSC", "dbo", "v_R_System") => "[CM_PSC].[dbo].[v_R_System]"
        /// BuildQualifiedTableName("[CM_PSC]", "[dbo]", "[v_R_System]") => "[CM_PSC].[dbo].[v_R_System]"
        /// BuildQualifiedTableName("CM_PSC", null, "v_R_System") => "[CM_PSC]..[v_R_System]"
        /// BuildQualifiedTableName("CM_PSC", "", "v_R_System") => "[CM_PSC]..[v_R_System]"
        /// </example>
        public static string BuildQualifiedTableName(string database, string schema, string table)
        {
            if (string.IsNullOrEmpty(database))
                throw new ArgumentException("Database name cannot be null or empty.", nameof(database));
            if (string.IsNullOrEmpty(table))
                throw new ArgumentException("Table name cannot be null or empty.", nameof(table));

            // Format: [database]..[table] (no schema) or [database].[schema].[table]
            return string.IsNullOrEmpty(schema)
                ? $"[{database.Trim('[', ']')}]..[{table.Trim('[', ']')}]"
                : $"[{database.Trim('[', ']')}].[{schema.Trim('[', ']')}].[{table.Trim('[', ']')}]";
        }

        /// <summary>
        /// Replaces large hex literals in a SQL string with a truncated placeholder
        /// so log lines stay readable. Keeps the first 8 hex chars and appends &lt;strip&gt;.
        /// Only intended for display — never pass the result back to SQL Server.
        /// </summary>
        public static string StripHexPayload(string query)
            => Regex.Replace(query, @"0x([0-9A-Fa-f]{9,})",
                m => $"0x{m.Groups[1].Value.Substring(0, 8)}<strip>");

        /// <summary>
        /// Returns null if the string is empty or whitespace, otherwise returns the trimmed string.
        /// </summary>
        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
