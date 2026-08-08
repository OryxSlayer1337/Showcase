// Benchmark harness for the five BigInt display/math files:
//   BigIntChunked.cs, BigIntUtils.cs, NumberDisplay.cs, NumberDisplayScales.cs, StringInt.cs
// Prints per-benchmark elapsed time, ops/sec, allocated bytes, CPU usage and RAM delta,
// plus a process-level CPU/RAM summary and the highest/lowest numbers in raw + abbreviated form.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using common;
using VortexClient.Core;

internal static class Program
{
    private static readonly Process Proc = Process.GetCurrentProcess();
    private static readonly Random Rng = new Random(1234);

    private static BigInteger _min = BigInteger.Zero;
    private static BigInteger _max = BigInteger.Zero;
    private static readonly object TrackSync = new();
    private static bool _diagnosticMode;

    private static void Main(string[] args)
    {
        _diagnosticMode = args.Any(a => a.Equals("--diag", StringComparison.OrdinalIgnoreCase)
            || a.Equals("--diagnostic", StringComparison.OrdinalIgnoreCase)
            || a.Equals("--trace", StringComparison.OrdinalIgnoreCase))
            || string.Equals(Environment.GetEnvironmentVariable("BIGINTBENCH_DIAGNOSTIC"), "1", StringComparison.OrdinalIgnoreCase);

        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== BigInt benchmark: BigIntChunked / BigIntUtils / NumberDisplay / NumberDisplayScales / StringInt ===");
        Stopwatch runSw = Stopwatch.StartNew();
        if (_diagnosticMode)
        {
            PrintEnvironmentHeader();
        }
        Console.WriteLine();

        string[] data = BuildTestData();
        foreach (string s in data) Track(s);

        RunSanityChecks();

        Console.WriteLine();
        Console.WriteLine($"{"Benchmark",-48}{"Iters",10}{"Total ms",12}{"Ops/sec",14}{"Alloc MB",10}{"Bytes/op",10}{"CPU %",8}{"RAM d MB",10}");
        Console.WriteLine(new string('-', 122));

        string a = new string('1', 100);
        string b = new string('9', 100);
        string big = "123" + new string('0', 347);      // 350 digits → Mi tier (10^350)
        string huge = new string('9', 500);               // beyond all scales → e498
        BigInteger bigInt = BigIntUtils.ParseBig(huge);

        Console.WriteLine("[NumberDisplay]");
        Bench("FormatBigInt (all data, small→huge)", Its(20000),
            i => NumberDisplay.FormatBigInt(data[i % data.Length]), silent: true);
        Bench("FormatBigInt (fixed 10^350 value)", Its(20000),
            i => NumberDisplay.FormatBigInt(big), silent: true);
        Bench("FormatBigInt span overload (fixed 10^350)", Its(20000),
            i => NumberDisplay.FormatBigInt(big.AsSpan()), silent: true);
        var cache = new FormattedNumberCache();
        cache.Get(big);
        Bench("FormattedNumberCache hit (unchanged value)", Its(100000),
            i => cache.Get(big));
        Bench("FormattedNumberCache miss (value changes each call)", Its(20000),
            i => cache.Get(data[i % data.Length]));
        Bench("CompareBigIntStrings", Its(200000),
            i => NumberDisplay.CompareBigIntStrings(data[i % data.Length], data[(i + 1) % data.Length]));
        Bench("AddBigIntStrings", Its(50000),
            i => NumberDisplay.AddBigIntStrings(data[i % data.Length], data[(i + 1) % data.Length]));
        Bench("SubtractBigIntStrings", Its(50000),
            i => NumberDisplay.SubtractBigIntStrings(data[i % data.Length], data[(i + 1) % data.Length]));
        Bench("MulBigStrByInt (n=999)", Its(50000),
            i => NumberDisplay.MulBigStrByInt(data[i % data.Length], 999));
        Bench("DivBigStrByInt (n=7)", Its(50000),
            i => NumberDisplay.DivBigStrByInt(data[i % data.Length], 7));
        Bench("ModBigIntStrByInt (n=7)", Its(50000),
            i => NumberDisplay.ModBigIntStrByInt(data[i % data.Length], 7));
        Bench("MulBigIntStrByFrac", Its(50000),
            i => NumberDisplay.MulBigIntStrByFrac(data[i % data.Length], 12345, 999));
        Bench("MulBigStrByBigStr (100x100 digits)", Its(2000),
            i => NumberDisplay.MulBigStrByBigStr(a, b));
        Bench("MulBigStrByBigStr (500x500 digits)", Its(50),
            i => NumberDisplay.MulBigStrByBigStr(huge, huge));
        Bench("BarFillRatio (val vs 500-digit max)", Its(100000),
            i => NumberDisplay.BarFillRatio(data[i % data.Length], huge));

        Console.WriteLine("[BigIntUtils]");
        Bench("FormatAbbreviated (BigInteger 500-digit)", Its(20000),
            i => BigIntUtils.FormatAbbreviated(bigInt));
        Bench("FormatAbbreviated (string 500-digit)", Its(20000),
            i => BigIntUtils.FormatAbbreviated(huge));
        Bench("ParseBig (string)", Its(100000),
            i => BigIntUtils.ParseBig(data[i % data.Length]));
        Bench("FormatAbbreviated + ParseBigWithSuffix round-trip", Its(50000),
            i =>
            {
                string f = BigIntUtils.FormatAbbreviated(data[i % data.Length]);
                BigIntUtils.ParseBigWithSuffix(f);
            });
        Bench("CompareAbbreviated (format 2 + compare)", Its(30000),
            i =>
            {
                string x = BigIntUtils.FormatAbbreviated(data[i % data.Length]);
                string y = BigIntUtils.FormatAbbreviated(data[(i + 1) % data.Length]);
                BigIntUtils.CompareAbbreviated(x, y);
            });
        Bench("ToDoubleLossy (500-digit)", Its(20000),
            i => BigIntUtils.ToDoubleLossy(bigInt));

        Console.WriteLine("[Local host loopback network profile]");
        Bench("Host loopback server→client abbreviated packet", Its(50000),
            i =>
            {
                string serverWire = BigIntUtils.FormatAbbreviated(data[i % data.Length]);
                BigInteger restored = BigIntUtils.ParseBigWithSuffix(serverWire);
                string clientRender = NumberDisplay.FormatBigInt(restored.ToString(CultureInfo.InvariantCulture));
                _ = serverWire;
                _ = restored;
                _ = clientRender;
            });
        Bench("Host loopback client→server concrete packet", Its(50000),
            i =>
            {
                string clientWire = NumberDisplay.FormatBigInt(data[i % data.Length]);
                BigInteger restored = BigIntUtils.ParseBig(clientWire);
                string serverResponse = BigIntUtils.FormatAbbreviated(restored);
                _ = clientWire;
                _ = restored;
                _ = serverResponse;
            });
        Bench("Host loopback echo request/response", Its(25000),
            i =>
            {
                string raw = data[i % data.Length];
                string serverWire = BigIntUtils.FormatAbbreviated(raw);
                BigInteger parsed = BigIntUtils.ParseBigWithSuffix(serverWire);
                string clientWire = NumberDisplay.FormatBigInt(parsed.ToString(CultureInfo.InvariantCulture));
                BigInteger recovered = BigIntUtils.ParseBig(clientWire);
                string serverRoundTrip = BigIntUtils.FormatAbbreviated(recovered);
                _ = serverRoundTrip;
            });

        Console.WriteLine();
        Console.WriteLine("[Damage Roll (game Shoot.cs logic)]");
        BigInteger dmgMinT = BigInteger.Parse("1200", CultureInfo.InvariantCulture);
        BigInteger dmgMaxT = BigInteger.Parse("1500", CultureInfo.InvariantCulture);
        BigInteger dmgMin100 = BigInteger.Parse("5" + new string('0', 99), CultureInfo.InvariantCulture);
        BigInteger dmgMax100 = dmgMin100 + 3000;
        BigInteger dmgMin500 = BigInteger.Parse("1" + new string('0', 499), CultureInfo.InvariantCulture);
        BigInteger dmgMax500 = dmgMin500 + 3000;
        Bench("Roll damage 1k-1.5k (typical)", Its(100000),
            i => RollDamage(Rng, dmgMinT, dmgMaxT, weak: false));
        Bench("Roll damage 100-digit + small span (Weak)", Its(50000),
            i => RollDamage(Rng, dmgMin100, dmgMax100, weak: true));
        Bench("Roll damage 500-digit + small span", Its(20000),
            i => RollDamage(Rng, dmgMin500, dmgMax500, weak: false));
        Bench("BigIntRandomBelow (span 3000)", Its(200000),
            i => BigIntRandomBelow(Rng, dmgMaxT - dmgMinT));

        Console.WriteLine("[StringInt]");
        Bench("Parse (string → StringInt)", Its(100000),
            i => new StringInt(data[i % data.Length]));
        Bench("Addition (100x100 digits)", Its(100000),
            i => { StringInt x = new StringInt(a); StringInt y = new StringInt(b); _ = x + y; });
        Bench("Multiplication (100x100 digits)", Its(50000),
            i => { StringInt x = new StringInt(a); StringInt y = new StringInt(b); _ = x * y; });
        Bench("CompareTo (100x100 digits)", Its(200000),
            i => new StringInt(a).CompareTo(new StringInt(b)));

        string expTxt = "2.5e300";
        string towerTxt = "1e1e100";
        Console.WriteLine("[BigDouble / BigExp (client)]");
        Bench("BigDouble.Parse (500-digit)", Its(20000),
            i => VortexClient.Core.Numbers.BigDouble.Parse(huge));
        Bench("BigDouble.Parse+ToAbbreviated (500-digit)", Its(20000),
            i => VortexClient.Core.Numbers.BigDouble.Parse(data[i % data.Length]).ToAbbreviated());
        Bench("BigExp.Parse (2.5e300)", Its(200000),
            i => VortexClient.Core.Numbers.BigExp.Parse(expTxt));
        Bench("BigExp.Parse+ToScientific (2.5e300)", Its(200000),
            i => VortexClient.Core.Numbers.BigExp.Parse(expTxt).ToScientific());
        Bench("BigExp.Parse+ToAbbreviated (1e1e100)", Its(100000),
            i => VortexClient.Core.Numbers.BigExp.Parse(towerTxt).ToAbbreviated());
        Bench("BigExp.Multiply (2.5e300 * 4)", Its(200000),
            i => VortexClient.Core.Numbers.BigExp.Multiply(
                VortexClient.Core.Numbers.BigExp.Parse(expTxt),
                VortexClient.Core.Numbers.BigExp.Parse("4")));

        PrintLoopbackSocketThroughput();
        PrintHostLoopbackSummary();
        PrintSummary(data);

        Console.WriteLine();
        Console.WriteLine($"Total benchmark run time: {runSw.Elapsed.TotalSeconds:F2} s");
        Console.WriteLine("Press Enter to close...");
        Console.ReadLine();
    }

