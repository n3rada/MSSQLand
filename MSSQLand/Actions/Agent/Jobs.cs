// MSSQLand/Actions/Agent/Jobs.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Agent
{
    /// <summary>
    /// Enumerate SQL Server Agent jobs with steps, commands, owner, and schedule info.
    /// Queries msdb.dbo.sysjobs, sysjobsteps, syscategories, and sysschedules.
    /// </summary>
    internal class Jobs : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "n", LongName = "name", Description = "Filter jobs by name (substring match)")]
        private string _name = "";

        [ArgumentMetadata(ShortName = "c", LongName = "commands", Description = "Show full command text instead of command length")]
        private bool _showCommands = false;

        [ArgumentMetadata(ShortName = "l", LongName = "limit", Description = "Limit number of results (default: 25)")]
        private int _limit = 25;

        public override object Execute(DatabaseContext databaseContext)
        {
            if (!AgentHelper.CheckAgentRunning(databaseContext))
                return null;

            string filterMsg = !string.IsNullOrEmpty(_name) ? $" matching '{_name}'" : "";
            Logger.TaskNested($"Enumerating SQL Server Agent jobs{filterMsg}");

            string whereClause = "";
            if (!string.IsNullOrEmpty(_name))
            {
                whereClause = $"WHERE j.name LIKE '%{_name.Replace("'", "''")}%'";
            }

            string topClause = BuildTopClause(_limit);

            string query;

            if (_showCommands)
            {
                // Per-step view with full command text
                query = $@"
                SELECT {topClause}
                    j.job_id,
                    j.name AS JobName,
                    SUSER_SNAME(j.owner_sid) AS Owner,
                    j.enabled AS Enabled,
                    c.name AS Category,
                    j.description AS Description,
                    j.date_created AS Created,
                    j.date_modified AS Modified,
                    js.step_id AS StepId,
                    js.step_name AS StepName,
                    js.subsystem AS Subsystem,
                    js.command AS Command,
                    js.database_name AS StepDatabase
                FROM msdb.dbo.sysjobs j
                LEFT JOIN msdb.dbo.sysjobsteps js
                    ON j.job_id = js.job_id
                LEFT JOIN msdb.dbo.syscategories c
                    ON j.category_id = c.category_id
                {whereClause}
                ORDER BY j.name, js.step_id;";
            }
            else
            {
                // Grouped view: one row per job
                query = $@"
                SELECT {topClause}
                    j.job_id,
                    j.name AS JobName,
                    SUSER_SNAME(j.owner_sid) AS Owner,
                    j.enabled AS Enabled,
                    c.name AS Category,
                    j.description AS Description,
                    j.date_created AS Created,
                    j.date_modified AS Modified,
                    COUNT(js.step_id) AS Steps,
                    STUFF((SELECT DISTINCT ', ' + s.subsystem
                           FROM msdb.dbo.sysjobsteps s
                           WHERE s.job_id = j.job_id
                           FOR XML PATH('')), 1, 2, '') AS Subsystems
                FROM msdb.dbo.sysjobs j
                LEFT JOIN msdb.dbo.sysjobsteps js
                    ON j.job_id = js.job_id
                LEFT JOIN msdb.dbo.syscategories c
                    ON j.category_id = c.category_id
                {whereClause}
                GROUP BY j.job_id, j.name, j.owner_sid, j.enabled, c.name, j.description, j.date_created, j.date_modified
                ORDER BY j.name;";
            }

            DataTable result = databaseContext.QueryService.ExecuteTable(query);

            if (result.Rows.Count == 0)
            {
                Logger.Info("No SQL Agent jobs found.");
                return null;
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(result));

            Logger.Success($"Found {result.Rows.Count} row(s)");

            return result;
        }
    }
}
