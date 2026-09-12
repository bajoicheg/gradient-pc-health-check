namespace G.PcHealthCheck;

internal sealed record IncidentWindow(DateTimeOffset From, DateTimeOffset To, int MaxPerLog = 1000);
internal sealed record IncidentFilter(string Text = "", string Log = "", int? EventId = null, bool WarningsOnly = false);
internal sealed class IncidentEvent
{
    public string Log { get; set; } = "";
    public long? RecordId { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public int EventId { get; set; }
    public int? Level { get; set; }
    public string Provider { get; set; } = "";
    public string Message { get; set; } = "";
    public string MessageState { get; set; } = "Available";
    public int? EmitterPid { get; set; }
}
internal sealed class IncidentLogResult
{
    public string Log { get; set; } = "";
    public string State { get; set; } = "NotRead";
    public List<IncidentEvent> Events { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
internal sealed class IncidentSnapshot
{
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset FinishedAt { get; set; }
    public IncidentWindow Window { get; set; } = new(DateTimeOffset.Now.AddHours(-1), DateTimeOffset.Now);
    public string State { get; set; } = "NotRead";
    public List<IncidentLogResult> Logs { get; set; } = [];
}
internal sealed class ProcessReviewEntry
{
    public uint Pid { get; set; }
    public uint? ParentPid { get; set; }
    public string CreationKey { get; set; } = "";
    public DateTimeOffset? CreatedAt { get; set; }
    public string Name { get; set; } = "";
    public string Executable { get; set; } = "";
    public string CommandLine { get; set; } = "";
    public uint? SessionId { get; set; }
    public uint? Threads { get; set; }
    public uint? Handles { get; set; }
    public ulong? WorkingSetBytes { get; set; }
    public List<string> Warnings { get; set; } = [];
}
internal sealed class ProcessReviewSnapshot
{
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset FinishedAt { get; set; }
    public string State { get; set; } = "NotRead";
    public List<ProcessReviewEntry> Processes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<ProcessOwnerEvidence> OwnerChecks { get; set; } = [];
}
internal sealed record ProcessOwnerEvidence(uint Pid, string CreationKey, string State, string Owner, string Detail, DateTimeOffset CheckedAt);
internal interface IIncidentEventSource
{
    IEnumerable<IncidentEvent> Read(string log, IncidentWindow window, CancellationToken ct);
}
internal interface IProcessReviewSource
{
    IEnumerable<ProcessReviewEntry> Read(CancellationToken ct);
    ProcessReviewEntry? Lookup(uint pid, CancellationToken ct);
    string ReadOwner(uint pid, CancellationToken ct);
}
