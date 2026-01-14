// MSSQLand/Utilities/Misc.cs

using System;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.IO.Compression;
using System.IO;
using System.Security.Cryptography;
using System.Xml;

namespace MSSQLand.Utilities
{
    internal class Misc
    {
        private static readonly Random _random = new Random();

        /// <summary>
        /// Sanitizes a string to ensure it contains only valid UTF-8 characters.
        /// Replaces Windows-1252 control characters and other invalid bytes with safe alternatives.
        /// </summary>
        /// <param name="input">The string to sanitize</param>
        /// <returns>A UTF-8 safe string</returns>
        public static string SanitizeToUtf8(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? "";

            var sb = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (c == '\t' || c == '\n' || c == '\r')
                    sb.Append(c);  // Keep common whitespace
                else if (c < 0x20)
                    sb.Append(' ');  // Control characters -> space
                else if (c == 0xA0)
                    sb.Append(' ');  // Non-breaking space -> regular space
                else if (c >= 0x80 && c <= 0x9F)
                    sb.Append('?');  // Windows-1252 control range
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Generates a random number within the specified range (inclusive of min, exclusive of max).
        /// </summary>
        /// <param name="min">The inclusive lower bound of the random number.</param>
        /// <param name="max">The exclusive upper bound of the random number.</param>
        /// <returns>A random integer between min (inclusive) and max (exclusive).</returns>
        public static int GetRandomNumber(int min, int max)
        {
            return _random.Next(min, max);
        }

        /// <summary>
        /// Converts a nibble (4-bit value from 0 to 15) into its corresponding hexadecimal character.
        /// This method avoids allocations and conditionally returns either uppercase or lowercase letters.
        ///
        /// Example:
        /// <c>GetHexChar(10, true) => 'A'</c>
        /// <c>GetHexChar(10, false) => 'a'</c>
        /// </summary>
        /// <param name="value">An integer from 0 to 15 representing the nibble.</param>
        /// <param name="upper">If true, returns uppercase ('A'-'F'); otherwise, lowercase ('a'-'f').</param>
        /// <returns>A hexadecimal character corresponding to the input nibble.</returns>
        public static char GetHexChar(int value, bool upper)
        {
            return (char)(value < 10 ? '0' + value : (upper ? 'A' : 'a') + (value - 10));
        }

        /// <summary>
        /// Decodes a Base64-encoded string and decompresses it using GZip.
        /// </summary>
        /// <param name="encoded">The Base64-encoded, GZip-compressed data.</param>
        /// <returns>The decompressed byte array.</returns>
        public static byte[] DecodeAndDecompress(string encoded)
        {
            byte[] compressedBytes = Convert.FromBase64String(encoded);
            using var inputStream = new MemoryStream(compressedBytes);
            using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();
            gzipStream.CopyTo(outputStream);
            return outputStream.ToArray();
        }

        /// <summary>
        /// Converts a hexadecimal string to a byte array.
        /// </summary>
        /// <param name="hex">The hexadecimal string (must have even length).</param>
        /// <returns>The byte array representation of the hex string.</returns>
        /// <example>
        /// HexStringToBytes("48656C6C6F") => { 0x48, 0x65, 0x6C, 0x6C, 0x6F } ("Hello")
        /// </example>
        public static byte[] HexStringToBytes(string hex)
        {
            int length = hex.Length;
            byte[] bytes = new byte[length / 2];
            for (int i = 0; i < length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        /// <summary>
        /// Converts a byte array to a lowercase hexadecimal string.
        /// </summary>
        /// <param name="bytes">The byte array to convert.</param>
        /// <returns>A lowercase hexadecimal string representation.</returns>
        /// <example>
        /// BytesToHexString(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }) => "48656c6c6f"
        /// </example>
        public static string BytesToHexString(byte[] bytes)
        {
            StringBuilder hex = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                hex.AppendFormat("{0:x2}", b);
            }
            return hex.ToString();
        }

        /// <summary>
        /// Gets a random available TCP port on the loopback interface.
        /// Binds a socket to port 0 which causes the OS to assign an available ephemeral port.
        /// </summary>
        /// <returns>An available TCP port number.</returns>
        public static int GetRandomUnusedPort()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint).Port;
        }

        /// <summary>
        /// Computes a SHA-256 hash from an input string.
        /// </summary>
        public static string ComputeSHA256(string input)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = sha256.ComputeHash(inputBytes);

            return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        }

        /// <summary>
        /// Computes a SHA-256 hash from a byte array.
        /// </summary>
        public static string ComputeSHA256(byte[] input)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(input);

