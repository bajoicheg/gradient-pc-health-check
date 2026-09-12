namespace G.PcHealthCheck;

internal sealed record FolderUsageOptions(int MaxEntries = 200000, int MaxDirectories = 20000, int MaxSeconds = 120, int TopFiles = 200);
internal sealed record FolderItem(string Path, FileAttributes Attributes, long? Bytes = null, DateTimeOffset? Modified = null, string Error = "");
internal sealed record LargeFolderFile(string Path, long Bytes, DateTimeOffset? Modified);
internal sealed class FolderUsageRow
{
    public string Path { get; set; } = "";
    public int ParentIndex { get; set; } = -1;
    public decimal OwnBytes { get; set; }
    public int OwnFiles { get; set; }
    public decimal Bytes { get; set; }
    public int Files { get; set; }
    public bool Incomplete { get; set; } = true;
}
internal sealed class FolderUsageSnapshot
{
    public string Root { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset FinishedAt { get; set; }
    public string Outcome { get; set; } = "NotStarted";
    public FolderUsageOptions Options { get; set; } = new();
    public int EntriesVisited { get; set; }
    public int SkippedLinks { get; set; }
    public int Errors { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<FolderUsageRow> Folders { get; set; } = [];
    public List<LargeFolderFile> LargestFiles { get; set; } = [];
}
internal interface IFolderUsageSource
{
    FileAttributes GetAttributes(string path);
    IEnumerable<FolderItem> ReadDirectory(string path);
}
internal sealed record DiskReliability(string DeviceId, int? Temperature = null, int? TemperatureMax = null,
    int? Wear = null, ulong? PowerOnHours = null, ulong? ReadErrorsUncorrected = null, ulong? WriteErrorsUncorrected = null);
internal sealed class PhysicalDiskDetail
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Firmware { get; set; } = "";
    public ulong? Size { get; set; }
    public int? MediaType { get; set; }
    public int? BusType { get; set; }
    public int? Health { get; set; }
    public int[] OperationalStatus { get; set; } = [];
    public DiskReliability? Reliability { get; set; }
    public string CounterState { get; set; } = "Unavailable";
    public List<string> Warnings { get; set; } = [];
}
internal sealed class DiskDetailsSnapshot
{
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset FinishedAt { get; set; }
    public string Outcome { get; set; } = "NotStarted";
    public List<PhysicalDiskDetail> Disks { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
internal interface IPhysicalDiskDetailsSource
{
    IEnumerable<PhysicalDiskDetail> Read(CancellationToken ct);
}
