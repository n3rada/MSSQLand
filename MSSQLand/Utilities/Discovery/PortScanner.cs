// MSSQLand/Utilities/Discovery/PortScanner.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MSSQLand.Utilities.Discovery
{
    /// <summary>
    /// TCP port scanner with TDS protocol validation for SQL Server discovery.
    /// 
    /// <para><b>Strategy:</b></para>
    /// <list type="number">
    /// <item>DNS resolution cached upfront</item>
    /// <item>Parallel TCP connect + TDS validation (500 concurrent)</item>
    /// <item>Edges-to-middle scanning for faster discovery</item>
    /// </list>
    /// 
    /// <para><b>Expected timing for ephemeral range (16,384 ports):</b></para>
    /// <para>With 500 concurrent connections and 500ms timeout:</para>
    /// <para>Batches = 16,384 / 500 ≈ 33 waves</para>
    /// <list type="bullet">
    /// <item>Best case (closed ports RST fast): ~5-10s</item>
    /// <item>Average (some filtering): ~15-25s</item>
    /// <item>Worst case (all filtered, full timeout): 33 × 500ms ≈ 17s + overhead = ~25-35s</item>
    /// </list>
    /// 
    /// <para><b>OPSEC Note:</b> Active TCP connections may be logged. Use -browse first.</para>
    /// </summary>
    public static class PortScanner
    {
        /// <summary>
        /// Known SQL Server ports. Scanned first before ephemeral range.
        /// </summary>
        public static readonly int[] KnownPorts = new int[]
        {
            1433,   // Default instance
            14711, 14712,  // Seen in the wild
        };

        private const int EphemeralStart = 49152;
        private const int EphemeralEnd = 65535;

        private const int DefaultTimeoutMs = 500;
        private const int DefaultParallelism = 500;
        private const int TdsPreloginPacketType = 0x12;

        public class ScanResult
        {
            public int Port { get; set; }
            public bool IsTds { get; set; }
            public string ResponseInfo { get; set; }
        }

        /// <summary>
        /// Scan: known ports first, then ephemeral range.
        /// </summary>
        public static List<ScanResult> Scan(string hostname, int timeoutMs = DefaultTimeoutMs, int maxParallelism = DefaultParallelism, bool stopOnFirst = true)
        {
            var globalStopwatch = Stopwatch.StartNew();
            
            // Resolve DNS once upfront
            IPAddress ip;
            try
            {
                ip = ResolveHostname(hostname);
                Logger.InfoNested($"Resolved to {ip}");
            }
            catch (Exception ex)
            {
                Logger.Error($"DNS resolution failed: {ex.Message}");
                return new List<ScanResult>();
            }

            var cts = new CancellationTokenSource();

            // Phase 1: Known ports
            Logger.Info($"Phase 1: Testing {KnownPorts.Length} known ports");
            var knownStopwatch = Stopwatch.StartNew();
            var knownResults = ScanPortsParallel(ip, KnownPorts, timeoutMs, maxParallelism, stopOnFirst ? cts : null);
            knownStopwatch.Stop();
            Logger.InfoNested($"Completed in {knownStopwatch.ElapsedMilliseconds}ms");

            if (stopOnFirst && knownResults.Count > 0)
            {
                LogSummary(hostname, knownResults, globalStopwatch);
                return knownResults;
            }

            // Phase 2: Ephemeral range
            Logger.NewLine();
            int ephemeralCount = EphemeralEnd - EphemeralStart + 1;
            Logger.Info($"Phase 2: Scanning ephemeral range ({EphemeralStart}-{EphemeralEnd}) - {ephemeralCount} ports");
            Logger.InfoNested("Strategy: edges-to-middle for faster discovery");
            
            var ephemeralStopwatch = Stopwatch.StartNew();
            var ephemeralPorts = GenerateEdgesToMiddle(EphemeralStart, EphemeralEnd);
            var ephemeralResults = ScanPortsParallel(ip, ephemeralPorts, timeoutMs, maxParallelism, stopOnFirst ? cts : null);
            ephemeralStopwatch.Stop();
            
            int portsPerSec = (int)(ephemeralCount * 1000L / Math.Max(1, ephemeralStopwatch.ElapsedMilliseconds));
            Logger.InfoNested($"Completed in {ephemeralStopwatch.Elapsed.TotalSeconds:F1}s ({portsPerSec} ports/sec)");

            var allResults = knownResults.Concat(ephemeralResults).OrderBy(r => r.Port).ToList();
            LogSummary(hostname, allResults, globalStopwatch);
            return allResults;
        }

        /// <summary>
        /// Parallel port scanning with TDS validation.
        /// </summary>
        private static List<ScanResult> ScanPortsParallel(IPAddress ip, int[] ports, int timeoutMs, int maxParallelism, CancellationTokenSource stopCts)
        {
            var results = new ConcurrentBag<ScanResult>();
            var semaphore = new SemaphoreSlim(maxParallelism);

            var tasks = ports.Select(port => Task.Run(async () =>
            {
                if (stopCts?.IsCancellationRequested == true)
                    return;

                await semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (stopCts?.IsCancellationRequested == true)
                        return;

                    var result = await ProbePortAsync(ip, port, timeoutMs).ConfigureAwait(false);
                    if (result != null)
                    {
                        results.Add(result);
                        stopCts?.Cancel();
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            })).ToArray();

            try { Task.WaitAll(tasks); } catch { }

            return results.OrderBy(r => r.Port).ToList();
        }

        /// <summary>
        /// Probes a single port asynchronously: TCP connect + TDS validation.
        /// </summary>
        private static async Task<ScanResult> ProbePortAsync(IPAddress ip, int port, int timeoutMs)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    // Connect with timeout using async + CancellationToken
                    using (var cts = new CancellationTokenSource(timeoutMs))
                    {
                        try
                        {
                            await client.ConnectAsync(ip, port).ConfigureAwait(false);
                            
                            // Check if we connected before timeout
                            if (cts.IsCancellationRequested || !client.Connected)
                                return null;
                        }
                        catch (OperationCanceledException)
                        {
                            return null;
                        }
                        catch (SocketException)
                        {
                            return null;
                        }
                    }

                    // Port is open - validate TDS
                    var stream = client.GetStream();
                    stream.ReadTimeout = timeoutMs;
                    stream.WriteTimeout = timeoutMs;

                    // Send TDS prelogin packet
                    byte[] prelogin = { TdsPreloginPacketType, 0x01, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00 };
                    await stream.WriteAsync(prelogin, 0, prelogin.Length).ConfigureAwait(false);

                    // Read response
                    byte[] response = new byte[8];
                    int bytesRead;
                    try
                    {
                        using (var readCts = new CancellationTokenSource(timeoutMs))
                        {
                            bytesRead = await stream.ReadAsync(response, 0, response.Length).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        return null;
                    }

                    if (bytesRead > 0)
                    {
                        byte type = response[0];
                        // TDS response types: 0x04 (tabular) or 0x12 (prelogin)
                        if (type == 0x04 || type == 0x12)
                        {
                            return new ScanResult { Port = port, IsTds = true, ResponseInfo = $"TDS 0x{type:X2}" };
                        }
                    }
                }
            }
            catch (AggregateException) { }
            catch (SocketException) { }
            catch { }

            return null;
        }

        /// <summary>
        /// Resolves hostname to IP (cached for all port checks).
        /// </summary>
        private static IPAddress ResolveHostname(string hostname)
        {
            if (IPAddress.TryParse(hostname, out var ip))
                return ip;

            var addresses = Dns.GetHostAddresses(hostname);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) 
                   ?? addresses.First();
        }

        /// <summary>
        /// Generates ports from edges toward middle: 49152, 65535, 49153, 65534...
        /// </summary>
        private static int[] GenerateEdgesToMiddle(int start, int end)
        {
            var ports = new int[end - start + 1];
            int low = start, high = end, i = 0;
            while (low <= high)
            {
                ports[i++] = low++;
                if (low <= high)
                    ports[i++] = high--;
            }
            return ports;
        }

        /// <summary>
        /// Scans known ports only.
        /// </summary>
        public static List<ScanResult> ScanKnownPorts(string hostname, int timeoutMs = DefaultTimeoutMs, int maxParallelism = DefaultParallelism)
        {
            try
            {
                var ip = ResolveHostname(hostname);
                return ScanPortsParallel(ip, KnownPorts, timeoutMs, maxParallelism, null);
            }
            catch
            {
                return new List<ScanResult>();
            }
        }

        private static void LogSummary(string hostname, List<ScanResult> results, Stopwatch stopwatch)
        {
            stopwatch.Stop();
            Logger.NewLine();
            Logger.Info($"Total scan time: {stopwatch.Elapsed.TotalSeconds:F1}s");

            if (results.Count == 0)
            {
                Logger.Warning("No SQL Server ports found");
            }
            else
            {
                Logger.Success($"Found {results.Count} SQL Server port(s):");
                foreach (var r in results.OrderBy(r => r.Port))
                {
                    Logger.SuccessNested($"{hostname}:{r.Port}");
                }
            }
        }


    }
}
