namespace G.PcHealthCheck;

/// <summary>One system-volume selection rule for local diagnostics and presentation.</summary>
internal static class SystemDiskSelection
{
    // Environment.SystemDirectory is the Windows directory, not the working directory
    // or the drive containing the portable EXE. Never guess C: on lookup failure.
    public static string? CurrentDriveId => Normalize(Path.GetPathRoot(Environment.SystemDirectory));

    public static LogicalDiskInfo? Find(DiagnosticData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Select(data.LogicalDisks, CurrentDriveId);
    }

    // Pure selector: an explicit root can also be supplied by a future snapshot reader.
    // It neither reads the machine nor mutates the supplied disk records.
    public static LogicalDiskInfo? Select(IEnumerable<LogicalDiskInfo> disks, string? systemDrive)
    {
        ArgumentNullException.ThrowIfNull(disks);
        var root = Normalize(systemDrive);
        if (root is null) return null;
        LogicalDiskInfo? selected = null;
        foreach (var disk in disks)
        {
            if (disk is null || Normalize(disk.Drive) != root) continue;
            if (selected is not null) return null; // Ambiguous duplicate identity.
            selected = disk;
        }
        return selected is not null && IsValidMeasurement(selected) ? selected : null;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var root = value.Trim();
        if (root.Length == 3 && (root[2] == '\\' || root[2] == '/')) root = root[..2];
        if (root.Length != 2 || root[1] != ':') return null;
        var letter = char.ToUpperInvariant(root[0]);
        return letter is >= 'A' and <= 'Z' ? letter + ":" : null;
    }

    private static bool IsValidMeasurement(LogicalDiskInfo disk)
        => double.IsFinite(disk.SizeGB) && disk.SizeGB > 0
           && double.IsFinite(disk.FreeGB) && disk.FreeGB >= 0 && disk.FreeGB <= disk.SizeGB
           && double.IsFinite(disk.FreePercent) && disk.FreePercent is >= 0 and <= 100;
}
