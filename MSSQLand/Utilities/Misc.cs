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

        private static (Encoding Encoding, int BomLength) DetectEncoding(ReadOnlySpan<byte> data)
        {
            if (data.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
                return (Encoding.UTF8, 3);

            if (data.StartsWith(new byte[] { 0xFF, 0xFE }))
                return (Encoding.Unicode, 2); // UTF-16 LE

            if (data.StartsWith(new byte[] { 0xFE, 0xFF }))
                return (Encoding.BigEndianUnicode, 2); // UTF-16 BE

            // Default: UTF-8 without BOM
            return (Encoding.UTF8, 0);
        }

        private static string DecodeText(byte[] data, Encoding encoding, int offset)
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
    }
}
