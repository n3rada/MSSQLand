# MSSQLand Command Reference

## 📌 Command-Line Arguments


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
| /silent or /s      | Enable silent mode (minimal output).                   |
| /debug             | Enable debug mode for detailed logs.                   |
| /help              | Display this help message and exit.                    |
| /printhelp         | Save commands to COMMANDS.md file.                     |


## 🔑 Credential Types


| Type    | Required Arguments         |
| ------- | -------------------------- |
| token   | None                       |
| domain  | username, password, domain |
| local   | username, password         |
| entraid | username, password         |
| azure   | username, password         |


## 🛠 Available Actions


| Action          | Description                                                                                             | Arguments                                                                                                                                                           |
| --------------- | ------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| info            | Retrieve information about the DBMS server.                                                             |                                                                                                                                                                     |
| whoami          | Retrieve information about the current user.                                                            |                                                                                                                                                                     |
| links           | Retrieve linked server information.                                                                     |                                                                                                                                                                     |
| monitor         | List running SQL commands.                                                                              |                                                                                                                                                                     |
| oledb-providers | Retrieve detailed configuration and properties of OLE DB providers.                                     |                                                                                                                                                                     |
| databases       | List available databases.                                                                               |                                                                                                                                                                     |
| tables          | List tables in a database.                                                                              | database (string)                                                                                                                                                   |
| rows            | Retrieve rows from a table.                                                                             | database (string), schema (string, default: dbo), table (string)                                                                                                    |
| procedures      | List stored procedures, read their definitions, or execute them with arguments.                         | mode (enum: Mode [list, exec, read], default: list), procedureName (string), procedureArgs (string)                                                                 |
| users           | List database users.                                                                                    |                                                                                                                                                                     |
| permissions     | Enumerate permissions.                                                                                  | database (string), schema (string, default: dbo), table (string)                                                                                                    |
| search          | Search for specific keyword in database.                                                                | database (string), keyword (string)                                                                                                                                 |
| linkmap         | Enumerate all possible linked server chains and access paths.                                           |                                                                                                                                                                     |
| impersonate     | Check and perform user impersonation.                                                                   |                                                                                                                                                                     |
| query           | Execute a custom T-SQL query.                                                                           | query (string)                                                                                                                                                      |
| exec            | Execute commands using xp_cmdshell.                                                                     | command (string)                                                                                                                                                    |
| pwsh            | Execute PowerShell commands.                                                                            | script (string)                                                                                                                                                     |
| pwshdl          | Download and execute a PowerShell script.                                                               | url (string)                                                                                                                                                        |
| ole             | Executes the specified command using OLE Automation Procedures.                                         | command (string)                                                                                                                                                    |
| clr             | Deploy and execute CLR assemblies.                                                                      | dllURI (string), function (string)                                                                                                                                  |
| rpc             | Enable or disable RPC on a server.                                                                      | action (enum: RpcActionMode [add, del], default: add), linkedServerName (string)                                                                                    |
| smb             | Leverages xp_dirtree to send SMB requests to a specified UNC path, potentially coercing authentication. | uncPath (string)                                                                                                                                                    |
| adsi            | Enumerate ADSI linked servers, extract credentials, or impersonate users via ADSI exploitation.         | mode (enum: Mode [list, self, link], default: list), targetServer (string)                                                                                          |
| config          | Use sp_configure to modify settings.                                                                    | state (int, default: 0), optionName (string)                                                                                                                        |
| agents          | Interact with and manage SQL Server Agent jobs.                                                         | action (enum: ActionMode [status, exec], default: status), command (string), subSystem (enum: SubSystemMode [cmd, powershell, tsql, vbscript], default: powershell) |
| read            | Read file contents.                                                                                     | filePath (string)                                                                                                                                                   |
| kill            | Terminate running SQL commands by session ID or all.                                                    | target (string)                                                                                                                                                     |


## 🔎 Available Enumerations


| Enumeration | Description                |
| ----------- | -------------------------- |
| servers     | Search for MS SQL Servers. |

