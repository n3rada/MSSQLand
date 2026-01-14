using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace MSSQLand.Utilities.Discovery
{
    /// <summary>
    /// Scans TCP ports to discover SQL Server instances by validating TDS (Tabular Data Stream) protocol.
    /// More reliable than SQL Browser as it validates actual listening ports, not configured values.
    /// 
    /// <para>
    /// <b>How it works:</b>
    /// 1. Attempts TCP connection to each port
    /// 2. Sends TDS prelogin packet
    /// 3. If server responds with TDS, it's a SQL Server
    /// </para>
    /// 
    /// <para>
    /// <b>OPSEC Note:</b> This performs active TCP connections which may be logged by firewalls/IDS.
    /// Use SQL Browser (-browse) first for quieter discovery.
    /// </para>
    /// </summary>
    public static class PortScanner
    {
        // Common SQL Server ports to check first
        private static readonly int[] CommonPorts = new int[]
        {
            1433,  // Default instance
            1435,  // Common alternate
            2433,  // Common alternate
            14330, // Common high port
            // Dynamic port range commonly used by named instances
            49152, 49153, 49154, 49155, 49156, 49157, 49158, 49159, 49160
        };

        private const int DefaultTimeoutMs = 500;
        private const int TdsPreloginPacketType = 0x12;

        /// <summary>
        /// Result of a port scan indicating a SQL Server was found.
        /// </summary>
        public class ScanResult
        {
            public int Port { get; set; }
            public bool IsTds { get; set; }
            public string ResponseInfo { get; set; }
        }

        /// <summary>
        /// Scans common SQL Server ports on the specified host.
        /// </summary>
        /// <param name="hostname">The hostname or IP to scan</param>
        /// <param name="timeoutMs">Connection timeout in milliseconds</param>
        /// <returns>List of ports that respond with TDS protocol</returns>
        public static List<ScanResult> ScanCommonPorts(string hostname, int timeoutMs = DefaultTimeoutMs)
        {
            return ScanPorts(hostname, CommonPorts, timeoutMs);
        }

        /// <summary>
        /// Scans a range of ports on the specified host.
        /// </summary>
        /// <param name="hostname">The hostname or IP to scan</param>
        /// <param name="startPort">Start of port range</param>
        /// <param name="endPort">End of port range</param>
        /// <param name="timeoutMs">Connection timeout in milliseconds</param>
        /// <returns>List of ports that respond with TDS protocol</returns>
        public static List<ScanResult> ScanRange(string hostname, int startPort, int endPort, int timeoutMs = DefaultTimeoutMs)
        {
            var ports = new List<int>();
            for (int p = startPort; p <= endPort; p++)
            {
                ports.Add(p);
            }
            return ScanPorts(hostname, ports.ToArray(), timeoutMs);
        }

        /// <summary>
        /// Scans specific ports on the specified host.
        /// </summary>
        private static List<ScanResult> ScanPorts(string hostname, int[] ports, int timeoutMs)
        {
            var results = new List<ScanResult>();

            foreach (int port in ports)
            {
                var result = ProbePort(hostname, port, timeoutMs);
                if (result != null)
                {
                    results.Add(result);
                }
            }

            return results;
        }

        /// <summary>
        /// Probes a single port to check if it's running SQL Server.
        /// Sends a TDS prelogin packet and validates the response.
        /// </summary>
        private static ScanResult ProbePort(string hostname, int port, int timeoutMs)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    // Set connect timeout
                    var connectResult = client.BeginConnect(hostname, port, null, null);
                    bool connected = connectResult.AsyncWaitHandle.WaitOne(timeoutMs);
                    
                    if (!connected)
                    {
                        return null; // Connection timeout
                    }

                    try
                    {
                        client.EndConnect(connectResult);
                    }
                    catch
                    {
                        return null; // Connection refused
                    }

                    // Port is open - now validate TDS
                    var stream = client.GetStream();
                    stream.ReadTimeout = timeoutMs;
                    stream.WriteTimeout = timeoutMs;

                    // Send TDS prelogin packet
                    // TDS packet header: Type (1 byte), Status (1 byte), Length (2 bytes), SPID (2 bytes), PacketID (1 byte), Window (1 byte)
                    // Minimal prelogin: just the header with type 0x12 (prelogin)
                    byte[] preloginPacket = new byte[]
                    {
                        TdsPreloginPacketType, // Packet type: Prelogin
                        0x01,                   // Status: End of message
                        0x00, 0x08,            // Length: 8 bytes (just header)
                        0x00, 0x00,            // SPID: 0
                        0x00,                   // PacketID: 0
                        0x00                    // Window: 0
                    };

                    stream.Write(preloginPacket, 0, preloginPacket.Length);

                    // Try to read response
                    byte[] response = new byte[8];
                    int bytesRead = 0;
                    
                    try
                    {
                        bytesRead = stream.Read(response, 0, response.Length);
                    }
                    catch (System.IO.IOException)
                    {
                        // Read timeout - not SQL Server or filtered
                        return null;
                    }

                    if (bytesRead > 0)
                    {
                        // Check if response looks like TDS
                        // TDS prelogin response has type 0x04 (tabular result) or 0x12 (prelogin response)
                        byte responseType = response[0];
                        
                        if (responseType == 0x04 || responseType == 0x12)
                        {
                            return new ScanResult
                            {
                                Port = port,
                                IsTds = true,
                                ResponseInfo = $"TDS response type: 0x{responseType:X2}"
                            };
                        }
                        else if (bytesRead >= 4)
                        {
                            // Could still be SQL Server with different response
                            return new ScanResult
                            {
                                Port = port,
                                IsTds = false,
                                ResponseInfo = $"Unknown response: 0x{response[0]:X2} 0x{response[1]:X2} 0x{response[2]:X2} 0x{response[3]:X2}"
                            };
                        }
                    }
                }
            }
            catch (SocketException)
            {
                // Connection failed - port closed or filtered
            }
            catch (Exception)
            {
                // Other error
            }

            return null;
        }

        /// <summary>
        /// Logs scan results to the console.
        /// </summary>
        public static void LogResults(string hostname, List<ScanResult> results)
        {
            if (results.Count == 0)
            {
                Logger.Warning("No SQL Server ports found");
                return;
            }

            Logger.Success($"Found {results.Count} SQL Server port(s):");
            foreach (var result in results)
            {
                string status = result.IsTds ? "[TDS Confirmed]" : "[Possible]";
                Logger.SuccessNested($"Port {result.Port} {status}");
                Logger.InfoNested($"  Connection: {hostname}:{result.Port}");
                Logger.Debug($"  {result.ResponseInfo}");
            }
        }
    }
}
