// MSSQLand/Utilities/Discovery/SqlBrowser.cs

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

using MSSQLand.Utilities.Formatters;

namespace MSSQLand.Utilities.Discovery
{
    /// <summary>
    /// Queries SQL Server Browser service (UDP 1434) to discover SQL instances and their ports.
    /// The SQL Browser service returns instance information including TCP ports and named pipe paths.
    ///
    /// <para>
    /// <b>Important:</b> SQL Browser returns the <i>configured</i> TCP port from SQL Server Configuration Manager,
    /// not the actual runtime port. If the port was changed without restarting SQL Server, the reported
    /// port may differ from the port the instance is actually listening on.
    /// </para>
    /// </summary>
    public static class SqlBrowser
    {
        private const int BrowserPort = 1434;
        private const int TimeoutMs = 3000;
        private const byte InstanceListRequest = 0x02;

        /// <summary>
        /// Represents a SQL Server instance returned by the Browser service.
        /// </summary>
        public class SqlInstance
        {
            public string ServerName { get; set; }
            public string InstanceName { get; set; }
            public bool IsClustered { get; set; }
            public string Version { get; set; }
            public int? TcpPort { get; set; }
            public string NamedPipe { get; set; }

            /// <summary>
            /// Returns connection string format: hostname,port or hostname\instance
            /// </summary>
            public string GetConnectionTarget(string hostname)
            {
                if (TcpPort.HasValue)
                    return $"{hostname}:{TcpPort.Value}";
                if (!string.IsNullOrEmpty(InstanceName) && InstanceName != "MSSQLSERVER")
                    return $"{hostname}\\{InstanceName}";
                return hostname;
            }
        }

        /// <summary>
        /// Queries the SQL Browser service on the specified host.
        /// </summary>
        /// <param name="ip">The resolved IP address to query</param>
        /// <param name="hostname">The original hostname for display purposes</param>
        /// <returns>List of discovered SQL instances, or empty list if browser unavailable</returns>
        public static List<SqlInstance> Query(IPAddress ip, string hostname)
        {
            var instances = new List<SqlInstance>();

            try
            {
                using (var udpClient = new UdpClient())
                {
                    udpClient.Client.ReceiveTimeout = TimeoutMs;
                    udpClient.Connect(ip, BrowserPort);

                    // Send instance list request (0x02)
                    udpClient.Send(new byte[] { InstanceListRequest }, 1);

                    // Receive response
                    IPEndPoint remoteEP = null;
                    byte[] response = udpClient.Receive(ref remoteEP);

                    if (response.Length > 3)
                    {
                        // Skip first 3 bytes (header)
                        string responseStr = Encoding.ASCII.GetString(response, 3, response.Length - 3);
                        instances = ParseBrowserResponse(responseStr);
                    }
                }
            }
            catch (SocketException)
            {
                // Browser service not available or blocked - return empty list
            }
            catch (Exception)
            {
                // Any other error - return empty list
            }

            return instances;
        }

