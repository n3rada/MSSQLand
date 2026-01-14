// MSSQLand/Actions/Database/Rows.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Database
{
    public class Rows : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Table name: [table], [schema.table], or [database.schema.table]")]
        private string _fqtn = "";

        [ArgumentMetadata(Position = 1, ShortName = "l", LongName = "limit", Description = "Maximum rows to retrieve (default: 25)")]
        private int _limit = 25;

        [ArgumentMetadata(LongName = "all", Description = "Retrieve all rows without limit")]
        private bool _all = false;

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);

            if (string.IsNullOrEmpty(_fqtn))
            {
                throw new ArgumentException("Table name is required. Format: [table], [schema.table], or [database.schema.table]");
            }

            // Validate the FQTN can be parsed
            try
            {
                Misc.ParseQualifiedTableName(_fqtn);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Invalid table name: {ex.Message}");
            }

            // If --all is specified, override limit
            if (_all)
            {
                _limit = 0;
            }

            if (_limit < 0)
            {
                throw new ArgumentException($"Limit must be a non-negative integer, got: {_limit}");
            }
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            // Parse the FQTN
            var (database, schema, table) = Misc.ParseQualifiedTableName(_fqtn);

            // Use execution database if none specified
            if (string.IsNullOrEmpty(database))
            {
                database = databaseContext.QueryService.ExecutionServer.Database;
            }

            // Validate table name after parsing
            if (string.IsNullOrEmpty(table))
            {
                throw new ArgumentException("Table name cannot be empty.");
            }

            // Build the qualified table name for the query
            string targetTable = Misc.BuildQualifiedTableName(database, schema, table);
            
            Logger.TaskNested($"Retrieving rows from {targetTable}");
            
            if (_limit > 0)
            {
                Logger.TaskNested($"Limiting to {_limit} row(s)");
            }
            else
            {
                Logger.TaskNested("Retrieving all rows (no limit)");
            }

            // Build query with optional TOP
            string query = _limit > 0
                ? $"SELECT TOP ({_limit}) * FROM {targetTable};"
                : $"SELECT * FROM {targetTable};";

            DataTable rows = databaseContext.QueryService.ExecuteTable(query);

            Console.WriteLine(OutputFormatter.ConvertDataTable(rows));

            Logger.Success($"Extracted {rows.Rows.Count} row(s)");

            return rows;
        }
    }
}
