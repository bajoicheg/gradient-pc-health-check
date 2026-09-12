using System.Security.Cryptography;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class ReadOnlyReviewSelfTest
{
    public static int Run()
    {
        var failures = new List<string>(); var count = 0;
        void Test(string name, Action test)
        {
            count++;
            try { test(); } catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
        }
        var root = Path.Combine(Path.GetTempPath(), "GPcHealthCheck-ReviewTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var now = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Local);
        string Folder() { var p = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
        string FileAt(string folder, string name, int size, DateTime modified)
        {
            var path = Path.Combine(folder, name); File.WriteAllBytes(path, new byte[size]); File.SetLastWriteTime(path, modified); return path;
        }
        try
        {
            Test("empty folder is a complete zero estimate", () =>
            {
                var s = TempPreviewService.Scan(Folder(), 3, now);
                Require(s.State == ReviewCollectionState.Complete && s.CandidateFiles == 0 && s.CandidateBytes == 0, "Empty folder not reported as complete zero.");
            });
            Test("missing folder is distinct from inaccessible", () => Require(TempPreviewService.Scan(Path.Combine(root, "missing"), 3, now).State == ReviewCollectionState.Missing, "Missing root lost its state."));
            Test("file supplied as root is unavailable", () => Require(TempPreviewService.Scan(FileAt(Folder(), "root.txt", 1, now), 3, now).State == ReviewCollectionState.Unavailable, "Non-directory root accepted."));
            Test("strict age boundary agrees with CleanTemp", () =>
            {
                var p = Folder(); var cutoff = now.AddDays(-3);
                FileAt(p, "old", 11, cutoff.AddSeconds(-1)); FileAt(p, "boundary", 17, cutoff); FileAt(p, "fresh", 23, now);
                var s = TempPreviewService.Scan(p, 3, now);
                Require(s.CandidateFiles == 1 && s.CandidateBytes == 11 && s.Cutoff == cutoff, "Wrong age boundary.");
            });
            Test("nested and zero-byte candidates count", () =>
            {
                var p = Folder(); Directory.CreateDirectory(Path.Combine(p, "nested"));
                FileAt(p, "empty", 0, now.AddDays(-10)); FileAt(Path.Combine(p, "nested"), "old", 20, now.AddDays(-10));
                var s = TempPreviewService.Scan(p, 3, now);
                Require(s.CandidateFiles == 2 && s.CandidateBytes == 20, "Nested/empty files omitted.");
            });
            Test("hidden ordinary files remain in scope", () =>
            {
                var p = Folder(); var f = FileAt(p, "hidden", 7, now.AddDays(-10)); File.SetAttributes(f, FileAttributes.Hidden);
                Require(TempPreviewService.Scan(p, 3, now).CandidateBytes == 7, "Hidden file excluded unlike CleanTemp.");
            });
            Test("top rows bounded while totals remain complete", () =>
            {
                var p = Folder(); for (var i = 1; i <= 8; i++) FileAt(p, "file" + i, i * 10, now.AddDays(-10));
                var s = TempPreviewService.Scan(p, 3, now, maxRows: 3);
                Require(s.State == ReviewCollectionState.Complete && s.CandidateFiles == 8 && s.CandidateBytes == 360, "Top limit truncated totals.");
                Require(s.LargestFiles.Select(x => x.Bytes).SequenceEqual(new long[] { 80, 70, 60 }), "Largest files not ordered/capped.");
            });
            Test("equal-size order deterministic", () =>
            {
                var p = Folder(); FileAt(p, "b", 4, now.AddDays(-10)); FileAt(p, "a", 4, now.AddDays(-10));
                Require(Path.GetFileName(TempPreviewService.Scan(p, 3, now).LargestFiles[0].Path) == "a", "Tie order unstable.");
            });
            Test("entry limit marks partial estimate", () =>
            {
                var p = Folder(); for (var i = 0; i < 5; i++) FileAt(p, "f" + i, 4, now.AddDays(-10));
                var s = TempPreviewService.Scan(p, 3, now, maxEntries: 2);
                Require(s.State == ReviewCollectionState.Partial && s.VisitedEntries <= 2 && s.Issues.Count > 0, "Entry limit silently reported complete.");
            });
            Test("time limit marks partial estimate", () =>
            {
                var s = TempPreviewService.Scan(Folder(), 3, now, timeLimit: TimeSpan.Zero);
                Require(s.State == ReviewCollectionState.Partial && s.Issues.Count > 0, "Time limit not explicit.");
            });
            Test("precancelled scan throws cancellation", () => Throws<OperationCanceledException>(() => TempPreviewService.Scan(Folder(), 3, now, new CancellationToken(true))));
            Test("preview leaves contents and modification times untouched", () =>
            {
                var p = Folder(); var f = FileAt(p, "old", 19, now.AddDays(-7)); var stamp = File.GetLastWriteTimeUtc(f); var hash = SHA256.HashData(File.ReadAllBytes(f));
                TempPreviewService.Scan(p, 3, now);
                Require(File.Exists(f) && File.GetLastWriteTimeUtc(f) == stamp && SHA256.HashData(File.ReadAllBytes(f)).SequenceEqual(hash), "Preview modified a file.");
            });
            Test("directory link excluded without traversing target", () =>
            {
                var p = Folder(); var target = Folder(); FileAt(target, "outside", 99, now.AddDays(-10));
                var link = Path.Combine(p, "link"); Directory.CreateSymbolicLink(link, target);
                try { var s = TempPreviewService.Scan(p, 3, now); Require(s.CandidateFiles == 0 && s.SkippedLinks == 1, "Directory link traversed."); }
                finally { Directory.Delete(link); }
            });
            Test("file link excluded", () =>
            {
                var p = Folder(); var f = FileAt(Folder(), "outside", 99, now.AddDays(-10)); var link = Path.Combine(p, "link"); File.CreateSymbolicLink(link, f);
                try { Require(TempPreviewService.Scan(p, 3, now).SkippedLinks == 1, "File link accepted."); } finally { File.Delete(link); }
            });
            Test("linked root is unavailable", () =>
            {
                var link = Path.Combine(root, "root-link"); Directory.CreateSymbolicLink(link, Folder());
                try { Require(TempPreviewService.Scan(link, 3, now).State == ReviewCollectionState.Unavailable, "Linked root accepted."); } finally { Directory.Delete(link); }
            });
            foreach (var days in new[] { 0, -1, 31 }) Test("reject invalid age " + days, () => Throws<ArgumentOutOfRangeException>(() => TempPreviewService.Scan(Folder(), days, now)));
            Test("reject empty root", () => Throws<ArgumentException>(() => TempPreviewService.Scan("", 3, now)));
            Test("reject invalid scan limits", () => Throws<ArgumentOutOfRangeException>(() => TempPreviewService.Scan(Folder(), 3, now, maxEntries: 0)));

            var startup = new StartupReviewSnapshot { Account = "SYNTHETIC\\user", CollectedAt = now, State = ReviewCollectionState.Complete, Entries =
            [
                new() { Name = "VPN", Command = "\"C:\\Program Files\\VPN\\vpn.exe\" --start", Scope = "Все пользователи", Source = "HKLM Run [64]" },
                new() { Name = "Почта", Command = "%APPDATA%\\mail.exe", Scope = "SYNTHETIC\\user", Source = "HKCU RunOnce [64]" }
            ] };
            Test("startup empty search preserves rows", () => Require(StartupReviewService.Filter(startup, " ").Count == 2, "Empty search lost entries."));
            foreach (var query in new[] { "vpn", "PROGRAM FILES", "все пользователи", "hklm run", "не определено" })
                Test("startup searches visible field " + query, () => Require(StartupReviewService.Filter(startup, query).Count > 0, "Visible field not searchable."));
            Test("startup Unicode search", () => Require(StartupReviewService.Filter(startup, "ПОЧТА").Single().Name == "Почта", "Unicode search failed."));
            Test("startup search is literal and non-mutating", () =>
            {
                Require(StartupReviewService.Filter(startup, ".*").Count == 0, "Search treated input as regex.");
                Require(startup.Entries[1].Command == "%APPDATA%\\mail.exe" && startup.Entries.Count == 2, "Search mutated evidence.");
            });
            Test("startup missing source is expected absence", () =>
            {
                var s = StartupReviewService.CollectSources([_ => (new ReviewSource { Name = "Absent key", State = ReviewCollectionState.Missing }, new List<StartupReviewEntry>())]);
                Require(s.State == ReviewCollectionState.Complete && s.Sources.Count == 1, "Absent source treated as failed collection.");
            });
            Test("startup source failure retains other observations", () =>
            {
                var s = StartupReviewService.CollectSources([
                    _ => (new ReviewSource { Name = "OK", State = ReviewCollectionState.Complete }, startup.Entries),
                    _ => throw new UnauthorizedAccessException("synthetic failure")]);
                Require(s.State == ReviewCollectionState.Partial && s.Entries.Count == 2 && s.Sources.Count == 2 && s.Issues.Count > 0, "Partial source failure hid evidence.");
            });
            Test("startup all failed is unavailable not empty healthy", () =>
            {
                var s = StartupReviewService.CollectSources([_ => throw new IOException("synthetic")]);
                Require(s.State == ReviewCollectionState.Unavailable, "Failed collection reported healthy empty.");
            });
            Test("startup cancellation not swallowed", () => Throws<OperationCanceledException>(() => StartupReviewService.CollectSources([_ => throw new OperationCanceledException()])));
            Test("startup presence never implies enabled", () => Require(StartupReviewService.Filter(startup, null).All(x => x.State == "Не определено"), "Presence interpreted as enabled."));
            Test("startup HTML encodes arbitrary commands", () =>
            {
                var s = new StartupReviewSnapshot { Entries = [new() { Name = "<script>alert(1)</script>", Command = "<img src=x onerror=1>" }] };
                var html = ReviewReport.Html(s);
                Require(!html.Contains("<script>") && !html.Contains("<img src=x") && html.Contains("&lt;script&gt;"), "HTML permits injected markup.");
            });
            Test("JSON records type, schema and original command", () =>
            {
                using var json = JsonDocument.Parse(ReviewReport.Json(startup)); var r = json.RootElement;
                Require(r.GetProperty("SchemaVersion").GetInt32() == 1 && r.GetProperty("Kind").GetString() == "StartupReview", "Invalid report envelope.");
                Require(r.GetProperty("Snapshot").GetProperty("Entries")[1].GetProperty("Command").GetString() == "%APPDATA%\\mail.exe", "Raw command not preserved.");
            });
            Test("partial preview summary contains boundaries and no cleanup promise", () =>
            {
                var s = new TempPreviewSnapshot { Root = "SYNTHETIC", State = ReviewCollectionState.Partial, CandidateBytes = 1024, CandidateFiles = 1, SkippedLinks = 2, Errors = 3, Cutoff = now, Issues = ["test limit"] };
                var text = ReviewReport.Summary(s);
                Require(text.Contains("Неполный") && text.Contains("оценка") && text.Contains("test limit") && text.Contains("2") && text.Contains("3"), "Partial estimate or skips/errors missing.");
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Console.WriteLine($"Read-only inspection regression: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 190;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
