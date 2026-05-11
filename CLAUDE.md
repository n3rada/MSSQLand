# MSSQLand — AI Context

## Documentation

Read these before making changes:

| File | Purpose |
|---|---|
| [README.md](README.md) | Usage, CLI syntax, linked server chain format, discovery modes, credential types |
| [DEVELOPMENT.md](DEVELOPMENT.md) | Architecture deep-dive, design principles, query chain internals, full action skeleton |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Contribution workflow, branching, PR guidelines |
| [ORIGIN.md](ORIGIN.md) | Project history — not technical, skip unless context on motivation is needed |

## Project

.NET Framework 4.8, C# LangVersion 11.0, AnyCPU. Built on Windows with `msbuild` or `dotnet build`. The output is a single `.exe` designed to run as an in-memory assembly inside a beacon using the current execution context.

ConfigMgr actions are gated behind `#if ENABLE_CM`. Enable at build time:
```
msbuild /p:EnableCM=true /p:Configuration=Release
```

## Architecture

```
MSSQLand/
├── Actions/        # One class per action, grouped by category
│   ├── Administration/
│   ├── Agent/
│   ├── ConfigMgr/  # Compiled only when ENABLE_CM is defined
│   ├── Database/
│   ├── Domain/
│   ├── Execution/
│   ├── FileSystem/
│   └── Remote/
├── Services/       # DatabaseContext, QueryService, UserService, AuthenticationService, ConfigurationService
├── Models/         # Server, LinkedServers
├── Utilities/      # Logger, Formatters, ByteHelper, SqlHelper, NetworkHelper, EncodingHelper, SidParser, Discovery/
└── Exceptions/     # Custom exception types
```

[`DatabaseContext`](MSSQLand/Services/DatabaseContext.cs) is the single facade actions receive. It composes all services. Actions never need to know whether impersonation or linked servers are active — [`QueryService`](MSSQLand/Services/QueryService.cs)`.PrepareQuery()` transparently wraps raw SQL with `EXECUTE AS LOGIN` and `OPENQUERY`/`EXEC AT` nesting at each hop.

## Adding a New Action

### 1. Create the class

```csharp
// MSSQLand/Actions/<Category>/MyAction.cs
namespace MSSQLand.Actions.<Category>
{
    internal class MyAction : BaseAction  // BaseAction: MSSQLand/Actions/BaseAction.cs
    {
        [ArgumentMetadata(Position = 0, ShortName = "n", LongName = "name",
            Required = true, Description = "The target name")]
        private string _name = "";

        [ArgumentMetadata(Position = 1, ShortName = "c", LongName = "count",
            Required = false, Description = "Row limit")]
        private int _count = 10;

        // [ArgumentMetadata]: MSSQLand/Actions/ArgumentMetadataAttribute.cs
        // Bool with Toggle = true accepts: +/-, on/off, enable/disable, add/del, 1/0, true/false
        [ArgumentMetadata(Position = 2, ShortName = "e", LongName = "enable",
            Required = true, Toggle = true, Description = "Enable or disable")]
        private bool _enable = false;

        // Remainder = true joins all remaining positional args with a space (useful for shell commands)
        [ArgumentMetadata(Position = 3, ShortName = "cmd", LongName = "command",
            Required = false, Remainder = true, Description = "Shell command")]
        private string _command = "";

        // [ExcludeFromArguments]: MSSQLand/Actions/ExcludeFromArgumentsAttribute.cs
        [ExcludeFromArguments]
        private string _internal = "";

        // No need to override ValidateArguments() for standard cases.
        // BaseAction.ValidateArguments() calls BindArguments() automatically which:
        //   - Parses positional and named args (-n / --name)
        //   - Binds them to fields via reflection
        //   - Converts types (string, int, bool, enum)
        //   - Throws MissingRequiredArgumentException for missing required args
        //
        // Only override for custom validation:
        // public override void ValidateArguments(string[] args)
        // {
        //     BindArguments(args);
        //     if (_count < 0) throw new ArgumentException("Count must be positive");
        // }

        public override object Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Running with name={_name}, count={_count}");

            string query = $"SELECT TOP {_count} column FROM sys.some_table WHERE name = '{_name}';";

            DataTable result = databaseContext.QueryService.ExecuteTable(query);

            if (result.Rows.Count == 0)
            {
                Logger.Warning("No results.");
                return null;
            }

            // OutputFormatter: MSSQLand/Utilities/Formatters/OutputFormatter.cs
            Console.WriteLine(OutputFormatter.ConvertDataTable(result));
            Logger.Success($"Done. {result.Rows.Count} row(s).");
            return result;
        }
    }
}
```

### 2. Register in ActionFactory

Open [`MSSQLand/Actions/ActionFactory.cs`](MSSQLand/Actions/ActionFactory.cs) and add an entry to the `ActionMetadata` dictionary in the appropriate section:

```csharp
{ "my-action", (typeof(MyAction), "One-line description shown in help.", new[] { "ma", "alias2" }) },
```

Aliases are optional (`null` if none). The key is the CLI name (lowercase).

### 3. Add to .csproj

Open [`MSSQLand/MSSQLand.csproj`](MSSQLand/MSSQLand.csproj) and add a `<Compile>` entry:

```xml
<Compile Include="Actions\<Category>\MyAction.cs" />
```

ConfigMgr actions must be wrapped:
```xml
<Compile Condition="'$(EnableCM)' == 'true'" Include="Actions\ConfigMgr\MyAction.cs" />
```

## QueryService API — [`MSSQLand/Services/QueryService.cs`](MSSQLand/Services/QueryService.cs)

| Method | Returns | Use for |
|---|---|---|
| `ExecuteTable(query)` | `DataTable` | SELECT queries returning rows |
| `ExecuteScalar(query)` | `object` | Single value (COUNT, @@SERVERNAME, etc.) |
| `Execute(query)` | `SqlDataReader` | Raw reader access |

## Logging — [`MSSQLand/Utilities/Logger.cs`](MSSQLand/Utilities/Logger.cs)

```csharp
Logger.TaskNested("Starting...");   // [>] indented task line
Logger.Success("Done.");            // [✓] green
Logger.Warning("Watch out.");       // [!] yellow
Logger.Error("Something broke.");   // [✗] red
```

## Key Conventions

- All action fields **must have default values** (required for .NET Framework 4.8 + reflection binding, avoids CS0649).
- Boolean fields with `Toggle = true` never need `ValidateArguments()` override; `BindArguments()` handles all toggle aliases.
- Actions must not duplicate impersonation or linked-server logic — `QueryService.PrepareQuery()` handles all of it transparently.
- [`DatabaseContext`](MSSQLand/Services/DatabaseContext.cs) implements `IDisposable`; [`Program.cs`](MSSQLand/Program.cs) wraps it in `using` — never manually close the connection in an action.
