namespace G.PcHealthCheck;

// Test-first contracts. Replaced after observing the failing behavior in Windows CI.
internal static class EndpointReviewCore
{
    public static EndpointTable Decode(byte[] bytes, string table, int limit = 5000) => throw new NotImplementedException();
    public static List<EndpointRow> Associate(IEnumerable<EndpointRow> rows, IEnumerable<EndpointProcess> before, IEnumerable<EndpointProcess> after) => throw new NotImplementedException();
    public static List<EndpointObservation> Compare(EndpointSnapshot current, EndpointSnapshot? previous) => throw new NotImplementedException();
    public static bool Matches(EndpointObservation row, string query, string filter) => throw new NotImplementedException();
}
internal static class EndpointReviewService
{
    public static EndpointSnapshot Collect(IEndpointSource source, ExecutionContextInfo? context, CancellationToken ct, IProgress<string>? progress = null) => throw new NotImplementedException();
}
internal static class EndpointReviewReport
{
    public static string Html(EndpointSnapshot current, EndpointSnapshot? previous) => throw new NotImplementedException();
    public static string Json(EndpointSnapshot current, EndpointSnapshot? previous) => throw new NotImplementedException();
    public static string Summary(EndpointSnapshot current, EndpointSnapshot? previous) => throw new NotImplementedException();
}
