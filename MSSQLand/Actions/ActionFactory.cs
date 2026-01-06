using System;
using System.Collections.Generic;
using MSSQLand.Actions;
using MSSQLand.Actions.Remote;
using MSSQLand.Actions.Database;
using MSSQLand.Actions.FileSystem;
using MSSQLand.Actions.Execution;
using MSSQLand.Actions.Domain;
using MSSQLand.Actions.Administration;
using MSSQLand.Actions.SCCM;
using MSSQLand.Exceptions;

namespace MSSQLand.Utilities
{
    public static class ActionFactory
    {
        private static readonly Dictionary<string, (Type ActionClass, string Description)> ActionMetadata =
        new()
        {
            // ═══════════════════════════════════════════════════════════════════════════════
            // DATABASE ACTIONS - BASIC INFO & AUTHENTICATION
            // ═══════════════════════════════════════════════════════════════════════════════
            { "info", (typeof(Info), "Retrieve detailed information about the SQL Server instance.") },
            { "whoami", (typeof(Whoami), "Display current user context, roles, and accessible databases.") },
            { "authtoken", (typeof(AuthToken), "Display all groups from the Windows authentication token (AD, BUILTIN, NT AUTHORITY, etc.).") },

            // ═══════════════════════════════════════════════════════════════════════════════
            // DATABASE ACTIONS - ENUMERATION
            // ═══════════════════════════════════════════════════════════════════════════════
            { "databases", (typeof(Databases), "List all available databases.") },
            { "tables", (typeof(Tables), "List all tables in a specified database.") },
            { "rows", (typeof(Rows), "Retrieve and display rows from a specified table.") },
            { "procedures", (typeof(Procedures), "List, read, or execute stored procedures.") },
            { "xprocs", (typeof(ExtendedProcs), "Enumerate available extended stored procedures on the server.") },
            { "users", (typeof(Users), "List all database users.") },
            { "hashes", (typeof(Hashes), "Dump SQL Server login password hashes in hashcat format.") },
            { "loginmap", (typeof(LoginMap), "Map server logins to database users across all accessible databases.") },
            { "roles", (typeof(Roles), "List all database roles and their members in the current database.") },
            { "rolemembers", (typeof(RoleMembers), "List members of a specific server role (e.g., sysadmin).") },
            { "permissions", (typeof(Permissions), "Enumerate user and role permissions.") },
            { "impersonate", (typeof(Impersonation), "Check impersonation permissions for SQL logins and Windows principals.") },
            { "trustworthy", (typeof(Trustworthy), "Detect and exploit privilege escalation via TRUSTWORTHY database setting (db_owner → sysadmin).") },
            { "oledb-providers", (typeof(OleDbProvidersInfo), "Retrieve information about installed OLE DB providers and their configurations.") },

            // ═══════════════════════════════════════════════════════════════════════════════
            // DATABASE ACTIONS - OPERATIONS
            // ═══════════════════════════════════════════════════════════════════════════════
            { "search", (typeof(Search), "Search for keywords in column names and data across databases.") },
            { "query", (typeof(Query), "Execute a custom T-SQL query.") },
            { "queryall", (typeof(QueryAll), "Execute a custom T-SQL query across all databases using sp_MSforeachdb.") },
            { "monitor", (typeof(Monitor), "Display currently running SQL commands and active sessions.") },

            // ═══════════════════════════════════════════════════════════════════════════════
            // ADMINISTRATION ACTIONS
            // ═══════════════════════════════════════════════════════════════════════════════
            { "config", (typeof(Config), "List security-sensitive configuration options or set their values using sp_configure.") },
            { "user-add", (typeof(UserAdd), "Create a SQL login with specified server role privileges (default: sysadmin).") },
            { "sessions", (typeof(Sessions), "Display active SQL Server sessions with login and connection information.") },
            { "kill", (typeof(Kill), "Terminate SQL Server sessions by session ID or kill all running sessions.") },

            // ═══════════════════════════════════════════════════════════════════════════════
            // DOMAIN ACTIONS
            // ═══════════════════════════════════════════════════════════════════════════════
            { "ad-domain", (typeof(AdDomain), "Retrieve the domain SID using DEFAULT_DOMAIN() and SUSER_SID().") },
            { "ad-sid", (typeof(AdSid), "Retrieve the current user's SID using SUSER_SID().") },
            { "ad-groups", (typeof(AdGroups), "Retrieve Active Directory domain groups with SQL Server principals that the user is a member of.") },
            { "ad-members", (typeof(AdMembers), "Retrieve members of an Active Directory group.") },
            { "ridcycle", (typeof(RidCycle), "Enumerate domain users by RID cycling using SUSER_SNAME().") },
            
            // ═══════════════════════════════════════════════════════════════════════════════
            // EXECUTION ACTIONS
            // ═══════════════════════════════════════════════════════════════════════════════
            { "exec", (typeof(XpCmd), "Execute operating system commands.") },
            { "pwsh", (typeof(PowerShell), "Execute PowerShell scripts.") },
            { "pwshdl", (typeof(RemotePowerShellExecutor), "Download and execute a remote PowerShell script from a URL.") },
            { "ole", (typeof(ObjectLinkingEmbedding), "Execute operating system commands via procedures.") },
            { "clr", (typeof(ClrExecution), "Deploy and execute custom CLR assemblies.") },
            { "agents", (typeof(Agents), "Manage and interact with SQL Server Agent jobs.") },
            { "run", (typeof(Run), "Execute a remote file on the SQL Server.") },

            // ═══════════════════════════════════════════════════════════════════════════════
            // FILESYSTEM ACTIONS
            // ═══════════════════════════════════════════════════════════════════════════════
            { "read", (typeof(FileRead), "Read file contents from the server's file system.") },
            { "tree", (typeof(Tree), "Display directory tree structure in Linux tree-style format.") },
            { "upload", (typeof(Upload), "Upload a local file to the SQL Server filesystem.") },

            // ═══════════════════════════════════════════════════════════════════════════════
            // REMOTE DATA ACCESS ACTIONS
            // ═══════════════════════════════════════════════════════════════════════════════
            { "links", (typeof(Links), "Enumerate linked servers and their configuration.") },
            { "linkmap", (typeof(LinkMap), "Map all possible linked server chains and execution paths.") },
            { "rpc", (typeof(RemoteProcedureCall), "Enable or disable RPC (Remote Procedure Calls) on linked servers.") },
            { "ext-sources", (typeof(ExternalSources), "Enumerate External Data Sources (Azure SQL Database, Synapse, PolyBase).") },
            { "ext-creds", (typeof(ExternalCredentials), "Enumerate database-scoped credentials used by External Data Sources.") },
            { "ext-tables", (typeof(ExternalTables), "Enumerate external tables and their remote data locations.") },
            { "adsi-manager", (typeof(AdsiManager), "Manage ADSI linked servers: list, create, or delete ADSI providers.") },
            { "adsi-query", (typeof(AdsiQuery), "Query Active Directory via ADSI using fully qualified domain name (auto-creates temp server if needed).") },
            { "adsi-creds", (typeof(AdsiCredentialExtractor), "Extract credentials from ADSI linked servers by intercepting LDAP authentication.") },
            { "smbcoerce", (typeof(SmbCoerce), "Force SMB authentication to a specified UNC path to capture time-limited Net-NTLMv2 challenge/response.") },

#if ENABLE_SCCM
            // ═══════════════════════════════════════════════════════════════════════════════
            // SCCM ACTIONS
            // ═══════════════════════════════════════════════════════════════════════════════
            { "sccm-info", (typeof(SccmInfo), "Retrieve SCCM site information including site code, version, components, and distribution points.") },
            { "sccm-admins", (typeof(SccmAdmins), "Enumerate SCCM RBAC administrators.") },
            { "sccm-servers", (typeof(SccmServers), "List servers in the hierarchy with associated database server and site code.") },
            { "sccm-collections", (typeof(SccmCollections), "List SCCM collections with properties and member counts.") },
            { "sccm-devices", (typeof(SccmDevices), "List known devices with ResourceID, name, online status, collections, and last activity.") },
            { "sccm-health", (typeof(SccmHealth), "Display client health status, check-in times, and inventory scan schedules.") },
            { "sccm-deployments", (typeof(SccmDeployments), "Enumerate SCCM deployments with target collections and deployment settings.") },
            { "sccm-packages", (typeof(SccmPackages), "List SCCM packages with source paths and program details.") },
            { "sccm-apps", (typeof(SccmApplications), "Enumerate SCCM applications with installation commands and deployment types.") },
            { "sccm-dp", (typeof(SccmDistributionPoints), "List distribution points with content shares and properties.") },
            { "sccm-accounts", (typeof(SccmAccounts), "Enumerate SCCM stored credentials (NAA, Client Push, Task Sequence accounts).") },
            { "sccm-aad-apps", (typeof(SccmAadApps), "List Azure AD application configurations with encrypted secrets.") },
            { "sccm-scripts", (typeof(SccmScripts), "Enumerate SCCM scripts.") },
            { "sccm-script-add", (typeof(SccmScriptAdd), "Add a PowerShell script to SCCM (auto-approved, hidden from console).") },
            { "sccm-script-delete", (typeof(SccmScriptDelete), "Delete an SCCM script by GUID.") },
            { "sccm-script-run", (typeof(SccmScriptRun), "Execute an SCCM script on a target device via BGB notification channel.") },
            { "sccm-script-status", (typeof(SccmScriptStatus), "Check the execution status of an SCCM script task.") }
#endif
        };

