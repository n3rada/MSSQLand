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


## Credential Types

| Type    | Required Arguments         |
| ------- | -------------------------- |
| token   | None                       |
| domain  | username, password, domain |
| local   | username, password         |
| entraid | username, password         |
| azure   | username, password         |


## Available Actions

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

## Enumerations

| Enumeration | Description                |
| ----------- | -------------------------- |
| servers     | Search for MS SQL Servers. |
