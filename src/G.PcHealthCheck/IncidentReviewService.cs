namespace G.PcHealthCheck;

internal static class IncidentQueries
{
    public static void Validate(IncidentWindow window) => throw new NotImplementedException("Incident window validation is not implemented.");
    public static string XPath(IncidentWindow window) => throw new NotImplementedException("Incident query is not implemented.");
    public static List<IncidentEvent> Events(IncidentSnapshot snapshot, IncidentFilter filter) => throw new NotImplementedException("Event filtering is not implemented.");
    public static List<ProcessReviewEntry> Processes(ProcessReviewSnapshot snapshot, string text) => throw new NotImplementedException("Process filtering is not implemented.");
}
internal sealed class IncidentEvents(IIncidentEventSource source)
{
    private readonly IIncidentEventSource _source = source;
    public IncidentSnapshot Collect(IncidentWindow window, CancellationToken ct) { _ = _source; throw new NotImplementedException("Event collection is not implemented."); }
}
internal sealed class ProcessReview(IProcessReviewSource source)
{
    private readonly IProcessReviewSource _source = source;
    public ProcessReviewSnapshot Collect(int maximum, CancellationToken ct) { _ = _source; throw new NotImplementedException("Process collection is not implemented."); }
    public ProcessOwnerEvidence Owner(ProcessReviewEntry selected, CancellationToken ct) { _ = _source; throw new NotImplementedException("Identity-checked owner lookup is not implemented."); }
}
internal static class IncidentReport
{
    public static string Json(object snapshot, object filter) => throw new NotImplementedException("Incident JSON is not implemented.");
    public static string Html(object snapshot, object filter) => throw new NotImplementedException("Incident HTML is not implemented.");
    public static string Summary(object snapshot, object filter) => throw new NotImplementedException("Incident summary is not implemented.");
}
