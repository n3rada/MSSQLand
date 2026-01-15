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
        /// Covers common SQL ports below the ephemeral range (49152+).
        /// </summary>
        public static readonly int[] KnownPorts = new int[]
        {
            1433,                           // Default instance
            1434, 1435, 1436,               // SQL Browser and nearby
            1491, 1533, 1668,
            14711, 14712,
            5001,
            8564,
            14333, 14336, 14337, 14346,     // Custom high ports
            14711, 14712,
            15001,
            17001,
        };

        private const int MiddleRangeStart = 1433;   // Start of middle range
        private const int MiddleRangeEnd = 49151;    // Before ephemeral range
        private const int EphemeralStart = 49152;
        private const int EphemeralEnd = 65535;

        private const int DefaultTimeoutMs = 250;
        private const int MinTimeoutMs = 50;
        private const int DefaultParallelism = 500;

        // Adaptive timeout tracking
        private static int _adaptiveTimeoutMs = DefaultTimeoutMs;
        private static readonly object _timingLock = new();
        private static List<long> _responseTimes = new();

        /// <summary>
        /// Minimal valid TDS prelogin packet for SQL Server detection.
        ///
        /// <para><b>TDS Protocol Overview:</b></para>
        /// <para>SQL Server uses the Tabular Data Stream (TDS) protocol. Before authentication,
        /// the client sends a PRELOGIN packet (type 0x12) to negotiate version, encryption, etc.</para>
        ///
        /// <para><b>Detection Strategy:</b></para>
        /// <para>We send a minimal but valid PRELOGIN packet. SQL Server responds with either:</para>
        /// <list type="bullet">
        /// <item>0x12 (PRELOGIN response) - Standard prelogin negotiation</item>
        /// <item>0x04 (TABULAR response) - Error/info response (still confirms SQL Server)</item>
        /// </list>
        /// <para>Non-SQL services will either: close connection, timeout, or send different data.</para>
        ///
        /// <para><b>Packet Structure:</b></para>
        /// <para>Header (8 bytes): Type, Status, Length (big-endian), SPID, PacketID, Window</para>
        /// <para>Options: VERSION, ENCRYPTION, INSTOPT, THREADID, MARS, then 0xFF terminator</para>
        /// <para>Option data follows, referenced by offsets in the option list.</para>
        ///
        /// <para><b>Reference:</b> MS-TDS specification section 2.2.6.4 (PRELOGIN)</para>
        /// </summary>
        private static readonly byte[] TdsPreloginPacket = new byte[]
        {
            // TDS Header (8 bytes)
            0x12,       // Packet type: Pre-Login
            0x01,       // Status: EOM (End of Message)
            0x00, 0x2F, // Length: 47 bytes total (big-endian)
            0x00, 0x00, // SPID: 0 (assigned by server)
            0x01,       // PacketID: 1
            0x00,       // Window: 0

            // Prelogin option list (5 bytes per option: token, offset[2], length[2])
            // Offsets are relative to start of packet (after 8-byte header = position 8)
            0x00, 0x00, 0x15, 0x00, 0x06,  // VERSION: offset 21, length 6
            0x01, 0x00, 0x1B, 0x00, 0x01,  // ENCRYPTION: offset 27, length 1
            0x02, 0x00, 0x1C, 0x00, 0x01,  // INSTOPT: offset 28, length 1
            0x03, 0x00, 0x1D, 0x00, 0x04,  // THREADID: offset 29, length 4
            0x04, 0x00, 0x21, 0x00, 0x01,  // MARS: offset 33, length 1
            0xFF,                          // TERMINATOR

            // Option data (referenced by offsets above)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,  // VERSION: 0.0.0.0, subbuild 0
            0x02,                                 // ENCRYPTION: NOT_SUP (don't require encryption)
            0x00,                                 // INSTOPT: empty instance name
            0x00, 0x00, 0x00, 0x00,               // THREADID: 0
            0x00                                  // MARS: disabled
        };

        public class ScanResult
        {
            public int Port { get; set; }
            public bool IsTds { get; set; }
            public string ResponseInfo { get; set; }
        }

        /// <summary>
        /// Scan: known ports first, then ephemeral range.
        /// </summary>
        public static List<ScanResult> Scan(IPAddress ip, string hostname, int timeoutMs = DefaultTimeoutMs, int maxParallelism = DefaultParallelism, bool stopOnFirst = true)
        {
            Logger.TaskNested("Using TDS prelogin packet for SQL Server validation");
            Logger.InfoNested("Strategy: edges-to-middle for faster discovery");

            var globalStopwatch = Stopwatch.StartNew();

            // Reset adaptive timeout for new scan
            lock (_timingLock)
            {
                _adaptiveTimeoutMs = timeoutMs;
                _responseTimes.Clear();
            }

            var cts = new CancellationTokenSource();

            Logger.NewLine();

            // Phase 1: Known ports
            Logger.Info($"Testing {KnownPorts.Length} known ports");
            Logger.InfoNested($"{string.Join(", ", KnownPorts)}");
            var knownStopwatch = Stopwatch.StartNew();
            var knownResults = ScanPortsParallel(ip, KnownPorts, timeoutMs, maxParallelism, stopOnFirst ? cts : null);
            knownStopwatch.Stop();

            if (knownResults.Count > 0)
            {
                Logger.SuccessNested($"Found {knownResults.Count}: {string.Join(", ", knownResults.Select(r => r.Port))} ({knownStopwatch.ElapsedMilliseconds}ms)");
            }
            else
            {
                Logger.InfoNested($"None found ({knownStopwatch.ElapsedMilliseconds}ms)");
            }

            if (stopOnFirst && knownResults.Count > 0)
            {
                LogSummary(hostname, knownResults, globalStopwatch, stoppedEarly: true);
                return knownResults;
            }

            // Phase 2: Ephemeral range
            var ephemeralPortsToScan = Enumerable.Range(EphemeralStart, EphemeralEnd - EphemeralStart + 1).ToArray();
            int ephemeralCount = ephemeralPortsToScan.Length;

            Logger.NewLine();
            Logger.Info($"Scanning ephemeral range ({EphemeralStart}-{EphemeralEnd}) - {ephemeralCount} ports");

            var ephemeralStopwatch = Stopwatch.StartNew();
            var ephemeralPorts = ReorderEdgesToMiddle(ephemeralPortsToScan);
            var ephemeralResults = ScanPortsParallel(ip, ephemeralPorts, timeoutMs, maxParallelism, stopOnFirst ? cts : null);
            ephemeralStopwatch.Stop();

            int ephemeralPortsPerSec = (int)(ephemeralCount * 1000L / Math.Max(1, ephemeralStopwatch.ElapsedMilliseconds));

            if (ephemeralResults.Count > 0)
            {
                Logger.SuccessNested($"Found {ephemeralResults.Count}: {string.Join(", ", ephemeralResults.Select(r => r.Port))} ({ephemeralStopwatch.Elapsed.TotalSeconds:F1}s, {ephemeralPortsPerSec} ports/sec)");
            }
            else
            {
                Logger.InfoNested($"None found ({ephemeralStopwatch.Elapsed.TotalSeconds:F1}s, {ephemeralPortsPerSec} ports/sec)");
            }
            
            if (stopOnFirst && ephemeralResults.Count > 0)
            {
                var allResults = knownResults.Concat(ephemeralResults).OrderBy(r => r.Port).ToList();
                LogSummary(hostname, allResults, globalStopwatch, stoppedEarly: true);
                return allResults;
            }

            // Phase 3: Middle range (1433-49151, excluding known ports already scanned)
            var middlePorts = GenerateMiddleRangePorts();
            int middleCount = middlePorts.Length;
            
            if (middleCount > 0)
            {
                Logger.NewLine();
                Logger.Info($"Scanning middle range ({MiddleRangeStart}-{MiddleRangeEnd}, excluding known ports) - {middleCount} ports");
                
                var middleStopwatch = Stopwatch.StartNew();
                var reorderedMiddlePorts = ReorderEdgesToMiddle(middlePorts);
                var middleResults = ScanPortsParallel(ip, reorderedMiddlePorts, timeoutMs, maxParallelism, stopOnFirst ? cts : null);
                middleStopwatch.Stop();
                
                int middlePortsPerSec = (int)(middleCount * 1000L / Math.Max(1, middleStopwatch.ElapsedMilliseconds));
                
                if (middleResults.Count > 0)
                {
                    Logger.SuccessNested($"Found {middleResults.Count}: {string.Join(", ", middleResults.Select(r => r.Port))} ({middleStopwatch.Elapsed.TotalSeconds:F1}s, {middlePortsPerSec} ports/sec)");
                }
                else
                {
                    Logger.InfoNested($"None found ({middleStopwatch.Elapsed.TotalSeconds:F1}s, {middlePortsPerSec} ports/sec)");
                }
                
                var allResults = knownResults.Concat(ephemeralResults).Concat(middleResults).OrderBy(r => r.Port).ToList();
                bool stoppedEarly = stopOnFirst && allResults.Count > 0;
                LogSummary(hostname, allResults, globalStopwatch, stoppedEarly);
                return allResults;
            }

            var finalResults = knownResults.Concat(ephemeralResults).OrderBy(r => r.Port).ToList();
            LogSummary(hostname, finalResults, globalStopwatch);
            return finalResults;
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
        /// Uses adaptive timeout based on observed response times.
        /// </summary>
        private static async Task<ScanResult> ProbePortAsync(IPAddress ip, int port, int initialTimeoutMs)
        {
            var probeStopwatch = Stopwatch.StartNew();

            // Use adaptive timeout (may be lower than initial after we learn RTT)
            int effectiveTimeout;
            lock (_timingLock)
            {
                effectiveTimeout = _adaptiveTimeoutMs;
            }

            try
            {
                using (var client = new TcpClient())
                {
                    // Connect with adaptive timeout
                    var connectTask = client.ConnectAsync(ip, port);
                    var timeoutTask = Task.Delay(effectiveTimeout);

                    var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);

                    if (completedTask == timeoutTask)
                    {
                        Logger.Trace($"Port {port}: Connect timeout ({effectiveTimeout}ms)");
                        return null;
                    }

                    // Record response time for adaptive timeout
                    long responseMs = probeStopwatch.ElapsedMilliseconds;

                    try
                    {
                        await connectTask.ConfigureAwait(false); // Propagate any exception
                    }
                    catch (SocketException ex)
                    {
                        // Port closed/refused - still useful for timing (RST received)
                        UpdateAdaptiveTimeout(responseMs, initialTimeoutMs);
                        Logger.Trace($"Port {port}: SocketException {ex.SocketErrorCode} ({responseMs}ms)");
                        return null;
                    }

                    if (!client.Connected)
                    {
                        Logger.Trace($"Port {port}: Not connected after await");
                        return null;
                    }

                    // Connected - record timing
                    UpdateAdaptiveTimeout(responseMs, initialTimeoutMs);
                    Logger.Trace($"Port {port}: Connected in {responseMs}ms, sending TDS prelogin");

                    // Port is open - validate TDS
                    var stream = client.GetStream();
                    stream.ReadTimeout = effectiveTimeout;
                    stream.WriteTimeout = effectiveTimeout;

                    // Send proper TDS prelogin packet
                    await stream.WriteAsync(TdsPreloginPacket, 0, TdsPreloginPacket.Length).ConfigureAwait(false);

                    // Read response with timeout
                    byte[] response = new byte[8];
                    int bytesRead;
                    try
                    {
                        var readTask = stream.ReadAsync(response, 0, response.Length);
                        var readTimeoutTask = Task.Delay(effectiveTimeout);

                        if (await Task.WhenAny(readTask, readTimeoutTask).ConfigureAwait(false) == readTimeoutTask)
                        {
                            Logger.Trace($"Port {port}: Read timeout");
                            return null;
                        }

                        bytesRead = await readTask.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logger.Trace($"Port {port}: Read exception: {ex.GetType().Name}");
                        return null;
                    }

                    if (bytesRead > 0)
                    {
                        byte type = response[0];
                        Logger.Trace($"Port {port}: Got {bytesRead} bytes, type=0x{type:X2}");

                        // TDS response types: 0x04 (tabular) or 0x12 (prelogin)
                        if (type == 0x04 || type == 0x12)
                        {
                            Logger.Trace($"Port {port}: TDS confirmed!");
                            return new ScanResult { Port = port, IsTds = true, ResponseInfo = $"TDS 0x{type:X2}" };
                        }
                        else
                        {
                            Logger.Trace($"Port {port}: Not TDS (type 0x{type:X2})");
                        }
                    }
                    else
                    {
                        Logger.Trace($"Port {port}: No bytes read");
                    }
                }
            }
            catch (AggregateException ex)
            {
                Logger.Trace($"Port {port}: AggregateException: {ex.InnerException?.Message}");
            }
            catch (SocketException ex)
            {
                Logger.Trace($"Port {port}: SocketException: {ex.SocketErrorCode}");
            }
            catch (Exception ex)
            {
                Logger.Trace($"Port {port}: Exception: {ex.GetType().Name}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Updates adaptive timeout based on observed response times.
        /// Uses max of last N samples * multiplier to avoid false positives from network jitter.
        /// </summary>
        private static void UpdateAdaptiveTimeout(long responseMs, int initialTimeoutMs)
        {
            lock (_timingLock)
            {
                _responseTimes.Add(responseMs);

                // Need at least 3 samples before adapting
                if (_responseTimes.Count >= 3)
                {
                    // Use max of recent samples * 3 + 20% buffer to avoid false negatives
                    var recentSamples = _responseTimes.Skip(Math.Max(0, _responseTimes.Count - 10)).ToList();
                    long maxResponseMs = recentSamples.Max();
                    int baseTimeout = (int)(maxResponseMs * 3);
                    int newTimeout = Math.Max(MinTimeoutMs, baseTimeout + (baseTimeout / 5)); // +20% buffer

                    // Only reduce, never increase beyond initial
                    if (newTimeout < _adaptiveTimeoutMs)
                    {
                        int oldTimeout = _adaptiveTimeoutMs;
                        _adaptiveTimeoutMs = newTimeout;
                        Logger.InfoNested($"Timeout: {oldTimeout}ms -> {newTimeout}ms (max RTT: {maxResponseMs}ms)");
                    }
                }
            }
        }

        /// <summary>
        /// Generates middle range ports (1433-49151) excluding known ports.
        /// Creates multiple ranges to avoid re-scanning known ports.
        /// </summary>
        private static int[] GenerateMiddleRangePorts()
        {
            var knownPortsSet = new HashSet<int>(KnownPorts);
            var middlePorts = new List<int>();
            
            for (int port = MiddleRangeStart; port <= MiddleRangeEnd; port++)
            {
                if (!knownPortsSet.Contains(port))
                {
                    middlePorts.Add(port);
                }
            }
            
            return middlePorts.ToArray();
        }

        /// <summary>
        /// Scans known ports only.
        /// </summary>
        public static List<ScanResult> ScanKnownPorts(IPAddress ip, int timeoutMs = DefaultTimeoutMs, int maxParallelism = DefaultParallelism)
        {
            return ScanPortsParallel(ip, KnownPorts, timeoutMs, maxParallelism, null);
        }

        /// <summary>
        /// Scans specific ports provided by the user.
        /// Examples: single port, range, or comma-separated list.
        /// </summary>
        public static List<ScanResult> ScanPorts(IPAddress ip, string hostname, int[] ports, int timeoutMs = DefaultTimeoutMs, int maxParallelism = DefaultParallelism)
        {
            var globalStopwatch = Stopwatch.StartNew();

            Logger.NewLine();
            Logger.InfoNested($"Testing ports: {FormatPortList(ports)}");
            
            // Apply edges-to-middle strategy for better discovery (especially for large ranges)
            if (ports.Length > 10)
            {
                Logger.InfoNested("Strategy: edges-to-middle for faster discovery");
                ports = ReorderEdgesToMiddle(ports);
            }

            var results = ScanPortsParallel(ip, ports, timeoutMs, maxParallelism, null);
            LogSummary(hostname, results, globalStopwatch);
            return results;
        }
        
        /// <summary>
        /// Reorders an array of ports to scan from edges toward middle.
        /// Preserves original port order but reorders for optimal discovery.
        /// </summary>
        private static int[] ReorderEdgesToMiddle(int[] ports)
        {
            var sorted = ports.OrderBy(p => p).ToArray();
            var reordered = new int[sorted.Length];
            int low = 0, high = sorted.Length - 1, i = 0;
            while (low <= high)
            {
                reordered[i++] = sorted[low++];
                if (low <= high)
                    reordered[i++] = sorted[high--];
            }
            return reordered;
        }

        /// <summary>
        /// Formats port list for display (collapses ranges).
        /// </summary>
        private static string FormatPortList(int[] ports)
        {
            if (ports.Length == 1)
                return ports[0].ToString();
            if (ports.Length <= 5)
                return string.Join(", ", ports);

            // Collapse to range if contiguous
            var sorted = ports.OrderBy(p => p).ToArray();
            if (sorted[sorted.Length - 1] - sorted[0] == sorted.Length - 1)
                return $"{sorted[0]}-{sorted[sorted.Length - 1]}";

            return $"{sorted[0]}, {sorted[1]}, ... ({ports.Length} ports)";
        }

        private static void LogSummary(string hostname, List<ScanResult> results, Stopwatch stopwatch, bool stoppedEarly = false)
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
                    Logger.SuccessNested($"{r.Port}");
                }
                if (stoppedEarly)
                {
                    Logger.InfoNested($"Stopped early after finding {results.Count} port(s) - use --all to continue full scan");
                }
            }
        }


    }
}