        public static BaseAction GetAction(string actionType, string[] actionArguments)
        {
            if (!ActionMetadata.TryGetValue(actionType.ToLower(), out var metadata))
            {
                throw new ActionNotFoundException(actionType);
            }

            // Create an instance of the action class
            BaseAction action = (BaseAction)Activator.CreateInstance(metadata.ActionClass);

            // Validate and initialize the action with the action arguments
            action.ValidateArguments(actionArguments);
            return action;
        }

        public static List<(string ActionName, string Description, List<string> Arguments, string Category)> GetAvailableActions()
        {
            var result = new List<(string ActionName, string Description, List<string> Arguments, string Category)>();

            foreach (var action in ActionMetadata)
            {
                BaseAction actionInstance = (BaseAction)Activator.CreateInstance(action.Value.ActionClass);
                List<string> arguments = actionInstance.GetArguments();
                
                // Extract category from namespace (last part after the last dot)
                string fullNamespace = action.Value.ActionClass.Namespace;
                string category = fullNamespace.Substring(fullNamespace.LastIndexOf('.') + 1);
                
                result.Add((action.Key, action.Value.Description, arguments, category));
            }

            return result;
        }

        /// <summary>
        /// Gets the Type of an action by its name.
        /// </summary>
        /// <param name="actionName">The name of the action.</param>
        /// <returns>The Type of the action class, or null if not found.</returns>
        public static Type GetActionType(string actionName)
        {
            if (ActionMetadata.TryGetValue(actionName.ToLower(), out var metadata))
            {
                return metadata.ActionClass;
            }
            return null;
        }

        /// <summary>
        /// Gets all actions that start with the given prefix.
        /// </summary>
        /// <param name="prefix">The prefix to search for.</param>
        /// <returns>List of matching action names and descriptions.</returns>
        public static List<(string ActionName, string Description)> GetActionsByPrefix(string prefix)
        {
            var matches = new List<(string ActionName, string Description)>();
            string lowerPrefix = prefix.ToLower();

            foreach (var action in ActionMetadata)
            {
                if (action.Key.StartsWith(lowerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add((action.Key, action.Value.Description));
                }
            }

            return matches;
        }
    }
}
