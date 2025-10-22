# MSSQLand Command Reference

## 📌 Command-Line Arguments


| Argument           | Description                                                           |
| ------------------ | --------------------------------------------------------------------- |
| /findsql <domain>  | Find SQL Servers in Active Directory (no database connection needed). |
| /h or /host        | Specify the target SQL Server (mandatory for actions).                |
| /c or /credentials | Specify the credential type (mandatory for actions).                  |
| /u or /username    | Provide the username (if required by credential type).                |
| /p or /password    | Provide the password (if required by credential type).                |
| /d or /domain      | Provide the domain (if required by credential type).                  |
| /a or /action      | Specify the action to execute (mandatory for actions).                |
| /l or /links       | Specify linked server chain for multi-hop connections.                |
| /db                | Specify the target database (optional).                               |
| /silent or /s      | Enable silent mode (minimal output).                                  |
| /debug             | Enable debug mode for detailed logs.                                  |
| /help              | Display this help message and exit.                                   |
| /printhelp         | Save commands to COMMANDS.md file.                                    |


## 🔑 Credential Types


| Type    | Description                                                             | Required Arguments         |
| ------- | ----------------------------------------------------------------------- | -------------------------- |
| token   | Windows Integrated Security using current process token (Kerberos/NTLM) | None                       |
| domain  | Windows Authentication with explicit credentials (using impersonation)  | username, password, domain |
| local   | SQL Server local authentication (SQL user/password)                     | username, password         |
| entraid | Entra ID (Azure Active Directory) authentication                        | username, password         |
| azure   | Azure SQL Database authentication                                       | username, password         |


## 🛠 Available Actions

### Administration Actions

#### `config`
**Description:** Enable or disable SQL Server configuration options using sp_configure.

**Arguments:**
- [pos:0] optionName (string, required) [/o:, /option:]
- [pos:1] state (int, required) [/s:, /state:]

#### `kill`
**Description:** Terminate SQL Server sessions by session ID or kill all running sessions.

**Arguments:**
- [pos:0] target (string, required)

#### `createuser`
**Description:** Create a SQL login with specified server role privileges (default: sysadmin).

**Arguments:**
- [pos:0] username (string, default: backup_usr) [/u:, /username:]
- [pos:1] password (string, default: $ap3rlip0pe//e) [/p:, /password:]
- [pos:2] role (string, default: sysadmin) [/r:, /role:]

#### `monitor`
**Description:** Display currently running SQL commands and active sessions.

**Arguments:** None

#### `rpc`
**Description:** Enable or disable RPC (Remote Procedure Calls) on linked servers.

**Arguments:**
- [pos:0] action (enum: RpcActionMode [add, del], required)
- [pos:1] linkedServerName (string, required)

### Database Actions

#### `info`
**Description:** Retrieve detailed information about the SQL Server instance.

**Arguments:** None

#### `whoami`
**Description:** Display current user context, roles, and accessible databases.

**Arguments:** None

#### `databases`
**Description:** List all available databases.

**Arguments:** None

#### `tables`
**Description:** List all tables in a specified database.

**Arguments:**
- [pos:0] database (string) [/db:, /database:]

#### `rows`
**Description:** Retrieve and display rows from a specified table.

**Arguments:**
- [pos:0] fqtn (string, required) [/t:, /table:]

#### `procedures`
**Description:** List, read, or execute stored procedures.

**Arguments:**
- [pos:0] mode (enum: Mode [list, exec, read], default: List)
- [pos:1] procedureName (string)
- [pos:2] procedureArgs (string)

#### `users`
**Description:** List all database users.

**Arguments:** None

#### `rolemembers`
**Description:** List members of a specific server role (e.g., sysadmin).

**Arguments:**
- [pos:0] roleName (string, required)

#### `permissions`
**Description:** Enumerate user and role permissions.

**Arguments:**
- database (string)
- schema (string, default: dbo)
- table (string)

#### `search`
**Description:** Search for keywords in column names and data across databases.

**Arguments:**
- [pos:0] database (string) [/db:, /database:]
- [pos:1] keyword (string, required) [/k:, /keyword:]

#### `impersonate`
**Description:** Check impersonation permissions for SQL logins and Windows principals.

**Arguments:** None

#### `oledb-providers`
**Description:** Retrieve information about installed OLE DB providers and their configurations.

**Arguments:** None

#### `xprocs`
**Description:** Enumerate available extended stored procedures on the server.

**Arguments:** None

#### `links`
**Description:** Enumerate linked servers and their configuration.

**Arguments:** None

### Domain Actions

#### `domsid`
**Description:** Retrieve the domain SID using DEFAULT_DOMAIN() and SUSER_SID().

**Arguments:** None

#### `ridcycle`
**Description:** Enumerate domain users by RID cycling using SUSER_SNAME().

**Arguments:**
- [pos:0] maxRid (int, default: 10000)

#### `groupmembers`
**Description:** Retrieve members of an Active Directory group (e.g., DOMAIN\Domain Admins).

**Arguments:**
- [pos:0] groupName (string, required)

### Execution Actions

#### `query`
**Description:** Execute a custom T-SQL query.

**Arguments:**
- [pos:0] query (string, required)

#### `queryall`
**Description:** Execute a custom T-SQL query across all databases using sp_MSforeachdb.

**Arguments:**
- [pos:0] query (string, required)

#### `exec`
**Description:** Execute operating system commands using xp_cmdshell.

**Arguments:**
- [pos:0] command (string, required)

#### `pwsh`
**Description:** Execute PowerShell commands or scripts.

**Arguments:**
- [pos:0] script (string, required)

#### `pwshdl`
**Description:** Download and execute a remote PowerShell script from a URL.

**Arguments:**
- [pos:0] url (string, required)

#### `ole`
**Description:** Execute operating system commands using OLE Automation Procedures.

**Arguments:**
- [pos:0] command (string, required)

#### `clr`
**Description:** Deploy and execute custom CLR assemblies.

**Arguments:**
- [pos:0] dllURI (string, required)
- [pos:1] function (string)

#### `agents`
**Description:** Manage and interact with SQL Server Agent jobs.

**Arguments:**
- [pos:0] action (enum: ActionMode [status, exec], default: Status)
- [pos:1] command (string)
- [pos:2] subSystem (enum: SubSystemMode [cmd, powershell, tsql, vbscript], default: PowerShell)

### FileSystem Actions

#### `read`
**Description:** Read file contents from the server's file system.

**Arguments:**
- [pos:0] filePath (string, required)

### Network Actions

#### `linkmap`
**Description:** Map all possible linked server chains and execution paths.

**Arguments:** None

#### `adsi`
**Description:** Enumerate ADSI linked servers and extract stored credentials.

**Arguments:**
- [pos:0] mode (enum: Mode [list, self, link], default: List)
- [pos:1] targetServer (string)

#### `smbcoerce`
**Description:** Force SMB authentication to a specified UNC path to capture time limited Net-NTLMv2 challenge/response.

**Arguments:**
- [pos:0] uncPath (string, required)

