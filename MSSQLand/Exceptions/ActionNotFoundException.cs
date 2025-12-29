using System;

namespace MSSQLand.Exceptions
{
    /// <summary>
    /// Exception thrown when a requested action is not found in the ActionFactory.
    /// </summary>
    public class ActionNotFoundException : Exception
    {
        public string ActionName { get; }

        public ActionNotFoundException(string actionName) 
            : base($"Action '{actionName}' not found.")
        {
            ActionName = actionName;
        }

        public ActionNotFoundException(string actionName, string message) 
            : base(message)
        {
            ActionName = actionName;
        }
    }
}