            return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        }

        /// <summary>
        /// Detects the text encoding of a byte array by examining its Byte Order Mark (BOM).
        /// Supports UTF-8, UTF-16 LE, and UTF-16 BE encodings.
        /// </summary>
        /// <param name="data">The byte array to analyze.</param>
        /// <returns>A tuple containing the detected encoding and the BOM length in bytes.</returns>
        /// <example>
        /// DetectEncoding(new byte[] { 0xEF, 0xBB, 0xBF, ... }) => (Encoding.UTF8, 3)
        /// DetectEncoding(new byte[] { 0xFF, 0xFE, ... }) => (Encoding.Unicode, 2)  // UTF-16 LE
        /// DetectEncoding(new byte[] { 0x48, 0x65, ... }) => (Encoding.UTF8, 0)     // No BOM, default UTF-8
        /// </example>
        public static (Encoding Encoding, int BomLength) DetectEncoding(byte[] data)
        {
            if (data == null || data.Length == 0)
                return (Encoding.UTF8, 0);

            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return (Encoding.UTF8, 3);

            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return (Encoding.Unicode, 2); // UTF-16 LE

            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
                return (Encoding.BigEndianUnicode, 2); // UTF-16 BE

            // Default: UTF-8 without BOM
            return (Encoding.UTF8, 0);
        }

        /// <summary>
        /// Decodes a byte array to a string using the specified encoding, starting at the given offset.
        /// Typically used after <see cref="DetectEncoding"/> to skip the BOM bytes.
        /// </summary>
        /// <param name="data">The byte array containing text data.</param>
        /// <param name="encoding">The encoding to use for decoding.</param>
        /// <param name="offset">The starting offset (typically the BOM length).</param>
        /// <returns>The decoded string, or empty string if data is null or too short.</returns>
        public static string DecodeText(byte[] data, Encoding encoding, int offset)
        {
            if (data == null || data.Length <= offset)
                return string.Empty;

            return encoding.GetString(data, offset, data.Length - offset);
        }

        /// <summary>
        /// Formats (beautifies) an XML string with proper indentation and line breaks.
        /// Also decodes HTML entities (like &amp;lt; to &lt;) in text content and attributes.
        /// Useful for displaying large XML data in a readable format.
        /// </summary>
        /// <param name="xmlData">The XML string to format.</param>
        /// <param name="indent">The indentation string to use (default is two spaces).</param>
        /// <returns>Formatted XML string, or the original string if parsing fails.</returns>
        /// <example>
        /// string xml = "&lt;root&gt;&lt;child&gt;value&lt;/child&gt;&lt;/root&gt;";
        /// string formatted = BeautifyXml(xml);
        /// // Returns:
        /// // &lt;root&gt;
        /// //   &lt;child&gt;value&lt;/child&gt;
        /// // &lt;/root&gt;
        /// </example>
        public static string BeautifyXml(string xmlData, string indent = "  ")
        {
            if (string.IsNullOrEmpty(xmlData))
                return xmlData;

            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xmlData);

                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = indent,
                    NewLineChars = "\n",
                    NewLineHandling = NewLineHandling.Replace,
                    OmitXmlDeclaration = false,
                    Encoding = Encoding.UTF8
                };

                using var stringWriter = new StringWriter();
                using var xmlWriter = XmlWriter.Create(stringWriter, settings);
                doc.Save(xmlWriter);
                
                // Decode HTML entities in the final formatted output for better readability
                // XmlWriter encodes entities for valid XML, but we want human-readable output
                string formatted = stringWriter.ToString();
                return System.Net.WebUtility.HtmlDecode(formatted);
            }
            catch
            {
                // If parsing fails, return original
                return xmlData;
            }
        }



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
            if (name.IndexOfAny(new[] { ':', '/', '@', ';' }) >= 0)
            {
                return $"[{name}]";
            }
            return name;
        }

        /// <summary>
        /// Strips square brackets from a SQL Server identifier.
        /// Used when parsing user input that may contain bracketed identifiers.
        /// </summary>
        /// <param name="identifier">The identifier that may have brackets.</param>
        /// <returns>The identifier without surrounding brackets.</returns>
        /// <example>
        /// StripBrackets("[database]") => "database"
        /// StripBrackets("database") => "database"
        /// StripBrackets(null) => null
        /// </example>
        private static string StripBrackets(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return identifier;
            return identifier.Trim('[', ']');
        }

        /// <summary>
        /// Converts byte count to human-readable format with appropriate unit (B, KB, MB, GB, TB).
        /// Uses binary units (1024 bytes = 1 KB) and formats with 2 decimal places.
        /// </summary>
        /// <param name="bytes">The number of bytes to format.</param>
        /// <param name="showBytes">If true, includes raw byte count in parentheses (default: true).</param>
        /// <returns>Formatted string like "499.29 KB (511,272 bytes)" or "1.5 MB".</returns>
        /// <example>
        /// FormatByteSize(512) => "512 B"
        /// FormatByteSize(1536) => "1.50 KB (1,536 bytes)"
        /// FormatByteSize(1048576) => "1.00 MB (1,048,576 bytes)"
        /// FormatByteSize(511272) => "499.29 KB (511,272 bytes)"
        /// FormatByteSize(0) => "0 B"
        /// </example>
        public static string FormatByteSize(long bytes, bool showBytes = true)
        {
            if (bytes == 0)
                return "0 B";

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unitIndex = 0;
            double size = bytes;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            // For bytes, don't show decimal places
            if (unitIndex == 0)
                return $"{bytes:N0} B";

            // For larger units, show 2 decimal places and optionally raw bytes
            string formatted = $"{size:F2} {units[unitIndex]}";
            if (showBytes)
                formatted += $" ({bytes:N0} bytes)";

            return formatted;
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

            var parts = new System.Collections.Generic.List<string>();
            var current = new StringBuilder();
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
        /// Returns null if the string is empty or whitespace, otherwise returns the trimmed string.
        /// </summary>
        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Builds a fully qualified table name in SQL Server format.
        /// Handles empty schema by using the double-dot notation (database..table).
        /// Automatically strips brackets from inputs to prevent double-bracketing.
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

            // Strip brackets from inputs to prevent double-bracketing
            database = StripBrackets(database);
            table = StripBrackets(table);

            if (string.IsNullOrEmpty(schema))
            {
                // Format: [database]..[table] (no schema)
                return $"[{database}]..[{table}]";
            }
            else
            {
                // Format: [database].[schema].[table]
                schema = StripBrackets(schema);
                return $"[{database}].[{schema}].[{table}]";
            }
        }
    }
}
