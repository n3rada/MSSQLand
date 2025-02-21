using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MSSQLand.Actions
{
    /// <summary>
    /// Abstract base class for all actions, enforcing validation and execution logic.
    /// </summary>
    
    [AttributeUsage(AttributeTargets.Field)]
    public abstract class BaseAction : Attribute
    {

        /// <summary>
        /// Validates the additional argument passed for the action.
        /// </summary>
        /// <param name="additionalArguments">The additional argument for the action.</param>
        public abstract void ValidateArguments(string additionalArguments);


        /// <summary>
        /// Executes the action using the provided ConnectionManager.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager for database operations.</param>
        public abstract object? Execute(DatabaseContext databaseContext = null);

        protected string[] SplitArguments(string additionalArguments, string separator = CommandParser.AdditionalArgumentsSeparator)
        {
            if (string.IsNullOrWhiteSpace(additionalArguments))
            {
                Logger.Debug("No arguments provided.");
                return Array.Empty<string>();
            }

            string[] splitted = Regex.Split(additionalArguments, $"({Regex.Escape(separator)})")
                              .Where(arg => arg != separator) // Remove standalone separators
                              .ToArray();

            Logger.Debug("Splitted arguments: {" + string.Join(",", splitted) + "}");

            return splitted;
        }


        /// <summary>
        /// Returns the name of the class as a string.
        /// </summary>
        /// <returns>The name of the current class.</returns>
        public string GetName()
        {
            return GetType().Name;
        }

        /// <summary>
        /// Retrieves argument names, types, and default values of private fields in the derived class.
        /// </summary>
        /// <returns>A formatted string of argument details.</returns>
        public virtual string GetArguments()
        {
            var fields = this.GetType()
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(field => !Attribute.IsDefined(field, typeof(ExcludeFromArgumentsAttribute))) // Exclude marked fields
                .Select(field =>
                {
                    string fieldName = field.Name.TrimStart('_'); // Remove leading underscore
                    string fieldType = SimplifyType(field.FieldType); // Get the simplified type name
                    var defaultValue = field.GetValue(this); // Get the default value, if any
                    string defaultValueStr = defaultValue != null ? $", default: {defaultValue}" : string.Empty;

                    return $"{fieldName} ({fieldType}{defaultValueStr})".Trim();
                });

            return string.Join(",", fields);
        }


        /// <summary>
        /// Simplifies the type name for better readability.
        /// </summary>
        /// <param name="type">The type to simplify.</param>
        /// <returns>The simplified type name.</returns>
        private string SimplifyType(Type type)
        {
            return type.Name switch
            {
                "Int32" => "int",
                "String" => "string",
                "Boolean" => "bool",
                "Dictionary" => "Dictionary",
                "List" => "List",
                _ => type.Name
            };
        }
    }
}
