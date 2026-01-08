using System;
using System.Collections.Generic;
using MSSQLand.Actions;
using MSSQLand.Actions.Remote;
using MSSQLand.Actions.Database;
using MSSQLand.Actions.FileSystem;
using MSSQLand.Actions.Execution;
using MSSQLand.Actions.Domain;
using MSSQLand.Actions.Administration;
using MSSQLand.Actions.CM;
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

#if ENABLE_CM
            // ═══════════════════════════════════════════════════════════════════════════════
            // CONFIGURATION MANAGER ACTIONS (Microsoft Configuration Manager / ConfigMgr)
            // Uses "cm-" prefix to align with Microsoft's official PowerShell cmdlet naming (Get-CM*, Set-CM*, etc.)
            // Formerly known as: System Center Configuration Manager (SCCM), MECM, SMS
            // Microsoft's current official names: Configuration Manager, ConfigMgr, MCM
            // ═══════════════════════════════════════════════════════════════════════════════
            { "cm-info", (typeof(CMInfo), "Display ConfigMgr site information (site code, version, build, database server, management points) for infrastructure mapping.") },
            { "cm-admins", (typeof(CMAdmins), "Enumerate ConfigMgr RBAC administrators with assigned roles and scopes to identify privileged users.") },
            { "cm-servers", (typeof(CMServers), "Enumerate ConfigMgr site servers, management points, and distribution points in the hierarchy for infrastructure mapping.") },
            { "cm-collections", (typeof(CMCollections), "Enumerate device and user collections with member counts for targeted deployment attacks.") },
            { "cm-collection", (typeof(CMCollection), "Display comprehensive information about a specific collection including all member devices and deployments.") },
            { "cm-devices", (typeof(CMDevices), "Enumerate managed devices with filtering by attributes for device discovery and inventory queries.") },
            { "cm-device", (typeof(CMDevice), "Display comprehensive information about a specific device including all deployments and targeted content.") },
            { "cm-device-users", (typeof(CMDeviceUsers), "Show historical user login patterns on devices with usage statistics from hardware inventory.") },
            { "cm-health", (typeof(CMHealth), "Display client health diagnostics and communication status for troubleshooting client issues.") },
            { "cm-deployments", (typeof(CMDeployments), "Enumerate active deployments showing what content is pushed to which collections for hijacking.") },
            { "cm-deployment", (typeof(CMDeployment), "Display detailed information about a specific deployment including rerun behavior and device status.") },
            { "cm-packages", (typeof(CMPackages), "Enumerate ConfigMgr packages with source paths, versions, and program counts.") },
            { "cm-package", (typeof(CMPackage), "Display comprehensive information about a specific package including programs and deployments.") },
            { "cm-programs", (typeof(CMPrograms), "Enumerate programs for legacy packages with command lines and decoded execution flags.") },
            { "cm-tasksequences", (typeof(CMTaskSequences), "Enumerate all task sequences with summary information.") },
            { "cm-tasksequence", (typeof(CMTaskSequence), "Display detailed information for a specific task sequence including all referenced content.") },
            { "cm-apps", (typeof(CMApplications), "Enumerate applications with deployment types, install commands, and content locations for modification.") },
            { "cm-dp", (typeof(CMDistributionPoints), "Enumerate distribution points with content library paths for lateral movement and content poisoning.") },
            { "cm-accounts", (typeof(CMAccounts), "Enumerate encrypted credentials (NAA, Client Push, Task Sequence) for decryption on site server.") },
            { "cm-aad-apps", (typeof(CMAadApps), "Enumerate Azure AD app registrations with encrypted secrets for cloud infrastructure access.") },
            { "cm-scripts", (typeof(CMScripts), "Enumerate PowerShell scripts with metadata overview (excludes script content).") },
            { "cm-script", (typeof(CMScript), "Display detailed information for a specific script including full content and parameters.") },
            { "cm-script-add", (typeof(CMScriptAdd), "Upload PowerShell script to ConfigMgr bypassing approval workflow (auto-approved, hidden from console).") },
            { "cm-script-delete", (typeof(CMScriptDelete), "Remove script from ConfigMgr by GUID to clean up operational artifacts.") },
            { "cm-script-run", (typeof(CMScriptRun), "Execute PowerShell script on target device via BGB notification channel (requires ResourceID and script GUID).") },
            { "cm-script-status", (typeof(CMScriptStatus), "Monitor script execution status and retrieve output from target devices by Task ID.") }
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
