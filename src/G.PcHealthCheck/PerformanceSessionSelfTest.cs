using System.Text.Json;

namespace G.PcHealthCheck;

internal static class PerformanceSessionSelfTest
{
    public static int Run()
    {
        var failures = new List<string>(); var count = 0;
        void Test(string name, Action test) { count++; try { test(); } catch (Exception ex) { failures.Add(name + ": " + ex.Message); } }
        Test("default options", () => PerformanceValues.Validate(new()));
        foreach (var o in new[] { new PerformanceSessionOptions(0, 1), new(601, 1), new(30, 0), new(30, 11), new(1, 2) })
            Test("reject options " + o, () => Throws<ArgumentException>(() => PerformanceValues.Validate(o)));
        Test("CPU kernel includes idle", () => Equal(50, PerformanceValues.Cpu(new(10, 20, 20), new(60, 80, 60))));
        Test("CPU idle", () => Equal(0, PerformanceValues.Cpu(new(10, 20, 20), new(20, 30, 20))));
        Test("CPU fully busy", () => Equal(100, PerformanceValues.Cpu(new(10, 20, 20), new(10, 30, 20))));
        Test("CPU missing baseline", () => Require(PerformanceValues.Cpu(null, new(0, 0, 0)) is null, "Invented baseline."));
        Test("CPU no counter interval", () => Require(PerformanceValues.Cpu(new(1, 1, 1), new(1, 1, 1)) is null, "Zero delta is not idle."));
        Test("CPU counter reset", () => Require(PerformanceValues.Cpu(new(10, 20, 20), new(1, 2, 2)) is null, "Counter reset is not usage."));
        Test("CPU inconsistent idle delta", () => Require(PerformanceValues.Cpu(new(0, 0, 0), new(20, 5, 5)) is null, "Impossible counters accepted."));
        Test("memory formula", () => Equal(75, PerformanceValues.Memory(1000, 250)));
        Test("memory empty capacity", () => Require(PerformanceValues.Memory(0, 0) is null, "Zero capacity accepted."));
        Test("memory invalid free", () => Require(PerformanceValues.Memory(100, 101) is null, "Invalid free accepted."));
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, -1d, 101d })
            Test("invalid percent " + invalid, () => { var r = PerformanceValues.Normalize(new(invalid, invalid, invalid, 0, [])); Require(r.CpuPercent is null && r.MemoryUsedPercent is null && r.DiskBusyPercent is null, "Invalid metric survived."); Require(r.Warnings.Count > 0, "Missing explanation."); });
        Test("queue is not percent", () => Equal(150, PerformanceValues.Normalize(new(0, 0, 0, 150, [])).DiskQueueLength));
        Test("normalization does not mutate source", () => { var r = new PerformanceReading(double.NaN, 40, 5, 0, []); PerformanceValues.Normalize(r); Require(double.IsNaN(r.CpuPercent!.Value) && r.Warnings.Count == 0, "Input mutated."); });
        Test("scheduled collection", () => { var clock = new FakeClock(); using var source = new FakeSource(); var s = Run(new(3, 1), source, clock); Require(s.Outcome == "Completed" && s.Samples.Select(x => x.OffsetMs).SequenceEqual(new long[] { 1000, 2000, 3000 }), "Wrong schedule."); Require(s.ElapsedMs == 3000 && s.MissedSlots == 0, "Incorrect duration or skipped slots."); });
        Test("slow provider skips slots without bursts", () => { var clock = new FakeClock(); using var source = new FakeSource(() => { clock.ElapsedMs += 1500; return Healthy(); }); var s = Run(new(5, 1), source, clock); Require(s.Samples.Select(x => x.OffsetMs).SequenceEqual(new long[] { 2500, 4500, 6500 }), "Catch-up or timestamps wrong."); Require(s.MissedSlots == 2 && s.Warnings.Count > 0, "Missed slots/overrun hidden."); });
        Test("nondivisible duration ends on deadline", () => { var clock = new FakeClock(); using var source = new FakeSource(); var s = Run(new(5, 2), source, clock); Require(s.Samples.Count == 2 && s.ElapsedMs == 5000, "Wrong final delay."); });
        Test("pre-cancel makes no source call", () => { using var c = new CancellationTokenSource(); c.Cancel(); using var source = new FakeSource(); var s = Run(new(3, 1), source, new(), c.Token); Require(s.Outcome == "Stopped" && s.Samples.Count == 0 && source.Calls == 0, "Pre-cancel sampled."); });
        Test("stop retains completed samples", () => { using var c = new CancellationTokenSource(); var clock = new FakeClock(); clock.OnDelay = () => { if (clock.ElapsedMs >= 2000) c.Cancel(); }; using var source = new FakeSource(); var s = Run(new(3, 1), source, clock, c.Token); Require(s.Outcome == "Stopped" && s.Samples.Count == 1, "Stop discarded observations or invented another."); });
        Test("provider exception retains an explicit missing sample", () => { using var source = new FakeSource(() => throw new IOException("private-text")); var s = Run(new(2, 1), source, new()); Require(s.Samples.Count == 2 && s.Samples.All(x => x.Reading.CpuPercent is null), "Failure replaced with zero."); Require(s.Samples.All(x => x.Reading.Warnings.Count > 0 && !string.Join("", x.Reading.Warnings).Contains("private-text", StringComparison.Ordinal)), "Raw exception leaked or failure hidden."); });
        Test("stop during read keeps earlier evidence", () => { using var c = new CancellationTokenSource(); var calls = 0; using var source = new FakeSource(() => { if (++calls == 2) { c.Cancel(); throw new OperationCanceledException(c.Token); } return Healthy(); }); var s = Run(new(3, 1), source, new(), c.Token); Require(s.Outcome == "Stopped" && s.Samples.Count == 1 && s.Warnings.Count > 0, "In-flight cancellation disappeared."); });
        Test("progress sees each completed sample", () => { var seen = new List<PerformanceSample>(); using var source = new FakeSource(); new PerformanceSessionService().RunAsync(new(2, 1), source, new FakeClock(), new InlineProgress(seen.Add), default).GetAwaiter().GetResult(); Require(seen.Count == 2, "Missing progress."); });
        Test("bounded maximum session", () => { using var source = new FakeSource(); var s = Run(new(600, 1), source, new()); Require(s.Samples.Count == 600, "Unbounded sample count."); });
        Test("statistics ignore missing samples", () => { var s = Snapshot(1, 10, null, 20, 100); var a = PerformanceStatistics.For(s, SessionMetric.Cpu); Require(a.Total == 5 && a.Valid == 4 && a.HighCount == 1, "Wrong denominator/threshold."); Equal(15, a.Median); Equal(100, a.P95); Equal(1, a.Minimum); Equal(100, a.Maximum); });
        Test("no measurements remain unknown", () => { var a = PerformanceStatistics.For(Snapshot(null, null), SessionMetric.Cpu); Require(a.Valid == 0 && a.Median is null && a.P95 is null && a.Maximum is null, "Unknown became zero."); });
        Test("single valid sample percentile", () => Equal(12, PerformanceStatistics.For(Snapshot(12), SessionMetric.Cpu).P95));
        Test("series break at unavailable value", () => Require(PerformanceStatistics.Segments(Snapshot(1, null, 2), SessionMetric.Cpu).Count == 2, "Graph bridged missing data."));
        Test("series break at missed time slot", () => { var s = Snapshot(1, 2); s.Samples[1] = s.Samples[1] with { OffsetMs = 5000 }; Require(PerformanceStatistics.Segments(s, SessionMetric.Cpu).Count == 2, "Graph bridged missed interval."); });
        Test("adjacent points stay a segment", () => Require(PerformanceStatistics.Segments(Snapshot(1, 2), SessionMetric.Cpu).Single().Count == 2, "Continuous series split."));
        Test("marker exact time and trimmed note", () => { var list = new List<PerformanceMarker>(); Require(PerformanceStatistics.AddMarker(list, 1234, "  Зависание  "), "Marker rejected."); Require(list.Single() == new PerformanceMarker(1234, "Зависание"), "Marker altered."); });
        Test("empty marker is not accepted", () => Require(!PerformanceStatistics.AddMarker([], 0, "  "), "Blank marker accepted."));
        Test("marker length limit", () => Require(!PerformanceStatistics.AddMarker([], 0, new string('x', 161)), "Long marker accepted."));
        Test("marker count limit", () => { var list = Enumerable.Range(0, 100).Select(x => new PerformanceMarker(x, "x")).ToList(); Require(!PerformanceStatistics.AddMarker(list, 101, "y") && list.Count == 100, "Marker cap ignored."); });
        Test("negative marker offset", () => Require(!PerformanceStatistics.AddMarker([], -1, "x"), "Negative timestamp accepted."));
        Test("HTML encodes marker and includes charts", () => { var s = Snapshot(10, null, 20); s.Markers.Add(new(1500, "<script>alert(1)</script>&")); var html = PerformanceSessionReport.Html(s); Require(!html.Contains("<script>", StringComparison.Ordinal) && html.Contains("&lt;script&gt;", StringComparison.Ordinal), "HTML injection."); Require(html.Contains("<svg", StringComparison.Ordinal), "No time-series report."); });
        Test("JSON retains null and stopped outcome", () => { var s = Snapshot(10, null); s.Outcome = "Stopped"; using var j = JsonDocument.Parse(PerformanceSessionReport.Json(s)); var snapshot = j.RootElement.GetProperty("Snapshot"); Require(snapshot.GetProperty("Outcome").GetString() == "Stopped" && snapshot.GetProperty("Samples")[1].GetProperty("Reading").GetProperty("CpuPercent").ValueKind == JsonValueKind.Null, "Report lost outcome or missing data."); });
        Test("summary is not a health verdict", () => { var text = PerformanceSessionReport.Summary(Snapshot(90)); Require(text.Contains("не доказывает", StringComparison.Ordinal) && text.Contains("замеров", StringComparison.Ordinal), "Statistics presented as diagnosis/time fraction."); });
        Console.WriteLine($"Performance session regression: {count - failures.Count}/{count} passed.");
        foreach (var f in failures) Console.Error.WriteLine("FAIL: " + f);
        return failures.Count == 0 ? 0 : 220;
    }

    private static PerformanceReading Healthy() => new(10, 40, 5, 0, []);
    private static PerformanceSessionSnapshot Run(PerformanceSessionOptions o, FakeSource source, FakeClock clock, CancellationToken ct = default)
        => new PerformanceSessionService().RunAsync(o, source, clock, null, ct).GetAwaiter().GetResult();
    internal static PerformanceSessionSnapshot Snapshot(params double?[] values) => new()
    {
        Options = new(10, 1), Outcome = "Completed", StartedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"), ElapsedMs = values.Length * 1000,
        Samples = values.Select((v, i) => new PerformanceSample((i + 1) * 1000, 5, DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddSeconds(i + 1), new(v, 40, 5, 0, []))).ToList()
    };
    private sealed class FakeClock : IPerformanceSessionClock
    {
        public long ElapsedMs { get; set; }
        public DateTimeOffset Now => DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddMilliseconds(ElapsedMs);
        public Action? OnDelay { get; set; }
        public Task DelayAsync(int ms, CancellationToken ct) { ct.ThrowIfCancellationRequested(); ElapsedMs += ms; OnDelay?.Invoke(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }
    private sealed class FakeSource(Func<PerformanceReading>? read = null) : IPerformanceSessionSource
    {
        public int Calls { get; private set; }
        public PerformanceReading Read(CancellationToken ct) { ct.ThrowIfCancellationRequested(); Calls++; return read?.Invoke() ?? Healthy(); }
        public void Dispose() { }
    }
    private sealed class InlineProgress(Action<PerformanceSample> action) : IProgress<PerformanceSample> { public void Report(PerformanceSample value) => action(value); }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Equal(double expected, double? value) => Require(value is not null && Math.Abs(expected - value.Value) < 0.0001, $"Expected {expected}; got {value}.");
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
