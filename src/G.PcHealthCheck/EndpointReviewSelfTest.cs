using System.Buffers.Binary;
using System.Net;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class EndpointReviewSelfTest
{
    internal static DateTimeOffset Stamp => new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    internal static EndpointRow Row(uint pid = 42, uint state = 5) => new() { Table = "TCP4", Protocol = "TCP", Family = "IPv4", LocalAddress = "192.0.2.1", LocalPort = 1234, RemoteAddress = "198.51.100.1", RemotePort = 443, Pid = pid, StateCode = state, State = state == 5 ? "ESTABLISHED" : "CLOSE_WAIT" };
    internal static EndpointSnapshot Snapshot(params EndpointRow[] rows) => new()
    {
        ComputerName = "SYNTHETIC", StartedAt = Stamp, FinishedAt = Stamp.AddSeconds(1), State = "Complete",
        ExecutionContext = ExecutionContextSelfTest.User(),
        Tables = new[] { "TCP4", "TCP6", "UDP4", "UDP6" }.Select(x => new EndpointTable { Name = x, State = "Complete", Rows = rows.Where(r => r.Table == x).ToList(), ReportedCount = (uint)rows.Count(r => r.Table == x) }).ToList()
    };
    public static int Run()
    {
        var failures = new List<string>(); var count = 0;
        void Test(string name, Action test) { count++; try { test(); } catch (Exception ex) { failures.Add(name + ": " + ex.Message); } }
        foreach (var table in new[] { "TCP4", "TCP6", "UDP4", "UDP6" })
        {
            Test("native layout " + table, () =>
            {
                var result = EndpointReviewCore.Decode(Buffer(table), table); var r = result.Rows.Single();
                Require(result.State == "Complete" && result.ReportedCount == 1 && r.Pid == 4321 && r.LocalPort == 5353, "Layout or port/PID endian decoding failed.");
                Require(r.LocalAddress == (table.EndsWith('4') ? "192.0.2.7" : "fe80::7%9"), "Local address/scope failed.");
                if (table.StartsWith("TCP")) Require(r.RemotePort == 443 && r.State == "ESTABLISHED", "TCP state/remote port failed.");
                else Require(r.RemotePort is null && r.RemoteAddress is null && r.StateCode is null && r.State == "BOUND", "UDP acquired a remote peer or TCP state.");
            });
            Test("empty collected table " + table, () =>
            {
                var result = EndpointReviewCore.Decode(new byte[4], table);
                Require(result.State == "Complete" && result.Rows.Count == 0 && result.ReportedCount == 0, "Empty is not complete.");
            });
        }
        Test("LISTEN remote fields have no meaning", () =>
        {
            foreach (var table in new[] { "TCP4", "TCP6" })
            {
                var bytes = Buffer(table); Put(bytes, table == "TCP4" ? 4 : 52, 2);
                var r = EndpointReviewCore.Decode(bytes, table).Rows.Single();
                Require(r.State == "LISTEN" && r.RemoteAddress is null && r.RemotePort is null, "Listener remote fields were fabricated.");
            }
        });
        Test("unknown native state is preserved", () => { var b = Buffer("TCP4"); Put(b, 4, 999); Require(EndpointReviewCore.Decode(b, "TCP4").Rows[0].State == "UNKNOWN(999)", "Unknown state was guessed."); });
        foreach (var bytes in new[] { Array.Empty<byte>(), new byte[3], new byte[] { 255, 255, 255, 255 }, new byte[] { 1, 0, 0, 0 } })
            Test("invalid native buffer is rejected", () => Throws<InvalidDataException>(() => EndpointReviewCore.Decode(bytes, "TCP4")));
        Test("unknown source rejected", () => Throws<ArgumentException>(() => EndpointReviewCore.Decode(new byte[4], "TCP9")));
        Test("invalid limit rejected", () => Throws<ArgumentOutOfRangeException>(() => EndpointReviewCore.Decode(Buffer("TCP4"), "TCP4", 0)));
        Test("row cap retains count and marks partial", () =>
        {
            var b = Buffer("TCP4"); var two = new byte[52]; b.CopyTo(two, 0); b.AsSpan(4).CopyTo(two.AsSpan(28)); Put(two, 0, 2);
            var r = EndpointReviewCore.Decode(two, "TCP4", 1); Require(r.Rows.Count == 1 && r.ReportedCount == 2 && r.State == "Partial", "Truncation hidden.");
        });
        var owner = new EndpointProcess(42, "Synthetic", Stamp);
        Test("stable PID and start time attribute name", () =>
        {
            var original = Row(); var r = EndpointReviewCore.Associate([original], [owner], [owner]).Single();
            Require(r.ProcessName == "Synthetic" && r.ProcessStartedAt == Stamp && r.ProcessEvidence == "Stable" && original.ProcessName == "", "Incorrect attribution or mutation.");
        });
        Test("PID reuse cannot inherit earlier owner", () => Require(EndpointReviewCore.Associate([Row()], [owner], [owner with { StartedAt = Stamp.AddSeconds(1) }])[0].ProcessEvidence == "Changed", "PID reuse hidden."));
        Test("exited owner stays unknown", () => Require(EndpointReviewCore.Associate([Row()], [owner], [])[0].ProcessName == "", "Exited owner attributed."));
        Test("new process after tables stays unknown", () => Require(EndpointReviewCore.Associate([Row()], [], [owner])[0].ProcessName == "", "Late owner attributed."));
        Test("duplicate process identity is ambiguous", () => Require(EndpointReviewCore.Associate([Row()], [owner, owner], [owner])[0].ProcessName == "", "Duplicate owner guessed."));
        Test("PID zero is not idle process", () => Require(EndpointReviewCore.Associate([Row(0)], [owner with { Pid = 0 }], [owner with { Pid = 0 }])[0].ProcessEvidence == "Unavailable", "PID 0 attributed."));
        Test("first snapshot is a baseline", () => Require(EndpointReviewCore.Compare(Snapshot(Row()), null).Single().Change == "Baseline", "First snapshot called new connection."));
        Test("complete repeat detects appeared absent and state change", () =>
        {
            var previous = Snapshot(Row(), Row(43)); var current = Snapshot(Row(42, 8), Row(44)); current.StartedAt = Stamp.AddSeconds(2); current.FinishedAt = Stamp.AddSeconds(3);
            var rows = EndpointReviewCore.Compare(current, previous);
            Require(rows.Count == 3 && rows.Any(x => x.Change == "Changed" && x.PreviousState == "ESTABLISHED") && rows.Any(x => x.Change == "Appeared") && rows.Any(x => x.Change == "NotObserved"), "Comparison wrong.");
        });
        foreach (var state in new[] { "Unavailable", "Partial", "NotCollected" })
            Test("failed table cannot imply disappearance " + state, () =>
            {
                var previous = Snapshot(Row()); var current = Snapshot(); current.StartedAt = Stamp.AddSeconds(2); current.Tables[0].State = state;
                Require(!EndpointReviewCore.Compare(current, previous).Any(x => x.Change == "NotObserved"), "Missing collection turned into disappearance.");
            });
        Test("different host disables comparison", () =>
        { var p = Snapshot(Row()); var c = Snapshot(Row(44)); c.StartedAt = Stamp.AddSeconds(2); c.ComputerName = "OTHER"; Require(EndpointReviewCore.Compare(c, p).All(x => x.Change == "NotCompared"), "Cross-machine comparison allowed."); });
        Test("different actor disables comparison", () =>
        { var p = Snapshot(Row()); var c = Snapshot(Row(44)); c.StartedAt = Stamp.AddSeconds(2); c.ExecutionContext = ExecutionContextSelfTest.User() with { ProcessSid = "OTHER" }; Require(EndpointReviewCore.Compare(c, p).All(x => x.Change == "NotCompared"), "Cross-account comparison allowed."); });
        Test("unknown legacy context cannot compare", () =>
        { var p = Snapshot(Row()); var c = Snapshot(Row()); c.StartedAt = Stamp.AddSeconds(2); p.ExecutionContext = null; Require(EndpointReviewCore.Compare(c, p).Single().Change == "NotCompared", "Unknown actor guessed."); });
        Test("overlapping snapshots do not compare", () => Require(EndpointReviewCore.Compare(Snapshot(Row()), Snapshot(Row())).Single().Change == "NotCompared", "Unordered snapshots compared."));
        Test("duplicate endpoints are preserved not paired", () =>
        { var p = Snapshot(Row()); var c = Snapshot(Row(), Row()); c.StartedAt = Stamp.AddSeconds(2); var rows = EndpointReviewCore.Compare(c, p); Require(rows.Count == 2 && rows.All(x => x.Change == "Ambiguous"), "Duplicate tuples collapsed."); });
        Test("literal search and protocol filter", () =>
        { var row = new EndpointObservation(Row() with { ProcessName = "ТЕСТ [x]" }, "Appeared"); Require(EndpointReviewCore.Matches(row, "тест [x]", "All") && EndpointReviewCore.Matches(row, "443", "TCP") && !EndpointReviewCore.Matches(row, "", "UDP"), "Search/filter wrong."); });
        Test("collection preserves four independent source outcomes", () =>
        { var s = EndpointReviewService.Collect(new FakeSource { FailedTable = "TCP6" }, ExecutionContextSelfTest.User(), CancellationToken.None); Require(s.State == "Partial" && s.Tables.Count == 4 && s.Tables.Single(x => x.Name == "TCP6").State == "Unavailable" && s.Tables.Single(x => x.Name == "TCP4").Rows[0].ProcessEvidence == "Stable", "Source failure hid other results."); });
        Test("pre-cancellation does not read", () => { using var c = new CancellationTokenSource(); c.Cancel(); var source = new FakeSource(); Throws<OperationCanceledException>(() => EndpointReviewService.Collect(source, null, c.Token)); Require(source.Calls == 0, "Cancelled collector accessed provider."); });
        Test("mid-cancel retains completed tables only", () =>
        { using var c = new CancellationTokenSource(); var s = EndpointReviewService.Collect(new FakeSource { Cancel = c }, null, c.Token); Require(s.State == "Cancelled" && s.Tables[0].Rows.Count == 1 && s.Tables.Skip(1).All(x => x.State == "NotCollected"), "Partial cancellation misreported."); });
        Test("HTML and JSON preserve context snapshots and encode names", () =>
        {
            var s = Snapshot(Row() with { ProcessName = "<script>bad</script>&", ProcessEvidence = "Stable" });
            var html = EndpointReviewReport.Html(s, null); Require(!html.Contains("<script>") && html.Contains("&lt;script&gt;") && html.Contains("SYNTHETIC"), "HTML unsafe or missing context.");
            using var json = JsonDocument.Parse(EndpointReviewReport.Json(s, null)); Require(json.RootElement.GetProperty("Current").GetProperty("Tables").GetArrayLength() == 4 && json.RootElement.GetProperty("Previous").ValueKind == JsonValueKind.Null, "Export envelope loses source state.");
        });
        Test("summary qualifies UDP listeners and snapshot meaning", () => { var text = EndpointReviewReport.Summary(Snapshot(Row()), null); Require(text.Contains("UDP") && text.Contains("снимк", StringComparison.OrdinalIgnoreCase) && text.Contains("SYNTHETIC\\user"), "Summary lacks meaning/context."); });
        Console.WriteLine($"Endpoint review regression: {count - failures.Count}/{count} passed."); foreach (var f in failures) Console.Error.WriteLine("FAIL: " + f);
        return failures.Count == 0 ? 0 : 243;
    }
    internal static byte[] Buffer(string table)
    {
        var size = table switch { "TCP4" => 24, "TCP6" => 56, "UDP4" => 12, "UDP6" => 28, _ => throw new ArgumentException() };
        var b = new byte[4 + size]; Put(b, 0, 1); var v6 = table.EndsWith('6'); var tcp = table.StartsWith("TCP"); var start = 4;
        if (tcp && !v6) { Put(b, 4, 5); start += 4; }
        var address = IPAddress.Parse(v6 ? "fe80::7" : "192.0.2.7").GetAddressBytes(); address.CopyTo(b, start);
        var port = start + address.Length;
        if (v6) { BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(port, 4), 9); port += 4; }
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(port, 2), 5353);
        if (tcp)
        {
            var remote = IPAddress.Parse(v6 ? "2001:db8::8" : "198.51.100.8").GetAddressBytes(); remote.CopyTo(b, port + 4);
            var rp = port + 4 + remote.Length + (v6 ? 4 : 0); BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(rp, 2), 443);
            if (v6) Put(b, 52, 5);
        }
        Put(b, b.Length - 4, 4321); return b;
    }
    internal static void Put(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    internal static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class FakeSource : IEndpointSource
    {
        public string FailedTable { get; init; } = "";
        public CancellationTokenSource? Cancel { get; init; }
        public int Calls { get; private set; }
        public List<EndpointProcess> ReadProcesses(CancellationToken ct, List<string> warnings) { Calls++; return [new(42, "Synthetic", Stamp)]; }
        public EndpointTable ReadTable(string name, CancellationToken ct)
        {
            Calls++; if (name == FailedTable) throw new UnauthorizedAccessException();
            var table = new EndpointTable { Name = name, State = "Complete", ReportedCount = 1, Rows = [Row() with { Table = name }] };
            Cancel?.Cancel(); return table;
        }
    }
}
