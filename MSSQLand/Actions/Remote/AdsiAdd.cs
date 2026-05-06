// MSSQLand/Actions/Remote/AdsiAdd.cs

using MSSQLand.Services;
using MSSQLand.Utilities;

namespace MSSQLand.Actions.Remote
{
    internal class AdsiAdd : BaseAction
    {
        [ArgumentMetadata(Position = 0, Description = "Linked server name (auto-generated if omitted)")]
        private string _serverName = null;

        [ArgumentMetadata(Position = 1, Description = "Data source for the ADSI linked server (default: localhost)")]
        private string _dataSource = "localhost";

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);
        }

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
            Logger.InfoNested($"Data source: {_dataSource}");

            return true;
        }
    }
}