        /// <summary>
        /// Parses the SQL Browser response string into SqlInstance objects.
        /// Format: ServerName;NAME;InstanceName;INST1;IsClustered;No;Version;X.X.X;tcp;PORT;np;PIPE;;
        /// Multiple instances are separated by ;;
        /// </summary>
        private static List<SqlInstance> ParseBrowserResponse(string response)
        {
            var instances = new List<SqlInstance>();

            // Split by ;; to get individual instances
            string[] instanceStrings = response.Split(new[] { ";;" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string instanceStr in instanceStrings)
            {
                var instance = new SqlInstance();
                string[] parts = instanceStr.Split(';');

                for (int i = 0; i < parts.Length - 1; i += 2)
                {
                    string key = parts[i].ToLowerInvariant();
                    string value = parts[i + 1];

                    switch (key)
                    {
                        case "servername":
                            instance.ServerName = value;
                            break;
                        case "instancename":
                            instance.InstanceName = value;
                            break;
                        case "isclustered":
                            instance.IsClustered = value.Equals("Yes", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "version":
                            instance.Version = value;
                            break;
                        case "tcp":
                            if (int.TryParse(value, out int port))
                                instance.TcpPort = port;
                            break;
                        case "np":
                            instance.NamedPipe = value;
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(instance.ServerName) || !string.IsNullOrEmpty(instance.InstanceName))
                {
                    instances.Add(instance);
                }
            }

            return instances;
        }

        /// <summary>
        /// Sends a UDP broadcast to port 1434 to discover all SQL Server instances on the local network.
        /// This mimics SSMS "Browse for more..." behavior by broadcasting to all reachable subnets.
        /// </summary>
        /// <param name="timeoutMs">How long to wait for responses (default: 3000ms)</param>
        /// <returns>Number of unique servers found</returns>
        public static int Broadcast(int timeoutMs = 3000)
        {
            Logger.Task("Broadcasting for SQL Servers on the local network (UDP 1434)");
            Logger.TaskNested("Sending SQL Browser broadcast: servers with Browser service enabled will respond.");

            var allInstances = new Dictionary<string, List<SqlInstance>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var udpClient = new UdpClient())
                {
                    udpClient.EnableBroadcast = true;
                    udpClient.Client.ReceiveTimeout = timeoutMs;

                    // Send broadcast to 255.255.255.255:1434
                    var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, BrowserPort);
                    udpClient.Send(new byte[] { InstanceListRequest }, 1, broadcastEndpoint);

                    // Collect responses until timeout
                    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                    while (DateTime.UtcNow < deadline)
                    {
                        try
                        {
                            IPEndPoint remoteEP = null;
                            byte[] response = udpClient.Receive(ref remoteEP);

                            if (response.Length > 3)
                            {
                                string responseStr = Encoding.ASCII.GetString(response, 3, response.Length - 3);
                                var instances = ParseBrowserResponse(responseStr);

                                string responder = remoteEP.Address.ToString();

                                // Use the ServerName from the response if available, otherwise use IP
                                string serverKey = instances.Count > 0 && !string.IsNullOrEmpty(instances[0].ServerName)
                                    ? instances[0].ServerName
                                    : responder;

                                if (!allInstances.ContainsKey(serverKey))
                                {
                                    allInstances[serverKey] = instances;
                                }
                            }

                            // Reduce remaining timeout
                            int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                            if (remaining > 0)
                            {
                                udpClient.Client.ReceiveTimeout = remaining;
                            }
                        }
                        catch (SocketException)
                        {
                            // Timeout reached, no more responses
                            break;
                        }
                    }
                }
            }
            catch (SocketException ex)
            {
                Logger.Error($"Broadcast failed: {ex.Message}");
                Logger.ErrorNested("Ensure UDP port 1434 is not blocked by a local firewall.");
                return 0;
            }

            if (allInstances.Count == 0)
            {
                Logger.Warning("No SQL Servers responded to broadcast.");
                Logger.WarningNested("Possible causes: no Browser service running, UDP 1434 blocked, or no servers on local subnet.");
                return 0;
            }

            // Build result table
            DataTable resultTable = new();
            resultTable.Columns.Add("Server", typeof(string));
            resultTable.Columns.Add("Instance", typeof(string));
            resultTable.Columns.Add("Version", typeof(string));
            resultTable.Columns.Add("TCP Port", typeof(string));
            resultTable.Columns.Add("Named Pipe", typeof(string));
            resultTable.Columns.Add("Clustered", typeof(string));

            int totalInstances = 0;
            foreach (var kvp in allInstances.OrderBy(k => k.Key))
            {
                foreach (var instance in kvp.Value)
                {
                    resultTable.Rows.Add(
                        kvp.Key,
                        instance.InstanceName ?? "MSSQLSERVER",
                        instance.Version ?? "Unknown",
                        instance.TcpPort.HasValue ? instance.TcpPort.Value.ToString() : "-",
                        instance.NamedPipe ?? "-",
                        instance.IsClustered ? "Yes" : "No"
                    );
                    totalInstances++;
                }
            }

            Console.WriteLine(OutputFormatter.ConvertDataTable(resultTable));
            Logger.Success($"{totalInstances} instance(s) found on {allInstances.Count} server(s).");

            return allInstances.Count;
        }

        /// <summary>
        /// Logs discovered instances to the console.
        /// </summary>
        public static void LogInstances(string hostname, List<SqlInstance> instances)
        {
            if (instances.Count == 0)
            {
                Logger.Warning("SQL Browser service not available or no instances found");
                return;
            }

            Logger.Success($"SQL Browser returned {instances.Count} instance(s):");
            Logger.SuccessNested("Note: Ports are configured values, not necessarily the current listening ports");

            foreach (var instance in instances)
            {
                Logger.NewLine();
                string portInfo = instance.TcpPort.HasValue ? $"TCP {instance.TcpPort}" : "No TCP";
                string clustered = instance.IsClustered ? " [Clustered]" : "";
                Logger.Info($"{instance.InstanceName}: {portInfo}, Version {instance.Version}{clustered}");
                Logger.InfoNested($"Connection: {instance.GetConnectionTarget(hostname)}");
                if (!string.IsNullOrEmpty(instance.NamedPipe))
                {
                    Logger.InfoNested($"Named Pipe: {instance.NamedPipe}");
                }
            }
        }
    }
}
