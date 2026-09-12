namespace G.PcHealthCheck;

internal enum ProcessMetric { Cpu, WorkingSet, PrivateCommit, ReadRate, WriteRate }
internal sealed record ProcessObservationTarget(uint Pid, DateTimeOffset CreatedAt, string Name);
internal sealed record ProcessCounters
{
    public uint Pid { get; init; }
    public ulong CreatedFileTime { get; init; }
    public string State { get; init; } = "Unavailable";
    public ulong? Kernel100ns { get; init; }
    public ulong? User100ns { get; init; }
    public ulong? WorkingSetBytes { get; init; }
    public ulong? PrivateBytes { get; init; }
    public ulong? ReadBytes { get; init; }
    public ulong? WriteBytes { get; init; }
    public int LogicalProcessors { get; init; }
    public string ImagePath { get; init; } = "";
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
internal sealed record ProcessCounterSample(long OffsetMs, DateTimeOffset CollectedAt, ProcessCounters Counters);
internal sealed record ProcessObservationReading(double? CpuPercent, double? WorkingSetMiB, double? PrivateCommitMiB, double? ReadMiBps, double? WriteMiBps, long? IntervalMs, IReadOnlyList<string> Warnings);
internal sealed record ProcessObservationSample(ProcessCounterSample Process, ProcessObservationReading Reading, PerformanceSample System);
internal sealed class ProcessObservationSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string ComputerName { get; set; } = Environment.MachineName;
    public ProcessObservationTarget Target { get; set; } = new(0, DateTimeOffset.MinValue, "");
    public ExecutionContextInfo? ExecutionContext { get; set; }
    public PerformanceSessionSnapshot System { get; set; } = new();
    public List<ProcessObservationSample> Samples { get; set; } = [];
}
internal interface IProcessObservationSource : IDisposable
{
    ProcessCounters Read(CancellationToken ct);
}
