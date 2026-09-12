using System.Text.Json;

namespace G.PcHealthCheck;

internal static class StorageReviewSelfTest
{
    public static int Run()
    {
        var failures = new List<string>(); var count = 0;
        void Test(string name, Action action)
        {
            count++;
            try { action(); } catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
        }
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Synthetic-Storage-Review"));
        string P(string name) => Path.Combine(root, name);
        FolderUsageSnapshot Scan(FakeFolder source, FolderUsageOptions? options = null, CancellationToken ct = default, Func<long>? clock = null)
            => FolderUsageService.Collect(root, options ?? new(), source, ct, elapsedMs: clock);
        Test("valid absolute root", () => Require(FolderUsageService.Validate(root, new()) == root));
        foreach (var path in new[] { "", "relative", "C:relative" })
            Test("reject relative root " + path, () => ThrowsArgument(() => FolderUsageService.Validate(path, new())));
        foreach (var options in new[] { new FolderUsageOptions(MaxEntries: 0), new FolderUsageOptions(MaxDirectories: 0), new FolderUsageOptions(MaxSeconds: 0), new FolderUsageOptions(TopFiles: 0), new FolderUsageOptions(MaxEntries: 1000001) })
            Test("reject options " + options, () => ThrowsArgument(() => FolderUsageService.Validate(root, options)));
        Test("empty folder is zero not unavailable", () =>
        {
            var s = Scan(new()); Require(s.Outcome == "Completed" && s.Folders.Count == 1 && s.Folders[0].Bytes == 0 && !s.Folders[0].Incomplete);
        });
        Test("nested rollup does not double count", () =>
        {
            var f = new FakeFolder(); f.Data[root] = [File(P("a"), 10), Dir(P("child"))]; f.Data[P("child")] = [File(P("child/b"), 30)];
            var s = Scan(f); Require(s.Folders[0].Bytes == 40 && s.Folders[0].OwnBytes == 10 && s.Folders[0].Files == 2 && s.Folders[1].Bytes == 30);
        });
        Test("top files bounded and ordered", () =>
        {
            var f = new FakeFolder(); f.Data[root] = [File(P("c"), 20), File(P("b"), 30), File(P("a"), 30)];
            var s = Scan(f, new(TopFiles: 2)); Require(s.LargestFiles.Select(x => x.Path).SequenceEqual(new[] { P("a"), P("b") }) && s.Folders[0].Bytes == 80);
        });
        Test("zero-length file counted", () =>
        {
            var f = new FakeFolder(); f.Data[root] = [File(P("empty"), 0)]; var s = Scan(f); Require(s.Folders[0].Files == 1 && s.Folders[0].Bytes == 0 && s.LargestFiles.Count == 1);
        });
        foreach (var length in new long?[] { null, -1 })
            Test("unavailable file size " + length, () =>
            {
                var f = new FakeFolder(); f.Data[root] = [new(P("bad"), FileAttributes.Normal, length)]; var s = Scan(f);
                Require(s.Outcome == "Partial" && s.Errors == 1 && s.Folders[0].Files == 0);
            });
        Test("large aggregate avoids Int64 overflow", () =>
        {
            var f = new FakeFolder(); f.Data[root] = [File(P("a"), long.MaxValue), File(P("b"), long.MaxValue)];
            Require(Scan(f).Folders[0].Bytes == (decimal)long.MaxValue * 2);
        });
        Test("root link never enumerated", () =>
        {
            var f = new FakeFolder(); f.Attributes[root] = FileAttributes.Directory | FileAttributes.ReparsePoint;
            var s = Scan(f); Require(s.Outcome == "Failed" && f.Reads == 0);
        });
        Test("linked ancestor never enumerated", () =>
        {
            var f = new FakeFolder(); f.Attributes[Path.GetDirectoryName(root)!] = FileAttributes.Directory | FileAttributes.ReparsePoint;
            Require(Scan(f).Outcome == "Failed" && f.Reads == 0);
        });
        Test("child link and linked file excluded", () =>
        {
            var f = new FakeFolder(); f.Data[root] = [new(P("link"), FileAttributes.Directory | FileAttributes.ReparsePoint), new(P("filelink"), FileAttributes.ReparsePoint, 999), File(P("a"), 1)];
            var s = Scan(f); Require(s.SkippedLinks == 2 && s.Folders[0].Bytes == 1 && f.Reads == 1 && s.Outcome == "Partial");
        });
        Test("directory swapped to a link is rechecked", () =>
        {
            var f = new FakeFolder(); f.Data[root] = [Dir(P("swap"))]; f.Attributes[P("swap")] = FileAttributes.Directory | FileAttributes.ReparsePoint;
            var s = Scan(f); Require(s.SkippedLinks == 1 && f.Reads == 1 && s.Outcome == "Partial");
        });
        Test("inaccessible child preserves other bytes", () =>
        {
            var f = new FakeFolder(); f.Data[root] = [Dir(P("denied")), File(P("a"), 123)]; f.Denied.Add(P("denied"));
            var s = Scan(f); Require(s.Outcome == "Partial" && s.Folders[0].Bytes == 123 && s.Errors > 0 && s.Folders[0].Incomplete);
        });
        Test("late iterator failure preserves earlier files", () =>
        {
            var f = new FakeFolder(); f.Data[root] = [File(P("a"), 50)]; f.FailAfter.Add(root);
            var s = Scan(f); Require(s.Outcome == "Partial" && s.Folders[0].Bytes == 50);
        });
        Test("per-file error does not stop directory", () =>
        {
            var f = new FakeFolder(); f.Data[root] = [new(P("bad"), FileAttributes.Normal, Error: "denied"), File(P("good"), 4)];
            var s = Scan(f); Require(s.Errors == 1 && s.Folders[0].Bytes == 4);
        });
        Test("unexpected outside entry rejected", () =>
        {
            var f = new FakeFolder(); f.Data[root] = [File(Path.Combine(Path.GetDirectoryName(root)!, "outside"), 999)];
            var s = Scan(f); Require(s.Errors > 0 && s.Folders[0].Bytes == 0 && s.Outcome == "Partial");
        });
        Test("entry limit retains partial totals", () =>
        {
            var f = new FakeFolder(); f.Data[root] = [File(P("a"), 1), File(P("b"), 2), File(P("c"), 3)];
            var s = Scan(f, new(MaxEntries: 2)); Require(s.EntriesVisited == 2 && s.Folders[0].Bytes == 3 && s.Outcome == "Partial");
        });
        Test("directory limit bounds memory", () =>
        {
            var f = new FakeFolder(); f.Data[root] = [Dir(P("a")), Dir(P("b"))];
            var s = Scan(f, new(MaxDirectories: 2)); Require(s.Folders.Count == 2 && s.Outcome == "Partial");
        });
        Test("pre-cancel makes no reads", () =>
        {
            var f = new FakeFolder(); var s = Scan(f, ct: new CancellationToken(true)); Require(s.Outcome == "Stopped" && f.Reads == 0);
        });
        Test("cancel during iterator preserves processed bytes", () =>
        {
            using var c = new CancellationTokenSource(); var f = new FakeFolder(); f.Data[root] = [File(P("a"), 5), File(P("b"), 10)];
            f.AfterFirst = c.Cancel; var s = Scan(f, ct: c.Token); Require(s.Outcome == "Stopped" && s.Folders[0].Bytes == 5);
        });
        Test("cooperative deadline prevents enumeration", () =>
        {
            var f = new FakeFolder(); var s = Scan(f, clock: () => 120001); Require(s.Outcome == "Partial" && f.Reads == 0);
        });
        Test("source records are immutable", () =>
        {
            var x = File(P("a"), 12); var f = new FakeFolder(); f.Data[root] = [x]; Scan(f); Require(x.Bytes == 12 && x.Path == P("a"));
        });
        Test("matching association preserves zero values", () =>
        {
            var d = Disk(); DiskDetailsService.BindCounters(d, [new("0", 0, 70, 0, 0, 0, 0)]);
            Require(d.CounterState == "Available" && d.Reliability?.Temperature == 0 && d.Reliability.Wear == 0);
        });
        Test("missing counters stay unavailable", () => { var d = Disk(); DiskDetailsService.BindCounters(d, []); Require(d.Reliability is null && d.CounterState != "Available"); });
        Test("counter ID mismatch rejected", () => { var d = Disk(); DiskDetailsService.BindCounters(d, [new("1", 30)]); Require(d.Reliability is null && d.CounterState != "Available"); });
        Test("duplicate association rejected", () => { var d = Disk(); DiskDetailsService.BindCounters(d, [new("0", 20), new("0", 21)]); Require(d.Reliability is null && d.CounterState == "Ambiguous"); });
        Test("empty identities do not match", () => { var d = Disk(); d.DeviceId = ""; DiskDetailsService.BindCounters(d, [new("", 20)]); Require(d.Reliability is null); });
        Test("invalid counter refresh clears old value", () => { var d = Disk(); DiskDetailsService.BindCounters(d, [new("0", 20)]); DiskDetailsService.BindCounters(d, [new("1", 30)]); Require(d.Reliability is null); });
        foreach (var health in new int?[] { null, 5, 99 })
            Test("unknown health " + health, () => { var d = Disk(); d.Health = health; Require(DiskDetailsService.Attention(d) == "UNKNOWN"); });
        Test("known unhealthy outranks absent counters", () => { var d = Disk(); d.Health = 2; Require(DiskDetailsService.Attention(d) == "CRIT"); });
        Test("Windows warning remains warning", () => { var d = Disk(); d.Health = 1; Require(DiskDetailsService.Attention(d) == "WARN"); });
        Test("wear limit not remaining health", () => { var d = Disk(); DiskDetailsService.BindCounters(d, [new("0", Wear: 100)]); Require(DiskDetailsService.Attention(d) == "WARN" && DiskDetailsService.Describe(d).Contains("износ", StringComparison.OrdinalIgnoreCase)); });
        Test("uncorrected errors visible despite healthy status", () => { var d = Disk(); DiskDetailsService.BindCounters(d, [new("0", ReadErrorsUncorrected: 1)]); Require(DiskDetailsService.Attention(d) == "WARN"); });
        Test("manufacturer limit used for temperature", () => { var d = Disk(); DiskDetailsService.BindCounters(d, [new("0", 81, 80)]); Require(DiskDetailsService.Attention(d) == "WARN"); });
        Test("no invented universal temperature threshold", () => { var d = Disk(); DiskDetailsService.BindCounters(d, [new("0", 60)]); Require(DiskDetailsService.Attention(d) != "WARN"); });
        Test("empty storage enumeration not all healthy", () => Require(DiskDetailsService.Collect(new FakeDisks([]), default).Outcome == "Unavailable"));
        Test("partial storage enumeration retains devices", () =>
        {
            var s = DiskDetailsService.Collect(new FakeDisks([Disk()], true), default); Require(s.Disks.Count == 1 && s.Outcome == "Partial");
        });
        Test("storage disk count bounded", () => { var s = DiskDetailsService.Collect(new FakeDisks([Disk(), Disk(), Disk()]), default, 2); Require(s.Disks.Count == 2 && s.Outcome == "Partial"); });
        Test("cancelled storage cannot report completed", () => Require(DiskDetailsService.Collect(new FakeDisks([]), new CancellationToken(true)).Outcome == "Stopped"));
        Test("HTML encodes arbitrary paths and device names", () =>
        {
            var d = new DiskDetailsSnapshot { Disks = [new() { Name = "<script>&", Firmware = "<img>" }] };
            var html = StorageReviewReport.Html(d); Require(!html.Contains("<script>", StringComparison.Ordinal) && html.Contains("&lt;script&gt;", StringComparison.Ordinal));
        });
        Test("JSON preserves null rather than zero", () =>
        {
            using var json = JsonDocument.Parse(StorageReviewReport.Json(new DiskDetailsSnapshot { Disks = [Disk()] }));
            Require(json.RootElement.GetProperty("Snapshot").GetProperty("Disks")[0].GetProperty("Reliability").ValueKind == JsonValueKind.Null);
        });
        Test("summary identifies logical-size scope", () =>
        {
            var text = StorageReviewReport.Summary(Scan(new())); Require(text.Contains("логичес", StringComparison.OrdinalIgnoreCase) && text.Contains(root, StringComparison.Ordinal));
        });
        Test("folder HTML includes all rows and escapes", () =>
        {
            var s = new FolderUsageSnapshot { Root = "<root>", Folders = [new() { Path = "<root>" }], LargestFiles = [new("<file>", 7, null)] };
            var h = StorageReviewReport.Html(s); Require(h.Contains("&lt;root&gt;", StringComparison.Ordinal) && h.Contains("&lt;file&gt;", StringComparison.Ordinal));
        });
        Console.WriteLine($"Storage review regression: {count - failures.Count}/{count} passed.");
        foreach (var f in failures) Console.Error.WriteLine("FAIL: " + f);
        return failures.Count == 0 ? 0 : 230;
    }
    private static FolderItem File(string path, long bytes) => new(path, FileAttributes.Normal, bytes, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    private static FolderItem Dir(string path) => new(path, FileAttributes.Directory);
    private static PhysicalDiskDetail Disk() => new() { DeviceId = "0", Name = "Synthetic disk", Health = 0 };
    private static void Require(bool value) { if (!value) throw new InvalidOperationException("Expected storage review invariant was not satisfied."); }
    private static void ThrowsArgument(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected ArgumentException.");
    }
    private sealed class FakeFolder : IFolderUsageSource
    {
        public Dictionary<string, FolderItem[]> Data { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, FileAttributes> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Denied { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FailAfter { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Reads { get; private set; }
        public Action? AfterFirst { get; set; }
        public FileAttributes GetAttributes(string path) => Attributes.GetValueOrDefault(path, FileAttributes.Directory);
        public IEnumerable<FolderItem> ReadDirectory(string path)
        {
            Reads++; if (Denied.Contains(path)) throw new UnauthorizedAccessException("synthetic");
            var items = Data.GetValueOrDefault(path, []);
            for (var i = 0; i < items.Length; i++) { yield return items[i]; if (i == 0) AfterFirst?.Invoke(); }
            if (FailAfter.Contains(path)) throw new IOException("synthetic iterator error");
        }
    }
    private sealed class FakeDisks(PhysicalDiskDetail[] disks, bool fail = false) : IPhysicalDiskDetailsSource
    {
        public IEnumerable<PhysicalDiskDetail> Read(CancellationToken ct)
        {
            foreach (var disk in disks) { ct.ThrowIfCancellationRequested(); yield return disk; }
            if (fail) throw new UnauthorizedAccessException("synthetic");
        }
    }
}
