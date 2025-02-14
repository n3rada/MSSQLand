## Project Structure 📚
This project follows several key software development principles and practices.

1. **Single Responsability Principle (SRP)**

Each class should have one, and only one, reason to change. Each action class in the [`Actions`](./MSSQLand/Actions) directory, like [`Tables`](./MSSQLand/Actions/Database/Tables.cs) or [`Permissions`](./MSSQLand/Actions/Database/Permissions.cs), is responsible for a single operation.
The [`Logger`](./MSSQLand/Utilities/Logger.cs) class solely handles logging, decoupling it from other logic.

3. **Open/Close Principle (OCP)**

Software entities should be open for extension but closed for modification. Here, the [`BaseAction`](./MSSQLand/Actions/BaseAction.cs) abstract class defines a common interface-like for all actions. New actions can be added by inheriting from it without modifying existing code. Then, the [`ActionFactory`](./MSSQLand/Actions/ActionFactory.cs) enables seamless addition of new actions by simply adding them to the switch case.

4. **Liskov Substitution Principle (LSP)**

Subtypes should be substitutable for their base types without altering program behavior. Here, the [`BaseAction`](./MSSQLand/Actions/BaseAction.cs) class ensures all derived actions (e.g., Tables, Permissions, Smb) can be used interchangeably, provided they implement `ValidateArguments` and `Execute`.

5. **DRY (Don't Repeat Yourself)**

Avoid duplicating logic across the codebase. The [`QueryService`](./MSSQLand/Services/QueryService.cs) centralizes query execution, avoiding repetition in individual actions.

6. **KISS (Keep It Simple, Stupid)**

Systems should be as simple as possible but no simpler. Complex linked server queries and impersonation are abstracted into services, simplifying their usage.

8. **Extensibility**

The system should be easy to extend with new features. New actions can be added without altering core functionality by extending [`BaseAction`](./MSSQLand/Actions/BaseAction.cs) and adding the created-one to the [factory](./MSSQLand/Actions/ActionFactory.cs).

#### Directories

- [`Models`](./MSSQLand/Models)

Contains classes representing SQL Server entities, such as Server and LinkedServers.

- [`Services`](./MSSQLand/Services)

The backbone of the application, responsible for connection management, query execution, user management, and configuration handling.

- [`Actions`](./MSSQLand/Actions)

This directory contains all the specific operations that MSSQLand can perform. Each action follows a modular design using the command pattern to encapsulate its logic, such as PowerShell execution, querying, impersonation, and more.

- [`Utilities`](./MSSQLand/Utilities)

Helper classes like Logger and MarkdownFormatter that make your life easier.
