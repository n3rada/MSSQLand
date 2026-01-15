// MSSQLand/Actions/ActionFactory.cs

using System;
using System.Collections.Generic;
using MSSQLand.Actions;
using MSSQLand.Actions.Remote;
using MSSQLand.Actions.Database;
using MSSQLand.Actions.FileSystem;
using MSSQLand.Actions.Execution;
using MSSQLand.Actions.Domain;
using MSSQLand.Actions.Administration;
#if ENABLE_CM
using MSSQLand.Actions.ConfigMgr;
#endif
using MSSQLand.Exceptions;

namespace MSSQLand.Utilities
{
    public static class ActionFactory
    {
        private static readonly Dictionary<string, (Type ActionClass, string Description, string[] Aliases)> ActionMetadata =
        new()
        {
            // ═══════════════════════════════════════════════════════════════════════════════
            // DATABASE ACTIONS - BASIC INFO & AUTHENTICATION
            // ═══════════════════════════════════════════════════════════════════════════════
            { "info", (typeof(Info), "Retrieve detailed information about the SQL Server instance.", null) },
            { "whoami", (typeof(Whoami), "Display current user context, roles, and accessible databases.", null) },
            { "authtoken", (typeof(AuthToken), "Display all groups from the Windows authentication token (AD, BUILTIN, NT AUTHORITY, etc.).", null) },

            // ═══════════════════════════════════════════════════════════════════════════════
            // DATABASE ACTIONS - ENUMERATION
            // ═══════════════════════════════════════════════════════════════════════════════
            { "databases", (typeof(Databases), "List all available databases.", null) },
            { "tables", (typeof(Tables), "List all tables in a specified database.", null) },
            { "rows", (typeof(Rows), "Retrieve and display rows from a specified table.", null) },
            { "procedures", (typeof(Procedures), "List, read, or execute stored procedures in the current database.", null) },
            { "xprocs", (typeof(ExtendedProcs), "Enumerate built-in extended (xp_*), OLE Automation (sp_OA*), and system procedures with execution permissions.", new[] { "extendedprocs", "sysprocs" }) },
            { "users", (typeof(Users), "List all database users.", null) },
            { "hashes", (typeof(Hashes), "Dump SQL Server login password hashes in hashcat format.", null) },
            { "roles", (typeof(Roles), "List all database roles and their members in the current database.", null) },
            { "rolemembers", (typeof(RoleMembers), "List members of a specific server role (e.g., sysadmin).", null) },
            { "permissions", (typeof(Permissions), "Enumerate user and role permissions.", null) },
            { "impersonate", (typeof(Impersonation), "Check impersonation permissions for SQL logins and Windows principals.", new[] { "impersonation" }) },
            { "trustworthy", (typeof(Trustworthy), "Detect and exploit privilege escalation via TRUSTWORTHY database setting (db_owner → sysadmin).", null) },
            { "oledb-providers", (typeof(OleDbProvidersInfo), "Retrieve information about installed OLE DB providers and their configurations.", null) },

            // ═══════════════════════════════════════════════════════════════════════════════
            // DATABASE ACTIONS - OPERATIONS
            // ═══════════════════════════════════════════════════════════════════════════════
            { "search", (typeof(Search), "Search for keywords in column names and data across databases.", new[] { "find" }) },
            { "query", (typeof(Query), "Execute a custom T-SQL query.", null) },
            { "queryall", (typeof(QueryAll), "Execute a custom T-SQL query across all databases using sp_MSforeachdb.", null) },
            { "monitor", (typeof(Monitor), "Display currently running SQL commands and active sessions.", null) },

            // ═══════════════════════════════════════════════════════════════════════════════
            // ADMINISTRATION ACTIONS
            // ═══════════════════════════════════════════════════════════════════════════════
            { "config", (typeof(Config), "List security-sensitive configuration options or set their values using sp_configure.", new[] { "settings" }) },
            { "user-add", (typeof(UserAdd), "Create a SQL login with specified server role privileges (default: sysadmin).", null) },
            { "sessions", (typeof(Sessions), "Display active SQL Server sessions with login and connection information.", null) },
            { "kill", (typeof(Kill), "Terminate SQL Server sessions by session ID or kill all running sessions.", null) },

            // ═══════════════════════════════════════════════════════════════════════════════
            // DOMAIN ACTIONS
            // ═══════════════════════════════════════════════════════════════════════════════
            { "ad-domain", (typeof(AdDomain), "Retrieve the domain SID using DEFAULT_DOMAIN() and SUSER_SID().", null) },
            { "ad-sid", (typeof(AdSid), "Retrieve the current user's SID using SUSER_SID().", null) },
            { "ad-groups", (typeof(AdGroups), "Retrieve Active Directory domain groups with SQL Server principals that the user is a member of.", null) },
            { "ad-members", (typeof(AdMembers), "Retrieve members of an Active Directory group.", null) },
            { "ridcycle", (typeof(RidCycle), "Enumerate domain users by RID cycling using SUSER_SNAME().", null) },
            
            // ═══════════════════════════════════════════════════════════════════════════════
            // EXECUTION ACTIONS
            // ═══════════════════════════════════════════════════════════════════════════════
            { "exec", (typeof(XpCmd), "Execute operating system commands via xp_cmdshell.", new[] { "xpcmd", "xpcmdshell", "xp_cmdshell" }) },
            { "pwsh", (typeof(PowerShell), "Execute PowerShell scripts.", null) },
            { "pwshdl", (typeof(RemotePowerShellExecutor), "Download and execute a remote PowerShell script from a URL.", null) },
            { "ole", (typeof(ObjectLinkingEmbedding), "Execute operating system commands via procedures.", null) },
            { "clr", (typeof(ClrExecution), "Deploy and execute custom CLR assemblies.", null) },
            { "agents", (typeof(Agents), "Manage and interact with SQL Server Agent jobs.", null) },
            { "run", (typeof(Run), "Execute a remote file on the SQL Server.", null) },

            // ═══════════════════════════════════════════════════════════════════════════════
            // FILESYSTEM ACTIONS
            // ═══════════════════════════════════════════════════════════════════════════════
            { "read", (typeof(FileRead), "Read file contents from the server's file system.", null) },
            { "tree", (typeof(Tree), "Display directory tree structure in Linux tree-style format.", null) },
            { "upload", (typeof(Upload), "Upload a local file to the SQL Server filesystem.", null) },

            // ═══════════════════════════════════════════════════════════════════════════════
            // REMOTE DATA ACCESS ACTIONS
            // ═══════════════════════════════════════════════════════════════════════════════
            { "links", (typeof(Links), "Enumerate linked servers and their configuration.", null) },
            { "linkmap", (typeof(LinkMap), "Map all possible linked server chains and execution paths.", null) },
            { "rpc", (typeof(RemoteProcedureCall), "Enable or disable RPC (Remote Procedure Calls) on linked servers.", null) },
            { "data", (typeof(DataAccess), "Enable or disable data access (OPENQUERY) on linked servers.", null) },
            { "ext-sources", (typeof(ExternalSources), "Enumerate External Data Sources (Azure SQL Database, Synapse, PolyBase).", null) },
            { "ext-creds", (typeof(ExternalCredentials), "Enumerate database-scoped credentials used by External Data Sources.", null) },
            { "ext-tables", (typeof(ExternalTables), "Enumerate external tables and their remote data locations.", null) },
            { "adsi-manager", (typeof(AdsiManager), "Manage ADSI linked servers: list, create, or delete ADSI providers.", null) },
            { "adsi-query", (typeof(AdsiQuery), "Query Active Directory via ADSI using fully qualified domain name (auto-creates temp server if needed).", null) },
            { "adsi-creds", (typeof(AdsiCredentialExtractor), "Extract SQL login passwords via LDAP simple bind interception (useful through linked server chains or for ADSI servers with mapped credentials).", null) },
            { "smbcoerce", (typeof(SmbCoerce), "Force SMB authentication to a specified UNC path to capture time-limited Net-NTLMv2 challenge/response.", null) },

#if ENABLE_CM
            // ═══════════════════════════════════════════════════════════════════════════════
            // CONFIGURATION MANAGER ACTIONS (Microsoft Configuration Manager / ConfigMgr)
            // Uses "cm-" prefix to align with Microsoft's official PowerShell cmdlet naming (Get-CM*, Set-CM*, etc.)
            // Formerly known as: System Center Configuration Manager (SCCM), MECM, SMS
            // Microsoft's current official names: Configuration Manager, ConfigMgr, MCM
            // ═══════════════════════════════════════════════════════════════════════════════
            { "cm-info", (typeof(CMInfo), "Display ConfigMgr site information (site code, version, build, database server, management points) for infrastructure mapping.", null) },
            { "cm-admins", (typeof(CMAdmins), "Enumerate ConfigMgr RBAC administrators with assigned roles and scopes to identify privileged users.", null) },
            { "cm-servers", (typeof(CMServers), "Enumerate ConfigMgr site servers, management points, and distribution points in the hierarchy for infrastructure mapping.", null) },
            { "cm-collections", (typeof(CMCollections), "Enumerate device and user collections with member counts for targeted deployment attacks.", null) },
            { "cm-collection", (typeof(CMCollection), "Display comprehensive information about a specific collection including all member devices and deployments.", null) },
            { "cm-devices", (typeof(CMDevices), "Enumerate managed devices with filtering by attributes for device discovery and inventory queries.", null) },
            { "cm-device", (typeof(CMDevice), "Display comprehensive information about a specific device including all deployments and targeted content.", null) },
            { "cm-health", (typeof(CMHealth), "Display client health diagnostics and communication status for troubleshooting client issues.", null) },
            { "cm-deployments", (typeof(CMDeployments), "Enumerate active deployments showing what content is pushed to which collections for hijacking.", new[] { "cm-assignments" }) },
            { "cm-deployment", (typeof(CMDeployment), "Display detailed information about a specific deployment including rerun behavior and device status.", new[] { "cm-assignment" }) },
            { "cm-trace", (typeof(CMLogTrace), "Trace a deployment type GUID from client logs back to assignments and collections.", new[] { "cm-find-assignments", "cm-log-trace" }) },
            { "cm-deploymenttype", (typeof(CMDeploymentType), "Display detailed technical information about a deployment type (detection method, install commands, requirements, XML).", new[] { "cm-dt" }) },
            { "cm-deploymenttypes", (typeof(CMDeploymentTypes), "Display an overview of all deployment types ordered by modification/creation date.", new[] { "cm-dts" }) },
            { "cm-packages", (typeof(CMPackages), "Enumerate ConfigMgr packages with source paths, versions, and program counts.", null) },
            { "cm-package", (typeof(CMPackage), "Display comprehensive information about a specific package including programs and deployments.", null) },
            { "cm-programs", (typeof(CMPrograms), "Enumerate programs for legacy packages with command lines and decoded execution flags.", null) },
            { "cm-tasksequences", (typeof(CMTaskSequences), "Enumerate all task sequences with summary information.", new[] { "cm-ts" }) },
            { "cm-tasksequence", (typeof(CMTaskSequence), "Display detailed information for a specific task sequence including all referenced content.", null) },
            { "cm-applications", (typeof(CMApplications), "Enumerate applications with deployment types, install commands, and content locations for modification.", new[] { "cm-apps" }) },
            { "cm-distribution-points", (typeof(CMDistributionPoints), "Enumerate distribution points with content library paths for lateral movement and content poisoning.", new[] { "cm-dps" }) },
            { "cm-accounts", (typeof(CMAccounts), "Enumerate encrypted credentials (NAA, Client Push, Task Sequence) for decryption on site server.", null) },
            { "cm-aad-apps", (typeof(CMAadApps), "Enumerate Azure AD app registrations with encrypted secrets for cloud infrastructure access.", new[] { "cm-aad" }) },
            { "cm-scripts", (typeof(CMScripts), "Enumerate PowerShell scripts with metadata overview (excludes script content).", null) },
            { "cm-script", (typeof(CMScript), "Display detailed information for a specific script including full content and parameters.", null) },
            { "cm-script-add", (typeof(CMScriptAdd), "Upload PowerShell script to ConfigMgr bypassing approval workflow (auto-approved, hidden from console).", null) },
            { "cm-script-delete", (typeof(CMScriptDelete), "Remove script from ConfigMgr by GUID to clean up operational artifacts.", null) },
            { "cm-script-run", (typeof(CMScriptRun), "Execute PowerShell script on target device via BGB notification channel (requires ResourceID and script GUID).", null) },
            { "cm-script-status", (typeof(CMScriptStatus), "Monitor script execution status and retrieve output from target devices by Task ID.", null) }
#endif
        };

