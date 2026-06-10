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
using MSSQLand.Actions.Agent;
#if CONFIGMGR
using MSSQLand.Actions.ConfigMgr;
#endif
using MSSQLand.Exceptions;

namespace MSSQLand.Actions
{
    public static class ActionFactory
    {
        private static readonly Dictionary<string, (Type ActionClass, string Description, string[] Aliases)> ActionMetadata =
        new()
        {
            // DATABASE ACTIONS - BASIC INFO & AUTHENTICATION
            { "info", (typeof(Info), "Enumerate SQL Server instance properties: server name, version, edition, authentication mode, service account, data/log paths, OS details, and Azure service tier when applicable.", null) },
            { "whoami", (typeof(Whoami), "Display current user context, roles, and accessible databases.", new[] { "id", "groups" }) },
            { "authtoken", (typeof(AuthToken), "Display all groups from the Windows authentication token (AD, BUILTIN, NT AUTHORITY, etc.).", null) },

            // DATABASE ACTIONS - ENUMERATION
            { "databases", (typeof(Databases), "List all databases with accessibility, owner, TRUSTWORTHY flag, state, and file paths.", null) },
            { "tables", (typeof(Tables), "List all tables in a specified database.", null) },
            { "rows", (typeof(Rows), "Retrieve and display rows from a specified table.", null) },
            { "procedures", (typeof(Procedures), "List, read, or execute stored procedures in the current database.", new[] { "procs", "sprocs" }) },
            { "xprocs", (typeof(ExtendedProcs), "Enumerate built-in extended (xp_*), OLE Automation (sp_OA*), and system procedures with execution permissions.", new[] { "extendedprocs", "sysprocs" }) },
            { "users", (typeof(Users), "Enumerate server-level principals (logins) with their server roles, and database users in the current database context.", null) },
            { "hashes", (typeof(Hashes), "Dump SQL Server login password hashes in hashcat format.", new[] { "passwords" }) },
            { "roles", (typeof(Roles), "List all database roles and their members in the current database.", null) },
            { "rolemembers", (typeof(RoleMembers), "List members of a specific server role (e.g., sysadmin).", null) },
            { "permissions", (typeof(Permissions), "Enumerate current user's permissions via fn_my_permissions. No argument shows server-level and database-level permissions plus accessible databases. Pass schema.table or database.schema.table to check object-level permissions.", null) },
            { "impersonate", (typeof(Impersonation), "Enumerate SQL logins and Windows principals with their impersonation status from the current context. If the current user is sysadmin, all principals are listed as implicitly impersonatable. Use impersonate-chains to discover logins reachable through multi-hop EXECUTE AS paths.", new[] { "impersonation", "imp" }) },
            { "impersonate-chains", (typeof(ImpersonationMap), "Map multi-hop EXECUTE AS impersonation chains reachable from the current login. Records system accounts as endpoints without recursing. No-op if the current user is already sysadmin. Output lists each chain with starting login, intermediate hops, and end login.", new[] { "impchains", "impmap" }) },
            { "oledb", (typeof(OleDbProvidersInfo), "Enumerate OLE DB providers and their registry configuration.", new[] { "ole-providers" }) },

            // DATABASE ACTIONS - OPERATIONS
            { "search", (typeof(Search), "Search for keywords in column names and data across databases.", new[] { "find" }) },
            { "query", (typeof(Query), "Execute a custom T-SQL query. Use --all to execute across all accessible databases.", new[] { "sql" }) },
            { "requests", (typeof(Requests), "Display currently executing SQL requests with query text and wait information.", null) },

            // ADMINISTRATION ACTIONS
            { "config", (typeof(Config), "List security-sensitive configuration options or set their values using sp_configure.", new[] { "settings" }) },
            { "audit", (typeof(Audit), "Enumerate SQL Server audit objects, event groups, log destinations, and ON_FAILURE behavior.", new[] { "audits", "audit-status" }) },
            { "user-add", (typeof(UserAdd), "Create a SQL login with specified server role privileges (default: sysadmin).", null) },
            { "sessions", (typeof(Sessions), "Display active SQL Server sessions with login and connection information.", new[] { "who" }) },
            { "kill", (typeof(Kill), "Terminate SQL Server sessions by session ID or kill all running sessions.", null) },

            // DOMAIN ACTIONS
            { "ad-domain", (typeof(AdDomain), "Resolve the AD domain name and SID the SQL Server is joined to, using the Domain Admins group as the pivot principal.", null) },
            { "ad-sid", (typeof(AdSid), "Resolve the current login's Security Identifier (SID) with domain SID prefix and RID breakdown for AD accounts.", null) },
            { "ad-users", (typeof(AdUsers), "Enumerate domain accounts by iterating RIDs and resolving each to a login name. Accepts a max RID limit and output format: default (plain list), table, bash, or python.", new[] { "rid-brute" }) },
            { "adsi-add", (typeof(AdsiAdd), "Create an ADSI linked server (auto-generates name if omitted).", null) },
            { "adsi-del", (typeof(AdsiDel), "Delete an ADSI linked server by name.", new[] { "adsi-delete", "adsi-drop" }) },
            { "adsi-query", (typeof(AdsiQuery), "Execute LDAP queries against Active Directory via an ADSI linked server using OPENQUERY.", new[] { "ldap" }) },
            { "adsi-creds", (typeof(AdsiCredentialExtractor), "Extract SQL login passwords via LDAP simple bind interception using a local CLR listener. Requires CONTROL SERVER or sysadmin.", null) },
            { "adsi-redirect", (typeof(AdsiRedirect), "Redirect an ADSI linked server LDAP query to an attacker-controlled listener to capture cleartext credentials. No privileges required.", null) },

            // EXECUTION ACTIONS
            { "exec", (typeof(XpCmd), "Execute OS commands on the SQL Server host via the command shell extended procedure and return output.", null) },
            { "ole", (typeof(Ole), "Execute OS commands via OLE Automation (fire-and-forget, no output).", new[] { "oamethod" }) },
            { "powershell", (typeof(PowerShell), "Execute PowerShell scripts or commands on the SQL Server host. The script is base64-encoded and invoked non-interactively. Returns command output.", new[] { "pwsh" }) },
            { "clr", (typeof(ClrExecution), "Deploy and execute custom CLR assemblies.", null) },
            { "clr-list", (typeof(ClrList), "Enumerate user-defined CLR assemblies in the current database.", new[] { "assemblies" }) },
            { "clr-inspect", (typeof(ClrInspect), "Show exported procedures and metadata for a named CLR assembly.", new[] { "assembly" }) },
            { "run", (typeof(Run), "Execute a file on the SQL Server filesystem using OLE Automation.", null) },

            // SQL SERVER AGENT ACTIONS (sysjobs / sysjobsteps / sysjobhistory / sysproxies)
            { "jobs", (typeof(Agent.Jobs), "Enumerate SQL Server Agent jobs with steps, commands, owner, and category.", new[] { "agents" }) },
            { "job", (typeof(Agent.Job), "Display detailed information about a specific Agent job including all steps, schedule, and history.", null) },
            { "job-history", (typeof(Agent.JobHistory), "Display SQL Server Agent job execution history with status and output messages.", null) },
            { "job-proxies", (typeof(Agent.JobProxies), "Enumerate Agent proxy accounts, mapped credentials, logins, and allowed subsystems.", null) },
            { "job-exec", (typeof(Agent.JobExec), "Dispatch OS commands asynchronously via SQL Server Agent (CmdExec, PowerShell, TSQL, VBScript). Returns immediately after queuing. Poll output with job-history.", null) },

            // FILESYSTEM ACTIONS
            { "read", (typeof(FileRead), "Read file contents from the server's file system.", new[] { "cat" }) },
            { "tree", (typeof(Tree), "Display directory tree structure in Linux tree-style format.", null) },
            { "upload", (typeof(Upload), "Upload a local file to the SQL Server filesystem.", null) },
            { "rm", (typeof(RemoveFile), "Delete a file on the SQL Server filesystem.", new[] { "del", "delete" }) },

            // REMOTE DATA ACCESS ACTIONS
            { "links", (typeof(Links), "Enumerate linked servers and their login mappings: whether the caller is forwarded as-is (pass-through), substituted with a fixed remote credential (mapped), or blocked (denied). Also shows RPC out and OPENQUERY flags. Only returns entries visible to the current login.", new[] { "linkedservers" }) },
            { "linkmap", (typeof(LinkMap), "Recursively map linked server chains with loop detection, checking impersonation paths at each hop. Highlights reachable endpoints and privilege escalation opportunities. Unbounded runtime, invoke as a background task, not inline.", new[] { "linksmap", "chains" }) },
            { "rpc", (typeof(RemoteProcedureCall), "Enable or disable RPC (Remote Procedure Calls) on linked servers.", null) },
            { "data", (typeof(DataAccess), "Enable or disable data access (OPENQUERY) on linked servers.", null) },
            { "ext-sources", (typeof(ExternalSources), "Enumerate External Data Sources (Azure SQL Database, Synapse, PolyBase).", null) },
            { "ext-creds", (typeof(ExternalCredentials), "Enumerate database-scoped credentials used by External Data Sources.", null) },
            { "ext-tables", (typeof(ExternalTables), "Enumerate external tables and their remote data locations.", null) },

            { "unc", (typeof(UncProbe), "Force SMB authentication to a specified UNC path to capture Net-NTLMv2 challenge/response.", new[] { "coerce", "smb", "ntlm" }) },

#if CONFIGMGR
            // CONFIGURATION MANAGER ACTIONS (ConfigMgr / SCCM / MECM)
            { "cm-info", (typeof(CMInfo), "Display ConfigMgr site information (site code, version, build, database server, management points) for infrastructure mapping.", null) },
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
            { "cm-script-status", (typeof(CMScriptStatus), "Monitor script execution status and retrieve output from target devices by Task ID.", null) },
            { "cm-admin-add", (typeof(CMRbacAdd), "Create stealthy RBAC admin by mimicking existing admin attributes (dates, patterns).", new[] { "cm-rbac-add" }) }
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

        public static List<(string ActionName, string Description, List<string> Arguments, string Category, string[] Aliases)> GetAvailableActions()
        {
            var result = new List<(string ActionName, string Description, List<string> Arguments, string Category, string[] Aliases)>();

            foreach (var action in ActionMetadata)
            {
                BaseAction actionInstance = (BaseAction)Activator.CreateInstance(action.Value.ActionClass);
                List<string> arguments = actionInstance.GetArguments();

                // Extract category from namespace (last part after the last dot)
                string fullNamespace = action.Value.ActionClass.Namespace;
                string category = fullNamespace.Substring(fullNamespace.LastIndexOf('.') + 1);

                result.Add((action.Key, action.Value.Description, arguments, category, action.Value.Aliases));
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