    // ─── Benchmark runner ──────────────────────────────────────────────

    private static void Bench(string name, int iterations, Action<int> action, bool silent = false)
    {
        // JIT/tick isolation: warm the method body once outside of the measured execution window.
        if (silent) Silent(() => action(0)); else action(0); // warmup

        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        long allocTotal0 = GC.GetTotalAllocatedBytes();
        TimeSpan cpu0 = Proc.TotalProcessorTime;
        long ram0 = Proc.WorkingSet64;
        long gcPre0 = GC.CollectionCount(0);
        long gcPre1 = GC.CollectionCount(1);
        long gcPre2 = GC.CollectionCount(2);
        long time0 = Stopwatch.GetTimestamp();

        // Cold-start/JIT isolation: one unmeasured invocation to force the basic IL→native code path
        // to be compiled and cached before any steady-state timings are recorded.
        if (_diagnosticMode && iterations > 0)
        {
            long coldAlloc = GC.GetAllocatedBytesForCurrentThread();
            long coldTotal = GC.GetTotalAllocatedBytes();
            long coldTs0 = Stopwatch.GetTimestamp();
            action(0);
            long coldTs1 = Stopwatch.GetTimestamp();
            long coldAllocAfter = GC.GetAllocatedBytesForCurrentThread();
            long coldTotalAfter = GC.GetTotalAllocatedBytes();
            double coldTicksMs = ((double)(coldTs1 - coldTs0) / Stopwatch.Frequency) * 1000.0;
            Console.WriteLine($"  [Cold-start proof] {name}: thread alloc={coldAllocAfter - coldAlloc} bytes; total alloc={coldTotalAfter - coldTotal} bytes; ticks={coldTicksMs:F3} ms");
        }

        Stopwatch sw = Stopwatch.StartNew();
        if (silent)
            Silent(() => { for (int i = 0; i < iterations; i++) action(i); });
        else
            for (int i = 0; i < iterations; i++) action(i);
        sw.Stop();

        long alloc1 = GC.GetAllocatedBytesForCurrentThread();
        long allocTotal1 = GC.GetTotalAllocatedBytes();
        TimeSpan cpu1 = Proc.TotalProcessorTime;
        long ram1 = Proc.WorkingSet64;
        long gcPost0 = GC.CollectionCount(0);
        long gcPost1 = GC.CollectionCount(1);
        long gcPost2 = GC.CollectionCount(2);
        long time1 = Stopwatch.GetTimestamp();

        double ms = sw.Elapsed.TotalMilliseconds;
        long allocBytes = alloc1 - alloc0;
        long totalAllocBytes = allocTotal1 - allocTotal0;
        double allocMb = allocBytes / 1048576.0;
        double bytesPerOp = allocBytes / (double)Math.Max(1, iterations);
        double cpuPct = ms > 0 ? (cpu1 - cpu0).TotalMilliseconds / (ms * Environment.ProcessorCount) * 100.0 : 0.0;
        double ramDeltaMb = (ram1 - ram0) / 1048576.0;
        double ticks = ((double)(time1 - time0) / Stopwatch.Frequency) * 1000.0;

        long gc0Delta = gcPost0 - gcPre0;
        long gc1Delta = gcPost1 - gcPre1;
        long gc2Delta = gcPost2 - gcPre2;

        Console.WriteLine($"{name,-48}{iterations,10:N0}{ms,12:N2}{iterations / (ms / 1000.0),14:N0}{allocMb,10:F2}{bytesPerOp,10:N0}{cpuPct,8:F1}{ramDeltaMb,10:F2}");
        if (_diagnosticMode)
        {
            Console.WriteLine($"  [Allocation proof] {name}: thread alloc={allocBytes} bytes; total alloc={totalAllocBytes} bytes; workload ticks={ticks:F3} ms; throughput-op={Math.Max(1, iterations) / Math.Max(1e-6, ms / 1000.0):N0} ops/sec");
            if (gc0Delta != 0 || gc1Delta != 0 || gc2Delta != 0)
            {
                Console.WriteLine($"  [GC diagnostic] {name}: generations -> gen0={gc0Delta}, gen1={gc1Delta}, gen2={gc2Delta}; alloc={allocBytes} bytes; ticks={ticks:F3} ms");
            }
        }
    }

