using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.SCCM
{
    /// <summary>
    /// List SCCM collections with their properties and member counts.
    /// Queries Collections_G table.
    /// </summary>
    internal class SccmCollections : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "f", LongName = "filter", Description = "Filter by collection name")]
        private string _filter = "";

        [ArgumentMetadata(Position = 1, ShortName = "t", LongName = "type", Description = "Filter by type: user (1) or device (2)")]
        private string _collectionType = "";

        [ArgumentMetadata(Position = 2, ShortName = "l", LongName = "limit", Description = "Limit number of results (default: 100)")]
        private int _limit = 100;

        public override void ValidateArguments(string[] args)
        {
            var (named, positional) = ParseActionArguments(args);

            _filter = GetNamedArgument(named, "f", null)
                   ?? GetNamedArgument(named, "filter", null)
                   ?? GetPositionalArgument(positional, 0, "");

            _collectionType = GetNamedArgument(named, "t", null)
                           ?? GetNamedArgument(named, "type", null)
                           ?? GetPositionalArgument(positional, 1, "");

            string limitStr = GetNamedArgument(named, "l", null)
                           ?? GetNamedArgument(named, "limit", null)
                           ?? GetPositionalArgument(positional, 2);
            if (!string.IsNullOrEmpty(limitStr))
            {
                _limit = int.Parse(limitStr);
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            string filterMsg = !string.IsNullOrEmpty(_filter) ? $" (filter: {_filter})" : "";
            string typeMsg = !string.IsNullOrEmpty(_collectionType) ? $" (type: {_collectionType})" : "";
            Logger.TaskNested($"Enumerating SCCM collections{filterMsg}{typeMsg}");

            SccmService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            string[] requiredTables = { "Collections_G" };
            var databases = sccmService.GetValidatedSccmDatabases(requiredTables, 1);

            if (databases.Count == 0)
            {
                Logger.Warning("No SCCM databases found");
                return null;
            }

            foreach (string db in databases)
            {
                Logger.NewLine();
                string siteCode = SccmService.GetSiteCode(db);
                Logger.Info($"SCCM database: {db} (Site Code: {siteCode})");

                try
                {
                    string whereClause = "WHERE 1=1";
                    
                    // Add filter conditions
                    if (!string.IsNullOrEmpty(_filter))
                    {
                        whereClause += $" AND (Name LIKE '%{_filter.Replace("'", "''")}%' " +
                                      $"OR Comment LIKE '%{_filter.Replace("'", "''")}%')";
                    }

                    // Add collection type filter
                    if (!string.IsNullOrEmpty(_collectionType))
                    {
                        if (_collectionType.Equals("user", StringComparison.OrdinalIgnoreCase) || _collectionType == "1")
                        {
                            whereClause += " AND CollectionType = 1";
                        }
                        else if (_collectionType.Equals("device", StringComparison.OrdinalIgnoreCase) || _collectionType == "2")
                        {
                            whereClause += " AND CollectionType = 2";
                        }
                    }

                    string topClause = _limit > 0 ? $"TOP {_limit}" : "";

                    string query = $@"
SELECT {topClause} *
FROM [{db}].dbo.v_Collection
{whereClause}
ORDER BY CollectionType, MemberCount DESC, LastMemberChangeTime DESC";

                    DataTable collectionsTable = databaseContext.QueryService.ExecuteTable(query);

                    if (collectionsTable.Rows.Count == 0)
                    {
                        Logger.NewLine();
                        Logger.Warning("No collections found");
                        continue;
                    }

                    Console.WriteLine(OutputFormatter.ConvertDataTable(collectionsTable));

                    Logger.Success($"Found {collectionsTable.Rows.Count} collection(s)");

                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to enumerate collections: {ex.Message}");
                }
            }

            return null;
        }
    }
}
