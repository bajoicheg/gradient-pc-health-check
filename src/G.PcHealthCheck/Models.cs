using System.Text.Json.Serialization;

namespace G.PcHealthCheck;

public sealed class Thresholds
{
    public double CpuWarnPercent { get; init; } = 85;
    public double CpuCriticalPercent { get; init; } = 95;
    public double MemoryWarnAvailablePercent { get; init; } = 15;
    public double MemoryCriticalAvailablePercent { get; init; } = 8;
    public double SystemDiskWarnFreePercent { get; init; } = 15;
    public double SystemDiskCriticalFreePercent { get; init; } = 8;
    public double SystemDiskWarnFreeGB { get; init; } = 20;
    public double SystemDiskCriticalFreeGB { get; init; } = 10;
    public double DiskQueueWarn { get; init; } = 2;
    public double DiskQueueCritical { get; init; } = 5;
    public double UptimeWarnDays { get; init; } = 14;
    public double UptimeCriticalDays { get; init; } = 30;
    public int RecentErrorWarnCount { get; init; } = 20;
    public int RecentErrorCriticalCount { get; init; } = 100;
    public int RepeatedEventWarnCount { get; init; } = 5;
    public int RepeatedEventCriticalCount { get; init; } = 20;
    public int StartupWarnCount { get; init; } = 20;
    public int TempOlderThanDays { get; init; } = 3;
    public int CoverageWarnPercent { get; init; } = 65;
    public int CoverageHighPercent { get; init; } = 85;
}

public sealed class SystemInfo
{
    public string ComputerName { get; set; } = Environment.MachineName;
    public string UserName { get; set; } = Environment.UserName;
    public string Domain { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string OS { get; set; } = "";
    public string OSVersion { get; set; } = "";
    public string BuildNumber { get; set; } = "";
    public string Architecture { get; set; } = Environment.Is64BitOperatingSystem ? "x64" : "x86";
    public string Cpu { get; set; } = "";
    public int LogicalProcessors { get; set; } = Environment.ProcessorCount;
    public double TotalMemoryGB { get; set; }
    public DateTime LastBoot { get; set; }
    public double UptimeDays { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.Now;
    public bool IsAdministrator { get; set; }
}

public sealed class PerformanceSnapshot
{
    public double? CpuPercent { get; set; }
    public double? MemoryAvailablePercent { get; set; }
    public double? MemoryUsedPercent { get; set; }
    public double? MemoryAvailableGB { get; set; }
    public double? DiskBusyPercent { get; set; }
    public double? DiskQueueLength { get; set; }
}

public sealed class LogicalDiskInfo
{
    public string Drive { get; set; } = "";
    public string VolumeName { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public double SizeGB { get; set; }
    public double FreeGB { get; set; }
    public double FreePercent { get; set; }
}

public sealed class PhysicalDiskInfo
{
    public string Name { get; set; } = "";
    public string MediaType { get; set; } = "";
    public double SizeGB { get; set; }
    public string HealthStatus { get; set; } = "Unknown";
    public string OperationalStatus { get; set; } = "";
    public string Source { get; set; } = "";
}

public sealed class ProcessInfo
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public double CpuPercent { get; set; }
    public double MemoryMB { get; set; }
    public double IoMBPerSec { get; set; }
}

public sealed class PendingRebootInfo
{
    public bool Pending { get; set; }
    public List<string> Reasons { get; set; } = [];
}

public sealed class EventSummary
{
    public int Hours { get; set; } = 24;
    public int CriticalCount { get; set; }
    public int ErrorCount { get; set; }
    public List<EventGroup> Top { get; set; } = [];
}

public sealed class EventGroup
{
    public string Log { get; set; } = "";
    public string Provider { get; set; } = "";
    public int EventId { get; set; }
    public int Count { get; set; }
    public DateTime? LastSeen { get; set; }
}

public sealed class StartupItem
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Location { get; set; } = "";
    public string User { get; set; } = "";
}

public sealed class SecurityProductInfo
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class NetworkAdapterInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Addresses { get; set; } = "";
    public string Gateway { get; set; } = "";
    public string Dns { get; set; } = "";
    public long SpeedMbps { get; set; }
}

public sealed class UpdateInfo
{
    public string Wuauserv { get; set; } = "Unknown";
    public string Bits { get; set; } = "Unknown";
    public List<string> RecentHotfixes { get; set; } = [];
}

public sealed class DiagnosticData
{
    public SystemInfo System { get; set; } = new();
    public PerformanceSnapshot Performance { get; set; } = new();
    public List<LogicalDiskInfo> LogicalDisks { get; set; } = [];
    public List<PhysicalDiskInfo> PhysicalDisks { get; set; } = [];
    public List<ProcessInfo> TopCpu { get; set; } = [];
    public List<ProcessInfo> TopMemory { get; set; } = [];
    public List<ProcessInfo> TopIo { get; set; } = [];
    public PendingRebootInfo PendingReboot { get; set; } = new();
    public EventSummary Events { get; set; } = new();
    public List<StartupItem> StartupItems { get; set; } = [];
    public List<SecurityProductInfo> SecurityProducts { get; set; } = [];
    public List<NetworkAdapterInfo> NetworkAdapters { get; set; } = [];
    public UpdateInfo Updates { get; set; } = new();
    public List<string> CollectionWarnings { get; set; } = [];
}

public sealed class Finding
{
    public string Severity { get; set; } = "OK";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Value { get; set; } = "";
    public string Recommendation { get; set; } = "";
    [JsonIgnore] public int Penalty { get; set; }
}

public sealed class Assessment
{
    public int Score { get; set; }
    public string Status { get; set; } = "OK";
    public int CoveragePercent { get; set; } = 100;
    public string CoverageStatus { get; set; } = "HIGH";
    public List<string> MissingSignals { get; set; } = [];
    public List<Finding> Findings { get; set; } = [];
}

public sealed class ActionRecommendation
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool CanAutomate { get; set; }
    public bool RequiresAdmin { get; set; }
    public bool Preselected { get; set; }
    public string Risk { get; set; } = "";
    public string Verification { get; set; } = "";
}

public sealed class ScanResult
{
    public DiagnosticData Data { get; set; } = new();
    public Assessment Assessment { get; set; } = new();
    public List<ActionRecommendation> Actions { get; set; } = [];
}

public sealed class RemediationActionResult
{
    public string Id { get; set; } = "";
    public bool Success { get; set; }
    public int? ExitCode { get; set; }
    public string Message { get; set; } = "";
    public string Output { get; set; } = "";
    public double? FreedMB { get; set; }
    public long? DeletedFiles { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
}

public sealed class RemediationBatchResult
{
    public string SessionId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public bool Elevated { get; set; }
    public List<RemediationActionResult> Actions { get; set; } = [];
}

public sealed class VerificationResult
{
    public ScanResult Before { get; set; } = new();
    public ScanResult After { get; set; } = new();
    public RemediationBatchResult Remediation { get; set; } = new();
}