        // Lazy-initialized alias lookup dictionary
        private static Dictionary<string, string> _aliasLookup;

        private static Dictionary<string, string> AliasLookup
        {
            get
            {
                if (_aliasLookup == null)
                {
                    _aliasLookup = new Dictionary<string, string>();
                    foreach (var action in ActionMetadata)
                    {
                        if (action.Value.Aliases != null)
                        {
                            foreach (var alias in action.Value.Aliases)
                            {
                                _aliasLookup[alias.ToLower()] = action.Key;
                            }
                        }
                    }
                }
                return _aliasLookup;
            }
        }

        public static BaseAction GetAction(string actionType, string[] actionArguments)
        {
            string actionName = actionType.ToLower();
            
            // Resolve alias if it exists
            if (AliasLookup.TryGetValue(actionName, out string canonicalName))
            {
                actionName = canonicalName;
            }

            if (!ActionMetadata.TryGetValue(actionName, out var metadata))
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
            string name = actionName.ToLower();
            
            // Resolve alias if it exists
            if (AliasLookup.TryGetValue(name, out string canonicalName))
            {
                name = canonicalName;
            }

            if (ActionMetadata.TryGetValue(name, out var metadata))
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

            // Check main actions
            foreach (var action in ActionMetadata)
            {
                if (action.Key.StartsWith(lowerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add((action.Key, action.Value.Description));
                }
            }

            // Check aliases
            foreach (var alias in AliasLookup)
            {
                if (alias.Key.StartsWith(lowerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (ActionMetadata.TryGetValue(alias.Value, out var metadata))
                    {
                        matches.Add((alias.Key, $"{metadata.Description} (alias for {alias.Value})"));
                    }
                }
            }

            return matches;
        }
    }
}
