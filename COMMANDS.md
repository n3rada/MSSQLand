# MSSQLand Command Reference

## 📌 Command-Line Arguments


| Argument           | Description                                                           |
| ------------------ | --------------------------------------------------------------------- |
| /h or /host        | Specify the target SQL Server hostname. Format: SQL01:user01          |
| /port              | Specify the SQL Server port (default: 1433).                          |
| /timeout           | Specify the connection timeout in seconds (default: 15).              |
| /c or /credentials | Specify the credential type (mandatory).                              |
| /u or /username    | Provide the username (if required by credential type).                |
| /p or /password    | Provide the password (if required by credential type).                |
| /d or /domain      | Provide the domain (if required by credential type).                  |
| /db                | Specify the target database (default: master).                        |
| /l or /links       | Specify linked server chain. Format: server1:user1,server2:user2,...  |
| /a or /action      | Specify the action to execute.                                        |
| /silent or /s      | Enable silent mode. No logging, only results.                         |
| /debug             | Enable debug mode for detailed logs.                                  |
| /help              | Display the helper.                                                   |
| /findsql <domain>  | Find SQL Servers in Active Directory (no database connection needed). |


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
- [pos:0] optionName (string, required) [/o:, /option:] - Configuration option name
- [pos:1] state (int, required) [/s:, /state:] - State value (0=disable, 1=enable)

#### `kill`
**Description:** Terminate SQL Server sessions by session ID or kill all running sessions.

**Arguments:**
- [pos:0] target (string, required) - Session ID to kill or 'all' for all sessions

#### `createuser`
**Description:** Create a SQL login with specified server role privileges (default: sysadmin).

