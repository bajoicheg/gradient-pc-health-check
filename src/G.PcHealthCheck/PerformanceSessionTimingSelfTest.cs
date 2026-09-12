namespace G.PcHealthCheck;

internal static class PerformanceSessionTimingSelfTest
{
    public static int Run()
    {
        var failures = new List<string>(); var count = 0;
        void Test(string name, Action test) { count++; try { test(); } catch (Exception ex) { failures.Add(name + ": " + ex.Message); } }
        Test("ordinary timer jitter does not discard the final due sample", () =>
        {
            var clock = new Clock(5); using var source = new Source();
            var s = new PerformanceSessionService().RunAsync(new(2, 1), source, clock, null, default).GetAwaiter().GetResult();
            Require(s.Samples.Count == 2 && s.MissedSlots == 0, "A 5-ms timer delay discarded the final scheduled sample.");
            Require(s.Samples[^1].OffsetMs == 2005 && s.ElapsedMs == 2005, "Actual time was replaced with nominal time.");
        });
        Test("large oversleep is not backfilled beyond the session", () =>
        {
            var clock = new Clock(2500); using var source = new Source();
            var s = new PerformanceSessionService().RunAsync(new(2, 1), source, clock, null, default).GetAwaiter().GetResult();
            Require(s.Samples.Count == 0 && s.MissedSlots == 2 && source.Calls == 0, "Expired slots were backfilled.");
        });
        Test("finite large queue measurements cannot overflow the median", () =>
        {
            var s = PerformanceSessionSelfTest.Snapshot(10, 20);
            s.Samples = s.Samples.Select(x => x with { Reading = x.Reading with { DiskQueueLength = double.MaxValue } }).ToList();
            var value = PerformanceStatistics.For(s, SessionMetric.DiskQueue).Median;
            Require(value.HasValue && double.IsFinite(value.Value) && value == double.MaxValue, "Median overflowed from valid finite inputs.");
            _ = PerformanceSessionReport.Json(s);
        });
        Test("stopped observations are never complete coverage", () =>
        {
            var s = PerformanceSessionSelfTest.Snapshot(10, 20); s.Outcome = "Stopped";
            Require(PerformanceStatistics.Completeness(s).Contains("Неполные", StringComparison.Ordinal), "Stopped session described as complete.");
        });
        Test("cancel-induced provider error remains stopped", () =>
        {
            using var cancellation = new CancellationTokenSource();
            using var source = new Source(() => { cancellation.Cancel(); throw new IOException("Synthetic cancellation failure"); });
            var s = new PerformanceSessionService().RunAsync(new(2, 1), source, new Clock(0), null, cancellation.Token).GetAwaiter().GetResult();
            Require(s.Outcome == "Stopped" && s.Samples.Count == 0, "Cancellation reported as an ordinary collected sample.");
        });
        Console.WriteLine($"Performance timing/review regression: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 222;
    }

    private sealed class Clock(int overshoot) : IPerformanceSessionClock
    {
        public long ElapsedMs { get; private set; }
        public DateTimeOffset Now => DateTimeOffset.UnixEpoch.AddMilliseconds(ElapsedMs);
        public Task DelayAsync(int milliseconds, CancellationToken ct) { ct.ThrowIfCancellationRequested(); ElapsedMs += milliseconds + overshoot; return Task.CompletedTask; }
    }
    private sealed class Source(Func<PerformanceReading>? read = null) : IPerformanceSessionSource
    {
        public int Calls { get; private set; }
        public PerformanceReading Read(CancellationToken ct) { Calls++; return read?.Invoke() ?? new(10, 40, 5, 0, []); }
        public void Dispose() { }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
