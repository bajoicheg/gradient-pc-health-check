namespace G.PcHealthCheck;

internal sealed record FileUseProcess
{
    public uint Pid { get; init; }
    public ulong StartFileTime { get; init; }
    public string ApplicationName { get; init; } = "";
    public string ServiceName { get; init; } = "";
    public uint ApplicationType { get; init; }
    public uint ApplicationStatus { get; init; }
    public uint SessionId { get; init; } = uint.MaxValue;
    public bool Restartable { get; init; }
    public string IdentityState { get; init; } = "NotChecked";
    public string ProcessName { get; init; } = "";
    public string ImagePath { get; init; } = "";
    public int? IdentityError { get; init; }
}
internal sealed record FileUseIdentity(uint Pid, ulong StartFileTime, string ImagePath, int? ErrorCode = null);
internal sealed record FileUseBatch(uint Code, uint Needed, uint Returned, uint RebootReasons, List<FileUseProcess> Processes);
internal interface IFileUseSource
{
    void CheckFile(string path, CancellationToken ct);
    uint StartSession(out uint handle);
    uint RegisterFile(uint handle, string path);
    FileUseBatch ReadList(uint handle, int capacity);
    uint EndSession(uint handle);
    FileUseIdentity? ReadIdentity(uint pid);
}
internal sealed class FileUseSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string TargetPath { get; set; } = "";
    public string ComputerName { get; set; } = Environment.MachineName;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset FinishedAt { get; set; }
    public ExecutionContextInfo? ExecutionContext { get; set; }
    public string State { get; set; } = "NotCollected";
    public string Stage { get; set; } = "";
    public bool ListCompleted { get; set; }
    public int QueryAttempts { get; set; }
    public uint? ReportedCount { get; set; }
    public uint? RebootReasons { get; set; }
    public int? ErrorCode { get; set; }
    public uint? EndSessionCode { get; set; }
    public List<FileUseProcess> Processes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
