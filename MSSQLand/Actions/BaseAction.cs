using MSSQLand.Services;
using System.Collections.Generic;

namespace MSSQLand.Actions
{
    /// <summary>
    /// Abstract base class for all actions, enforcing validation and execution logic.
    /// </summary>
    public abstract class BaseAction
    {
        /// <summary>
        /// Validates the additional argument passed for the action.
        /// </summary>
        /// <param name="additionalArguments">The additional argument for the action.</param>
        public abstract void ValidateArguments(string additionalArguments);


        /// <summary>
        /// Executes the action using the provided ConnectionManager.
        /// </summary>
        /// <param name="connectionManager">The ConnectionManager for database operations.</param>
        public abstract void Execute(DatabaseContext connectionManager);

        /// <summary>
        /// Returns the name of the class as a string.
        /// </summary>
        /// <returns>The name of the current class.</returns>
        public string GetName()
        {
            return GetType().Name;
        }
    }
}
