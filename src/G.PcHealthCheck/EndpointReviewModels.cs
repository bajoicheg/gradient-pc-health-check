namespace G.PcHealthCheck;

internal sealed record EndpointRow
{
    public string Table { get; init; } = "";
    public string Protocol { get; init; } = "";
    public string Family { get; init; } = "";
    public string LocalAddress { get; init; } = "";
    public int LocalPort { get; init; }
    public string? RemoteAddress { get; init; }
    public int? RemotePort { get; init; }
    public uint Pid { get; init; }
    public uint? StateCode { get; init; }
    public string State { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public DateTimeOffset? ProcessStartedAt { get; init; }
    public string ProcessEvidence { get; init; } = "NotChecked";
}
internal sealed class EndpointTable
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "NotCollected";
    public uint? ReportedCount { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public List<EndpointRow> Rows { get; set; } = [];
    public string Message { get; set; } = "";
}
internal sealed record EndpointProcess(uint Pid, string Name, DateTimeOffset StartedAt);
internal sealed class EndpointSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string ComputerName { get; set; } = Environment.MachineName;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset FinishedAt { get; set; }
    public string State { get; set; } = "NotCollected";
    public ExecutionContextInfo? ExecutionContext { get; set; }
    public List<EndpointTable> Tables { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
internal sealed record EndpointObservation(EndpointRow Row, string Change, string? PreviousState = null);
internal interface IEndpointSource
{
    List<EndpointProcess> ReadProcesses(CancellationToken ct, List<string> warnings);
    EndpointTable ReadTable(string name, CancellationToken ct);
}
