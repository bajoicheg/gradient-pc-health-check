using System.ComponentModel;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class FileUseSelfTest
{
    internal static readonly DateTimeOffset Stamp = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    internal static readonly ulong Start = (ulong)Stamp.UtcDateTime.ToFileTimeUtc();
    internal static FileUseProcess Row() => new() { Pid = 42, StartFileTime = Start, ApplicationName = "Synthetic app", ApplicationType = 1, SessionId = 2 };
    internal static FileUseSnapshot Snapshot(params FileUseProcess[] rows) => new()
    {
        ComputerName = "SYNTHETIC", TargetPath = @"C:\Test\файл.txt", StartedAt = Stamp, FinishedAt = Stamp.AddSeconds(1), State = "Complete", ListCompleted = true, Processes = rows.ToList(),
        ExecutionContext = new ExecutionContextInfo { ProcessAccount = "SYNTHETIC\\user", ProcessSid = "SYNTHETIC-SID", SessionId = 2, HasAdministratorToken = false, IsElevated = false, AdministratorMember = false }
    };
    public static int Run()
    {
        var n = 0; var failures = new List<string>();
        void Test(string name, Action test) { n++; try { test(); } catch (Exception ex) { failures.Add(name + ": " + ex.Message); } }
        foreach (var path in new[] { @"C:\Test\a.txt", @"D:\Новая папка\отчёт (1).txt", @"C:\Test\[a]&%.txt" })
            Test("absolute Unicode/spaces path " + path, () => Require(FileUseCore.NormalizeTarget(path) == path, "Valid path changed."));
        Test("quoted path is accepted", () => Require(FileUseCore.NormalizeTarget("  \"C:\\Test\\a.txt\"  ") == @"C:\Test\a.txt", "Quoted Explorer path not normalized."));
        foreach (var path in new[] { "", "a.txt", @"C:a.txt", @"\Test\a.txt", @"\\server\share\a.txt", @"\\?\C:\a.txt", @"\\.\pipe\test", "https://example.invalid/a", @"C:\a.txt:secret", @"C:\*.txt", @"C:\", "C:\\a\0.txt" })
            Test("reject unsupported target " + path, () => Throws<ArgumentException>(() => FileUseCore.NormalizeTarget(path)));
        Test("invalid FILETIME remains unknown", () => Require(FileUseCore.StartTime(0) is null && FileUseCore.StartTime(ulong.MaxValue) is null && FileUseCore.StartTime(Start) == Stamp, "Time guessed or shifted."));
        Test("verified process uses matching creation time", () =>
        { var original = Row(); var r = FileUseCore.Associate(original, new(42, Start, @"C:\Apps\Тест.exe")); Require(r.IdentityState == "Matched" && r.ProcessName == "Тест.exe" && original.ProcessName == "", "Match or immutability failed."); });
        Test("reused PID cannot inherit an executable", () =>
        { var r = FileUseCore.Associate(Row(), new(42, Start + 1, @"C:\Wrong.exe")); Require(r.IdentityState == "Changed" && r.ImagePath == "" && r.ApplicationName == "Synthetic app", "PID reuse misattributed."); });
        Test("different PID cannot match", () => Require(FileUseCore.Associate(Row(), new(43, Start, @"C:\Wrong.exe")).IdentityState != "Matched", "PID ignored."));
        Test("unavailable identity retains only RM evidence", () =>
        { var r = FileUseCore.Associate(Row(), new(42, 0, "", 5)); Require(r.IdentityState == "Unavailable" && r.IdentityError == 5 && r.ImagePath == "" && r.ApplicationName == "Synthetic app", "Denied data fabricated."); });
        Test("PID zero or missing start never matched", () => Require(FileUseCore.Associate(Row() with { StartFileTime = 0 }, new(42, 0, @"C:\Wrong.exe")).IdentityState != "Matched" && FileUseCore.Associate(Row() with { Pid = 0 }, new(0, Start, @"C:\Wrong.exe")).IdentityState != "Matched", "Invalid identity matched."));
        Test("service and critical session fields are not meaningful", () => Require(FileUseCore.Session(Row() with { ApplicationType = 3, SessionId = 0 }) == "—" && FileUseCore.Session(Row() with { ApplicationType = 1000, SessionId = 0 }) == "—" && FileUseCore.Session(Row() with { SessionId = uint.MaxValue }) == "—", "RM unused session shown as meaningful."));
        Test("literal case-insensitive search", () => Require(FileUseCore.Matches(Row() with { ServiceName = "СЛУЖБА [x]" }, "служба [x]") && FileUseCore.Matches(Row(), "42") && !FileUseCore.Matches(Row(), "not-present"), "Search wrong."));
        Test("complete empty list is not an unlocked verdict", () =>
        { var text = FileUseCore.Verdict(Snapshot()); Require(text.Contains("не сообщил", StringComparison.OrdinalIgnoreCase) && text.Contains("не доказывает", StringComparison.OrdinalIgnoreCase), "Empty list claims unlocked."); });
        Test("failed empty list is not no-users", () =>
        { var s = Snapshot(); s.State = "Unavailable"; s.ListCompleted = false; Require(!FileUseCore.Verdict(s).Contains("не сообщил", StringComparison.OrdinalIgnoreCase), "Failed query looks empty-successful."); });
        Test("normal lifecycle ends exactly once", () =>
        { var f = new Fake(); var s = Collect(f); Require(s.State == "Complete" && s.ListCompleted && s.Processes.Single().IdentityState == "Matched" && f.EndCalls == 1 && s.EndSessionCode == 0, "Lifecycle/evidence wrong."); });
        Test("zero-result completed query recorded", () =>
        { var f = new Fake(); f.Batches.Enqueue(new(0, 0, 0, 0, [])); var s = Collect(f); Require(s.State == "Complete" && s.ListCompleted && s.ReportedCount == 0 && f.EndCalls == 1, "Zero not complete."); });
        Test("MORE_DATA capacity retries then succeeds", () =>
        { var f = new Fake(); f.Batches.Enqueue(new(234, 2, 0, 0, [])); f.Batches.Enqueue(new(0, 2, 2, 1, [Row(), Row() with { ServiceName = "other-service" }])); var s = Collect(f); Require(s.Processes.Count == 2 && s.QueryAttempts == 2 && f.Capacities.SequenceEqual(new[] { 0, 2 }) && s.RebootReasons == 1 && f.EndCalls == 1, "Retry lost rows/native flags."); });
        Test("growth exhaustion is bounded unavailable", () =>
        { var f = new Fake { AlwaysMore = true }; var s = Collect(f); Require(!s.ListCompleted && s.State == "Unavailable" && s.QueryAttempts == 4 && f.EndCalls == 1 && s.Processes.Count == 0, "Unbounded retry or invalid partial list."); });
        Test("oversized list never allocated", () =>
        { var f = new Fake(); f.Batches.Enqueue(new(234, 1025, 0, 0, [])); var s = Collect(f); Require(s.State == "Unavailable" && f.Capacities.SequenceEqual(new[] { 0 }) && f.EndCalls == 1, "Native cap ignored."); });
        Test("error list rows are not trusted", () =>
        { var f = new Fake(); f.Batches.Enqueue(new(5, 1, 1, 0, [Row()])); var s = Collect(f); Require(s.ErrorCode == 5 && !s.ListCompleted && s.Processes.Count == 0 && f.EndCalls == 1, "Failed output treated as valid."); });
        Test("success count mismatch rejected", () =>
        { var f = new Fake(); f.Batches.Enqueue(new(234, 2, 0, 0, [])); f.Batches.Enqueue(new(0, 2, 2, 0, [Row()])); var s = Collect(f); Require(!s.ListCompleted && s.State == "Unavailable" && f.EndCalls == 1, "Malformed count accepted."); });
        Test("start failure does not end unknown session", () =>
        { var f = new Fake { StartCode = 353 }; var s = Collect(f); Require(s.State == "Unavailable" && s.ErrorCode == 353 && f.EndCalls == 0 && f.Capacities.Count == 0, "Start failure mishandled."); });
        Test("registration failure ends session", () =>
        { var f = new Fake { RegisterCode = 5 }; var s = Collect(f); Require(s.ErrorCode == 5 && f.EndCalls == 1 && f.Capacities.Count == 0, "Register failure leaked session."); });
        Test("missing target never starts RM", () =>
        { var f = new Fake { CheckFailure = new FileNotFoundException() }; var s = Collect(f); Require(s.State == "Unavailable" && f.StartCalls == 0 && f.EndCalls == 0, "Unavailable target queried as success."); });
        Test("cleanup failure does not erase completed list", () =>
        { var f = new Fake { EndCode = 6 }; var s = Collect(f); Require(s.ListCompleted && s.Processes.Count == 1 && s.EndSessionCode == 6 && s.State == "Partial" && s.Warnings.Count > 0, "Cleanup error hidden or list lost."); });
        Test("pre-cancel makes no provider calls", () =>
        { using var c = new CancellationTokenSource(); c.Cancel(); var f = new Fake(); Throws<OperationCanceledException>(() => FileUseService.Collect(f, @"C:\Test\a.txt", null, c.Token)); Require(f.StartCalls == 0, "Pre-cancel touched provider."); });
        Test("cancel after start still ends session", () =>
        { using var c = new CancellationTokenSource(); var f = new Fake { OnStart = c.Cancel }; var s = FileUseService.Collect(f, @"C:\Test\a.txt", null, c.Token); Require(s.State == "Cancelled" && !s.ListCompleted && f.EndCalls == 1, "Cancellation leaked session."); });
        Test("cancel after valid list preserves RM evidence", () =>
        { using var c = new CancellationTokenSource(); var f = new Fake { OnRead = c.Cancel }; var s = FileUseService.Collect(f, @"C:\Test\a.txt", null, c.Token); Require(s.State == "Cancelled" && s.ListCompleted && s.Processes.Count == 1 && f.EndCalls == 1, "Completed evidence lost at cancellation."); });
        Test("metadata exception does not turn list into failure", () =>
        { var f = new Fake { IdentityFailure = true }; var s = Collect(f); Require(s.ListCompleted && s.Processes.Count == 1 && s.Processes[0].IdentityState == "Unavailable" && f.EndCalls == 1, "Protected process erased source result."); });
        Test("exception during list still ends session", () =>
        { var f = new Fake { ReadFailure = true }; var s = Collect(f); Require(s.State == "Unavailable" && f.EndCalls == 1 && !s.ListCompleted, "List exception leaked session."); });
        Test("reports encode names and preserve both targets", () =>
        {
            var s = Snapshot(Row() with { ApplicationName = "<script>alert(1)</script>&" }); var previous = Snapshot(); previous.TargetPath = @"D:\Other.txt";
            var html = FileUseReport.Html(s, previous); Require(!html.Contains("<script>") && html.Contains("&lt;script&gt;") && html.Contains("Other.txt"), "Unsafe/lost HTML.");
            using var json = JsonDocument.Parse(FileUseReport.Json(s, previous)); Require(json.RootElement.GetProperty("Current").GetProperty("ListCompleted").GetBoolean() && json.RootElement.GetProperty("Previous").GetProperty("TargetPath").GetString() == previous.TargetPath, "Export loses raw context.");
        });
        Test("summary explains scope and account without unlock claim", () =>
        { var s = Snapshot(); var text = FileUseReport.Summary(s, null); Require(text.Contains("SYNTHETIC\\user") && text.Contains("Restart Manager") && text.Contains("не доказывает"), "Summary lacks semantics/context."); });
        Console.WriteLine($"File-use behavior: {n - failures.Count}/{n} passed."); foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 245;
    }
    private static FileUseSnapshot Collect(Fake source) => FileUseService.Collect(source, @"C:\Test\a.txt", Snapshot().ExecutionContext, CancellationToken.None);
    internal static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Fake : IFileUseSource
    {
        public uint StartCode { get; init; } public uint RegisterCode { get; init; } public uint EndCode { get; init; }
        public bool AlwaysMore { get; init; } public bool IdentityFailure { get; init; } public bool ReadFailure { get; init; }
        public Exception? CheckFailure { get; init; } public Action? OnStart { get; init; } public Action? OnRead { get; init; }
        public int StartCalls { get; private set; } public int EndCalls { get; private set; }
        public Queue<FileUseBatch> Batches { get; } = new(); public List<int> Capacities { get; } = [];
        public void CheckFile(string path, CancellationToken ct) { if (CheckFailure is not null) throw CheckFailure; }
        public uint StartSession(out uint handle) { StartCalls++; handle = 0; OnStart?.Invoke(); return StartCode; }
        public uint RegisterFile(uint handle, string path) => RegisterCode;
        public uint EndSession(uint handle) { EndCalls++; return EndCode; }
        public FileUseBatch ReadList(uint handle, int capacity)
        {
            Capacities.Add(capacity); if (ReadFailure) throw new IOException();
            // Mirror RmGetList: no nonempty successful output into a zero-capacity buffer.
            var batch = AlwaysMore ? new FileUseBatch(234, (uint)capacity + 1, 0, 0, [])
                : Batches.Count > 0 ? Batches.Dequeue()
                : capacity == 0 ? new FileUseBatch(234, 1, 0, 0, []) : new FileUseBatch(0, 1, 1, 0, [Row()]);
            if (batch.Code == 0) OnRead?.Invoke();
            return batch;
        }
        public FileUseIdentity? ReadIdentity(uint pid) => IdentityFailure ? throw new Win32Exception(5) : new(pid, Start, @"C:\Apps\Synthetic.exe");
    }
}
