# Development Guide

This guide covers the technical architecture, design principles, and instructions for extending MSSQLand.

## 📐 Design Principles

MSSQLand follows several key software development principles:

### Single Responsibility Principle (SRP)

Each class should have one, and only one, reason to change. Each action class in the [Actions](../MSSQLand/Actions) directory, like [Tables](../MSSQLand/Actions/Database/Tables.cs) or [Permissions](../MSSQLand/Actions/Database/Permissions.cs), is responsible for a single operation. The [Logger](../MSSQLand/Utilities/Logger.cs) class solely handles logging, decoupling it from other logic.

### Open/Close Principle (OCP)

Software entities should be open for extension but closed for modification. The [BaseAction](../MSSQLand/Actions/BaseAction.cs) abstract class defines a common interface for all actions. New actions can be added by inheriting from it without modifying existing code. The [ActionFactory](../MSSQLand/Actions/ActionFactory.cs) enables seamless addition of new actions by simply adding them to the switch case.

### Liskov Substitution Principle (LSP)

Subtypes should be substitutable for their base types without altering program behavior. The [BaseAction](../MSSQLand/Actions/BaseAction.cs) class ensures all derived actions (e.g., Tables, Permissions, Smb) can be used interchangeably, provided they implement `Execute`.

### DRY (Don't Repeat Yourself)

Avoid duplicating logic across the codebase. The [QueryService](../MSSQLand/Services/QueryService.cs) centralizes query execution, avoiding repetition in individual actions.

### KISS (Keep It Simple, Stupid)

Systems should be as simple as possible but no simpler. Complex linked server queries and impersonation are abstracted into services, simplifying their usage.

### Extensibility

The system should be easy to extend with new features. New actions can be added without altering core functionality by extending [BaseAction](../MSSQLand/Actions/BaseAction.cs) and adding the created action to the [factory](../MSSQLand/Actions/ActionFactory.cs).

---

## 🏗️ Architecture

MSSQLand is built on a clean, OOP-driven architecture designed for extensibility:

- **Modular Actions**: Each action is a self-contained class inheriting from [BaseAction](../MSSQLand/Actions/BaseAction.cs)
- **Factory Pattern**: Actions are automatically discovered and instantiated via [ActionFactory](../MSSQLand/Actions/ActionFactory.cs)
- **[Service Layer](../MSSQLand/Services)**: Separation of concerns with `DatabaseContext`, `QueryService`, `UserService`, and `AuthenticationService`
- **Credential Abstraction**: Multiple authentication methods through [CredentialsFactory](../MSSQLand/Services/Authentication/Credentials/CredentialsFactory.cs) (Token, Domain, Local, Azure, Entra ID, Windows Auth)
- **Chainable Operations**: Built-in support for linked server traversal and user impersonation chaining

---

## 📁 Project Structure

```
MSSQLand/
├── Actions/          # All action implementations
│   ├── Administration/
│   ├── ConfigMgr/
│   ├── Database/
│   ├── Domain/
│   ├── Execution/
│   ├── FileSystem/
│   └── Remote/
├── Services/         # Core services (DB, Auth, etc.)
├── Models/           # Data models
├── Utilities/        # Helper classes
└── Exceptions/       # Custom exceptions
```

### Directory Descriptions

#### [Models](../MSSQLand/Models)

Contains classes representing SQL Server entities, such as `Server` and `LinkedServers`.

#### [Services](../MSSQLand/Services)

The backbone of the application, responsible for connection management, query execution, user management, and configuration handling.

#### [Actions](../MSSQLand/Actions)

This directory contains all the specific operations that MSSQLand can perform. Each action follows a modular design using the command pattern to encapsulate its logic, such as PowerShell execution, querying, impersonation, and more.

#### [Utilities](../MSSQLand/Utilities)

Helper classes like `Logger` and `MarkdownFormatter` that make your life easier.

---

## 🎬 Adding a New Action

This design makes adding new features straightforward—simply create a new action class, and the framework handles the rest.

### Step 1: Choose the Right Directory

Depending on your new feature, create a new action class inside the appropriate subdirectory:

- `Actions/Administration/` - Server management, sessions, monitoring
- `Actions/Database/` - Database operations, queries, permissions
- `Actions/Domain/` - Active Directory interactions
- `Actions/Execution/` - Command execution, scripts
- `Actions/FileSystem/` - File operations
- `Actions/Remote/` - Linked servers, RPC, ADSI
- `Actions/ConfigMgr/` - Configuration Manager operations

### Step 2: Create Your Action Class

Copy-paste this skeleton and customize:

