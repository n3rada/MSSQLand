// MSSQLand/Utilities/Logger.cs

using System;
using System.Linq;

namespace MSSQLand.Utilities
{
    /// <summary>
    /// Defines the severity level for log messages.
    /// </summary>
    internal enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Success = 3,
        Warning = 4,
        Error = 5
    }

    /// <summary>
    /// Provides logging functionality with compact and visually intuitive output.
    /// </summary>
    /// <remarks>
    /// Usage convention:
    /// - Info/InfoNested: operations, context, configuration, or results
    /// - Success/Warning/Error: outcomes
    /// - Debug: notable diagnostic events for operators running --debug (connection failures with reasons, significant state changes); not for repetitive per-iteration detail
    /// - Trace: developer-level internal state (per-hop traversal, cache hits, skip decisions, internal counts, raw query routing); bypasses soft-silent mode; not intended for operators
    /// </remarks>
    internal static class Logger
    {
        /// <summary>
        /// Gets or sets the minimum log level. Messages below this level will not be displayed.
        /// </summary>
        public static LogLevel MinimumLogLevel { get; set; } = LogLevel.Info;

        /// <summary>
        /// Indicates whether all output should be suppressed.
        /// </summary>
        public static bool IsSilentModeEnabled { get; set; } = false;

        /// <summary>
        /// True once any visible output has been written. Used to avoid a leading blank line before the end banner.
        /// </summary>
        public static bool HasOutput { get; set; } = false;

        /// <summary>
        /// True when the last write already ended with a blank line (e.g. OutputFormatter output via Console.WriteLine).
        /// Prevents doubling up the blank line before the end banner.
        /// </summary>
        public static bool EndsWithBlankLine { get; set; } = false;

        private static void MarkWritten(bool trailingBlank = false)
        {
            HasOutput = true;
            EndsWithBlankLine = trailingBlank;
        }

/// <summary>
        /// Indicates whether output is suppressed by TemporarilySilent().
        /// Unlike IsSilentModeEnabled, this allows Trace-level messages through
        /// so diagnostic tracing remains visible during silenced sub-operations.
        /// </summary>
        private static bool _isSoftSilent = false;

        /// <summary>
        /// Returns true if output should be suppressed (either hard or soft silent mode).
        /// Trace-level methods bypass soft-silent and only respect hard-silent.
        /// </summary>
        public static bool IsSilenced => IsSilentModeEnabled || _isSoftSilent;

        /// <summary>
        /// Temporarily enables silent mode for the duration of the returned IDisposable.
        /// Use with 'using' statement to automatically restore previous state.
        /// </summary>
        /// <returns>An IDisposable that restores the previous silent mode state when disposed.</returns>
        public static IDisposable TemporarilySilent()
        {
            return new SilentModeScope();
        }

        private class SilentModeScope : IDisposable
        {
            private readonly bool _previousState;

            public SilentModeScope()
            {
                _previousState = _isSoftSilent;
                _isSoftSilent = true;
            }

            public void Dispose()
            {
                _isSoftSilent = _previousState;
            }
        }

        public static void NewLine()
        {
            if (IsSilenced) return;
            Console.Out.WriteLine();
            MarkWritten(trailingBlank: true);
        }

        public static int Banner(string message, char borderChar = '=', int totalWidth = 0)
        {
            if (IsSilenced) return 0;

            Console.ForegroundColor = ConsoleColor.DarkGray;

            if (string.IsNullOrWhiteSpace(message))
            {
                Console.Out.WriteLine(new string(borderChar, 30)); // Default width for empty or null messages
                return 0;
            }

            string[] lines = message.Split('\n');
            int padding = 2; // Minimum padding on each side of the message

            int maxLineLength = lines.Max(line => line.Length);

            if (totalWidth == 0 || totalWidth < maxLineLength + (padding * 2))
            {
                totalWidth = maxLineLength + (padding * 2);
            }

            string border = new(borderChar, totalWidth);
            Console.Out.WriteLine(border);

            foreach (string line in lines)
            {
                int spaces = totalWidth - line.Length;
                int leftPadding = spaces / 2; // Center by dividing spaces evenly
                int rightPadding = spaces - leftPadding;

                string centeredLine = new string(' ', leftPadding) + line + new string(' ', rightPadding);
                Console.Out.WriteLine(centeredLine);
            }

            Console.Out.WriteLine(border);
            Console.ResetColor();

            return totalWidth;
        }


        public static void Info(string message)
        {
            if (IsSilenced || MinimumLogLevel > LogLevel.Info) return;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Out.WriteLine($"[i] {message}");
            Console.ResetColor();
            MarkWritten();
        }

        public static void Success(string message)
        {
            if (IsSilenced || MinimumLogLevel > LogLevel.Success) return;
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.Out.WriteLine($"[+] {message}");
            Console.ResetColor();
            MarkWritten();
        }

        public static void Debug(string message)
        {
            if (IsSilenced || MinimumLogLevel > LogLevel.Debug) return;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Out.WriteLine($"[*] {message}");
            Console.ResetColor();
            MarkWritten();
        }

        public static void Trace(string message)
        {
            if (IsSilentModeEnabled || MinimumLogLevel > LogLevel.Trace) return;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Out.WriteLine($"[~] {message}");
            Console.ResetColor();
            MarkWritten();
        }

        public static void Warning(string message)
        {
            if (IsSilenced || MinimumLogLevel > LogLevel.Warning) return;
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Out.WriteLine($"[!] {message}");
            Console.ResetColor();
            MarkWritten();
        }

        public static void Error(string message)
        {
            if (IsSilenced || MinimumLogLevel > LogLevel.Error) return;
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.Out.WriteLine($"[-] {message}");
            Console.ResetColor();
            MarkWritten();
        }

        public static void InfoNested(string message, int indentLevel = 0, string symbol = "|->")
        {
            if (IsSilenced || MinimumLogLevel > LogLevel.Info) return;
            Console.ForegroundColor = ConsoleColor.White;
            string indent = new(' ', indentLevel * 4);
            Console.Out.WriteLine($"{indent}{symbol} {message}");
            Console.ResetColor();
            MarkWritten();
        }

        public static void SuccessNested(string message, int indentLevel = 0, string symbol = "|->")
        {
            if (IsSilenced || MinimumLogLevel > LogLevel.Success) return;
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            string indent = new(' ', indentLevel * 4);
            Console.Out.WriteLine($"{indent}{symbol} {message}");
            Console.ResetColor();
            MarkWritten();
        }

        public static void DebugNested(string message, int indentLevel = 0, string symbol = "|->")
        {
            if (IsSilenced || MinimumLogLevel > LogLevel.Debug) return;
            string indent = new(' ', indentLevel * 4);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Out.WriteLine($"{indent}{symbol} {message}");
            Console.ResetColor();
            MarkWritten();
        }

        public static void TraceNested(string message, int indentLevel = 0, string symbol = "|->")
        {
            if (IsSilentModeEnabled || MinimumLogLevel > LogLevel.Trace) return;
            string indent = new(' ', indentLevel * 4);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Out.WriteLine($"{indent}{symbol} {message}");
            Console.ResetColor();
            MarkWritten();
        }

        public static void ErrorNested(string message, int indentLevel = 0, string symbol = "|->")
        {
            if (IsSilenced || MinimumLogLevel > LogLevel.Error) return;
            string indent = new(' ', indentLevel * 4);
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.Out.WriteLine($"{indent}{symbol} {message}");
            Console.ResetColor();
            MarkWritten();
        }

        public static void WarningNested(string message, int indentLevel = 0, string symbol = "|->")
        {
            if (IsSilenced || MinimumLogLevel > LogLevel.Warning) return;
            string indent = new(' ', indentLevel * 4);
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Out.WriteLine($"{indent}{symbol} {message}");
            Console.ResetColor();
            MarkWritten();
        }
    }
}
