using System;
using MSSQLand.Services;
using MSSQLand.Utilities;

namespace MSSQLand.Actions.Administration
{
    internal class Configure : BaseAction
    {
        [ArgumentMetadata(Position = 0, ShortName = "o", LongName = "option", Required = true, Description = "Configuration option name")]
        private string _optionName;

        [ArgumentMetadata(Position = 1, ShortName = "s", LongName = "state", Required = true, Description = "State value (0=disable, 1=enable)")]
        private int _state;

        public override void ValidateArguments(string additionalArguments)
        {
            // Split the additional argument into parts (optionName and state)
            string[] parts = SplitArguments(additionalArguments);

            // Check if both arguments (option name and state) are provided
            if (parts == null || parts.Length != 2)
            {
                throw new ArgumentException("Invalid arguments. Usage: <optionName> <state>. Example: xp_cmdshell 1");
            }

            _optionName = parts[0];

            // Validate and parse the state (1 or 0)
            if (!int.TryParse(parts[1], out _state) || (_state != 0 && _state != 1))
            {
                throw new ArgumentException("Invalid state value. Use 1 to enable or 0 to disable.");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {

            Logger.TaskNested($"Passing {_optionName} to {_state}");

            databaseContext.ConfigService.SetConfigurationOption(_optionName, _state);

            return null;

        }
    }
}
