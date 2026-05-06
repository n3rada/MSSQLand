// MSSQLand/Actions/Remote/AdsiDel.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Remote
{
    internal class AdsiDel : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Name of the ADSI linked server to delete")]
        private string _serverName = "";

        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            AdsiService adsiService = new(databaseContext);

            if (!adsiService.AdsiServerExists(_serverName))
            {
                Logger.Error($"ADSI linked server '{_serverName}' not found.");
                return false;
            }

            try
            {
                adsiService.DropLinkedServer(_serverName);
                Logger.Success($"ADSI linked server '{_serverName}' deleted successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to delete ADSI linked server '{_serverName}': {ex.Message}");
                return false;
            }
        }
    }
}
