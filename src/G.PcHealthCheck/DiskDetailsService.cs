namespace G.PcHealthCheck;

internal static class DiskDetailsService
{
    public static DiskDetailsSnapshot Collect(IPhysicalDiskDetailsSource source, CancellationToken ct, int maxDisks = 64, Func<long>? elapsedMs = null)
        => throw new NotImplementedException("Storage collection is not implemented.");
    public static void BindCounters(PhysicalDiskDetail disk, IEnumerable<DiskReliability> candidates)
        => throw new NotImplementedException("Storage association validation is not implemented.");
    public static string Attention(PhysicalDiskDetail disk) => throw new NotImplementedException("Storage evidence interpretation is not implemented.");
    public static string Describe(PhysicalDiskDetail disk) => throw new NotImplementedException("Storage explanation is not implemented.");
}
