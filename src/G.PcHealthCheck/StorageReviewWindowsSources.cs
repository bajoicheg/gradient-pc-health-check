using System.Management;

namespace G.PcHealthCheck;

/// <summary>Streams file metadata only. File contents are not opened.</summary>
internal sealed class WindowsFolderUsageSource : IFolderUsageSource
{
    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

    public IEnumerable<FolderItem> ReadDirectory(string path)
    {
        var options = new System.IO.EnumerationOptions
        {
            RecurseSubdirectories = false, IgnoreInaccessible = false,
            AttributesToSkip = 0, ReturnSpecialDirectories = false
        };
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
            yield return Metadata(info);
    }

    private static FolderItem Metadata(FileSystemInfo info)
    {
        try
        {
            info.Refresh();
            var attributes = info.Attributes;
            if (!info.Exists) throw new FileNotFoundException("Запись исчезла во время обхода.");
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                return new(info.FullName, attributes);
            if (info is not FileInfo file)
                return new(info.FullName, attributes, Error: "Тип файловой записи не определён.");
            return new(info.FullName, attributes, file.Length, new DateTimeOffset(file.LastWriteTimeUtc));
        }
        catch (Exception ex)
        {
            return new(info.FullName, 0, Error: $"{ex.GetType().Name}, 0x{ex.HResult:X8}");
        }
    }
}

/// <summary>Reads local Windows Storage properties and associations; no device methods are invoked.</summary>
internal sealed class WindowsDiskDetailsSource : IPhysicalDiskDetailsSource
{
    private const string Scope = @"root\Microsoft\Windows\Storage";
    private const string PhysicalQuery =
        "SELECT DeviceId,FriendlyName,FirmwareVersion,Size,MediaType,BusType,HealthStatus,OperationalStatus FROM MSFT_PhysicalDisk";

    public IEnumerable<PhysicalDiskDetail> Read(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var searcher = new ManagementObjectSearcher(new ManagementScope(Scope), new ObjectQuery(PhysicalQuery), Options());
        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                ct.ThrowIfCancellationRequested();
                var disk = new PhysicalDiskDetail
                {
                    DeviceId = Text(item, "DeviceId"), Name = Text(item, "FriendlyName"),
                    Firmware = Text(item, "FirmwareVersion"), Size = Unsigned(item, "Size"),
                    MediaType = Integer(item, "MediaType"), BusType = Integer(item, "BusType"),
                    Health = Integer(item, "HealthStatus"), OperationalStatus = Integers(item, "OperationalStatus")
                };
                ReadCounters(item, disk, ct);
                ct.ThrowIfCancellationRequested();
                yield return disk;
            }
        }
    }

    private static void ReadCounters(ManagementObject item, PhysicalDiskDetail disk, CancellationToken ct)
    {
        try
        {
            // Query the explicit relationship on the original disk instance. Never join
            // independently enumerated lists by their order or by friendly display name.
            using var related = item.GetRelated(
                "MSFT_StorageReliabilityCounter", "MSFT_PhysicalDiskToStorageReliabilityCounter",
                null, null, "StorageReliabilityCounter", "PhysicalDisk", false, Options());
            var rows = new List<DiskReliability>();
            foreach (ManagementObject counter in related)
            {
                using (counter)
                {
                    ct.ThrowIfCancellationRequested();
                    rows.Add(new(Text(counter, "DeviceId"),
                        Integer(counter, "Temperature"), Integer(counter, "TemperatureMax"),
                        Integer(counter, "Wear"), Unsigned(counter, "PowerOnHours"),
                        Unsigned(counter, "ReadErrorsUncorrected"), Unsigned(counter, "WriteErrorsUncorrected")));
                    if (rows.Count == 2) break;
                }
            }
            ct.ThrowIfCancellationRequested();
            DiskDetailsService.BindCounters(disk, rows);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            disk.Reliability = null;
            disk.CounterState = "Failed";
            disk.Warnings.Add($"Связанные счётчики недоступны: {ex.GetType().Name}, 0x{ex.HResult:X8}.");
        }
    }

    private static System.Management.EnumerationOptions Options() => new()
    {
        ReturnImmediately = true, Rewindable = false, BlockSize = 1, Timeout = TimeSpan.FromSeconds(3)
    };
    private static object? Value(ManagementBaseObject item, string name)
    {
        try { return item.Properties[name]?.Value; }
        catch (ManagementException) { return null; }
    }
    private static string Text(ManagementBaseObject item, string name) => Value(item, name) as string ?? "";
    private static ulong? Unsigned(ManagementBaseObject item, string name) => Value(item, name) switch
    {
        byte x => x, ushort x => x, uint x => x, ulong x => x,
        sbyte x when x >= 0 => (ulong)x, short x when x >= 0 => (ulong)x,
        int x when x >= 0 => (ulong)x, long x when x >= 0 => (ulong)x, _ => null
    };
    private static int? Integer(ManagementBaseObject item, string name)
        => Unsigned(item, name) is ulong n && n <= int.MaxValue ? (int)n : null;
    private static int[] Integers(ManagementBaseObject item, string name) => Value(item, name) switch
    {
        ushort[] values => values.Select(x => (int)x).ToArray(),
        uint[] values => values.Where(x => x <= int.MaxValue).Select(x => (int)x).ToArray(),
        _ => []
    };
}
