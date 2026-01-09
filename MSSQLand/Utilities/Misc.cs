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


        public static byte[] DecodeAndDecompress(string encoded)
        {
            byte[] compressedBytes = Convert.FromBase64String(encoded);
            using var inputStream = new MemoryStream(compressedBytes);
            using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();
            gzipStream.CopyTo(outputStream);
            return outputStream.ToArray();
        }

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

        public static string BytesToHexString(byte[] bytes)
        {
            StringBuilder hex = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                hex.AppendFormat("{0:x2}", b);
            }
            return hex.ToString();
        }

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

        public static string GetFullExceptionMessage(Exception ex)
        {
            var message = ex.Message;
            var inner = ex.InnerException;
            while (inner != null)
            {
                message += " --> " + inner.Message;
                inner = inner.InnerException;
            }
            return message;
        }

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

        public static string DecodeText(byte[] data, Encoding encoding, int offset)
        {
            if (data == null || data.Length <= offset)
                return string.Empty;

            return encoding.GetString(data, offset, data.Length - offset);
        }

        /// <summary>
        /// Extracts a value from an XML string using an XPath expression.
        /// </summary>
        /// <param name="xmlData">The XML string to parse.</param>
        /// <param name="xpath">The XPath expression to locate the node.</param>
        /// <returns>The inner text of the first matching node, or null if not found.</returns>
        /// <example>
        /// string xml = "&lt;Settings&gt;&lt;Version&gt;1.0&lt;/Version&gt;&lt;/Settings&gt;";
        /// string version = GetXmlValue(xml, "//Version"); // Returns "1.0"
        /// </example>
        public static string GetXmlValue(string xmlData, string xpath)
        {
            if (string.IsNullOrEmpty(xmlData) || string.IsNullOrEmpty(xpath))
                return null;

            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xmlData);
                var node = doc.SelectSingleNode(xpath);
                return node?.InnerText;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts an attribute value from an XML string using an XPath expression.
        /// </summary>
        /// <param name="xmlData">The XML string to parse.</param>
        /// <param name="xpath">The XPath expression to locate the node.</param>
        /// <param name="attributeName">The name of the attribute to extract.</param>
        /// <returns>The attribute value, or null if not found.</returns>
        /// <example>
        /// string xml = "&lt;Property Name='Version' Value='1.0'/&gt;";
        /// string value = GetXmlAttribute(xml, "//Property[@Name='Version']", "Value"); // Returns "1.0"
        /// </example>
        public static string GetXmlAttribute(string xmlData, string xpath, string attributeName)
        {
            if (string.IsNullOrEmpty(xmlData) || string.IsNullOrEmpty(xpath) || string.IsNullOrEmpty(attributeName))
                return null;

            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xmlData);
                var node = doc.SelectSingleNode(xpath);
                return node?.Attributes?[attributeName]?.Value;
            }
            catch
            {
                return null;
            }
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

                // Decode HTML entities in all text nodes and attributes
                DecodeXmlEntities(doc.DocumentElement);

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
                return stringWriter.ToString();
            }
            catch
            {
                // If parsing fails, return original
                return xmlData;
            }
        }

        /// <summary>
        /// Recursively decodes HTML entities in XML nodes (text content and attributes).
        /// Converts &amp;lt; to &lt;, &amp;gt; to &gt;, &amp;amp; to &amp;, etc.
        /// </summary>
        private static void DecodeXmlEntities(XmlNode node)
        {
            if (node == null) return;

            // Decode text content
            if (node.NodeType == XmlNodeType.Text)
            {
                node.Value = System.Net.WebUtility.HtmlDecode(node.Value);
            }

            // Decode attributes
            if (node.Attributes != null)
            {
                foreach (XmlAttribute attr in node.Attributes)
                {
                    attr.Value = System.Net.WebUtility.HtmlDecode(attr.Value);
                }
            }

            // Recursively process child nodes
            foreach (XmlNode child in node.ChildNodes)
            {
                DecodeXmlEntities(child);
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
        public static string StripBrackets(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return identifier;
            return identifier.Trim('[', ']');
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
