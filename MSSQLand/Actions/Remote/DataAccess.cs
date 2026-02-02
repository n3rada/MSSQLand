// MSSQLand/Actions/Remote/DataAccess.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using System;

namespace MSSQLand.Actions.Remote
{
    /// <summary>
    /// Enables or disables data access on a linked server using sp_serveroption.
    /// Data access controls whether OPENQUERY and four-part naming can retrieve data from the linked server.
    /// </summary>
    internal class DataAccess : BaseAction
    {
        [ArgumentMetadata(Position = 0, Required = true, Description = "Action: enable/disable (or aliases: +/-, on/off, 1/0, true/false, add/del)")]
        private bool _enable;

        [ArgumentMetadata(Position = 1, Required = true, Description = "Linked server name")]
        private string _linkedServerName = "";

        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException("Data access action requires two arguments: action and linked server name.");
            }

            if (args.Length != 2)
            {
                throw new ArgumentException("Data access action requires exactly two arguments: action and linked server name.");
            }

            if (!TryParseToggleAction(args[0], out bool enable, out string error))
            {
                throw new ArgumentException(error);
            }

            var normalizedArgs = (string[])args.Clone();
            normalizedArgs[0] = enable ? "true" : "false";

            BindArguments(normalizedArgs);
        }

        public override object Execute(DatabaseContext databaseContext)
        {
            string dataAccessValue = _enable ? "true" : "false";
            string actionVerb = _enable ? "Enabling" : "Disabling";

            Logger.TaskNested($"{actionVerb} data access on linked server '{_linkedServerName}'");

            bool success = databaseContext.ConfigService.SetServerOption(_linkedServerName, "data access", dataAccessValue);

            if (success)
            {
                string status = _enable ? "enabled" : "disabled";
                Logger.Success($"Data access successfully {status} on '{_linkedServerName}'");
                if (_enable)
                {
                    Logger.SuccessNested("OPENQUERY operations are now available for this server.");
                }
                else
                {
                    Logger.SuccessNested("OPENQUERY operations are no longer available for this server.");
                }
            }
            else
            {
                Logger.Error($"Failed to modify data access settings on '{_linkedServerName}'");
            }

            return success;
        }
    }
}
