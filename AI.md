# MSSQLand: AI Engineering Guide

This file is the canonical AI guidance for this repository.

## Read Order

1. [README.md](README.md) - CLI behavior, usage, and operator-facing semantics.
2. [DEVELOPMENT.md](DEVELOPMENT.md) - architecture, design principles, and extension model (source of truth).
3. [CONTRIBUTING.md](CONTRIBUTING.md) - contribution and review workflow.

## Source Map (Start Here)

- Entry point and control flow: [MSSQLand/Program.cs](MSSQLand/Program.cs)
- Action registry and aliases: [MSSQLand/Actions/ActionFactory.cs](MSSQLand/Actions/ActionFactory.cs)
- Action base class and argument binding: [MSSQLand/Actions/BaseAction.cs](MSSQLand/Actions/BaseAction.cs)
- Query orchestration and linked-server routing: [MSSQLand/Services/QueryService.cs](MSSQLand/Services/QueryService.cs)
- Runtime service composition and impersonation flow: [MSSQLand/Services/DatabaseContext.cs](MSSQLand/Services/DatabaseContext.cs)
- Auth abstraction and credential types: [MSSQLand/Services/Authentication/AuthenticationService.cs](MSSQLand/Services/Authentication/AuthenticationService.cs)
- CLI parsing and modes: [MSSQLand/Utilities/CommandParser.cs](MSSQLand/Utilities/CommandParser.cs)
- Logging conventions: [MSSQLand/Utilities/Logger.cs](MSSQLand/Utilities/Logger.cs)
- Output formatting: [MSSQLand/Utilities/Formatters/OutputFormatter.cs](MSSQLand/Utilities/Formatters/OutputFormatter.cs)
- Project compile manifest: [MSSQLand/MSSQLand.csproj](MSSQLand/MSSQLand.csproj)

## Build and Configuration Facts

- Target framework: .NET Framework 4.8.
- **Do not attempt to build on Linux.** .NET Framework 4.8 is Windows-only; builds will fail. Only verify that code is structurally correct (types, references, csproj manifest).
- **On Windows, build exclusively via Visual Studio** (`msbuild` through the VS Developer Command Prompt, or the IDE itself). Do not use `dotnet build`: the .NET SDK CLI does not fully support .NET Framework 4.8 projects on Windows and may silently skip or mishandle build configurations.
- **To locate MSBuild**, use `vswhere` (ships with every Visual Studio installation):
  ```bat
  "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe
  ```
  Then invoke the returned path directly:
  ```bat
  <msbuild-path> MSSQLand\MSSQLand.csproj /p:Configuration=Release
  ```
- The project uses an explicit `<Compile Include=...>` list in [MSSQLand/MSSQLand.csproj](MSSQLand/MSSQLand.csproj). New `.cs` files must be added there or they will not build.
- ConfigMgr code paths are guarded by `#if CONFIGMGR` in source (for example [MSSQLand/Actions/ActionFactory.cs](MSSQLand/Actions/ActionFactory.cs)).
- Use build configurations, not ad-hoc symbols:
  - `Release` includes `CONFIGMGR`.
  - `Release-NoCM` excludes `CONFIGMGR`.

## Architecture and Design Principles

This section is a strict summary. Detailed rationale and examples live in [DEVELOPMENT.md](DEVELOPMENT.md).
If this file and [DEVELOPMENT.md](DEVELOPMENT.md) ever diverge, follow [DEVELOPMENT.md](DEVELOPMENT.md) for architecture and design decisions.

Enforce these rules on every change:

1. Single Responsibility Principle (SRP)
- Keep actions focused on one user-facing capability.
- Keep transport/routing logic in [MSSQLand/Services/QueryService.cs](MSSQLand/Services/QueryService.cs), not in actions.
- Keep identity and impersonation orchestration in [MSSQLand/Services/DatabaseContext.cs](MSSQLand/Services/DatabaseContext.cs) and [MSSQLand/Services/UserService.cs](MSSQLand/Services/UserService.cs).

2. OOP and separation of concerns
- Actions inherit from [MSSQLand/Actions/BaseAction.cs](MSSQLand/Actions/BaseAction.cs).
- Dependencies are consumed via `DatabaseContext` in `Execute(...)`.
- Prefer adding behavior to existing services instead of duplicating logic inside action classes.

3. Open/Closed Principle (OCP)
- Add new behavior by introducing new action classes and registering them in [MSSQLand/Actions/ActionFactory.cs](MSSQLand/Actions/ActionFactory.cs).
- Avoid changing shared base behavior unless there is a cross-cutting need.

