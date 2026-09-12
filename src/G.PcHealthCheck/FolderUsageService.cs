namespace G.PcHealthCheck;

internal static class FolderUsageService
{
    public static string Validate(string root, FolderUsageOptions options) => throw new NotImplementedException("Folder options and paths are not implemented.");
    public static FolderUsageSnapshot Collect(string root, FolderUsageOptions options, IFolderUsageSource source,
        CancellationToken ct, IProgress<string>? progress = null, Func<long>? elapsedMs = null)
        => throw new NotImplementedException("Folder traversal and rollups are not implemented.");
}