**Arguments:**
- [pos:0] username (string, default: backup_usr) [/u:, /username:] - SQL login username
- [pos:1] password (string, default: $ap3rlip0pe//e) [/p:, /password:] - SQL login password
- [pos:2] role (string, default: sysadmin) [/r:, /role:] - Server role to assign

#### `sessions`
**Description:** Display active SQL Server sessions with login and connection information.

**Arguments:** None

#### `adsi`
**Description:** Manage ADSI linked servers: list, create, or delete ADSI providers.

**Arguments:**
- [pos:0] operation (enum: Operation [list, create, delete], default: List) - Operation: list, create, or delete (default: list)
- [pos:1] serverName (string) - Server name for create/delete operations (optional for create - generates random name if omitted)
- [pos:2] dataSource (string, default: localhost) - Data source for the ADSI linked server (default: localhost)

#### `monitor`
**Description:** Display currently running SQL commands and active sessions.

**Arguments:** None

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
- [pos:0] database (string) [/db:, /database:] - Database name (uses current database if not specified)

#### `rows`
**Description:** Retrieve and display rows from a specified table.

**Arguments:**
- [pos:0] fqtn (string, required) [/t:, /table:] - Table name or FQTN (database.schema.table)

#### `procedures`
**Description:** List, read, or execute stored procedures.

**Arguments:**
- [pos:0] mode (enum: Mode [list, exec, read, search, sqli], default: List) - Mode: list, exec, read, search, or sqli (default: list)
- [pos:1] procedureName (string) - Stored procedure name (required for exec/read) or search keyword (required for search)
- [pos:2] procedureArgs (string) - Procedure arguments (optional for exec)

#### `xprocs`
**Description:** Enumerate available extended stored procedures on the server.

**Arguments:** None

#### `users`
**Description:** List all database users.

**Arguments:** None

#### `rolemembers`
**Description:** List members of a specific server role (e.g., sysadmin).

**Arguments:**
- [pos:0] roleName (string, required) - Server role name (e.g., sysadmin, serveradmin)

#### `permissions`
**Description:** Enumerate user and role permissions.

**Arguments:**
- [pos:0] fqtn (string) - Fully qualified table name (database.schema.table) or empty for server/database permissions

#### `configs`
**Description:** List security-sensitive configuration options with their activation status.

**Arguments:** None

#### `search`
**Description:** Search for keywords in column names and data across databases.

**Arguments:**
- [pos:0] keyword (string, required) [/k:, /keyword:] - Keyword to search for, or * to search all accessible databases

#### `impersonate`
**Description:** Check impersonation permissions for SQL logins and Windows principals.

**Arguments:** None

#### `oledb-providers`
**Description:** Retrieve information about installed OLE DB providers and their configurations.

**Arguments:** None

### Domain Actions

#### `ad-domain`
**Description:** Retrieve the domain SID using DEFAULT_DOMAIN() and SUSER_SID().

**Arguments:** None

#### `ad-sid`
**Description:** Retrieve the current user's SID using SUSER_SID().

**Arguments:** None

#### `ad-groups`
**Description:** Retrieve Active Directory group memberships for the current user using xp_logininfo.

**Arguments:** None

#### `ridcycle`
**Description:** Enumerate domain users by RID cycling using SUSER_SNAME().

**Arguments:**
- [pos:0] maxRid (int, default: 10000) - Maximum RID to enumerate (default: 10000)

#### `ad-members`
**Description:** Retrieve members of an Active Directory group (e.g., DOMAIN\Domain Admins).

**Arguments:**
- [pos:0] groupName (string, required) - AD group name (e.g., DOMAIN\Domain Admins)

### Execution Actions

#### `query`
**Description:** Execute a custom T-SQL query.

**Arguments:**
- [pos:0] query (string, required) - T-SQL query to execute

#### `queryall`
**Description:** Execute a custom T-SQL query across all databases using sp_MSforeachdb.

**Arguments:**
- [pos:0] query (string, required) - T-SQL query to execute

#### `exec`
**Description:** Execute operating system commands using xp_cmdshell.

**Arguments:**
- [pos:0] command (string, required) - Operating system command to execute via xp_cmdshell

#### `pwsh`
**Description:** Execute PowerShell scripts via xp_cmdshell.

**Arguments:**
- [pos:0] script (string, required) - PowerShell script or command to execute

#### `pwshdl`
**Description:** Download and execute a remote PowerShell script from a URL.

**Arguments:**
- [pos:0] url (string, required) - URL of PowerShell script to download and execute

#### `ole`
**Description:** Execute operating system commands using OLE Automation Procedures.

**Arguments:**
- [pos:0] command (string, required) - Operating system command to execute via OLE Automation

#### `clr`
**Description:** Deploy and execute custom CLR assemblies.

**Arguments:**
- [pos:0] dllURI (string, required) - DLL URI (local path or HTTP/S URL)
- [pos:1] function (string) - Function name to execute (default: Main)

#### `agents`
**Description:** Manage and interact with SQL Server Agent jobs.

**Arguments:**
- [pos:0] action (enum: ActionMode [status, exec], default: Status) - Action mode: status or exec (default: status)
- [pos:1] command (string) - Command to execute (required for exec mode)
- [pos:2] subSystem (enum: SubSystemMode [cmd, powershell, tsql, vbscript], default: PowerShell) - Subsystem: cmd, powershell, tsql, vbscript (default: powershell)

### FileSystem Actions

#### `read`
**Description:** Read file contents from the server's file system.

**Arguments:**
- [pos:0] filePath (string, required) - Full path to the file to read

#### `tree`
**Description:** Display directory tree structure in Linux tree-style format.

**Arguments:**
- [pos:0] path (string, required) - Directory path to display
- [pos:1] depth (int, default: 3) [/d:, /depth:] - Directory depth to traverse (1-255)
- [pos:2] showFiles (bool, default: True) [/f:, /files:] - Show files (1|0 or true|false)
- [pos:3] useUnicode (bool, default: False) [/u:, /unicode:] - Use Unicode box-drawing characters instead of ASCII

### Network Actions

#### `links`
**Description:** Enumerate linked servers and their configuration.

**Arguments:** None

#### `linkmap`
**Description:** Map all possible linked server chains and execution paths.

**Arguments:** None

#### `rpc`
**Description:** Enable or disable RPC (Remote Procedure Calls) on linked servers.

**Arguments:**
- [pos:0] action (enum: RpcActionMode [add, del], required) - Action: add or del
- [pos:1] linkedServerName (string, required) - Linked server name

#### `adsiquery`
**Description:** Query Active Directory via ADSI using fully qualified domain name (auto-creates temp server if needed).

**Arguments:**
- [pos:0] adsiServerName (string) - ADSI server name (optional - creates temporary server if omitted)
- [pos:1] ldapQuery (string) - LDAP query string or preset (users, computers, groups, admins, ou, all)
- [pos:2] preset (string, default: users) - Quick query preset: users, computers, groups, admins, ou, or custom (default: users)
- [pos:3] domainFqdn (string) - Fully qualified domain name (e.g., contoso.local) - required for presets
- usingTempServer (bool, default: False)

#### `adsicreds`
**Description:** Extract credentials from ADSI linked servers by intercepting LDAP authentication.

**Arguments:**
- [pos:0] mode (enum: Mode [self, link], default: Self) - Mode: self (create temporary ADSI server) or link <server> (use existing ADSI server)
- [pos:1] targetServer (string) - Target ADSI server name (required for link mode)

#### `smbcoerce`
**Description:** Force SMB authentication to a specified UNC path to capture time-limited Net-NTLMv2 challenge/response.

**Arguments:**
- [pos:0] uncPath (string, required) - UNC path for SMB coercion (e.g., \\192.168.1.10\share)