4. Liskov + composition boundaries
- Preserve BaseAction substitutability: every action must validate arguments and execute through the same lifecycle contracts in [MSSQLand/Actions/BaseAction.cs](MSSQLand/Actions/BaseAction.cs).
- Prefer composition through [MSSQLand/Services/DatabaseContext.cs](MSSQLand/Services/DatabaseContext.cs) instead of introducing inheritance-heavy hierarchies.

5. DRY and consistency
- Reuse parser, logger, formatter, and query helpers.
- Do not reimplement argument parsing that is already provided by [MSSQLand/Actions/BaseAction.cs](MSSQLand/Actions/BaseAction.cs).

6. Fail fast and observable behavior
- Use clear errors and structured logs via [MSSQLand/Utilities/Logger.cs](MSSQLand/Utilities/Logger.cs).
- Preserve existing operational semantics (timeouts, retries, linked-server fallback paths) unless a change explicitly targets them.

## Action Authoring Rules

When adding or changing an action:

1. Create action class in the correct category under [MSSQLand/Actions](MSSQLand/Actions).
2. Inherit from `BaseAction` and declare arguments with `ArgumentMetadataAttribute`.
3. Give every action field a default value (required by current reflection-binding patterns).
4. Prefer base `ValidateArguments(...)`; override only for truly custom validation.
5. Keep SQL chain, RPC/OPENQUERY decisions, and linked-server wrapping out of action code. Delegate to [MSSQLand/Services/QueryService.cs](MSSQLand/Services/QueryService.cs).
6. Register action and aliases in [MSSQLand/Actions/ActionFactory.cs](MSSQLand/Actions/ActionFactory.cs).
7. Add the new `.cs` file to [MSSQLand/MSSQLand.csproj](MSSQLand/MSSQLand.csproj).
8. If the action is ConfigMgr-only, guard with `#if CONFIGMGR` consistently.

## Logging and Output Rules

- Use `Task`/`TaskNested` for operational steps.
- Use `Info`/`InfoNested` for factual data/results.
- Use `Success`, `Warning`, `Error` for outcomes.
- Use `Debug` for notable diagnostic events useful to an operator running `--debug`: connection failures with reasons, significant state changes. Not for repetitive per-iteration detail.
- Use `Trace` for full internal state: per-hop traversal, cache hits, skip decisions, internal counts. Visible with `--trace`; intended for developers and LLM-assisted diagnosis.
- Use `Logger.NewLine()` to visually separate major phases in long-running operations. Do not use it between nested sub-steps of the same phase.
- Prefer formatter pipeline output over ad-hoc table rendering; see [MSSQLand/Utilities/Formatters](MSSQLand/Utilities/Formatters).

## OPSEC and IOC Discipline

MSSQLand is a professional tool. Static detection by AV, EDR, or SIEM commonly targets complete flagged string literals in binaries. **Never write a known IOC as a plain, complete string literal in source.**

### What counts as a flagged string

- SQL stored procedure names with known signatures: the procedure name used in `XpCmd`, the `sp_OA*` family, `xp_reg*`, `sp_addsrvrolemember`, and similar.
- T-SQL fragments that are well-known attack patterns when seen as whole strings.
- LDAP filter strings associated with enumeration queries.

### Canonical construction pattern

Split flagged strings at runtime so no complete form appears in the compiled binary. See [`MSSQLand/Actions/Execution/XpCmd.cs`](MSSQLand/Actions/Execution/XpCmd.cs) as the reference:

```csharp
// Wrong - complete IOC string present in binary:
string proc = "xp_cmdshell";

// Correct - assembled at runtime, no complete string in static analysis:
string proc = "x" + "p" + "_" + "cmdTest";
proc = proc.Replace("Test", "shell");
```

Alternatives: character arrays, `string.Concat`, `StringBuilder`, or `string.Format` with non-obvious indices. Choose whichever reads most naturally for the specific case.

### Where this rule applies

- **SQL query strings** sent to the server: highest priority.
- **Log and error messages**: do not echo the raw flagged procedure name back to stdout. Log the intent ("enable command shell"), not the literal token.
- **Help text and action descriptions**: describe what the action does functionally. Do not reproduce the exact SQL identifier in user-visible strings.

### Where this rule does NOT apply

C# symbol names (class names, method names, variable names) are not IOCs. They do not appear as runtime strings in the binary the same way SQL query strings do. `XpCmd` as a class name is fine; `"xp_cmdshell"` as a query string is not.

## Definition of Done for Code Changes

A change is not complete until all are true:

1. Behavior aligns with CLI semantics in [README.md](README.md).
2. Design follows SRP/OOP boundaries above.
3. New/changed files are reflected in [MSSQLand/MSSQLand.csproj](MSSQLand/MSSQLand.csproj) when needed.
4. Logging and output style remain consistent with existing patterns.
5. Build configuration impacts (`Release` vs `Release-NoCM`) are considered.
6. No unrelated refactors or formatting churn.
