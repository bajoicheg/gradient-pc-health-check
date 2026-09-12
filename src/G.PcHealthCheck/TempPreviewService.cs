namespace G.PcHealthCheck;

internal static class TempPreviewService
{
    // Test-first contract; no preview is available until the implementation commit.
    public static TempPreviewSnapshot Scan(string root, int olderThanDays, DateTime now,
        CancellationToken ct = default, int maxEntries = 100000, int maxRows = 200, TimeSpan? timeLimit = null)
        => throw new NotImplementedException("Temp preview is not implemented.");
}
