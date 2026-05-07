// MSSQLand/Utilities/EncodingHelper.cs

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;

namespace MSSQLand.Utilities
{
    internal static class EncodingHelper
    {
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
        /// Converts a string to Base64 encoding using UTF-16 (Unicode) encoding.
        /// PowerShell's -EncodedCommand flag expects UTF-16 LE Base64.
        /// </summary>
        /// <param name="input">The input string to encode.</param>
        /// <returns>The Base64-encoded string.</returns>
        public static string ConvertToBase64(string input)
        {
            byte[] inputBytes = Encoding.Unicode.GetBytes(input);
            return Convert.ToBase64String(inputBytes);
        }

        /// <summary>
        /// Formats (beautifies) an XML string with proper indentation and line breaks.
        /// Also decodes HTML entities (like &amp;lt; to &lt;) in text content and attributes.
        /// Useful for displaying large XML data in a readable format.
        /// </summary>
        /// <param name="xmlData">The XML string to format.</param>
        /// <param name="indent">The indentation string to use (default is two spaces).</param>
        /// <returns>Formatted XML string, or the original string if parsing fails.</returns>
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
                return WebUtility.HtmlDecode(formatted);
            }
            catch
            {
                // If parsing fails, return original
                return xmlData;
            }
        }
    }
}
