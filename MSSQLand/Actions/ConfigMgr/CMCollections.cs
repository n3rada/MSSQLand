// MSSQLand/Actions/ConfigMgr/CMCollections.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.ConfigMgr
{
    /// <summary>
    /// Enumerate ConfigMgr collections with member counts, types, and properties.
    /// Use this to identify device and user groupings for targeted deployment attacks.
    /// Shows collection names, types (device/user), member counts, and collection IDs needed for deployment targeting.
    /// Search by CollectionID or name, filter by type, and optionally show only collections with members.
    /// Essential for understanding organizational structure and planning deployment-based attacks.
    /// </summary>
    internal class CMCollections : BaseAction
    {
        [ArgumentMetadata(Position = 0, LongName = "collection-id", Description = "Search by CollectionID")]
        private string _collectionId = "";

        [ArgumentMetadata(Position = 1, ShortName = "n", LongName = "name", Description = "Search by collection name")]
        private string _nameFilter = "";

        [ArgumentMetadata(Position = 2, ShortName = "t", LongName = "type", Description = "Filter by type: other (0), user (1), or device (2)")]
        private string _collectionType = "";

        [ArgumentMetadata(Position = 3,  LongName = "limit", Description = "Limit number of results (default: 50)")]
        private int _limit = 50;

        [ArgumentMetadata(LongName = "with-members", Description = "Only show collections with members (MemberCount > 0)")]
        private bool _withMembers = false;

        public override object Execute(DatabaseContext databaseContext)
        {
            string idMsg = !string.IsNullOrEmpty(_collectionId) ? $" (ID: {_collectionId})" : "";
            string nameMsg = !string.IsNullOrEmpty(_nameFilter) ? $" (name: {_nameFilter})" : "";
            string typeMsg = !string.IsNullOrEmpty(_collectionType) ? $" (type: {_collectionType})" : "";
            Logger.TaskNested($"Enumerating ConfigMgr collections{idMsg}{nameMsg}{typeMsg}");
            Logger.TaskNested($"Limit: {_limit}");

            CMService sccmService = new(databaseContext.QueryService, databaseContext.Server);

            var databases = sccmService.GetSccmDatabases();

            if (databases.Count == 0)
            {
                Logger.Warning("No ConfigMgr databases found");
                return null;
            }

            foreach (string db in databases)
            {
                Logger.NewLine();
                string siteCode = CMService.GetSiteCode(db);
                Logger.Info($"ConfigMgr database: {db} (Site Code: {siteCode})");

                try
                {
                    string whereClause = "WHERE 1=1";
                    
                    // Add CollectionID search
                    if (!string.IsNullOrEmpty(_collectionId))
                    {
                        whereClause += $" AND CollectionID = '{_collectionId.Replace("'", "''")}'";
                    }

                    // Add name search
                    if (!string.IsNullOrEmpty(_nameFilter))
                    {
                        whereClause += $" AND (Name LIKE '%{_nameFilter.Replace("'", "''")}%' " +
                                      $"OR Comment LIKE '%{_nameFilter.Replace("'", "''")}%')";
                    }

                    // Add collection type filter
                    if (!string.IsNullOrEmpty(_collectionType))
                    {
                        if (_collectionType.Equals("other", StringComparison.OrdinalIgnoreCase) || _collectionType == "0")
                        {
                            whereClause += " AND CollectionType = 0";
                        }
                        else if (_collectionType.Equals("user", StringComparison.OrdinalIgnoreCase) || _collectionType == "1")
                        {
                            whereClause += " AND CollectionType = 1";
                        }
                        else if (_collectionType.Equals("device", StringComparison.OrdinalIgnoreCase) || _collectionType == "2")
                        {
                            whereClause += " AND CollectionType = 2";
                        }
                    }

                    // Add with-members filter
                    if (_withMembers)
                    {
                        whereClause += " AND MemberCount > 0";
                    }

                    string topClause = _limit > 0 ? $"TOP {_limit}" : "";

                    string query = $@"
SELECT {topClause} *
FROM [{db}].dbo.v_Collection
{whereClause}
ORDER BY LastChangeTime DESC, LastRefreshTime DESC, MemberCount DESC";

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
