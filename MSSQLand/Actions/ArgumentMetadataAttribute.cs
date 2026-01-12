// MSSQLand/Actions/ArgumentMetadataAttribute.cs

using System;

namespace MSSQLand.Actions
{
    /// <summary>
    /// Provides metadata about an action's argument, including position and aliases.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class ArgumentMetadataAttribute : Attribute
    {
        /// <summary>
        /// The position of this argument when passed positionally (0-based). 
        /// -1 means it's not positional.
        /// </summary>
        public int Position { get; set; } = -1;

        /// <summary>
        /// Short name alias for named arguments (e.g., "u" for username).
        /// </summary>
        public string ShortName { get; set; }

        /// <summary>
        /// Long name alias for named arguments (e.g., "username").
        /// </summary>
        public string LongName { get; set; }

        /// <summary>
        /// Indicates if this argument is required.
        /// </summary>
        public bool Required { get; set; } = false;

        /// <summary>
        /// Description of what this argument does.
        /// </summary>
        public string Description { get; set; }
    }
}
