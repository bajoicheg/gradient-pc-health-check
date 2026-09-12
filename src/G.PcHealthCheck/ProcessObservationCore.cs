namespace G.PcHealthCheck;

// Test-first contracts. Missing behavior must fail at runtime, not at compilation.
internal static class ProcessObservationCore
{
    public static ProcessObservationTarget FromEntry(ProcessReviewEntry entry) => throw new NotImplementedException();
    public static bool Matches(ProcessObservationTarget target, ProcessCounters counters) => throw new NotImplementedException();
    public static ProcessObservationReading Calculate(ProcessObservationTarget target, ProcessCounterSample? before, ProcessCounterSample after, int intervalSeconds) => throw new NotImplementedException();
    public static List<List<PerformancePoint>> Segments(ProcessObservationSnapshot snapshot, ProcessMetric metric) => throw new NotImplementedException();
}
internal static class ProcessObservationService
{
    public static Task<ProcessObservationSnapshot> RunAsync(ProcessObservationTarget target, PerformanceSessionOptions options, IProcessObservationSource process, IPerformanceSessionSource system, IPerformanceSessionClock clock, ExecutionContextInfo? context, IProgress<ProcessObservationSample>? progress, CancellationToken ct) => throw new NotImplementedException();
}
internal static class ProcessObservationReport
{
    public static string Summary(ProcessObservationSnapshot snapshot) => throw new NotImplementedException();
    public static string Html(ProcessObservationSnapshot snapshot) => throw new NotImplementedException();
    public static string Json(ProcessObservationSnapshot snapshot) => throw new NotImplementedException();
    public static string Save(ProcessObservationSnapshot snapshot, string parent) => throw new NotImplementedException();
}