```csharp
// MSSQLand/Actions/Database/NewAction.cs

using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Data;

namespace MSSQLand.Actions.Database
{
    internal class NewAction : BaseAction
    {
        /// <summary>
        /// Arguments are automatically bound via reflection using [ArgumentMetadata].
        /// Supports positional args, short names (-a), and long names (--argument).
        /// Fields must have default values for .NET Framework 4.8 compatibility.
        /// </summary>
        [ArgumentMetadata(Position = 0, ShortName = "a", LongName = "argument", Required = true, Description = "Describe what this argument does")]
        private string _argument = "";

        [ArgumentMetadata(Position = 1, ShortName = "c", LongName = "count", Required = false, Description = "Optional count parameter")]
        private int _count = 10;

        /// <summary>
        /// Use [ExcludeFromArguments] for internal fields that should not be parsed from CLI.
        /// </summary>
        [ExcludeFromArguments]
        private string _privateVariable = "";

        // Note: No need to override ValidateArguments() for simple cases.
        // BaseAction.ValidateArguments() automatically calls BindArguments() which:
        //   1. Parses positional and named arguments
        //   2. Binds them to fields decorated with [ArgumentMetadata]
        //   3. Converts types automatically (string, int, bool, enum)
        //   4. Throws MissingRequiredArgumentException for missing required args
        //
        // Override ValidateArguments() only for custom validation logic:
        //
        // public override void ValidateArguments(string[] args)
        // {
        //     BindArguments(args);  // Always call base binding first
        //     
        //     // Add custom validation
        //     if (_count < 0)
        //         throw new ArgumentException("Count must be positive");
        // }

        public override object Execute(DatabaseContext databaseContext)
        {
            // Log the action being performed
            Logger.TaskNested($"Performing action with argument: {_argument}, count: {_count}");

            // Your T-SQL query
            string query = @"
                SELECT TOP (@count)
                    column1,
                    column2
                FROM sys.some_table
                WHERE condition = @param;";

            try
            {
                // Execute query and get results
                DataTable result = databaseContext.QueryService.ExecuteTable(query);

                if (result.Rows.Count == 0)
                {
                    Logger.Warning("No results found.");
                    return null;
                }

                // Display results
                Console.WriteLine(OutputFormatter.ConvertDataTable(result));
                
                // Log success
                Logger.Success($"Action completed successfully. Retrieved {result.Rows.Count} row(s).");

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error executing action: {ex.Message}");
                return null;
            }
        }
    }
}
```

### Argument Binding Reference

The `[ArgumentMetadata]` attribute supports:

| Property | Type | Description |
|----------|------|-------------|
| `Position` | int | Positional index (0-based) for unnamed arguments |
| `ShortName` | string | Single-letter flag (e.g., `-a`) |
| `LongName` | string | Full name flag (e.g., `--argument`) |
| `Required` | bool | If true, throws exception when missing |
| `Description` | string | Help text for the argument |

**Supported field types:** `string`, `int`, `bool`, `enum`

**Usage examples:**
```shell
# Positional
MSSQLand localhost -c token newaction myvalue

# Named (short)
MSSQLand localhost -c token newaction -a myvalue -c 20

# Named (long)
MSSQLand localhost -c token newaction --argument myvalue --count 20
```

### Step 3: Register in ActionFactory

Add the newly created action to [ActionFactory.cs](../MSSQLand/Actions/ActionFactory.cs):

```csharp
{ "new", (typeof(NewAction), "Lorem ipsum dolor sit amet consectetur adipiscing elit quisque faucibus ex sapien vitae pellentesque sem.") },
```

### Step 4: Test

Now you can use it directly:

```shell
MSSQLand localhost -c token new
```

---

## 💻 Development Setup

### Requirements

- **Visual Studio 2019+** or **VS Code** with C# extension
- **.NET Framework 4.8** SDK
- **MSBuild** (included with Visual Studio)
- **NuGet** package manager

### Building

```bash
# Restore dependencies
nuget restore MSSQLand/MSSQLand.sln

# Build (Release)
msbuild MSSQLand/MSSQLand.sln /p:Configuration=Release

# Build (Debug)
msbuild MSSQLand/MSSQLand.sln /p:Configuration=Debug

# Build without ConfigMgr support
msbuild MSSQLand/MSSQLand.sln /p:Configuration=Release /p:EnableCM=false
```

### Testing

Before submitting changes:

1. **Build the project** without warnings
2. **Test against a real SQL Server** instance
3. **Verify both editions** (standard and ConfigMgr if applicable)
4. **Test no regressions** in existing features

---

## 📝 Code Guidelines

- **C# Version:** 11.0 targeting .NET Framework 4.8
- **Code Style:** Follow existing patterns (see `.editorconfig`)
- **Naming:** Use descriptive names, follow C# conventions
- **Comments:** Add XML documentation for public APIs, file path comments at the top of each file
- **Actions:** New actions should inherit from `BaseAction`
- **SQL Queries:** No comments in SQL strings (stealth requirement)

### File Header

Always include the file path comment at the top:

```csharp
// MSSQLand/Actions/Database/YourAction.cs

using MSSQLand.Services;
// ... rest of file
```

---

## 🌉 Port Forwarding with Linux

If you're running MSSQLand on your Windows host but need to access a SQL Server target through a Linux environment (Hyper-V VM, VMware, or WSL), you can easily forward the connection using `socat`:

```bash
sudo socat TCP4-LISTEN:1433,fork,reuseaddr TCP:10.10.11.90:1433
```

This command listens on port 1433 on your Linux machine and forwards all traffic to the target SQL Server at `10.10.11.90:1433`. You can then connect MSSQLand to your Linux VM's IP from your Windows host.

---

## 📚 Related Documentation

- **Contributing**: See [CONTRIBUTING.md](CONTRIBUTING.md) for PR guidelines, commit signing, and code review process
- **Security**: See [SECURITY.md](SECURITY.md) for reporting vulnerabilities
- **Releases**: See [RELEASING.md](RELEASING.md) for versioning and release process (maintainers only)
- **Usage**: See [README.md](../README.md) for user-facing documentation
