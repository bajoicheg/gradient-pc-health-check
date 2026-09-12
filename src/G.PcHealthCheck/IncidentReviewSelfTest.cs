using System.Text.Json;

namespace G.PcHealthCheck;

internal static class IncidentReviewSelfTest
{
    public static int Run()
    {
        var failures = new List<string>(); var count = 0;
        void Test(string name, Action action) { count++; try { action(); } catch (Exception ex) { failures.Add(name + ": " + ex.Message); } }
        var from = DateTimeOffset.Parse("2026-01-01T10:00:00+03:00"); var window = new IncidentWindow(from, from.AddHours(1), 2);
        IncidentEvent E(int id, int level = 2, string provider = "Synthetic") => new() { Log = "Application", EventId = id, Level = level, Provider = provider, Timestamp = from.AddMinutes(id % 50), RecordId = id, Message = "Synthetic text" };
        ProcessReviewEntry P(uint pid = 7, string key = "start-a") => new() { Pid = pid, CreationKey = key, Name = "Synthetic.exe", CommandLine = "<literal>& %USERPROFILE%", WorkingSetBytes = 100 };
        Test("valid interval", () => IncidentQueries.Validate(window));
        foreach (var invalid in new[] { new IncidentWindow(from, from), new IncidentWindow(from, from.AddMinutes(-1)), new IncidentWindow(from, from.AddDays(8)), new IncidentWindow(from, from.AddHours(1), 0), new IncidentWindow(from, from.AddHours(1), 5001) })
            Test("reject invalid interval " + invalid, () => Throws<ArgumentException>(() => IncidentQueries.Validate(invalid)));
        Test("query uses UTC and fixed timestamp-only syntax", () => { var x = IncidentQueries.XPath(window); Require(x.Contains("2026-01-01T07:00:00", StringComparison.Ordinal) && x.Contains("2026-01-01T08:00:00", StringComparison.Ordinal) && x.Contains("SystemTime", StringComparison.Ordinal), "UTC window not preserved."); });
        Test("two fixed sources even when empty", () => { var f = new EventsFake(); var s = new IncidentEvents(f).Collect(window, default); Require(f.Calls.SequenceEqual(new[] { "Application", "System" }) && s.Logs.Count == 2 && s.State == "Complete", "Fixed source or empty-result semantics changed."); });
        Test("inclusive time boundaries", () => { var a = E(1); a.Timestamp = window.From; var b = E(2); b.Timestamp = window.To; var f = new EventsFake { Rows = _ => [a, b] }; Require(new IncidentEvents(f).Collect(window, default).Logs.All(x => x.Events.Count == 2 && x.State == "Complete"), "Boundary events lost."); });
        Test("source cap is partial and preserves first records", () => { var s = new IncidentEvents(new EventsFake { Rows = _ => [E(1), E(2), E(3)] }).Collect(window, default); Require(s.State == "Partial" && s.Logs.All(x => x.Events.Count == 2 && x.Warnings.Count > 0), "Cap was hidden."); });
        Test("one denied log does not hide the other", () => { var s = new IncidentEvents(new EventsFake { Rows = l => l == "System" ? throw new UnauthorizedAccessException() : [E(1)] }).Collect(window, default); Require(s.State == "Partial" && s.Logs[0].Events.Count == 1 && s.Logs[1].State == "Unavailable", "Denied source corrupted complete source."); });
        Test("both denied logs are unavailable", () => { var s = new IncidentEvents(new EventsFake { Rows = _ => throw new UnauthorizedAccessException() }).Collect(window, default); Require(s.State == "Unavailable", "No evidence became complete."); });
        Test("mid-enumeration error retains observed events", () => { var s = new IncidentEvents(new EventsFake { Rows = _ => FailAfter(E(1)) }).Collect(window, default); Require(s.State == "Partial" && s.Logs.All(x => x.Events.Count == 1), "Partial rows lost."); });
        Test("missing event timestamp not silently in window", () => { var e = E(1); e.Timestamp = null; var s = new IncidentEvents(new EventsFake { Rows = _ => [e] }).Collect(window, default); Require(s.State == "Partial" && s.Logs.All(x => x.Events.Count == 0 && x.Warnings.Count > 0), "Unknown time accepted."); });
        Test("out of interval row excluded explicitly", () => { var e = E(1); e.Timestamp = from.AddDays(-1); var s = new IncidentEvents(new EventsFake { Rows = _ => [e] }).Collect(window, default); Require(s.State == "Partial" && s.Logs.All(x => x.Events.Count == 0), "Out of range event accepted."); });
        Test("unavailable message retains metadata and partial status", () => { var e = E(1); e.MessageState = "Unavailable"; var s = new IncidentEvents(new EventsFake { Rows = _ => [e] }).Collect(window, default); Require(s.State == "Partial" && s.Logs[0].Events.Count == 1, "Formatting failure hid event or completeness."); });
        Test("cancelled before collection makes no provider calls", () => { var f = new EventsFake(); var s = new IncidentEvents(f).Collect(window, new CancellationToken(true)); Require(s.State == "Cancelled" && f.Calls.Count == 0, "Cancelled request called provider."); });
        Test("cancellation retains events and marks attempt", () => { using var c = new CancellationTokenSource(); var f = new EventsFake { Rows = _ => CancelAfter(E(1), c) }; var s = new IncidentEvents(f).Collect(window, c.Token); Require(s.State == "Cancelled" && s.Logs[0].Events.Count == 1 && f.Calls.Count == 1, "Cancellation hid partial evidence or read next source."); });
        var snapshot = new IncidentSnapshot { State = "Complete", Window = window, Logs = [new IncidentLogResult { Log = "Application", State = "Complete", Events = [E(1, 4, "One"), E(2, 2, "Two"), E(3, 3, "One")] }] };
        Test("events sorted newest first", () => Require(IncidentQueries.Events(snapshot, new()).Select(x => x.EventId).SequenceEqual(new[] { 3, 2, 1 }), "Chronological order wrong."));
        Test("literal provider text filter", () => Require(IncidentQueries.Events(snapshot, new("oNe")).Count == 2, "Case-insensitive literal filter failed."));
        Test("exact numeric ID and warning level compose", () => Require(IncidentQueries.Events(snapshot, new(EventId: 2, WarningsOnly: true)).Single().EventId == 2, "Combined filters failed."));
        Test("log filter is exact", () => Require(IncidentQueries.Events(snapshot, new(Log: "System")).Count == 0, "Channel filter ignored."));
        Test("warnings exclude informational and unknown levels", () => Require(IncidentQueries.Events(snapshot, new(WarningsOnly: true)).Count == 2, "Level filter wrong."));
        Test("filter input is not regex or XPath", () => Require(IncidentQueries.Events(snapshot, new(".*[System]'")).Count == 0, "Search interpreted as expression."));
        Test("filter does not mutate snapshot", () => { IncidentQueries.Events(snapshot, new()); Require(snapshot.Logs[0].Events[0].EventId == 1, "Sorting mutated source."); });
        Test("process inventory", () => { var s = new ProcessReview(new ProcessesFake { Rows = [P()] }).Collect(2, default); Require(s.State == "Complete" && s.Processes.Count == 1, "Process data lost."); });
        Test("process limit remains partial", () => { var s = new ProcessReview(new ProcessesFake { Rows = [P(1), P(2), P(3)] }).Collect(2, default); Require(s.State == "Partial" && s.Processes.Count == 2 && s.Warnings.Count > 0, "Process cap hidden."); });
        Test("process access failure is unavailable", () => Require(new ProcessReview(new ProcessesFake { FailRead = true }).Collect(2, default).State == "Unavailable", "Denied inventory became empty success."));
        Test("process cancellation no read", () => { var f = new ProcessesFake(); var s = new ProcessReview(f).Collect(2, new CancellationToken(true)); Require(s.State == "Cancelled" && f.ReadCalls == 0, "Cancelled inventory made calls."); });
        Test("invalid process limit rejected", () => Throws<ArgumentException>(() => new ProcessReview(new ProcessesFake()).Collect(0, default)));
        Test("owner checks identity twice", () => { var f = new ProcessesFake { Current = P() }; var r = new ProcessReview(f).Owner(P(), default); Require(r.State == "Verified" && r.Owner == "SYNTHETIC\\user" && f.LookupCalls == 2 && f.OwnerCalls == 1, "Owner not identity bound."); });
        Test("missing creation key prevents owner call", () => { var f = new ProcessesFake { Current = P() }; Require(new ProcessReview(f).Owner(P(key: ""), default).State == "Unverified" && f.OwnerCalls == 0, "Unknown identity inherited owner."); });
        Test("exited process prevents owner call", () => { var f = new ProcessesFake(); Require(new ProcessReview(f).Owner(P(), default).State == "Stale" && f.OwnerCalls == 0, "Exited process looked current."); });
        Test("PID reused before lookup", () => { var f = new ProcessesFake { Current = P(key: "new") }; Require(new ProcessReview(f).Owner(P(), default).State == "Stale" && f.OwnerCalls == 0, "Reused PID inherited owner."); });
        Test("PID reused during lookup discards owner", () => { var f = new ProcessesFake { Current = P(), ChangeAfterOwner = true }; var r = new ProcessReview(f).Owner(P(), default); Require(r.State == "Stale" && r.Owner.Length == 0, "Racing owner retained."); });
        Test("owner access denied is not blank success", () => { var f = new ProcessesFake { Current = P(), DenyOwner = true }; Require(new ProcessReview(f).Owner(P(), default).State == "Unavailable", "Denied owner became verified."); });
        Test("owner cancellation prevents lookup", () => { var f = new ProcessesFake { Current = P() }; Require(new ProcessReview(f).Owner(P(), new CancellationToken(true)).State == "Cancelled" && f.LookupCalls == 0, "Cancelled owner queried WMI."); });
        Test("owner read does not modify selected metadata", () => { var p = P(); new ProcessReview(new ProcessesFake { Current = P() }).Owner(p, default); Require(p.CreationKey == "start-a" && p.CommandLine == "<literal>& %USERPROFILE%", "Owner lookup mutated record."); });
        Test("process search preserves raw command", () => { var s = new ProcessReviewSnapshot { Processes = [P()] }; var p = IncidentQueries.Processes(s, "%userprofile%").Single(); Require(p.CommandLine == "<literal>& %USERPROFILE%", "Command was expanded."); });
        Test("process PID searchable", () => Require(IncidentQueries.Processes(new ProcessReviewSnapshot { Processes = [P(987)] }, "987").Count == 1, "PID search failed."));
        Test("HTML encodes event provider and message", () => { var e = E(1, 2, "<script>"); e.Message = "<img src=x onerror=alert(1)> &"; var s = new IncidentSnapshot { Window = window, Logs = [new IncidentLogResult { Events = [e] }] }; var html = IncidentReport.Html(s, new IncidentFilter()); Require(!html.Contains("<script>", StringComparison.Ordinal) && html.Contains("&lt;script&gt;", StringComparison.Ordinal) && html.Contains("&lt;img", StringComparison.Ordinal), "HTML injection."); });
        Test("JSON includes full snapshot despite filter", () => { using var j = JsonDocument.Parse(IncidentReport.Json(snapshot, new IncidentFilter("no match"))); Require(j.RootElement.GetProperty("SchemaVersion").GetInt32() == 1 && j.RootElement.GetProperty("Snapshot").GetProperty("Logs")[0].GetProperty("Events").GetArrayLength() == 3 && j.RootElement.GetProperty("VisibleCount").GetInt32() == 0, "Export dropped hidden evidence."); });
        Test("summary states historical PID boundary", () => { var text = IncidentReport.Summary(snapshot, new IncidentFilter()); Require(text.Contains("PID", StringComparison.Ordinal) && text.Contains("причин", StringComparison.Ordinal), "Correlation limitation absent."); });
        Test("process HTML encodes command", () => { var html = IncidentReport.Html(new ProcessReviewSnapshot { Processes = [P()] }, ""); Require(!html.Contains("<literal>", StringComparison.Ordinal) && html.Contains("&lt;literal&gt;", StringComparison.Ordinal), "Process command became HTML."); });
        Console.WriteLine($"Incident review regression: {count - failures.Count}/{count} passed."); foreach (var f in failures) Console.Error.WriteLine("FAIL: " + f);
        return failures.Count == 0 ? 0 : 210;
    }
    private static IEnumerable<IncidentEvent> FailAfter(IncidentEvent e) { yield return e; throw new UnauthorizedAccessException(); }
    private static IEnumerable<IncidentEvent> CancelAfter(IncidentEvent e, CancellationTokenSource c) { yield return e; c.Cancel(); c.Token.ThrowIfCancellationRequested(); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action a) where T : Exception { try { a(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class EventsFake : IIncidentEventSource
    {
        public List<string> Calls { get; } = []; public Func<string, IEnumerable<IncidentEvent>> Rows { get; set; } = _ => [];
        public IEnumerable<IncidentEvent> Read(string log, IncidentWindow window, CancellationToken ct) { Calls.Add(log); return Rows(log); }
    }
    private sealed class ProcessesFake : IProcessReviewSource
    {
        public List<ProcessReviewEntry> Rows { get; set; } = []; public ProcessReviewEntry? Current { get; set; }
        public bool FailRead { get; set; } public bool ChangeAfterOwner { get; set; } public bool DenyOwner { get; set; }
        public int ReadCalls { get; private set; } public int LookupCalls { get; private set; } public int OwnerCalls { get; private set; }
        public IEnumerable<ProcessReviewEntry> Read(CancellationToken ct) { ReadCalls++; return FailRead ? throw new UnauthorizedAccessException() : Rows; }
        public ProcessReviewEntry? Lookup(uint pid, CancellationToken ct) { LookupCalls++; return Current; }
        public string ReadOwner(uint pid, CancellationToken ct) { OwnerCalls++; if (DenyOwner) throw new UnauthorizedAccessException(); if (ChangeAfterOwner) Current = new ProcessReviewEntry { Pid = pid, CreationKey = "new" }; return "SYNTHETIC\\user"; }
    }
}
