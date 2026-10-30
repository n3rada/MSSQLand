# Repository instructions

## Project

MSSQLand is a C#/.NET Framework 4.8 SQL Server post-exploitation tool designed to run in the target environment, commonly through beacon assembly execution. It unifies identity impersonation, linked-server traversal, and database context.

Read [`DEVELOPMENT.md`](DEVELOPMENT.md) when changing architecture, authentication, query routing, impersonation, linked-server behavior, actions, logging, output, or build configuration. For focused changes, read only the relevant section and its linked source files.

Before preparing a commit, pull the current branch with `git pull --rebase --autostash`; never commit against stale remote history. Then follow the human contribution and commit conventions in [`DEVELOPMENT.md`](DEVELOPMENT.md).

## Core contracts

- Keep transport and linked-server routing in `QueryService`, identity orchestration in `DatabaseContext` and `UserService`, and user-facing capabilities in focused action classes.
- Actions inherit `BaseAction`, receive dependencies through `DatabaseContext`, and use the existing argument-binding lifecycle.
- Add every new action to `ActionFactory` and the explicit `<Compile Include>` list in `MSSQLand.csproj`.
- Preserve `Release` with `CONFIGMGR` and `Release-NoCM` without it; guard ConfigMgr-only code consistently.
- Use structured logging and the formatter pipeline; do not duplicate parsing, routing, rendering, or logging logic inside actions.
- Preserve existing CLI, linked-server fallback, timeout, retry, and output semantics unless the task intentionally changes them.

## Verification

This project targets .NET Framework 4.8. Build it on Windows with Visual Studio MSBuild, not `dotnet build`. On Linux, perform source-level validation and state that a build was not run.
