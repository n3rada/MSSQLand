using System;

namespace MSSQLand.Exceptions
{
    /// <summary>
    /// Exception thrown when a required argument is missing.
    /// </summary>
    public class MissingRequiredArgumentException : Exception
    {
        public string ArgumentName { get; }
        public string Context { get; }

        public MissingRequiredArgumentException(string argumentName, string context) 
            : base($"Missing required argument '{argumentName}' for {context}.")
        {
            ArgumentName = argumentName;
            Context = context;
        }

        public MissingRequiredArgumentException(string argumentName, string context, string message) 
            : base(message)
        {
            ArgumentName = argumentName;
            Context = context;
        }
    }
}
