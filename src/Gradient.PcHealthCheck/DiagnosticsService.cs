using Microsoft.Win32;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;

namespace Gradient.PcHealthCheck;

public sealed class DiagnosticsService
{
    public async Task<DiagnosticData> CollectAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Collect(progress, cancellationToken), cancellationToken);
    }

    private DiagnosticData Collect(IProgress<string>? progress, CancellationToken ct)
    {
        var data = new DiagnosticData();
        RunSection("Сведения о системе", progress, data, () => data.System = GetSystemInfo());
        ct.ThrowIfCancellationRequested();
        RunSection("Устойчивая загрузка (5 замеров)", progress, data, () => data.Performance = GetPerformanceSnapshot(ct));
        RunSection("Логические диски", progress, data, () => data.LogicalDisks = GetLogicalDisks());
        RunSection("Физические накопители", progress, data, () => data.PhysicalDisks = GetPhysicalDisks());
        ct.ThrowIfCancellationRequested();

        progress?.Report("Снимаю TOP процессов по CPU / RAM / I/O…");
        try
        {
            var processSample = GetProcessSamples(ct);
            data.TopCpu = processSample.OrderByDescending(x => x.CpuPercent).Take(10).ToList();
            data.TopMemory = processSample.OrderByDescending(x => x.MemoryMB).Take(10).ToList();
            data.TopIo = processSample.OrderByDescending(x => x.IoMBPerSec).Take(10).ToList();
        }
        catch (Exception ex) { data.CollectionWarnings.Add($"TOP процессов: {ex.Message}"); }

        RunSection("Pending reboot", progress, data, () => data.PendingReboot = GetPendingReboot());
        RunSection("События Windows за 24 часа", progress, data, () => data.Events = GetEvents(24));
        RunSection("Автозагрузка", progress, data, () => data.StartupItems = GetStartupItems());
        RunSection("Антивирус / Kaspersky", progress, data, () => data.SecurityProducts = GetSecurityProducts());
        RunSection("Сетевые интерфейсы", progress, data, () => data.NetworkAdapters = GetNetworkAdapters());
        RunSection("Windows Update", progress, data, () => data.Updates = GetUpdateInfo());
        data.System.CollectedAt = DateTime.Now;
        progress?.Report("Диагностика завершена.");
        return data;
    }

    private static void RunSection(string title, IProgress<string>? progress, DiagnosticData data, Action action)
    {
        progress?.Report(title + "…");
        try { action(); }
        catch (Exception ex) { data.CollectionWarnings.Add($"{title}: {ex.Message}"); }
    }

    private static SystemInfo GetSystemInfo()
    {
        var result = new SystemInfo { IsAdministrator = IsAdministrator() };

        using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Domain,Manufacturer,Model,NumberOfLogicalProcessors,TotalPhysicalMemory,UserName FROM Win32_ComputerSystem"))
        using (var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault())
        {
            if (item is not null)
            {
                result.Domain = Convert.ToString(item["Domain"]) ?? "";
                result.Manufacturer = Convert.ToString(item["Manufacturer"]) ?? "";
                result.Model = Convert.ToString(item["Model"]) ?? "";
                result.LogicalProcessors = ToInt(item["NumberOfLogicalProcessors"], Environment.ProcessorCount);
                var total = ToDouble(item["TotalPhysicalMemory"]);
                result.TotalMemoryGB = Math.Round(total / 1024d / 1024d / 1024d, 1);
                var interactive = Convert.ToString(item["UserName"]);
                if (!string.IsNullOrWhiteSpace(interactive)) result.UserName = interactive;
            }
        }

        using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Caption,Version,BuildNumber,OSArchitecture,LastBootUpTime FROM Win32_OperatingSystem"))
        using (var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault())
        {
            if (item is not null)
            {
                result.OS = Convert.ToString(item["Caption"]) ?? "";
                result.OSVersion = Convert.ToString(item["Version"]) ?? "";
                result.BuildNumber = Convert.ToString(item["BuildNumber"]) ?? "";
                result.Architecture = Convert.ToString(item["OSArchitecture"]) ?? result.Architecture;
                var bootRaw = Convert.ToString(item["LastBootUpTime"]);
                if (!string.IsNullOrWhiteSpace(bootRaw))
                {
                    try { result.LastBoot = ManagementDateTimeConverter.ToDateTime(bootRaw); }
                    catch { result.LastBoot = DateTime.Now; }
                }
                if (result.LastBoot != default)
                    result.UptimeDays = Math.Round((DateTime.Now - result.LastBoot).TotalDays, 1);
            }
        }

        using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name FROM Win32_Processor"))
        using (var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault())
            result.Cpu = item is null ? "" : Convert.ToString(item["Name"]) ?? "";

        return result;
    }

    private static PerformanceSnapshot GetPerformanceSnapshot(CancellationToken ct)
    {
        const int sampleCount = 5;
        const int sampleDelayMs = 300;
        var cpuSamples = new List<double>(sampleCount);
        var diskBusySamples = new List<double>(sampleCount);
        var diskQueueSamples = new List<double>(sampleCount);

        for (var sample = 0; sample < sampleCount; sample++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'");
                using var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                if (item is not null)
                {
                    var value = ToDouble(item["PercentProcessorTime"]);
                    if (double.IsFinite(value)) cpuSamples.Add(Math.Clamp(value, 0d, 100d));
                }
            }
            catch { }

            try
            {
                using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT PercentDiskTime,CurrentDiskQueueLength FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'");
                using var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                if (item is not null)
                {
                    var busy = ToDouble(item["PercentDiskTime"]);
                    var queue = ToDouble(item["CurrentDiskQueueLength"]);
                    if (double.IsFinite(busy)) diskBusySamples.Add(Math.Max(0d, busy));
                    if (double.IsFinite(queue)) diskQueueSamples.Add(Math.Max(0d, queue));
                }
            }
            catch { }

            if (sample + 1 < sampleCount)
            {
                for (var wait = 0; wait < sampleDelayMs / 50; wait++)
                {
                    ct.ThrowIfCancellationRequested();
                    Thread.Sleep(50);
                }
            }
        }

        var result = new PerformanceSnapshot
        {
            CpuPercent = Median(cpuSamples),
            DiskBusyPercent = Median(diskBusySamples),
            DiskQueueLength = Median(diskQueueSamples)
        };

        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem");
            using var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
            if (item is not null)
            {
                var totalKb = ToDouble(item["TotalVisibleMemorySize"]);
                var freeKb = ToDouble(item["FreePhysicalMemory"]);
                if (totalKb > 0)
                {
                    result.MemoryAvailablePercent = Math.Round(freeKb / totalKb * 100d, 1);
                    result.MemoryUsedPercent = Math.Round(100d - result.MemoryAvailablePercent.Value, 1);
                    result.MemoryAvailableGB = Math.Round(freeKb / 1024d / 1024d, 1);
                }
            }
        }
        catch { }

        return result;
    }

    private static List<LogicalDiskInfo> GetLogicalDisks()
    {
        var list = new List<LogicalDiskInfo>();
        using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT DeviceID,VolumeName,FileSystem,Size,FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3");
        foreach (ManagementObject item in searcher.Get())
        {
            using (item)
            {
                var size = ToDouble(item["Size"]);
                var free = ToDouble(item["FreeSpace"]);
                list.Add(new LogicalDiskInfo
                {
                    Drive = Convert.ToString(item["DeviceID"]) ?? "",
                    VolumeName = Convert.ToString(item["VolumeName"]) ?? "",
                    FileSystem = Convert.ToString(item["FileSystem"]) ?? "",
                    SizeGB = Math.Round(size / 1024d / 1024d / 1024d, 1),
                    FreeGB = Math.Round(free / 1024d / 1024d / 1024d, 1),
                    FreePercent = size > 0 ? Math.Round(free / size * 100d, 1) : 0
                });
            }
        }
        return list;
    }

    private static List<PhysicalDiskInfo> GetPhysicalDisks()
    {
        var result = new List<PhysicalDiskInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\Microsoft\\Windows\\Storage", "SELECT FriendlyName,MediaType,Size,HealthStatus,OperationalStatus FROM MSFT_PhysicalDisk");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    result.Add(new PhysicalDiskInfo
                    {
                        Name = Convert.ToString(item["FriendlyName"]) ?? "",
                        MediaType = MapMediaType(ToInt(item["MediaType"])),
                        SizeGB = Math.Round(ToDouble(item["Size"]) / 1024d / 1024d / 1024d, 1),
                        HealthStatus = MapHealth(ToInt(item["HealthStatus"], 5)),
                        OperationalStatus = JoinArray(item["OperationalStatus"]),
                        Source = "MSFT_PhysicalDisk"
                    });
                }
            }
        }
        catch { }

        if (result.Count == 0)
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Model,MediaType,Size,Status FROM Win32_DiskDrive");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    var status = Convert.ToString(item["Status"]) ?? "Unknown";
                    result.Add(new PhysicalDiskInfo
                    {
                        Name = Convert.ToString(item["Model"]) ?? "",
                        MediaType = Convert.ToString(item["MediaType"]) ?? "",
                        SizeGB = Math.Round(ToDouble(item["Size"]) / 1024d / 1024d / 1024d, 1),
                        HealthStatus = status,
                        OperationalStatus = status,
                        Source = "Win32_DiskDrive"
                    });
                }
            }
        }
        return result;
    }

    private sealed class RawProcessSample
    {
        public int Pid { get; init; }
        public string Name { get; init; } = "";
        public TimeSpan Cpu { get; init; }
        public long WorkingSet { get; init; }
        public ulong IoBytes { get; init; }
    }

    private static List<ProcessInfo> GetProcessSamples(CancellationToken ct)
    {
        var first = CaptureProcesses();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 8; i++) { ct.ThrowIfCancellationRequested(); Thread.Sleep(100); }
        var second = CaptureProcesses();
        sw.Stop();
        var intervalMs = Math.Max(1, sw.Elapsed.TotalMilliseconds);
        var cpus = Math.Max(1, Environment.ProcessorCount);
        var list = new List<ProcessInfo>();

        foreach (var pair in second)
        {
            if (!first.TryGetValue(pair.Key, out var old)) continue;
            var cur = pair.Value;
            var cpuDeltaMs = Math.Max(0, (cur.Cpu - old.Cpu).TotalMilliseconds);
            var ioDelta = cur.IoBytes >= old.IoBytes ? cur.IoBytes - old.IoBytes : 0;
            list.Add(new ProcessInfo
            {
                Pid = cur.Pid,
                Name = cur.Name,
                CpuPercent = Math.Round(cpuDeltaMs / intervalMs / cpus * 100d, 1),
                MemoryMB = Math.Round(cur.WorkingSet / 1024d / 1024d, 1),
                IoMBPerSec = Math.Round(ioDelta / 1024d / 1024d / (intervalMs / 1000d), 2)
            });
        }
        return list;
    }

    private static Dictionary<int, RawProcessSample> CaptureProcesses()
    {
        var dict = new Dictionary<int, RawProcessSample>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                using (p)
                {
                    ulong io = 0;
                    try
                    {
                        if (GetProcessIoCounters(p.Handle, out var counters))
                            io = counters.ReadTransferCount + counters.WriteTransferCount + counters.OtherTransferCount;
                    }
                    catch { }
                    dict[p.Id] = new RawProcessSample
                    {
                        Pid = p.Id,
                        Name = p.ProcessName,
                        Cpu = p.TotalProcessorTime,
                        WorkingSet = p.WorkingSet64,
                        IoBytes = io
                    };
                }
            }
            catch { }
        }
        return dict;
    }

    private static PendingRebootInfo GetPendingReboot()
    {
        var r = new PendingRebootInfo();
        void CheckKey(string path, string reason)
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key is not null) r.Reasons.Add(reason);
        }
        try { CheckKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending", "Component Based Servicing"); } catch { }
        try { CheckKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", "Windows Update"); } catch { }
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            if (key?.GetValue("PendingFileRenameOperations") is not null) r.Reasons.Add("PendingFileRenameOperations");
        }
        catch { }
        r.Reasons = r.Reasons.Distinct().ToList();
        r.Pending = r.Reasons.Count > 0;
        return r;
    }

    private static EventSummary GetEvents(int hours)
    {
        var summary = new EventSummary { Hours = hours };
        var bucket = new Dictionary<string, EventGroup>(StringComparer.OrdinalIgnoreCase);
        var ms = (long)TimeSpan.FromHours(hours).TotalMilliseconds;
        foreach (var log in new[] { "System", "Application" })
        {
            try
            {
                var query = new EventLogQuery(log, PathType.LogName, $"*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= {ms}]]]" ) { ReverseDirection = true };
                using var reader = new EventLogReader(query);
                var read = 0;
                for (EventRecord? record = reader.ReadEvent(); record is not null && read < 2000; record = reader.ReadEvent())
                {
                    using (record)
                    {
                        read++;
                        if (record.Level == 1) summary.CriticalCount++; else summary.ErrorCount++;
                        var provider = record.ProviderName ?? "Unknown";
                        var key = $"{log}|{provider}|{record.Id}";
                        if (!bucket.TryGetValue(key, out var group))
                        {
                            group = new EventGroup { Log = log, Provider = provider, EventId = record.Id, LastSeen = record.TimeCreated };
                            bucket[key] = group;
                        }
                        group.Count++;
                        if (record.TimeCreated.HasValue && (!group.LastSeen.HasValue || record.TimeCreated.Value > group.LastSeen.Value)) group.LastSeen = record.TimeCreated;
                    }
                }
            }
            catch { }
        }
        summary.Top = bucket.Values.OrderByDescending(x => x.Count).ThenByDescending(x => x.LastSeen).Take(12).ToList();
        return summary;
    }

    private static List<StartupItem> GetStartupItems()
    {
        var list = new List<StartupItem>();
        using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name,Command,Location,User FROM Win32_StartupCommand");
        foreach (ManagementObject item in searcher.Get())
        {
            using (item)
            {
                list.Add(new StartupItem
                {
                    Name = Convert.ToString(item["Name"]) ?? "",
                    Command = Convert.ToString(item["Command"]) ?? "",
                    Location = Convert.ToString(item["Location"]) ?? "",
                    User = Convert.ToString(item["User"]) ?? ""
                });
            }
        }
        return list.OrderBy(x => x.Name).ToList();
    }

    private static List<SecurityProductInfo> GetSecurityProducts()
    {
        var list = new List<SecurityProductInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\SecurityCenter2", "SELECT displayName,pathToSignedProductExe,productState FROM AntiVirusProduct");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    var state = ToInt(item["productState"]);
                    list.Add(new SecurityProductInfo
                    {
                        Name = Convert.ToString(item["displayName"]) ?? "",
                        Path = Convert.ToString(item["pathToSignedProductExe"]) ?? "",
                        State = $"0x{state:X6}"
                    });
                }
            }
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name,DisplayName,State,PathName FROM Win32_Service");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    var name = Convert.ToString(item["Name"]) ?? "";
                    var display = Convert.ToString(item["DisplayName"]) ?? "";
                    var hay = name + " " + display;
                    if (!hay.Contains("Kaspersky", StringComparison.OrdinalIgnoreCase) &&
                        !hay.Contains("KES", StringComparison.OrdinalIgnoreCase) &&
                        !hay.Contains("KEDR", StringComparison.OrdinalIgnoreCase)) continue;
                    if (list.Any(x => string.Equals(x.Name, display, StringComparison.OrdinalIgnoreCase))) continue;
                    list.Add(new SecurityProductInfo
                    {
                        Name = display.Length > 0 ? display : name,
                        State = Convert.ToString(item["State"]) ?? "",
                        Path = Convert.ToString(item["PathName"]) ?? ""
                    });
                }
            }
        }
        catch { }
        return list;
    }

    private static List<NetworkAdapterInfo> GetNetworkAdapters()
    {
        var list = new List<NetworkAdapterInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            try
            {
                var ip = nic.GetIPProperties();
                var addresses = ip.UnicastAddresses.Where(a => a.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6).Select(a => a.Address.ToString());
                var gateways = ip.GatewayAddresses.Select(a => a.Address.ToString());
                var dns = ip.DnsAddresses.Select(a => a.ToString());
                list.Add(new NetworkAdapterInfo
                {
                    Name = nic.Name,
                    Type = nic.NetworkInterfaceType.ToString(),
                    Addresses = string.Join(", ", addresses),
                    Gateway = string.Join(", ", gateways),
                    Dns = string.Join(", ", dns),
                    SpeedMbps = nic.Speed > 0 ? nic.Speed / 1_000_000 : 0
                });
            }
            catch { }
        }
        return list;
    }

    private static UpdateInfo GetUpdateInfo()
    {
        var r = new UpdateInfo();
        r.Wuauserv = ServiceStatus("wuauserv");
        r.Bits = ServiceStatus("BITS");
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT HotFixID,InstalledOn,Description FROM Win32_QuickFixEngineering");
            var hotfixes = new List<(DateTime Date, string Text)>();
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    var id = Convert.ToString(item["HotFixID"]) ?? "";
                    var installed = Convert.ToString(item["InstalledOn"]) ?? "";
                    var desc = Convert.ToString(item["Description"]) ?? "";
                    var date = DateTime.MinValue;
                    DateTime.TryParse(installed, out date);
                    hotfixes.Add((date, $"{id} — {installed} — {desc}"));
                }
            }
            r.RecentHotfixes = hotfixes.OrderByDescending(x => x.Date).Take(10).Select(x => x.Text).ToList();
        }
        catch { }
        return r;
    }

    private static string ServiceStatus(string name)
    {
        try { using var sc = new ServiceController(name); return sc.Status.ToString(); }
        catch { return "Unknown"; }
    }

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static string? GetInteractiveUserProfile()
    {
        try
        {
            string? user;
            using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT UserName FROM Win32_ComputerSystem"))
            using (var item = searcher.Get().Cast<ManagementObject>().FirstOrDefault())
                user = item is null ? null : Convert.ToString(item["UserName"]);
            if (string.IsNullOrWhiteSpace(user)) return null;
            var sid = ((NTAccount)new NTAccount(user)).Translate(typeof(SecurityIdentifier)).Value;
            using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
            var path = Convert.ToString(key?.GetValue("ProfileImagePath"));
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = Environment.ExpandEnvironmentVariables(path);
            return Directory.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch { return null; }
    }

    private static int ToInt(object? value, int fallback = 0)
    {
        try { return Convert.ToInt32(value); } catch { return fallback; }
    }

    private static double ToDouble(object? value)
    {
        try { return Convert.ToDouble(value); } catch { return 0; }
    }

    private static double? Median(List<double> values)
    {
        if (values.Count == 0) return null;
        values.Sort();
        var middle = values.Count / 2;
        var median = values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2d;
        return Math.Round(median, 1);
    }

    private static string JoinArray(object? value)
    {
        if (value is Array a) return string.Join(", ", a.Cast<object?>().Select(x => Convert.ToString(x) ?? ""));
        return Convert.ToString(value) ?? "";
    }

    private static string MapHealth(int value) => value switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" };
    private static string MapMediaType(int value) => value switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Unspecified" };

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS lpIoCounters);
}
