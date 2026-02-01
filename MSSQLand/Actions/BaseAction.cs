// MSSQLand/Actions/BaseAction.cs

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
    /// ARGUMENT BINDING:
    /// =================
    /// 
    /// DEFAULT BEHAVIOR (auto-binding):
    /// --------------------------------
    /// Decorate fields with [ArgumentMetadata] and let the base class handle binding:
    /// 
    ///    [ArgumentMetadata(Position = 0, Required = true, Description = "Table name")]
    ///    private string _tableName;
    /// 
    ///    [ArgumentMetadata(Position = 1, ShortName = "l", LongName = "limit", Description = "Row limit")]
    ///    private int _limit = 25;  // Use typed fields, not string + parsing
    /// 
    /// If no custom validation is needed, you don't need to override ValidateArguments().
    /// The base class will call BindArguments() automatically.
    /// 
    /// CUSTOM VALIDATION:
    /// ------------------
    /// Override ValidateArguments() only when additional validation is required:
    /// 
    ///    public override void ValidateArguments(string[] args)
    ///    {
    ///        BindArguments(args);  // Always call first
    ///        if (_limit &lt; 0) throw new ArgumentException("Limit must be positive");
    ///    }
    /// 
    /// MANUAL PARSING (for complex cases):
    /// -----------------------------------
    /// Use ParseActionArguments(), GetNamedArgument(), GetPositionalArgument()
    /// when auto-binding is insufficient (e.g., FQTN parsing, joined arguments).
    /// </summary>
    
    [AttributeUsage(AttributeTargets.Field)]
    public abstract class BaseAction : Attribute
    {

        /// <summary>
        /// Validates the action arguments passed as string array (argparse-style).
        /// Default implementation calls BindArguments() for automatic field binding.
        /// Override only when custom validation or parsing logic is needed.
        /// </summary>
        /// <param name="args">The action-specific arguments as string array.</param>
        public virtual void ValidateArguments(string[] args)
        {
            BindArguments(args);
        }


        /// <summary>
        /// Executes the action using the provided ConnectionManager.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager for database operations.</param>
        public abstract object Execute(DatabaseContext databaseContext = null);

        /// <summary>
        /// Parse action arguments using modern CLI patterns (argparse-style).
        /// Supports: positional args, -flag value, --long-flag value, -flag:value, --long-flag=value
        /// 
        /// Used internally by BindArguments(). Only use directly for complex custom parsing.
        /// </summary>
        /// <param name="args">The action arguments array.</param>
        /// <returns>Dictionary of named arguments and list of positional arguments.</returns>
        protected (Dictionary<string, string> Named, List<string> Positional) ParseActionArguments(string[] args)
        {
            Logger.Trace($"Parsing action arguments: {string.Join(" ", args)}");
            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();

            if (args == null || args.Length == 0)
            {
                return (named, positional);
            }

            // Build lookup of boolean fields from ArgumentMetadata
            var booleanFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fields = GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.FieldType == typeof(bool))
                {
                    var metadata = field.GetCustomAttribute<ArgumentMetadataAttribute>();
                    if (metadata != null)
                    {
                        if (!string.IsNullOrEmpty(metadata.ShortName))
                            booleanFlags.Add(metadata.ShortName);
                        if (!string.IsNullOrEmpty(metadata.LongName))
                            booleanFlags.Add(metadata.LongName);
                    }
                }
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
                        
                        // Check if this flag is boolean
                        bool isBooleanFlag = booleanFlags.Contains(flagName);
                        
                        // Check if next arg is the value
                        if (i + 1 < args.Length && !isBooleanFlag)
                        {
                            string nextArg = args[i + 1];
                            // For non-boolean flags, consume next arg as value unless it looks like a flag
                            // A flag is: starts with -- OR starts with - followed by letters (not digits/special chars)
                            bool looksLikeFlag = nextArg.StartsWith("--") || 
                                               (nextArg.StartsWith("-") && nextArg.Length >= 2 && char.IsLetter(nextArg[1]));
                            
                            if (!looksLikeFlag)
                            {
                                flagValue = args[++i];
                            }
                        }
                        
                        // Only add to dictionary if:
                        // 1. It's a boolean flag (can be without value)
                        // 2. It's a non-boolean flag WITH a value
                        if (!string.IsNullOrEmpty(flagName))
                        {
                            if (isBooleanFlag)
                            {
                                named[flagName] = flagValue ?? "true";
                                Logger.TraceNested($"Parsed flag: {flagName} = {named[flagName]}");
                            }
                            else if (flagValue != null)
                            {
                                named[flagName] = flagValue;
                                Logger.TraceNested($"Parsed flag: {flagName} = {named[flagName]}");
                            }
                            else
                            {
                                Logger.TraceNested($"Flag --{flagName} requires a value but none was provided. Ignoring.");
                            }
                        }
                        continue;
                    }

                    if (!string.IsNullOrEmpty(flagName))
                    {
                        named[flagName] = flagValue ?? "true"; // For inline value formats
                        Logger.TraceNested($"Parsed flag: {flagName} = {named[flagName]}");
                    }
                }
                else
                {
                    // Positional argument
                    positional.Add(arg);
                    Logger.TraceNested($"Parsed positional arg: {arg}");
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
        /// Automatically binds parsed arguments to fields decorated with ArgumentMetadataAttribute.
        /// Uses reflection to match named/positional args to field metadata.
        /// </summary>
        /// <param name="args">The action arguments array.</param>
        /// <exception cref="ArgumentException">Thrown when required arguments are missing or conversion fails.</exception>
        protected void BindArguments(string[] args)
        {
            var (namedArgs, positionalArgs) = ParseActionArguments(args);
            var fields = GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance);

            // Second pass: bind arguments
            foreach (var field in fields)
            {
                var metadata = field.GetCustomAttribute<ArgumentMetadataAttribute>();
                if (metadata == null) continue;

                string value = null;

                // Try named arguments first (short name, then long name)
                if (!string.IsNullOrEmpty(metadata.ShortName))
                {
                    value = GetNamedArgument(namedArgs, metadata.ShortName);
                }
                if (value == null && !string.IsNullOrEmpty(metadata.LongName))
                {
                    value = GetNamedArgument(namedArgs, metadata.LongName);
                }

                // Fall back to positional argument
                if (value == null && metadata.Position >= 0)
                {
                    value = GetPositionalArgument(positionalArgs, metadata.Position);
                }

                // If value found, convert and set; otherwise check if required
                if (value != null)
                {
                    try
                    {
                        object convertedValue = ConvertArgumentValue(value, field.FieldType);
                        field.SetValue(this, convertedValue);
                    }
                    catch (Exception ex)
                    {
                        string argName = metadata.LongName ?? metadata.ShortName ?? field.Name.TrimStart('_');
                        throw new ArgumentException($"Failed to convert argument '{argName}' value '{value}' to {SimplifyType(field.FieldType)}: {ex.Message}");
                    }
                }
                else if (metadata.Required)
                {
                    string argName = metadata.LongName ?? metadata.ShortName ?? field.Name.TrimStart('_');
                    throw new ArgumentException($"Required argument '{argName}' is missing.");
                }
                // else: keep the field's default value
            }
        }

        /// <summary>
        /// Converts a string value to the target type.
        /// Supports: string, int, bool, enum types.
        /// </summary>
        /// <param name="value">The string value to convert.</param>
        /// <param name="targetType">The target type.</param>
        /// <returns>The converted value.</returns>
        private object ConvertArgumentValue(string value, Type targetType)
        {
            if (targetType == typeof(string))
            {
                return value;
            }

            if (targetType == typeof(int))
            {
                return int.Parse(value);
            }

            if (targetType == typeof(bool))
            {
                // Handle common boolean representations
                if (string.IsNullOrEmpty(value) || 
                    value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                return false;
            }

            if (targetType == typeof(long))
            {
                return long.Parse(value);
            }

            if (targetType == typeof(double))
            {
                return double.Parse(value);
            }

            if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, value, ignoreCase: true);
            }

            // Fallback to Convert.ChangeType for other types
            return Convert.ChangeType(value, targetType);
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
