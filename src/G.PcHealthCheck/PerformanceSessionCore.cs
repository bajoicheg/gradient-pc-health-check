namespace G.PcHealthCheck;

internal static class PerformanceValues
{
    public static void Validate(PerformanceSessionOptions options) => throw new NotImplementedException("Session options are not implemented.");
    public static double? Cpu(CpuTimeCounters? before, CpuTimeCounters after) => throw new NotImplementedException("CPU delta is not implemented.");
    public static double? Memory(ulong total, ulong available) => throw new NotImplementedException("Physical memory calculation is not implemented.");
    public static PerformanceReading Normalize(PerformanceReading reading) => throw new NotImplementedException("Missing-data normalization is not implemented.");
    public static double? Value(PerformanceReading reading, SessionMetric metric) => throw new NotImplementedException("Metric selection is not implemented.");
}

internal sealed class PerformanceSessionService
{
    public Task<PerformanceSessionSnapshot> RunAsync(PerformanceSessionOptions options, IPerformanceSessionSource source, IPerformanceSessionClock clock, IProgress<PerformanceSample>? progress, CancellationToken ct)
        => throw new NotImplementedException("Timed collection is not implemented.");
}

internal static class PerformanceStatistics
{
    public static PerformanceMetricStats For(PerformanceSessionSnapshot snapshot, SessionMetric metric) => throw new NotImplementedException("Sample statistics are not implemented.");
    public static List<List<PerformancePoint>> Segments(PerformanceSessionSnapshot snapshot, SessionMetric metric) => throw new NotImplementedException("Gap-preserving series are not implemented.");
    public static bool AddMarker(List<PerformanceMarker> markers, long offsetMs, string note) => throw new NotImplementedException("Symptom markers are not implemented.");
}
