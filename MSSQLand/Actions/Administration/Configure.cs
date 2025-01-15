using System;
using System.Data;
using MSSQLand.Services;
using MSSQLand.Utilities;

namespace MSSQLand.Actions.Administration
{
    internal class Configure : BaseAction
    {
        private int _state;
        private string _optionName;

        public override void ValidateArguments(string additionalArguments)
        {
            // Split the additional argument into parts (optionName and state)
            string[] args = additionalArguments.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

            // Check if both arguments (option name and state) are provided
            if (args == null || args.Length != 2)
            {
                throw new ArgumentException("Invalid arguments. Usage: <optionName> <state>. Example: xp_cmdshell 1");
            }

            _optionName = args[0];

            // Validate and parse the state (1 or 0)
            if (!int.TryParse(args[1], out _state) || (_state != 0 && _state != 1))
            {
                throw new ArgumentException("Invalid state value. Use 1 to enable or 0 to disable.");
            }
        }

        public override void Execute(DatabaseContext connectionManager)
        {

            Logger.TaskNested($"Passing {_optionName} to {_state}");

            connectionManager.ConfigService.EnsureAdvancedOptions();
            connectionManager.ConfigService.SetConfigurationOption(_optionName, _state);

        }
    }
}
