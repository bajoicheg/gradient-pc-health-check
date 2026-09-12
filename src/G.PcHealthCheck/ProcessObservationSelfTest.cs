using System.Text.Json;

namespace G.PcHealthCheck;

internal static class ProcessObservationSelfTest
{
    internal static readonly DateTimeOffset Stamp = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    internal static readonly ProcessObservationTarget Target = new(42, Stamp, "Synthetic");
    internal static ProcessCounters Counters => new() { Pid = 42, CreatedFileTime = (ulong)Stamp.UtcDateTime.ToFileTimeUtc(), State = "Live", Kernel100ns = 10000000, User100ns = 10000000, WorkingSetBytes = 2 * 1048576, PrivateBytes = 3 * 1048576, ReadBytes = 1048576, WriteBytes = 2 * 1048576, LogicalProcessors = 4 };
    internal static ProcessCounterSample Sample(long offset, ProcessCounters? counters = null) => new(offset, Stamp.AddMilliseconds(offset), counters ?? Counters);
    public static int Run()
    {
        var n = 0; var failures = new List<string>();
        void Test(string name, Action action) { n++; try { action(); } catch (Exception ex) { failures.Add(name + ": " + ex.Message); } }
        Test("selected row becomes an immutable target", () =>
        {
            var row = new ProcessReviewEntry { Pid = 42, CreatedAt = Stamp, CreationKey = "20260101120000.000000+000", Name = "Synthetic" };
            var target = ProcessObservationCore.FromEntry(row); row.Pid = 43;
            Require(target.Pid == 42 && target.CreatedAt == Stamp && target.Name == "Synthetic", "Selection mutated.");
        });
        foreach (var row in new[] { new ProcessReviewEntry(), new ProcessReviewEntry { Pid = 42 }, new ProcessReviewEntry { Pid = 0, CreatedAt = Stamp } })
            Test("unknown identity cannot start", () => Throws<ArgumentException>(() => ProcessObservationCore.FromEntry(row)));
        Test("WMI microsecond identity matches native submicrosecond precision", () => Require(ProcessObservationCore.Matches(Target, Counters with { CreatedFileTime = Counters.CreatedFileTime + 9 }), "Native time precision mismatch."));
        Test("different native microsecond and PID rejected", () => Require(!ProcessObservationCore.Matches(Target, Counters with { CreatedFileTime = Counters.CreatedFileTime + 10 }) && !ProcessObservationCore.Matches(Target, Counters with { Pid = 43 }), "Wrong instance accepted."));
        Test("first rate unknown but memory measured", () =>
        {
            var r = ProcessObservationCore.Calculate(Target, null, Sample(1000), 1);
            Require(r.CpuPercent is null && r.ReadMiBps is null && r.WorkingSetMiB == 2 && r.PrivateCommitMiB == 3, "Warmup invented rates or lost memory.");
        });
        Test("CPU uses actual interval and total logical processors", () =>
        {
            var r = ProcessObservationCore.Calculate(Target, Sample(1000), Sample(3000, Counters with { User100ns = 30000000 }), 2);
            Require(r.CpuPercent == 25 && r.IntervalMs == 2000, "CPU formula wrong.");
        });
        Test("zero counters are valid zero rates", () => { var r = ProcessObservationCore.Calculate(Target, Sample(1000), Sample(2000), 1); Require(r.CpuPercent == 0 && r.ReadMiBps == 0 && r.WriteMiBps == 0, "Zero treated as missing."); });
        Test("I/O MiB per second uses byte delta", () =>
        {
            var r = ProcessObservationCore.Calculate(Target, Sample(1000), Sample(3000, Counters with { ReadBytes = 5 * 1048576, WriteBytes = 8 * 1048576 }), 2);
            Require(r.ReadMiBps == 2 && r.WriteMiBps == 3, "I/O rates wrong.");
        });
        foreach (var state in new[] { "Exited", "IdentityChanged", "Unavailable", "AccessDenied" })
            Test("non-live telemetry cannot supply measurements " + state, () =>
            { var r = ProcessObservationCore.Calculate(Target, Sample(1000), Sample(2000, Counters with { State = state }), 1); Require(r.CpuPercent is null && r.WorkingSetMiB is null && r.ReadMiBps is null, "Stale values leaked."); });
        Test("PID reuse stops deltas", () => Require(ProcessObservationCore.Calculate(Target, Sample(1000), Sample(2000, Counters with { CreatedFileTime = Counters.CreatedFileTime + 10 }), 1).CpuPercent is null, "Reused PID measured."));
        foreach (var offset in new long[] { 1000, 999, 10000 })
            Test("zero backward and long intervals are gaps", () => Require(ProcessObservationCore.Calculate(Target, Sample(1000), Sample(offset), 1).CpuPercent is null, "Invalid/gap interval measured."));
        Test("counter rollback does not underflow", () =>
        { var r = ProcessObservationCore.Calculate(Target, Sample(1000), Sample(2000, Counters with { User100ns = 0, ReadBytes = 0 }), 1); Require(r.CpuPercent is null && r.ReadMiBps is null && r.WriteMiBps == 0, "Rollback overflow or independent rate lost."); });
        Test("missing CPU and I/O do not hide memory", () =>
        { var r = ProcessObservationCore.Calculate(Target, Sample(1000), Sample(2000, Counters with { User100ns = null, ReadBytes = null }), 1); Require(r.CpuPercent is null && r.ReadMiBps is null && r.PrivateCommitMiB == 3, "Independent signals coupled."); });
        Test("processor count changes cannot fake CPU", () => Require(ProcessObservationCore.Calculate(Target, Sample(1000), Sample(2000, Counters with { LogicalProcessors = 8 }), 1).CpuPercent is null, "CPU denominator changed."));
        Test("unknown processor count not guessed", () => Require(ProcessObservationCore.Calculate(Target, Sample(1000), Sample(2000, Counters with { LogicalProcessors = 0 }), 1).CpuPercent is null, "CPU denominator guessed."));
        Test("implausible CPU is missing, not clamped", () => Require(ProcessObservationCore.Calculate(Target, Sample(1000), Sample(2000, Counters with { User100ns = ulong.MaxValue }), 1).CpuPercent is null, "Invalid CPU accepted."));
        Test("large counters subtract before floating conversion", () =>
        {
            var before = Counters with { ReadBytes = ulong.MaxValue - 1048576, User100ns = ulong.MaxValue - 10000000 };
            var after = before with { ReadBytes = ulong.MaxValue, User100ns = ulong.MaxValue };
            var r = ProcessObservationCore.Calculate(Target, Sample(1000, before), Sample(2000, after), 1);
            Require(r.ReadMiBps == 1 && r.CpuPercent == 25, "Large delta precision/overflow.");
        });
        Test("prior missing evidence resets rate baseline", () => Require(ProcessObservationCore.Calculate(Target, Sample(1000, Counters with { State = "Unavailable" }), Sample(2000), 1).CpuPercent is null, "Bridged missing evidence."));
        Test("shared scheduler preserves separate process and host timestamps", () =>
        {
            var clock = new Clock(); using var process = new FakeProcess(); using var system = new FakeSystem { Clock = clock, Delay = 100 };
            var s = Run(process, system, clock, 3);
            Require(s.Samples.Count == 3 && s.System.Samples.Count == 3 && s.Samples[0].Process.OffsetMs == 1000 && s.Samples[0].System.OffsetMs == 1100, "Timestamp alignment invented.");
            Require(s.Samples[1].Reading.CpuPercent == 25 && s.System.Outcome == "Completed", "Scheduler/rates wrong.");
        });
        Test("process exit is sticky and does not stop system evidence", () =>
        {
            var clock = new Clock(); using var process = new FakeProcess { ExitAt = 2 }; using var system = new FakeSystem(); var s = Run(process, system, clock, 4);
            Require(s.System.Samples.Count == 4 && process.Calls == 2 && s.Samples.Skip(1).All(x => x.Process.Counters.State == "Exited" && x.Reading.WorkingSetMiB is null), "Exited process rebound or system stopped.");
        });
        Test("missing system does not discard process evidence", () =>
        { var clock = new Clock(); using var p = new FakeProcess(); using var h = new FakeSystem { Throw = true }; var s = Run(p, h, clock, 2); Require(s.Samples[1].Reading.CpuPercent == 25 && s.System.Samples.All(x => x.Reading.CpuPercent is null), "System error hid process."); });
        Test("process exception stays a gap then recovers same source", () =>
        { var clock = new Clock(); using var p = new FakeProcess { ThrowAt = 2 }; using var h = new FakeSystem(); var s = Run(p, h, clock, 4); Require(s.Samples[1].Reading.CpuPercent is null && s.Samples[2].Reading.CpuPercent is null && s.Samples[3].Reading.CpuPercent == 25, "Missing interval bridged."); });
        Test("pre-cancel performs no provider read", () =>
        { using var c = new CancellationTokenSource(); c.Cancel(); using var p = new FakeProcess(); using var h = new FakeSystem(); var s = Run(p, h, new Clock(), 2, c.Token); Require(s.System.Outcome == "Stopped" && p.Calls == 0 && s.Samples.Count == 0, "Cancelled read started."); });
        Test("cancel during system read discards unfinished pair", () =>
        { using var c = new CancellationTokenSource(); using var p = new FakeProcess(); using var h = new FakeSystem { Cancel = c }; var s = Run(p, h, new Clock(), 2, c.Token); Require(s.Samples.Count == 0 && s.System.Samples.Count == 0 && s.System.Outcome == "Stopped", "Half sample committed."); });
        Test("chart segments break at missing values", () =>
        { var clock = new Clock(); using var p = new FakeProcess { ThrowAt = 2 }; using var h = new FakeSystem(); var s = Run(p, h, clock, 4); Require(ProcessObservationCore.Segments(s, ProcessMetric.WorkingSet).Count == 2, "Chart bridged missing sample."); });
        Test("HTML JSON and summary preserve target context and all metrics", () =>
        {
            using var p = new FakeProcess(); using var h = new FakeSystem(); var s = Run(p, h, new Clock(), 3); s.Target = Target with { Name = "<script>bad</script>" }; s.System.Markers.Add(new(1500, "Симптом <x>"));
            var html = ProcessObservationReport.Html(s); Require(!html.Contains("<script>") && html.Contains("&lt;script&gt;") && html.Contains("MiB") && html.Contains("Симптом"), "Report encoding/metrics missing.");
            using var json = JsonDocument.Parse(ProcessObservationReport.Json(s)); Require(json.RootElement.GetProperty("Samples").GetArrayLength() == 3 && json.RootElement.GetProperty("System").GetProperty("Markers").GetArrayLength() == 1, "Report lost full samples.");
            Require(ProcessObservationReport.Summary(s).Contains("SYNTHETIC") && ProcessObservationReport.Summary(s).Contains("42"), "Summary lost context/identity.");
        });
        Console.WriteLine($"Process observation behavior: {n - failures.Count}/{n} passed."); foreach (var f in failures) Console.Error.WriteLine("FAIL: " + f); return failures.Count == 0 ? 0 : 247;
    }
    private static ProcessObservationSnapshot Run(FakeProcess p, FakeSystem h, Clock c, int duration, CancellationToken ct = default)
        => ProcessObservationService.RunAsync(Target, new(duration, 1), p, h, c, new ExecutionContextInfo { ProcessAccount = "SYNTHETIC\\user" }, null, ct).GetAwaiter().GetResult();
    internal static void Require(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    internal sealed class Clock : IPerformanceSessionClock
    {
        public long ElapsedMs { get; set; }
        public DateTimeOffset Now => Stamp.AddMilliseconds(ElapsedMs);
        public Task DelayAsync(int milliseconds, CancellationToken ct) { ct.ThrowIfCancellationRequested(); ElapsedMs += milliseconds; return Task.CompletedTask; }
    }
    private sealed class FakeProcess : IProcessObservationSource
    {
        public int Calls { get; private set; } public int ExitAt { get; init; } public int ThrowAt { get; init; }
        public ProcessCounters Read(CancellationToken ct) { Calls++; if (Calls == ThrowAt) throw new IOException("Synthetic"); return Counters with { State = Calls == ExitAt ? "Exited" : "Live", User100ns = (ulong)Calls * 10000000 }; }
        public void Dispose() { }
    }
    private sealed class FakeSystem : IPerformanceSessionSource
    {
        public Clock? Clock { get; init; } public int Delay { get; init; } public bool Throw { get; init; } public CancellationTokenSource? Cancel { get; init; }
        public PerformanceReading Read(CancellationToken ct) { Cancel?.Cancel(); if (Throw) throw new IOException(); if (Clock is not null) Clock.ElapsedMs += Delay; return new(10, 50, 20, 1, []); }
        public void Dispose() { }
    }
}
