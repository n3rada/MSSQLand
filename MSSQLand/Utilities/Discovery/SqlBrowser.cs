using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

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
        /// <param name="hostname">The hostname or IP address to query</param>
        /// <returns>List of discovered SQL instances, or empty list if browser unavailable</returns>
        public static List<SqlInstance> Query(string hostname)
        {
            var instances = new List<SqlInstance>();

            try
            {
                using (var udpClient = new UdpClient())
                {
                    udpClient.Client.ReceiveTimeout = TimeoutMs;
                    udpClient.Connect(hostname, BrowserPort);

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
        /// Logs discovered instances to the console.
        /// </summary>
        public static void LogInstances(string hostname, List<SqlInstance> instances)
        {
            if (instances.Count == 0)
            {
                Logger.InfoNested("SQL Browser service not available or no instances found");
                return;
            }

            Logger.Info($"SQL Browser returned {instances.Count} instance(s):");
            foreach (var instance in instances)
            {
                string portInfo = instance.TcpPort.HasValue ? $"TCP {instance.TcpPort}" : "No TCP";
                string clustered = instance.IsClustered ? " [Clustered]" : "";
                Logger.InfoNested($"{instance.InstanceName}: {portInfo}, Version {instance.Version}{clustered}");
                Logger.InfoNested($"  Connection: {instance.GetConnectionTarget(hostname)}");
            }
        }
    }
}
