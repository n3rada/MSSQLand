// MSSQLand/Utilities/ByteHelper.cs

using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MSSQLand.Utilities
{
    internal static class ByteHelper
    {
        private static readonly Random _random = new();

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
        /// Generates a random identifier string using hexadecimal characters.
        /// Useful for creating unique names for temporary SQL objects (functions, assemblies, etc.).
        /// </summary>
        /// <param name="length">The length of the random string (default: 8).</param>
        /// <returns>A random hexadecimal string of the specified length.</returns>
        /// <example>
        /// GetRandomIdentifier() => "a3f7c2b1"
        /// GetRandomIdentifier(4) => "e9d2"
        /// </example>
        public static string GetRandomIdentifier(int length = 8)
        {
            return Guid.NewGuid().ToString("N").Substring(0, length);
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
        /// Decompresses a GZip-compressed byte array.
        /// </summary>
        /// <param name="compressed">The GZip-compressed byte array.</param>
        /// <returns>The decompressed byte array.</returns>
        public static byte[] GzipDecompress(byte[] compressed)
        {
            using var inputStream = new MemoryStream(compressed);
            using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();
            gzipStream.CopyTo(outputStream);
            return outputStream.ToArray();
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
        /// Computes the SHA-512 hash and SQL-compatible hex string from raw assembly bytes.
        /// </summary>
        /// <returns>
        /// <c>[0]</c> = SHA-512 hash (lowercase hex), <c>[1]</c> = DLL content (uppercase hex).
        /// </returns>
        public static string[] ConvertDllToSqlBytes(byte[] dllBytes)
        {
            byte[] hashBytes;
            using (SHA512 sha512 = SHA512.Create())
            {
                hashBytes = sha512.ComputeHash(dllBytes);
            }

            char[] hashChars = new char[hashBytes.Length * 2];
            char[] dllHexChars = new char[dllBytes.Length * 2];

            for (int i = 0; i < hashBytes.Length; i++)
            {
                byte b = hashBytes[i];
                hashChars[i * 2]     = GetHexChar((b >> 4) & 0xF, false);
                hashChars[i * 2 + 1] = GetHexChar(b & 0xF, false);
            }

            for (int i = 0; i < dllBytes.Length; i++)
            {
                byte b = dllBytes[i];
                dllHexChars[i * 2]     = GetHexChar((b >> 4) & 0xF, true);
                dllHexChars[i * 2 + 1] = GetHexChar(b & 0xF, true);
            }

            return new[] { new string(hashChars), new string(dllHexChars) };
        }

        /// <summary>
        /// Reads a .NET assembly (.dll) from the local filesystem, computes its SHA-512 hash,
        /// and converts its binary content into a SQL-compatible hexadecimal string.
        /// </summary>
        /// <param name="dll">Full path to the DLL on disk.</param>
        /// <returns>
        /// <c>[0]</c> = SHA-512 hash (lowercase hex), <c>[1]</c> = DLL content (uppercase hex).
        /// Returns an array of two empty strings on failure.
        /// </returns>
        public static string[] ConvertDllToSqlBytes(string dll)
        {
            try
            {
                FileInfo fileInfo = new(dll);
                Logger.Info($"{dll} is {fileInfo.Length} bytes.");

                byte[] dllBytes = File.ReadAllBytes(dll);
                return ConvertDllToSqlBytes(dllBytes);
            }
            catch (FileNotFoundException)
            {
                Logger.Error($"Unable to load {dll}");
                return new[] { string.Empty, string.Empty };
            }
        }
    }
}
