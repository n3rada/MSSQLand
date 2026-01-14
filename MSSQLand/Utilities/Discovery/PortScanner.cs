using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

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
        /// <summary>
        /// Known SQL Server ports seen in the wild.
        /// These are tried first before scanning ephemeral port ranges.
        /// 
        /// Add ports you encounter in your engagements to speed up future scans.
        /// </summary>
        public static readonly int[] KnownPorts = new int[]
        {
            // Official/documented ports
            1433,   // Default SQL Server instance

            // Seen in the wild
            14711,
            14712,
        };

        // IANA ephemeral port range (Windows uses this for named instances)
        private const int EphemeralStart = 49152;
        private const int EphemeralEnd = 65535;

        private const int DefaultTimeoutMs = 500;
        private const int DefaultParallelism = 100;  // Max concurrent connections
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
        /// Scans known ports first, then ephemeral range from both ends toward middle.
        /// Stops as soon as a SQL Server is found.
        /// </summary>
        /// <param name="hostname">The hostname or IP to scan</param>
        /// <param name="timeoutMs">Connection timeout in milliseconds</param>
        /// <param name="maxParallelism">Maximum concurrent connections</param>
        /// <param name="stopOnFirst">If true, stops scanning after first SQL Server is found</param>
        /// <returns>List of ports that respond with TDS protocol</returns>
        public static List<ScanResult> Scan(string hostname, int timeoutMs = DefaultTimeoutMs, int maxParallelism = DefaultParallelism, bool stopOnFirst = true)
        {
            var results = new ConcurrentBag<ScanResult>();
            var found = new ManualResetEventSlim(false);

            // Phase 1: Scan known ports first
            Logger.Info($"Testing {KnownPorts.Length} known ports (1433, custom static ports)");
            var knownResults = ScanPortsParallel(hostname, KnownPorts, timeoutMs, maxParallelism, stopOnFirst ? found : null);
            foreach (var r in knownResults)
            {
                results.Add(r);
            }

            if (stopOnFirst && results.Count > 0)
            {
                return results.OrderBy(r => r.Port).ToList();
            }

            Logger.NewLine();

            // Phase 2: Scan ephemeral range from both ends toward middle
            Logger.Info($"Scanning IANA ephemeral range ({EphemeralStart}-{EphemeralEnd})");
            Logger.InfoNested("Windows allocates dynamic ports here for named instances");
            Logger.InfoNested("Scanning from edges toward middle for faster discovery");
            var ephemeralPorts = GenerateEdgesToMiddle(EphemeralStart, EphemeralEnd);
            var ephemeralResults = ScanPortsParallel(hostname, ephemeralPorts, timeoutMs, maxParallelism, stopOnFirst ? found : null);
            foreach (var r in ephemeralResults)
            {
                results.Add(r);
            }

            return results.OrderBy(r => r.Port).ToList();
        }

        /// <summary>
        /// Generates port list starting from both ends of a range, meeting in the middle.
        /// Example for range 1-10: 1, 10, 2, 9, 3, 8, 4, 7, 5, 6
        /// This is effective for ephemeral ports as Windows allocates from the start,
        /// but long-running services may have grabbed early ports.
        /// </summary>
        private static int[] GenerateEdgesToMiddle(int start, int end)
        {
            var ports = new List<int>();
            int low = start;
            int high = end;

            while (low <= high)
            {
                ports.Add(low);
                if (low != high)
                {
                    ports.Add(high);
                }
                low++;
                high--;
            }

            return ports.ToArray();
        }

        /// <summary>
        /// Scans known SQL Server ports only.
        /// Uses parallel scanning for speed.
        /// </summary>
        /// <param name="hostname">The hostname or IP to scan</param>
        /// <param name="timeoutMs">Connection timeout in milliseconds</param>
        /// <param name="maxParallelism">Maximum concurrent connections</param>
        /// <returns>List of ports that respond with TDS protocol</returns>
        public static List<ScanResult> ScanKnownPorts(string hostname, int timeoutMs = DefaultTimeoutMs, int maxParallelism = DefaultParallelism)
        {
            return ScanPortsParallel(hostname, KnownPorts, timeoutMs, maxParallelism, null);
        }

        /// <summary>
        /// Scans specific ports in parallel on the specified host.
        /// </summary>
        /// <param name="stopSignal">Optional signal to stop scanning early (e.g., when first result found)</param>
        private static List<ScanResult> ScanPortsParallel(string hostname, int[] ports, int timeoutMs, int maxParallelism, ManualResetEventSlim stopSignal)
        {
            var results = new ConcurrentBag<ScanResult>();
            var semaphore = new SemaphoreSlim(maxParallelism);
            var cts = new CancellationTokenSource();
            var tasks = new List<Task>();

            // Link stop signal to cancellation if provided
            if (stopSignal != null)
            {
                Task.Run(() =>
                {
                    stopSignal.Wait();
                    cts.Cancel();
                });
            }

            foreach (int port in ports)
            {
                if (cts.Token.IsCancellationRequested)
                    break;

                tasks.Add(Task.Run(async () =>
                {
                    if (cts.Token.IsCancellationRequested)
                        return;

                    await semaphore.WaitAsync(cts.Token).ConfigureAwait(false);
                    try
                    {
                        if (cts.Token.IsCancellationRequested)
                            return;

                        var result = ProbePort(hostname, port, timeoutMs);
                        if (result != null)
                        {
                            results.Add(result);
                            stopSignal?.Set();  // Signal to stop other scans
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when stopping early
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cts.Token));
            }

            try
            {
                Task.WaitAll(tasks.ToArray());
            }
            catch (AggregateException)
            {
                // Some tasks were cancelled, that's expected
            }
            
            return results.OrderBy(r => r.Port).ToList();
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
            Logger.NewLine();
            
            if (results.Count == 0)
            {
                Logger.Warning("No SQL Server ports found");
                return;
            }

            Logger.Success($"Found {results.Count} SQL Server port(s):");
            foreach (var result in results)
            {
                Logger.SuccessNested($"{hostname}:{result.Port}");
            }
        }
    }
}
