using MSSQLand.Services;
using MSSQLand.Utilities;
using MSSQLand.Utilities.Formatters;
using System;
using System.Collections.Generic;

namespace MSSQLand.Actions.Administration
{
    /// <summary>
    /// Manages ADSI (Active Directory Service Interfaces) linked servers.
    /// Supports listing, creating, and deleting ADSI linked servers.
    /// </summary>
    internal class AdsiManager : BaseAction
    {
        private enum Operation { List, Create, Delete }

        [ArgumentMetadata(Position = 0, Description = "Operation: list, create, or delete (default: list)")]
        private Operation _operation = Operation.List;

        [ArgumentMetadata(Position = 1, Description = "Server name for create/delete operations (optional for create - generates random name if omitted)")]
        private string? _serverName;

        [ArgumentMetadata(Position = 2, Description = "Data source for the ADSI linked server (default: localhost)")]
        private string _dataSource = "localhost";

        public override void ValidateArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                _operation = Operation.List;
                return;
            }

            string[] parts = args;
            string command = parts[0].ToLower();

            switch (command)
            {
                case "list":
                    _operation = Operation.List;
                    break;

                case "create":
                    _operation = Operation.Create;
                    
                    // If server name not provided, generate a random one
                    if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        _serverName = parts[1];
                    }
                    else
                    {
                        _serverName = $"ADSI-{Guid.NewGuid().ToString("N").Substring(0, 6)}";
                        Logger.Info($"No server name provided. Generated random name: {_serverName}");
                    }

                    // Optional data source parameter
                    if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
                    {
                        _dataSource = parts[2];
                    }
                    break;

                case "delete":
                case "del":
                case "remove":
                case "rm":
                    _operation = Operation.Delete;
                    
                    if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        throw new ArgumentException("Server name is required for delete operation. Example: /a:adsi delete SQL-02");
                    }
                    
                    _serverName = parts[1];
                    break;

                default:
                    throw new ArgumentException($"Invalid operation '{command}'. Use 'list', 'create', or 'delete'");
            }
        }

        public override object? Execute(DatabaseContext databaseContext)
        {
            switch (_operation)
            {
                case Operation.List:
                    return ListAdsiServers(databaseContext);

                case Operation.Create:
                    return CreateAdsiServer(databaseContext);

                case Operation.Delete:
                    return DeleteAdsiServer(databaseContext);

                default:
                    Logger.Error("Unknown operation.");
                    return null;
            }
        }

        /// <summary>
        /// Lists all ADSI linked servers and returns a list of their names.
        /// </summary>
        private List<string>? ListAdsiServers(DatabaseContext databaseContext)
        {
            Logger.Task("Enumerating ADSI linked servers");

            AdsiService adsiService = new(databaseContext);
            List<string> adsiServers = adsiService.ListAdsiServers();

            if (adsiServers == null || adsiServers.Count == 0)
            {
                Logger.Warning("No ADSI linked servers found.");
                return null;
            }

            Logger.Success($"Found {adsiServers.Count} ADSI linked server{(adsiServers.Count > 1 ? "s" : "")}");
            Logger.NewLine();

            // Display formatted markdown table
            Console.WriteLine(OutputFormatter.ConvertList(adsiServers, "ADSI Servers"));

            return adsiServers;
        }

        /// <summary>
        /// Creates a new ADSI linked server.
        /// </summary>
        private bool CreateAdsiServer(DatabaseContext databaseContext)
        {
            Logger.Task($"Creating ADSI linked server '{_serverName}'");

            AdsiService adsiService = new(databaseContext);

            // Check if server already exists
            if (adsiService.AdsiServerExists(_serverName))
            {
                Logger.Error($"ADSI linked server '{_serverName}' already exists.");
                return false;
            }

            bool success = adsiService.CreateAdsiLinkedServer(_serverName, _dataSource);

            if (success)
            {
                Logger.Success($"ADSI linked server '{_serverName}' created successfully");
                Logger.InfoNested($"Server name: {_serverName}");
                Logger.InfoNested($"Data source: {_dataSource}");
            }

            return success;
        }

        /// <summary>
        /// Deletes an existing ADSI linked server.
        /// </summary>
        private bool DeleteAdsiServer(DatabaseContext databaseContext)
        {
            Logger.Task($"Deleting ADSI linked server '{_serverName}'");

            AdsiService adsiService = new(databaseContext);

            // Check if server exists and is ADSI
            if (!adsiService.AdsiServerExists(_serverName))
            {
                Logger.Error($"ADSI linked server '{_serverName}' not found.");
                return false;
            }

            try
            {
                adsiService.DropLinkedServer(_serverName);
                
                Logger.Success($"ADSI linked server '{_serverName}' deleted successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to delete ADSI linked server '{_serverName}': {ex.Message}");
                return false;
            }
        }
    }
}
