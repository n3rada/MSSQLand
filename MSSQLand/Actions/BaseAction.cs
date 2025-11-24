using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MSSQLand.Actions
{
    /// <summary>
    /// Abstract base class for all actions, enforcing validation and execution logic.
    /// 
    /// ARGUMENT PARSING GUIDE:
    /// =======================
    /// 
    /// All derived actions MUST properly parse and assign arguments.
    /// 
    /// STANDARD PATTERN:
    /// -----------------
    /// 1. Decorate fields with [ArgumentMetadata] for documentation:
    ///    [ArgumentMetadata(Position = 0, Required = true, Description = "Table name")]
    ///    private string _tableName;
    /// 
    /// 2. Parse and assign manually in ValidateArguments():
    ///    public override void ValidateArguments(string[] args)
    ///    {
    ///        var (namedArgs, positionalArgs) = ParseActionArguments(args);
    ///        
    ///        // Extract positional arguments
    ///        _tableName = GetPositionalArgument(positionalArgs, 0);
    ///        
    ///        // Extract named arguments
    ///        string limitStr = GetNamedArgument(namedArgs, "limit", "0");
    ///        _limit = int.Parse(limitStr);
    ///        
    ///        // Add validation
    ///        if (string.IsNullOrEmpty(_tableName))
    ///            throw new ArgumentException("Table name is required");
    ///    }
    /// </summary>
    
    [AttributeUsage(AttributeTargets.Field)]
    public abstract class BaseAction : Attribute
    {

        /// <summary>
        /// Validates the action arguments passed as string array (argparse-style).
        /// </summary>
        /// <param name="args">The action-specific arguments as string array.</param>
        public abstract void ValidateArguments(string[] args);


        /// <summary>
        /// Executes the action using the provided ConnectionManager.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager for database operations.</param>
        public abstract object? Execute(DatabaseContext databaseContext = null);

        /// <summary>
        /// Parse action arguments using modern CLI patterns (argparse-style).
        /// Supports: positional args, -flag value, --long-flag value, -flag:value, --long-flag=value
        /// 
        /// Example usage:
        /// <code>
        /// var (namedArgs, positionalArgs) = ParseActionArguments(args);
        /// _mode = GetPositionalArgument(positionalArgs, 0);
        /// _tableName = GetPositionalArgument(positionalArgs, 1);
        /// _limit = int.Parse(GetNamedArgument(namedArgs, "limit", "0"));
        /// </code>
        /// </summary>
        /// <param name="args">The action arguments array.</param>
        /// <returns>Dictionary of named arguments and list of positional arguments.</returns>
        protected (Dictionary<string, string> Named, List<string> Positional) ParseActionArguments(string[] args)
        {
            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();

            if (args == null || args.Length == 0)
            {
                return (named, positional);
            }

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                // Check if it's a flag (starts with - or --)
                if (arg.StartsWith("-"))
                {
                    string flagName;
                    string flagValue = null;

                    // Check for inline value: -flag:value or --flag=value
                    int separatorIndex = arg.IndexOfAny(new[] { ':', '=' });
                    if (separatorIndex > 0)
                    {
                        flagName = arg.Substring(arg.StartsWith("--") ? 2 : 1, separatorIndex - (arg.StartsWith("--") ? 2 : 1));
                        flagValue = arg.Substring(separatorIndex + 1);
                    }
                    else
                    {
                        // Flag without inline value: -flag value or --flag value
                        flagName = arg.StartsWith("--") ? arg.Substring(2) : arg.Substring(1);
                        
                        // Check if next arg is the value (not another flag)
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        {
                            flagValue = args[++i];
                        }
                    }

                    if (!string.IsNullOrEmpty(flagName))
                    {
                        named[flagName] = flagValue ?? "true"; // Boolean flags default to "true"
                        Logger.Debug($"Parsed flag: {flagName} = {named[flagName]}");
                    }
                }
                else
                {
                    // Positional argument
                    positional.Add(arg);
                    Logger.Debug($"Parsed positional arg: {arg}");
                }
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
                            aliasList.Add($"-{metadata.ShortName}");
                        }
                        if (!string.IsNullOrEmpty(metadata.LongName))
                        {
                            aliasList.Add($"--{metadata.LongName}");
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
