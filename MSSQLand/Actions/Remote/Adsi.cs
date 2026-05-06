// MSSQLand/Actions/Remote/Adsi.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;

namespace MSSQLand.Actions.Remote
{
    internal class Adsi : BaseAction
    {
        public override void ValidateArguments(string[] args)
        {
            BindArguments(args);
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.Task("Enumerating ADSI linked servers");

            AdsiService adsiService = new(databaseContext);
            List<string> adsiServers = adsiService.ListAdsiServers();

            if (adsiServers.Count == 0)
            {
                Logger.Warning("No ADSI linked servers found");
                return adsiServers;
            }

            Console.WriteLine(OutputFormatter.ConvertList(adsiServers, "ADSI Servers"));
            Logger.Success($"Found {adsiServers.Count} ADSI linked server{(adsiServers.Count > 1 ? "s" : "")}");

            return adsiServers;
        }
    }
}
