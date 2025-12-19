// MSSQLand/Utilities/Misc.cs

using System;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.IO.Compression;
using System.IO;
using System.Security.Cryptography;

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
