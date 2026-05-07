// MSSQLand/Actions/Domain/AdsiAdd.cs

using MSSQLand.Services;
using MSSQLand.Utilities;

namespace MSSQLand.Actions.Domain
{
    internal class AdsiAdd : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Linked server name (auto-generated if omitted)")]
        private string _serverName = null;

        [ArgumentMetadata(Position = 1, ShortName = "ds", LongName = "data-source", Description = "Data source for the ADSI linked server (default: adsdatasource)")]
        private string _dataSource = "adsdatasource";

        public override object Execute(DatabaseContext databaseContext)
        {
            AdsiService adsiService = new(databaseContext);

            string serverName = _serverName;

            if (string.IsNullOrEmpty(serverName))
            {
                bool success = adsiService.CreateAdsiLinkedServer(out serverName, _dataSource);
                if (!success) return false;
            }
            else
            {
                if (adsiService.AdsiServerExists(serverName))
                {
                    Logger.Error($"ADSI linked server '{serverName}' already exists.");
                    return false;
                }

                bool success = adsiService.CreateAdsiLinkedServer(serverName, _dataSource);
                if (!success) return false;
            }

            Logger.Success($"ADSI linked server '{serverName}' created successfully");
            Logger.SuccessNested($"Data source: {_dataSource}");

            return true;
        }
    }
}
