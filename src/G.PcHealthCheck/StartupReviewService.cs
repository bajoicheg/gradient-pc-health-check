namespace G.PcHealthCheck;

internal static class StartupReviewService
{
    public static List<StartupReviewEntry> Filter(StartupReviewSnapshot snapshot, string? query)
        => throw new NotImplementedException("Startup review filtering is not implemented.");
    public static StartupReviewSnapshot CollectSources(IEnumerable<Func<CancellationToken, (ReviewSource Source, List<StartupReviewEntry> Entries)>> sources, CancellationToken ct = default)
        => throw new NotImplementedException("Startup review collection is not implemented.");
}
