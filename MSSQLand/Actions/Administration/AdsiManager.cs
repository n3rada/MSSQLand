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
        private string? _serverName = null;

        [ArgumentMetadata(Position = 2, Description = "Data source for the ADSI linked server (default: localhost)")]
        private string _dataSource = "localhost";

        public override void ValidateArguments(string[] args)
        {
            var (namedArgs, positionalArgs) = ParseActionArguments(args);
            
            // Parse operation (default: list)
            string opStr = GetPositionalArgument(positionalArgs, 0, "list");
            if (!Enum.TryParse<Operation>(opStr, true, out _operation))
            {
                throw new ArgumentException($"Invalid operation: {opStr}. Valid operations: list, create, delete");
            }
            
            // Parse server name (optional, used for create/delete)
            _serverName = GetPositionalArgument(positionalArgs, 1, null);
            
            // Parse data source (default: localhost)
            _dataSource = GetPositionalArgument(positionalArgs, 2, "localhost");
            
            // Validation
            if ((_operation == Operation.Delete) && string.IsNullOrEmpty(_serverName))
            {
                throw new ArgumentException("Server name is required for delete operation");
            }
            
            // Generate random name for create if not provided
            if ((_operation == Operation.Create) && string.IsNullOrEmpty(_serverName))
            {
                _serverName = $"ADSI_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
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