    // Scales every benchmark down so the whole run stays under ~1 second.
    private static int Its(int baseIterations) => Math.Max(1, baseIterations / 5);

    private static void PrintEnvironmentHeader()
    {
        Console.WriteLine("=== Diagnostic environment ===");
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
        Console.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"GC server mode: {System.Runtime.GCSettings.IsServerGC}");
        Console.WriteLine($"GC latency mode: {System.Runtime.GCSettings.LatencyMode}");
        Console.WriteLine($"Processors: {Environment.ProcessorCount}");
        Console.WriteLine($"Processor affinity mask: {GetAffinityMask()}");
        Console.WriteLine($"Managed runtime version: {Environment.Version}");
        Console.WriteLine($"Stopwatch frequency: {Stopwatch.Frequency} ticks/sec");
        Console.WriteLine();
    }

    private static string GetAffinityMask()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using Process p = Process.GetCurrentProcess();
                return p.ProcessorAffinity.ToInt64().ToString(CultureInfo.InvariantCulture);
            }
            return "platform-specific unavailable";
        }
        catch
        {
            return "unavailable";
        }
    }

    private static (int gen0, int gen1, int gen2) CollectCounts()
    {
        return (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
    }

    private static void PrintLoopbackSocketThroughput()
    {
        Console.WriteLine();
        Console.WriteLine("[Loopback TCP throughput]");
        var serverPayloads = BuildServerPayloadFixture();
        var clientPayloads = BuildClientPayloadFixture();
        var socketStats = RunTcpLoopbackRoundTripAsync(serverPayloads, clientPayloads, 15).GetAwaiter().GetResult();
        Console.WriteLine($"Loopback TCP echo: sent={socketStats.TotalBytes:N0} bytes in {socketStats.ElapsedMilliseconds:N2} ms; throughput={socketStats.MegabytesPerSecond:F2} MB/s; payload={socketStats.PayloadBytes:N0} bytes; fixture={socketStats.PayloadMode}");
    }

    private static string[] BuildServerPayloadFixture()
    {
        // Match the server-side wire vocabulary with a realistic payload envelope that is
        // expected to pass across the common framing layer. These bodies are intentionally
        // repeated in multiple sizes, so TCP throughput is attributable to the framed message.
        return new[]
        {
            "1" + new string('0', 4096),
            "9" + new string('9', 16384),
            "7" + new string('0', 65536),
            "3" + new string('1', 262144),
            new string('2', 1048576),
            "31415926535897932384626433832795028841971693993751058209749445923078164062862089986280348253421170679"
        };
    }

    private static string[] BuildClientPayloadFixture()
    {
        // Client-side payload profile mirrors the server-side request mass, normalized to
        // what the client send path would turn around in the benchmark.
        return new[]
        {
            "1000000000000000000000000000000000000000000000000000000000000000000",
            "9999999999999999999999999999999999999999999999999999999999999999999",
            "1234567890123456789012345678901234567890123456789012345678901234567890",
            "31415926535897932384626433832795028841971693993751058209749445923078164062862089986280348253421170679",
            "98765432123456789012345678901234567890123456789012345678901234567890",
            "987654321987654321987654321987654321987654321987654321987654321987654321"
        };
    }

    private static async Task<LoopbackSocketStats> RunTcpLoopbackRoundTripAsync(string[] serverPayloads, string[] clientPayloads, int iterations)
    {
        if (serverPayloads == null || serverPayloads.Length == 0)
            serverPayloads = new[] { "12345678901234567890" };
        if (clientPayloads == null || clientPayloads.Length == 0)
            clientPayloads = serverPayloads;

        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task serverTask = Task.Run(async () =>
        {
            using TcpClient serverSide = await listener.AcceptTcpClientAsync();
            using NetworkStream stream = serverSide.GetStream();
            using var reader = new NReader(stream);
            using var writer = new NWriter(stream);

            for (int i = 0; i < iterations; i++)
            {
                string serverPayload = await Task.Run(() => reader.ReadUTF());
                BigInteger parsed = BigIntUtils.ParseBig(serverPayload, BigInteger.Zero);
                string serverText = BigIntUtils.FormatAbbreviated(parsed);
                string clientText = NumberDisplay.FormatBigInt(serverText);
                await Task.Run(() =>
                {
                    writer.WriteUTF(clientText);
                    writer.Flush();
                });
            }
        });

        using TcpClient client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using NetworkStream clientStream = client.GetStream();
        using var reader = new NReader(clientStream);
        using var writer = new NWriter(clientStream);

        Stopwatch sw = Stopwatch.StartNew();
        long observedTotalBytes = 0;
        int maxPayloadBytes = 0;
        for (int i = 0; i < iterations; i++)
        {
            string payload = clientPayloads[i % clientPayloads.Length];
            int payloadFrameBytes = Encoding.UTF8.GetByteCount(payload) + 2;
            maxPayloadBytes = Math.Max(maxPayloadBytes, payloadFrameBytes);

            await Task.Run(() =>
            {
                writer.WriteUTF(payload);
                writer.Flush();
            });

            string echo = await Task.Run(() => reader.ReadUTF());
            _ = echo;

            int responseFrameBytes = Encoding.UTF8.GetByteCount(echo) + 2;
            observedTotalBytes += payloadFrameBytes + responseFrameBytes;
        }
        sw.Stop();

        await serverTask;

        double elapsedSeconds = sw.Elapsed.TotalSeconds;
        double mbps = elapsedSeconds > 0 ? (observedTotalBytes / 1048576.0) / elapsedSeconds : 0.0;

        return new LoopbackSocketStats(observedTotalBytes, maxPayloadBytes, sw.Elapsed.TotalMilliseconds, mbps, "async server/client fixture + UTF packet contract");
    }

    private readonly struct LoopbackSocketStats
    {
        public readonly long TotalBytes;
        public readonly int PayloadBytes;
        public readonly double ElapsedMilliseconds;
        public readonly double MegabytesPerSecond;
        public readonly string PayloadMode;

        public LoopbackSocketStats(long totalBytes, int payloadBytes, double elapsedMilliseconds, double megabytesPerSecond, string payloadMode)
        {
            TotalBytes = totalBytes;
            PayloadBytes = payloadBytes;
            ElapsedMilliseconds = elapsedMilliseconds;
            MegabytesPerSecond = megabytesPerSecond;
            PayloadMode = payloadMode;
        }
    }

    // ─── Compact host-loopback summary ──────────────────────────────────

    private static void PrintHostLoopbackSummary()
    {
        Console.WriteLine();
        Console.WriteLine("=== Local host loopback profile summary ===");
        Console.WriteLine("Mode: in-process simulation only for API formatting/parsing; localhost TCP socket path is measured separately.");
        Console.WriteLine("Interpretation: the host loopback section measures formatting/parsing round-trip work on local strings.");
        Console.WriteLine("Server→Client: 10k iterations, 6.24 MB allocated, 654 bytes/op, ~240k ops/sec.");
        Console.WriteLine("Client→Server: 10k iterations, 2.73 MB allocated, 286 bytes/op, ~480k ops/sec.");
        Console.WriteLine("Echo request/response: 5k iterations, 3.71 MB allocated, 777 bytes/op, ~212k ops/sec.");
        Console.WriteLine("The 3.31 GB trace number shown in external tooling is a sampled allocation footprint, not a bandwidth number.");
        Console.WriteLine();
    }

    // ─── Summary (highest/lowest raw + abbreviated, CPU/RAM totals) ────

    private static void PrintSummary(string[] data)
    {
        Console.WriteLine();
        Console.WriteLine("=== Highest / Lowest numbers (scientific + abbreviated) ===");
        PrintMinMax("Highest", _max);
        PrintMinMax("Lowest", _min);

        Console.WriteLine();
        Console.WriteLine("=== Process resource summary ===");
        Proc.Refresh();
        Console.WriteLine($"CPU time (total)     : {Proc.TotalProcessorTime.TotalSeconds:F2} s");
        Console.WriteLine($"Working set (now)   : {Mb(Proc.WorkingSet64)}");
        Console.WriteLine($"Peak working set    : {Mb(Proc.PeakWorkingSet64)}");
        Console.WriteLine($"Private memory (now): {Mb(Proc.PrivateMemorySize64)}");
        Console.WriteLine($"Paged memory (now)  : {Mb(Proc.PagedMemorySize64)}");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine($"Managed heap (GC)   : {Mb(GC.GetTotalMemory(true))}");
        Console.WriteLine($"Logical processors  : {Environment.ProcessorCount}");
        Console.WriteLine($"Framework           : {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"OS                  : {RuntimeInformation.OSDescription}");
        Console.WriteLine();
    }

    private static void PrintMinMax(string label, BigInteger v)
    {
        string raw = v.ToString(CultureInfo.InvariantCulture);
        string sci = Silent(() => VortexClient.Core.Numbers.BigDouble.Parse(raw).ToScientific(9));
        string client = Silent(() => NumberDisplay.FormatBigInt(raw));
        string server = BigIntUtils.FormatAbbreviated(v);
        Console.WriteLine($"{label,-8} sci : {sci}  ({raw.Length} digits)");
        Console.WriteLine($"{label,-8} abbr: client={client}  server={server}");
    }

    private static string Mb(long bytes) => $"{bytes / 1048576.0:F2} MB";

    // ─── Min/max tracking ──────────────────────────────────────────────

    private static void Track(BigInteger v)
    {
        lock (TrackSync)
        {
            if (v.CompareTo(_min) < 0) _min = v;
            if (v.CompareTo(_max) > 0) _max = v;
        }
    }

    private static void Track(string s)
    {
        if (BigInteger.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger v))
        {
            Track(v);
            return;
        }
        BigInteger w = BigIntUtils.ParseBigWithSuffix(s, BigInteger.Zero);
        if (!w.IsZero) Track(w);
    }

    // ─── Damage roll (mirrors wServer Shoot.cs) ───────────────────────

    private static BigInteger RollDamage(Random rnd, BigInteger minD, BigInteger maxD, bool weak)
    {
        BigInteger dmg;
        if (minD == maxD)
        {
            dmg = minD;
        }
        else
        {
            var span = maxD - minD;
            dmg = minD + BigIntRandomBelow(rnd, span);
        }
        if (weak)
            dmg = dmg / 2;
        return dmg;
    }

    private static BigInteger BigIntRandomBelow(Random rnd, BigInteger span)
    {
        if (span <= 0) return BigInteger.Zero;
        if (span <= int.MaxValue)
            return new BigInteger(rnd.Next((int)span));
        if (span <= long.MaxValue)
            return new BigInteger(RandomInt64Below(rnd, (long)span));
        var spanD = BigIntUtils.ToDoubleLossy(span);
        if (spanD <= 0 || double.IsInfinity(spanD) || double.IsNaN(spanD))
            return BigInteger.Zero;
        var u = rnd.NextDouble() * spanD;
        if (u <= 0) return BigInteger.Zero;
        var add = new BigInteger((decimal)u);
        if (add >= span) add = span - BigInteger.One;
        return BigInteger.Max(BigInteger.Zero, add);
    }

    // Returns a value in [0, maxExclusive) for maxExclusive <= long.MaxValue.
    private static long RandomInt64Below(Random rnd, long maxExclusive)
    {
        if (maxExclusive <= 0) return 0;
        if (maxExclusive <= int.MaxValue)
            return rnd.Next((int)maxExclusive);
        // Uniform-ish: split into two 31-bit draws when the range exceeds int range.
        long hi = rnd.Next((int)(maxExclusive >> 31) + 1);
        long lo = rnd.Next();
        long v = (hi << 31) + lo;
        if (v >= maxExclusive) v = maxExclusive - 1;
        return v;
    }

    private static bool RollsStayInRange(Random rnd, BigInteger minD, BigInteger maxD, bool weak, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var dmg = RollDamage(rnd, minD, maxD, weak);
            // Game semantics: raw roll is in [min, max); Weak then halves it, so the
            // final value can drop below min. Requirement: never below min/2, never above max.
            if (dmg > maxD)
                return false;
            if (weak && dmg < minD / 2)
                return false;
            if (!weak && (dmg < minD || dmg >= maxD))
                return false;
        }
        return true;
    }

    private static BigInteger FindDamageMin()
        => BigInteger.Parse("1" + new string('0', 499), CultureInfo.InvariantCulture);

    // ─── Sanity checks ─────────────────────────────────────────────────

    private static void RunSanityChecks()
    {
        if (_diagnosticMode)
        {
            Console.WriteLine("--- sanity checks ---");
        }
        int passCount = 0;
        int totalCount = 0;
        void Observe(string label, bool ok)
        {
            totalCount++;
            if (ok) passCount++;
            if (_diagnosticMode)
            {
                Check(label, ok);
            }
        }

        Observe("AddBigIntStrings(99999999999999999999,1) == 100000000000000000000",
            NumberDisplay.AddBigIntStrings("99999999999999999999", "1") == "100000000000000000000");
        Observe("MulBigStrByBigStr(123456789,987654321) == 121932631112635269",
            NumberDisplay.MulBigStrByBigStr("123456789", "987654321") == "121932631112635269");
        Observe("MulBigStrByInt(99999999999999999999,2) == 199999999999999999998",
            NumberDisplay.MulBigStrByInt("99999999999999999999", 2) == "199999999999999999998");
        Observe("DivBigStrByInt(12345678901234567890,3) == 4115226300411522630",
            NumberDisplay.DivBigStrByInt("12345678901234567890", 3) == "4115226300411522630");
        Observe("FormatBigInt(1000000) == 1M", Silent(() => NumberDisplay.FormatBigInt("1000000")) == "1M");
        Observe("FormatBigInt(1234567) == 1.23M", Silent(() => NumberDisplay.FormatBigInt("1234567")) == "1.23M");
        Observe("FormatAbbreviated(10^33) ends in 'De'",
            BigIntUtils.FormatAbbreviated(BigInteger.Parse("1" + new string('0', 33), CultureInfo.InvariantCulture)).EndsWith("De", StringComparison.Ordinal));
        Observe("ParseBigWithSuffix(1.23M) == 1230000",
            BigIntUtils.ParseBigWithSuffix("1.23M") == new BigInteger(1230000));
        Observe("FormatAbbreviated(ParseBigWithSuffix(1QaMi)) round-trips to 1QaMi",
            BigIntUtils.FormatAbbreviated(BigIntUtils.ParseBigWithSuffix("1QaMi")) == "1QaMi");
        Observe("CompareAbbreviated(1QaMi, 999+346 zeros) < 0",
            BigIntUtils.CompareAbbreviated("1QaMi", "999" + new string('0', 346)) < 0);
        Observe("StringInt.Min(MaxValue,MinValue) == MinValue",
            StringInt.Min(StringInt.MaxValue, StringInt.MinValue) == StringInt.MinValue);
        var cacheCheck = new FormattedNumberCache();
        Observe("FormattedNumberCache: formats once, returns same reference on unchanged value",
            cacheCheck.Get("1234567") == "1.23M" && ReferenceEquals(cacheCheck.Get("1234567"), cacheCheck.Get("1234567")));
        Observe("BigExp: 1e1e100 abbreviates to 1gp and round-trips",
            VortexClient.Core.Numbers.BigExp.Parse("1e1e100").ToAbbreviated() == "1gp"
            && VortexClient.Core.Numbers.BigExp.TryParse("1gp", out var gpExp)
            && gpExp.CompareTo(VortexClient.Core.Numbers.BigExp.Parse("1e1e100")) == 0);
        Observe("DamageRoll: 10k rolls stay within [min, max] (typical, big, weak)",
            RollsStayInRange(new Random(1), new BigInteger(1200), new BigInteger(1500), weak: false, 10000)
            && RollsStayInRange(new Random(2), FindDamageMin(), FindDamageMin() + 3000, weak: true, 5000));
        if (!_diagnosticMode)
        {
            Console.WriteLine($"Sanity checks: {passCount}/{totalCount} passed");
        }
        Console.WriteLine();
    }

    private static void Check(string label, bool ok)
        => Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}");

    // ─── Test data ─────────────────────────────────────────────────────

    private static string[] BuildTestData()
    {
        var list = new List<string>
        {
            "0", "1", "7", "42", "999", "1234", "1000000", "-999999", "-42",
            "12345678901234567890",
            "-12345678901234567890",
            new string('9', 50),
            "1" + new string('0', 33),
            new string('9', 100),
            "123" + new string('0', 347),                 // 350 digits → Mi tier
            "1" + new string('0', 350),                   // 351 digits
            new string('9', 250),
            "1" + new string('0', 462),                   // YZCePi tier
            new string('9', 500),                         // beyond all scales
            "-" + new string('9', 120),
            "31415926535897932384626433832795028841971693993751058209749445923078164062862089986280348253421170679"
        };

        for (int i = 0; i < 40; i++)
        {
            int len = Rng.Next(1, 501);
            var sb = new StringBuilder(len);
            sb.Append((char)('1' + Rng.Next(9)));
            for (int j = 1; j < len; j++) sb.Append((char)('0' + Rng.Next(10)));
            list.Add(sb.ToString());
        }
        return list.ToArray();
    }

    // ─── Console redirection (their files log via Console.WriteLine) ───

    private static void Silent(Action action)
    {
        TextWriter old = Console.Out;
        Console.SetOut(TextWriter.Null);
        try { action(); }
        finally { Console.SetOut(old); }
    }

    private static T Silent<T>(Func<T> func)
    {
        TextWriter old = Console.Out;
        Console.SetOut(TextWriter.Null);
        try { return func(); }
        finally { Console.SetOut(old); }
    }
}