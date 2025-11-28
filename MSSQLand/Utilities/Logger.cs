using System;
using System.Linq;

namespace MSSQLand.Utilities
{
    /// <summary>
    /// Defines the severity level for log messages.
    /// </summary>
    internal enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Task = 2,
        Success = 3,
        Warning = 4,
        Error = 5
    }

    /// <summary>
    /// Provides logging functionality with compact and visually intuitive output.
    /// </summary>
    internal static class Logger
    {
        /// <summary>
        /// Gets or sets the minimum log level. Messages below this level will not be displayed.
        /// </summary>
        public static LogLevel MinimumLogLevel { get; set; } = LogLevel.Debug;

        /// <summary>
        /// Indicates whether debug messages should be printed.
        /// </summary>
        public static bool IsDebugEnabled
        {
            get => MinimumLogLevel <= LogLevel.Debug;
            set => MinimumLogLevel = value ? LogLevel.Debug : LogLevel.Info;
        }

        /// <summary>
        /// Indicates whether all output should be suppressed.
        /// </summary>
        public static bool IsSilentModeEnabled { get; set; } = false;

        public static void NewLine()
        {
            if (IsSilentModeEnabled) return;
            Console.Out.WriteLine();
        }

        public static int Banner(string message, char borderChar = '=', int totalWidth = 0)
        {
            if (IsSilentModeEnabled) return 0;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            
            if (string.IsNullOrWhiteSpace(message))
            {
                Console.Out.WriteLine(new string(borderChar, 30)); // Default width for empty or null messages
                return 0;
            }

            string[] lines = message.Split('\n');
            int padding = 2; // Minimum padding on each side of the message

            if (totalWidth == 0)
            {
                int maxLineLength = lines.Max(line => line.Length);
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
            if (IsSilentModeEnabled || MinimumLogLevel > LogLevel.Info) return;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Out.WriteLine($"[i] {message}");
            Console.ResetColor();
        }

        public static void Task(string message)
        {
            if (IsSilentModeEnabled || MinimumLogLevel > LogLevel.Task) return;
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Out.WriteLine($"[>] {message}");
            Console.ResetColor();
        }

        public static void Success(string message)
        {
            if (IsSilentModeEnabled || MinimumLogLevel > LogLevel.Success) return;
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.Out.WriteLine($"[+] {message}");
            Console.ResetColor();
        }

        public static void Debug(string message)
        {
            if (IsSilentModeEnabled || MinimumLogLevel > LogLevel.Debug) return;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Out.WriteLine($"[*] {message}");
            Console.ResetColor();
        }

        public static void Warning(string message)
        {
            if (IsSilentModeEnabled || MinimumLogLevel > LogLevel.Warning) return;
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Out.WriteLine($"[!] {message}");
            Console.ResetColor();
        }

        public static void Error(string message)
        {
            if (IsSilentModeEnabled || MinimumLogLevel > LogLevel.Error) return;
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.Out.WriteLine($"[-] {message}");
            Console.ResetColor();
        }

        public static void InfoNested(string message, int indentLevel = 0, string symbol = "|->")
        {
            if (IsSilentModeEnabled || MinimumLogLevel > LogLevel.Info) return;
            Console.ForegroundColor = ConsoleColor.White;
            string indent = new(' ', indentLevel * 4);
            Console.Out.WriteLine($"{indent}{symbol} {message}");
            Console.ResetColor();
        }

        public static void SuccessNested(string message, int indentLevel = 0, string symbol = "|->")
        {
            if (IsSilentModeEnabled || MinimumLogLevel > LogLevel.Success) return;
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            string indent = new(' ', indentLevel * 4);
            Console.Out.WriteLine($"{indent}{symbol} {message}");
            Console.ResetColor();
        }

        public static void TaskNested(string message, int indentLevel = 0, string symbol = "|->")
        {
            if (IsSilentModeEnabled || MinimumLogLevel > LogLevel.Task) return;
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            string indent = new(' ', indentLevel * 4);
            Console.Out.WriteLine($"{indent}{symbol} {message}");
            Console.ResetColor();
        }

        public static void DebugNested(string message, int indentLevel = 0, string symbol = "|->")
        {
            if (IsSilentModeEnabled || MinimumLogLevel > LogLevel.Debug) return;
            string indent = new(' ', indentLevel * 4);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Out.WriteLine($"{indent}{symbol} {message}");
            Console.ResetColor();
        }

        public static void ErrorNested(string message, int indentLevel = 0, string symbol = "|->")
        {
            if (IsSilentModeEnabled || MinimumLogLevel > LogLevel.Error) return;
            string indent = new(' ', indentLevel * 4);
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.Out.WriteLine($"{indent}{symbol} {message}");
            Console.ResetColor();
        }

        public static void WarningNested(string message, int indentLevel = 0, string symbol = "|->")
        {
            if (IsSilentModeEnabled || MinimumLogLevel > LogLevel.Warning) return;
            string indent = new(' ', indentLevel * 4);
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Out.WriteLine($"{indent}{symbol} {message}");
            Console.ResetColor();
        }
    }
}
