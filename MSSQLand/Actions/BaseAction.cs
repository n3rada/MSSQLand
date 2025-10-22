using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Splits arguments using the default separator.
        /// </summary>
        protected string[] SplitArguments(string additionalArguments, string separator = CommandParser.AdditionalArgumentsSeparator)
        {
            if (string.IsNullOrWhiteSpace(additionalArguments))
            {
                Logger.Debug("No arguments provided.");
                return Array.Empty<string>();
            }

            string[] splitted = Regex.Split(additionalArguments, $"({Regex.Escape(separator)})")
                              .Where(arg => arg != separator)
                              .ToArray();

            Logger.Debug("Splitted arguments: {" + string.Join(",", splitted) + "}");

            return splitted;
        }

        /// <summary>
        /// Parses both positional and named arguments from the input string.
        /// Named arguments can be in formats: /name:value or /name=value
        /// Positional arguments are those without a name prefix.
        /// </summary>
        /// <param name="additionalArguments">The raw argument string.</param>
        /// <returns>A dictionary of named arguments and a list of positional arguments.</returns>
        protected (Dictionary<string, string> Named, List<string> Positional) ParseArguments(string additionalArguments)
        {
            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();

            if (string.IsNullOrWhiteSpace(additionalArguments))
            {
                return (named, positional);
            }

            string[] parts = SplitArguments(additionalArguments);

            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                
                // Check if it's a named argument (starts with / and contains : or =)
                if (trimmed.StartsWith("/"))
                {
                    // Try to parse as /name:value or /name=value
                    int separatorIndex = trimmed.IndexOfAny(new[] { ':', '=' });
                    
                    if (separatorIndex > 1)
                    {
                        string name = trimmed.Substring(1, separatorIndex - 1).Trim();
                        string value = trimmed.Substring(separatorIndex + 1).Trim();
                        
                        if (!string.IsNullOrEmpty(name))
                        {
                            named[name] = value;
                            Logger.Debug($"Parsed named argument: {name} = {value}");
                            continue;
                        }
                    }
                }

                // It's a positional argument
                positional.Add(trimmed);
                Logger.Debug($"Parsed positional argument: {trimmed}");
            }

            return (named, positional);
        }

        /// <summary>
        /// Gets a named argument value or returns the default if not found.
        /// </summary>
        protected string GetNamedArgument(Dictionary<string, string> namedArgs, string name, string defaultValue = null)
        {
            if (namedArgs.TryGetValue(name, out string value))
            {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// Gets a positional argument by index or returns the default if not found.
        /// </summary>
        protected string GetPositionalArgument(List<string> positionalArgs, int index, string defaultValue = null)
        {
            if (index >= 0 && index < positionalArgs.Count)
            {
                return positionalArgs[index];
            }
            return defaultValue;
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
        /// <returns>A list of formatted argument strings.</returns>
        public virtual List<string> GetArguments()
        {
            var fields = this.GetType()
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(field => !Attribute.IsDefined(field, typeof(ExcludeFromArgumentsAttribute)))
                .OrderBy(field =>
                {
                    // Order by position if ArgumentMetadata exists
                    var metadata = field.GetCustomAttribute<ArgumentMetadataAttribute>();
                    return metadata?.Position ?? int.MaxValue;
                })
                .Select(field =>
                {
                    string fieldName = field.Name.TrimStart('_');
                    string fieldType = SimplifyType(field.FieldType);
                    var defaultValue = field.GetValue(this);
                    string defaultValueStr = defaultValue != null ? $", default: {defaultValue}" : string.Empty;

                    // Get metadata if present
                    var metadata = field.GetCustomAttribute<ArgumentMetadataAttribute>();
                    string aliases = "";
                    string position = "";
                    string description = "";

                    if (metadata != null)
                    {
                        // Build position indicator
                        if (metadata.Position >= 0)
                        {
                            position = $"[pos:{metadata.Position}] ";
                        }

                        // Build aliases
                        var aliasList = new List<string>();
                        if (!string.IsNullOrEmpty(metadata.ShortName))
                        {
                            aliasList.Add($"/{metadata.ShortName}:");
                        }
                        if (!string.IsNullOrEmpty(metadata.LongName))
                        {
                            aliasList.Add($"/{metadata.LongName}:");
                        }
                        if (aliasList.Any())
                        {
                            aliases = $" [{string.Join(", ", aliasList)}]";
                        }

                        // Add description if available
                        if (!string.IsNullOrEmpty(metadata.Description))
                        {
                            description = $" - {metadata.Description}";
                        }

                        // Mark as required if specified
                        if (metadata.Required)
                        {
                            defaultValueStr = ", required";
                        }
                    }

                    // Handle Enum types
                    if (field.FieldType.IsEnum)
                    {
                        string enumValues = string.Join(", ", Enum.GetNames(field.FieldType).Select(v => v.ToLower()));
                        return $"{position}{fieldName} (enum: {fieldType} [{enumValues}]{defaultValueStr}){aliases}{description}";
                    }

                    return $"{position}{fieldName} ({fieldType}{defaultValueStr}){aliases}{description}".Trim();
                })
                .ToList();

            return fields;
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
