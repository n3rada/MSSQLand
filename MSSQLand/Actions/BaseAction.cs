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
    /// 
    /// ARGUMENT PARSING GUIDE:
    /// =======================
    /// 
    /// All derived actions MUST properly parse and assign arguments to avoid CS0649 warnings.
    /// 
    /// RECOMMENDED PATTERN (Automatic Binding):
    /// ----------------------------------------
    /// 1. Decorate fields with [ArgumentMetadata]:
    ///    [ArgumentMetadata(Position = 0, Required = true, Description = "Table name")]
    ///    private string _tableName;
    /// 
    /// 2. Call BindArgumentsToFields() in ValidateArguments():
    ///    public override void ValidateArguments(string[] args)
    ///    {
    ///        BindArgumentsToFields(args); // Automatic population
    ///        // Add custom validation here
    ///    }
    /// 
    /// MANUAL PATTERN (Custom Logic):
    /// -------------------------------
    /// 1. Decorate fields (for documentation):
    ///    [ArgumentMetadata(Position = 0, Description = "Mode")]
    ///    private Mode _mode;
    /// 
    /// 2. Parse and assign manually:
    ///    public override void ValidateArguments(string[] args)
    ///    {
    ///        var (namedArgs, positionalArgs) = ParseActionArguments(args);
    ///        _mode = GetPositionalArgument(positionalArgs, 0);
    ///        _name = GetPositionalArgument(positionalArgs, 1);
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
        /// IMPORTANT: After calling this method, you MUST extract your fields manually:
        /// 
        /// Example usage:
        /// <code>
        /// var (namedArgs, positionalArgs) = ParseActionArguments(args);
        /// _mode = GetPositionalArgument(positionalArgs, 0);
        /// _tableName = GetPositionalArgument(positionalArgs, 1);
        /// _limit = GetNamedArgument(namedArgs, "limit", "0");
        /// </code>
        /// 
        /// OR use BindArgumentsToFields() for automatic binding (recommended).
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
        /// Automatically binds parsed arguments to fields decorated with [ArgumentMetadata].
        /// This method handles both positional and named arguments automatically.
        /// 
        /// Call this in ValidateArguments() for automatic field population:
        /// <code>
        /// public override void ValidateArguments(string[] args)
        /// {
        ///     BindArgumentsToFields(args);
        ///     // Additional custom validation here if needed
        /// }
        /// </code>
        /// </summary>
        /// <param name="args">The action arguments array.</param>
        /// <exception cref="ArgumentException">If required arguments are missing or type conversion fails.</exception>
        protected void BindArgumentsToFields(string[] args)
        {
            var (namedArgs, positionalArgs) = ParseActionArguments(args);

            // Get all fields with ArgumentMetadata
            var fields = GetType()
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
                .Where(f => f.GetCustomAttribute<ArgumentMetadataAttribute>() != null)
                .OrderBy(f => f.GetCustomAttribute<ArgumentMetadataAttribute>().Position)
                .ToList();

            foreach (var field in fields)
            {
                var metadata = field.GetCustomAttribute<ArgumentMetadataAttribute>();
                string fieldName = field.Name.TrimStart('_');
                string value = null;

                // Try to get value from positional arguments first
                if (metadata.Position >= 0 && metadata.Position < positionalArgs.Count)
                {
                    value = positionalArgs[metadata.Position];
                    Logger.Debug($"Binding positional arg [{metadata.Position}] '{value}' to field '{fieldName}'");
                }

                // Try named arguments if no positional value found
                if (value == null)
                {
                    // Check long name
                    if (!string.IsNullOrEmpty(metadata.LongName) && namedArgs.TryGetValue(metadata.LongName, out var longValue))
                    {
                        value = longValue;
                        Logger.Debug($"Binding named arg '--{metadata.LongName}' = '{value}' to field '{fieldName}'");
                    }
                    // Check short name
                    else if (!string.IsNullOrEmpty(metadata.ShortName) && namedArgs.TryGetValue(metadata.ShortName, out var shortValue))
                    {
                        value = shortValue;
                        Logger.Debug($"Binding named arg '-{metadata.ShortName}' = '{value}' to field '{fieldName}'");
                    }
                    // Check field name itself
                    else if (namedArgs.TryGetValue(fieldName, out var fieldValue))
                    {
                        value = fieldValue;
                        Logger.Debug($"Binding named arg '{fieldName}' = '{value}' to field '{fieldName}'");
                    }
                }

                // Validate required fields
                if (value == null && metadata.Required)
                {
                    string argDescription = metadata.Position >= 0 
                        ? $"positional argument at position {metadata.Position}" 
                        : $"named argument '{fieldName}'";
                    
                    if (!string.IsNullOrEmpty(metadata.Description))
                    {
                        throw new ArgumentException($"Missing required {argDescription}: {metadata.Description}");
                    }
                    throw new ArgumentException($"Missing required {argDescription}");
                }

                // Set the field value with type conversion
                if (value != null)
                {
                    try
                    {
                        SetFieldValue(field, value);
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException($"Failed to set field '{fieldName}' with value '{value}': {ex.Message}", ex);
                    }
                }
            }
        }

        /// <summary>
        /// Sets a field value with automatic type conversion.
        /// Supports: string, int, bool, enum, nullable types.
        /// </summary>
        private void SetFieldValue(FieldInfo field, string value)
        {
            Type fieldType = field.FieldType;

            // Handle nullable types
            Type underlyingType = Nullable.GetUnderlyingType(fieldType);
            if (underlyingType != null)
            {
                if (string.IsNullOrEmpty(value))
                {
                    field.SetValue(this, null);
                    return;
                }
                fieldType = underlyingType;
            }

            // Handle enums
            if (fieldType.IsEnum)
            {
                // Use the generic TryParse method for enums
                var tryParseMethod = typeof(Enum).GetMethod("TryParse", new[] { typeof(Type), typeof(string), typeof(bool), typeof(object).MakeByRefType() });
                object[] parameters = new object[] { fieldType, value, true, null };
                
                if ((bool)tryParseMethod.Invoke(null, parameters))
                {
                    field.SetValue(this, parameters[3]);
                }
                else
                {
                    var validValues = string.Join(", ", Enum.GetNames(fieldType));
                    throw new ArgumentException($"Invalid value '{value}' for enum {fieldType.Name}. Valid values: {validValues}");
                }
                return;
            }

            // Handle primitive types
            object convertedValue = fieldType.Name switch
            {
                "String" => value,
                "Int32" => int.Parse(value),
                "Int64" => long.Parse(value),
                "Boolean" => ParseBoolean(value),
                "Double" => double.Parse(value),
                "Decimal" => decimal.Parse(value),
                _ => Convert.ChangeType(value, fieldType)
            };

            field.SetValue(this, convertedValue);
        }

        private static bool ParseBoolean(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string v = value.Trim().ToLower();
            if (v == "1" || v == "true" || v == "yes" || v == "y") return true;
            if (v == "0" || v == "false" || v == "no" || v == "n") return false;
            return bool.Parse(value);
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
