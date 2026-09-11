using System.Reflection;

namespace G.PcHealthCheck;

internal static class SystemDiskSelectionSelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        var count = 0;
        void Test(string name, Action test)
        {
            count++;
            try { test(); }
            catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); }
        }
        var drive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\')
            ?? throw new InvalidOperationException("Windows system root is unavailable on the test runner.");
        Test("ordinary local system disk remains healthy", () =>
        {
            var scan = Assess(Data(drive));
            Require(scan.Assessment.Status == "OK" && scan.Assessment.CoveragePercent == 100, "Healthy baseline changed.");
        });
        foreach (var name in new[] { drive.ToLowerInvariant(), drive + "\\", drive + "/", " " + drive + " " })
            Test("normalizes volume identity " + name, () =>
            {
                var scan = Assess(Data(name));
                Require(scan.Assessment.CoveragePercent == 100 && scan.Assessment.Status == "OK", "Equivalent root lost system-disk coverage.");
                Require(SupportSummary.Build(scan).Contains(drive + " 100 GB свободно", StringComparison.Ordinal), "Summary disagrees with normalized system disk.");
            });
        Test("main dashboard and report agree with a normalized root", () =>
        {
            var scan = Assess(Data(drive + "\\"));
            using var form = new MainForm();
            typeof(MainForm).GetMethod("Populate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [scan]);
            var label = (Label)typeof(MainForm).GetField("_disk", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            Require(label.Text == "100 GB", "Dashboard did not use the system-disk measurement.");
            var html = (string)typeof(ReportService).GetMethod("BuildScanHtml", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [scan])!;
            Require(html.Contains("100 GB", StringComparison.Ordinal), "HTML metric lost the system disk.");
        });
        Test("comparison identifies the system volume", () =>
        {
            var row = ReportService.CompareRows(Assess(Data(drive)), Assess(Data(drive))).Single(x => x.Name.StartsWith("Свободно", StringComparison.Ordinal));
            Require(row.Name.Contains("системном диске", StringComparison.Ordinal) && row.Name.Contains(drive, StringComparison.Ordinal), "Comparison label assumes C:.");
        });
        Test("comparison preserves unavailable measurements", () =>
        {
            var after = Data(drive); after.LogicalDisks.Clear();
            var row = ReportService.CompareRows(Assess(Data(drive)), Assess(after)).Single(x => x.Name.StartsWith("Свободно", StringComparison.Ordinal));
            Require(row.After == "—" && row.Delta == "—", "Missing measurement became zero or an improvement.");
        });
        foreach (var change in new (string Name, Action<LogicalDiskInfo> Apply)[]
        {
            ("zero capacity", x => x.SizeGB = 0), ("negative free space", x => x.FreeGB = -1),
            ("free exceeds capacity", x => x.FreeGB = 999), ("invalid percent", x => x.FreePercent = 101),
            ("NaN free space", x => x.FreeGB = double.NaN), ("infinite capacity", x => x.SizeGB = double.PositiveInfinity)
        })
            Test("invalid disk telemetry: " + change.Name, () =>
            {
                var data = Data(drive); change.Apply(data.LogicalDisks[0]);
                var scan = Assess(data);
                Require(scan.Assessment.CoveragePercent == 80, "Invalid disk telemetry counted as available.");
                Require(scan.Assessment.Status == "WARN" && scan.Assessment.Score == 100, "Invalid data became a confirmed fault or healthy.");
                var clean = scan.Actions.Single(x => x.Id == "CleanTemp");
                Require(!clean.Preselected && clean.Reason.Contains("не измерено", StringComparison.Ordinal), "Invalid measurement recommended cleanup or sufficient space.");
                Require(SupportSummary.Build(scan).Contains(drive + " —", StringComparison.Ordinal), "Summary printed invalid telemetry.");
            });
        Test("duplicate normalized volume is not arbitrarily selected", () =>
        {
            var data = Data(drive); data.LogicalDisks.Add(Disk(drive.ToLowerInvariant()));
            var scan = Assess(data);
            Require(scan.Assessment.CoveragePercent == 80 && scan.Assessment.Status == "WARN", "Ambiguous volume accepted.");
        });
        Test("zero free space is a real critical measurement", () =>
        {
            var data = Data(drive); data.LogicalDisks[0].FreeGB = 0; data.LogicalDisks[0].FreePercent = 0;
            var scan = Assess(data);
            Require(scan.Assessment.Status == "CRIT" && scan.Assessment.CoveragePercent == 100, "A full disk became unavailable.");
            Require(scan.Actions.Single(x => x.Id == "CleanTemp").Preselected, "Real low space lost its recommendation.");
        });
        Test("non-C root is selected and never replaced with C", () =>
        {
            var c = Disk("C:"); var d = Disk("D:");
            Require(ReferenceEquals(Select([c, d], "D:"), d), "Explicit D: was replaced by C:.");
            Require(Select([c], "D:") is null, "Missing D: fell back to C:.");
        });
        foreach (var id in new string?[] { null, "", "C:relative", "\\\\server\\share", "C:\\Windows", "1:" })
            Test("invalid requested volume stays unavailable " + id, () => Require(Select([Disk("C:")], id) is null, "Invalid identity was guessed as C:."));
        Test("selector does not mutate telemetry", () =>
        {
            var c = Disk("c:\\"); var z = Disk("Z:"); var list = new[] { z, c };
            Require(ReferenceEquals(Select(list, "C:"), c), "Normalized volume not selected.");
            Require(list[0] == z && c.Drive == "c:\\", "Selection mutated input.");
        });
        Console.WriteLine($"System volume regression: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 170;
    }
    // The test-only commit compiles against 0.5.0. New selector behavior is
    // resolved at runtime; missing functionality is an assertion, not a build error.
    private static LogicalDiskInfo? Select(IEnumerable<LogicalDiskInfo> disks, string? drive)
    {
        var type = typeof(AssessmentService).Assembly.GetType("G.PcHealthCheck.SystemDiskSelection");
        var method = type?.GetMethod("Select", BindingFlags.Public | BindingFlags.Static);
        Require(method is not null, "Unified system-volume selector is not implemented.");
        return (LogicalDiskInfo?)method!.Invoke(null, [disks, drive]);
    }
    private static ScanResult Assess(DiagnosticData data) => new AssessmentService().Assess(data);
    private static LogicalDiskInfo Disk(string drive) => new() { Drive = drive, SizeGB = 256, FreeGB = 100, FreePercent = 39.1 };
    private static DiagnosticData Data(string drive) => new()
    {
        System = new SystemInfo { ComputerName = "SYNTHETIC", UserName = "SYNTHETIC", OS = "Windows 11", BuildNumber = "26100", UptimeDays = 1 },
        Performance = new PerformanceSnapshot { CpuPercent = 10, MemoryAvailablePercent = 40, MemoryUsedPercent = 60, DiskBusyPercent = 5, DiskQueueLength = 0 },
        LogicalDisks = [Disk(drive)], PhysicalDisks = [new PhysicalDiskInfo { Name = "Synthetic SSD", HealthStatus = "Healthy" }],
        TopCpu = [new ProcessInfo { Name = "Synthetic", Pid = 1 }], SecurityProducts = [new SecurityProductInfo { Name = "Synthetic security" }]
    };
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
