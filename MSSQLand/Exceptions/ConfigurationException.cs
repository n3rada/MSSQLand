using System;

namespace MSSQLand.Exceptions
{
    /// <summary>
    /// Exception thrown when SQL Server configuration operation fails.
    /// </summary>
    public class ConfigurationException : Exception
    {
        public string ConfigurationOption { get; }
        public object Value { get; }

        public ConfigurationException(string configurationOption, object value) 
            : base($"Failed to set configuration option '{configurationOption}' to value '{value}'.")
        {
            ConfigurationOption = configurationOption;
            Value = value;
        }

        public ConfigurationException(string configurationOption, object value, string message) 
            : base(message)
        {
            ConfigurationOption = configurationOption;
            Value = value;
        }

        public ConfigurationException(string configurationOption, object value, string message, Exception innerException) 
            : base(message, innerException)
        {
            ConfigurationOption = configurationOption;
            Value = value;
        }
    }
}
