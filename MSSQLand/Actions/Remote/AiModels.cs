// MSSQLand/Actions/Remote/AiModels.cs

using System;
using System.Data;

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Actions.Remote
{
    internal class AiModels : BaseAction
    {
        public override object Execute(DatabaseContext databaseContext)
        {
            if (!RequiresSql2025(databaseContext)) return null;

            Logger.Info("Enumerating SQL Server 2025 AI configuration and external models");

            CheckAiConfiguration(databaseContext);
            EnumerateExternalModels(databaseContext);

            return null;
        }

        private static void CheckAiConfiguration(DatabaseContext databaseContext)
        {
            try
            {
                string query = @"SELECT name, value_in_use
FROM sys.configurations
WHERE name IN ('external rest endpoint enabled', 'external AI runtimes enabled')
ORDER BY name";

                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result == null || result.Rows.Count == 0)
                {
                    Logger.Warning("No AI-related configuration entries found");
                    return;
                }

                DataTable display = new();
                display.Columns.Add("Option", typeof(string));
                display.Columns.Add("Status", typeof(string));
                display.Columns.Add("Value",  typeof(int));

                foreach (DataRow row in result.Rows)
                {
                    int val = Convert.ToInt32(row["value_in_use"]);
                    display.Rows.Add(row["name"].ToString(), val == 1 ? "Enabled" : "Disabled", val);
                }

                Logger.Info("AI feature configuration:");
                Console.WriteLine(OutputFormatter.ConvertDataTable(display));
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not query AI configuration: {ex.Message}");
            }
        }

        private static void EnumerateExternalModels(DatabaseContext databaseContext)
        {
            try
            {
                DataTable result = databaseContext.QueryService.ExecuteTable(
                    "SELECT * FROM sys.external_models ORDER BY name");

                if (result == null || result.Rows.Count == 0)
                {
                    Logger.Warning("No external AI models registered (sys.external_models is empty or inaccessible)");
                    return;
                }

                Logger.Success($"Found {result.Rows.Count} registered external model(s)");

                DataTable display = new();
                display.Columns.Add("Name",               typeof(string));
                display.Columns.Add("Type",               typeof(string));
                display.Columns.Add("API Format",         typeof(string));
                display.Columns.Add("Location",           typeof(string));
                display.Columns.Add("Local Runtime Path", typeof(string));

                foreach (DataRow row in result.Rows)
                {
                    display.Rows.Add(
                        GetCol(result, row, "name"),
                        GetCol(result, row, "model_type_desc") ?? GetCol(result, row, "model_type"),
                        GetCol(result, row, "api_format"),
                        GetCol(result, row, "location"),
                        GetCol(result, row, "local_runtime_path")
                    );
                }

                Console.WriteLine(OutputFormatter.ConvertDataTable(display));
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not enumerate external models: {ex.Message}");
            }
        }

        private static string GetCol(DataTable table, DataRow row, string colName)
        {
            return table.Columns.Contains(colName) ? row[colName]?.ToString() ?? "" : "";
        }

        internal static bool RequiresSql2025(DatabaseContext databaseContext)
        {
            int major = databaseContext.QueryService.ExecutionServer.MajorVersion;
            if (major >= 17) return true;

            Logger.Error($"This action requires SQL Server 2025 (internal version 17+). Detected version: {major}");
            return false;
        }
    }
}
