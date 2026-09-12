namespace G.PcHealthCheck;

internal enum SessionMetric { Cpu, Memory, DiskBusy, DiskQueue }
internal sealed record PerformanceSessionOptions(int DurationSeconds = 120, int IntervalSeconds = 2);
internal sealed record CpuTimeCounters(ulong Idle, ulong Kernel, ulong User);
internal sealed record PerformanceReading(double? CpuPercent, double? MemoryUsedPercent, double? DiskBusyPercent, double? DiskQueueLength, IReadOnlyList<string> Warnings);
internal sealed record PerformanceSample(long OffsetMs, long CollectionMs, DateTimeOffset CollectedAt, PerformanceReading Reading);
internal sealed record PerformanceMarker(long OffsetMs, string Note);
internal sealed record PerformancePoint(long OffsetMs, double Value);
internal sealed record PerformanceMetricStats(SessionMetric Metric, int Total, int Valid, double? Minimum, double? Median, double? P95, double? Maximum, int HighCount);

internal sealed class PerformanceSessionSnapshot
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("D");
    public PerformanceSessionOptions Options { get; set; } = new();
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public long ElapsedMs { get; set; }
    public string Outcome { get; set; } = "NotStarted";
    public int MissedSlots { get; set; }
    public List<PerformanceSample> Samples { get; set; } = [];
    public List<PerformanceMarker> Markers { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

internal interface IPerformanceSessionSource : IDisposable
{
    PerformanceReading Read(CancellationToken cancellationToken);
}

internal interface IPerformanceSessionClock
{
    long ElapsedMs { get; }
    DateTimeOffset Now { get; }
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken);
}
