namespace G.PcHealthCheck;

internal static class CoverageRegressionSelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        var count = 0;
        void Test(string name, Action test)
        {
            count++;
            try { test(); }
            catch (Exception ex) { failures.Add($"{name}: {ex.Message}"); }
        }

        Test("complete healthy data stays OK", () =>
        {
            var a = Assess(HealthyData()).Assessment;
            Require(a.Status == "OK" && a.Score == 100 && a.CoveragePercent == 100, "Healthy baseline changed.");
            Require(a.MissingSignals.Count == 0, "Healthy baseline has missing signals.");
        });

        var missingCases = new (string Name, int Weight, Action<DiagnosticData> Remove)[]
        {
            ("CPU", 15, d => d.Performance.CpuPercent = null),
            ("RAM", 15, d => d.Performance.MemoryAvailablePercent = null),
            ("system disk", 20, d => d.LogicalDisks.Clear()),
            ("disk busy", 10, d => d.Performance.DiskBusyPercent = null),
            ("disk queue", 10, d => d.Performance.DiskQueueLength = null),
            ("physical disks", 15, d => d.PhysicalDisks.Clear()),
            ("Windows OS", 10, d => d.System.OS = ""),
            ("Windows build", 10, d => d.System.BuildNumber = ""),
            ("processes", 10, d => { d.TopCpu.Clear(); d.TopMemory.Clear(); d.TopIo.Clear(); }),
            ("security products", 5, d => d.SecurityProducts.Clear())
        };
        foreach (var testCase in missingCases)
        {
            Test($"missing {testCase.Name} cannot be OK", () =>
            {
                var data = HealthyData();
                testCase.Remove(data);
                AssertIncomplete(Assess(data), 100 - testCase.Weight);
            });
        }

        foreach (var unknown in new[] { "Unknown", "", "   " })
        {
            Test($"one of two disks has unavailable health [{unknown}]", () =>
            {
                var data = HealthyData();
                data.PhysicalDisks.Add(new PhysicalDiskInfo { Name = "Second synthetic disk", HealthStatus = unknown });
                var scan = Assess(data);
                AssertIncomplete(scan, 85);
                Require(!scan.Assessment.Findings.Any(x => x.Category == "Накопитель" && x.Severity == "CRIT"), "Unknown health was treated as disk failure.");
                Require(!scan.Actions.Any(x => x.Id == "InspectSmart"), "Unknown health triggered disk-failure remediation.");
            });
        }

        Test("all disk health unavailable", () =>
        {
            var data = HealthyData();
            data.PhysicalDisks[0].HealthStatus = "Unknown";
            AssertIncomplete(Assess(data), 85);
        });

        Test("two known healthy disks keep full coverage", () =>
        {
            var data = HealthyData();
            data.PhysicalDisks.Add(new PhysicalDiskInfo { Name = "Second synthetic disk", HealthStatus = "OK" });
            var a = Assess(data).Assessment;
            Require(a.Status == "OK" && a.CoveragePercent == 100, "Known healthy disks reduced coverage.");
        });

        Test("known disk failure is not missing telemetry", () =>
        {
            var data = HealthyData();
            data.PhysicalDisks.Add(new PhysicalDiskInfo { Name = "Second synthetic disk", HealthStatus = "Unhealthy" });
            var scan = Assess(data);
            Require(scan.Assessment.Status == "CRIT", "Known disk failure lost CRIT priority.");
            Require(scan.Assessment.CoveragePercent == 100 && scan.Assessment.MissingSignals.Count == 0, "A known bad status was treated as unavailable.");
            Require(scan.Actions.Any(x => x.Id == "InspectSmart"), "Known disk failure lost its manual recommendation.");
        });

        Test("critical finding outranks incomplete telemetry", () =>
        {
            var data = HealthyData();
            data.Performance.CpuPercent = null;
            data.LogicalDisks[0].FreeGB = 5;
            data.LogicalDisks[0].FreePercent = 2;
            var scan = Assess(data);
            Require(scan.Assessment.Status == "CRIT", "Missing telemetry hid a known critical finding.");
            Require(scan.Assessment.CoveragePercent == 85, "Incorrect coverage for missing CPU.");
            Require(scan.Assessment.Findings.Any(x => x.Category == "Данные" && x.Severity == "WARN"), "Missing CPU is not explained alongside CRIT.");
        });

        Test("collection warning remains visible at 100 percent", () =>
        {
            var data = HealthyData();
            data.CollectionWarnings.Add("Synthetic Event Log collection warning");
            var a = Assess(data).Assessment;
            Require(a.Status == "WARN" && a.CoveragePercent == 100 && a.Score == 100, "Collection warning semantics changed.");
        });

        Test("restored telemetry returns to OK without stale warning", () =>
        {
            var data = HealthyData();
            var service = new AssessmentService();
            data.Performance.CpuPercent = null;
            Require(service.Assess(data).Assessment.Status == "WARN", "Missing CPU passed as OK.");
            data.Performance.CpuPercent = 10;
            var a = service.Assess(data).Assessment;
            Require(a.Status == "OK" && a.CoveragePercent == 100 && a.MissingSignals.Count == 0, "Restored telemetry retained stale warning.");
        });

        Test("missing disk does not claim sufficient free space", () =>
        {
            var data = HealthyData();
            data.LogicalDisks.Clear();
            var clean = Assess(data).Actions.Single(x => x.Id == "CleanTemp");
            Require(!clean.Reason.Contains("Свободного места достаточно", StringComparison.Ordinal), "Unmeasured disk is described as having enough free space.");
            Require(clean.Reason.Contains("не измерено", StringComparison.Ordinal), "Unavailable disk measurement is not explained.");
            Require(!clean.Preselected && clean.Kind == "Дополнительно" && clean.CanAutomate && !clean.RequiresAdmin, "Missing telemetry changed the remediation boundary.");
        });

        Test("support summary exposes missing CPU and WARN", () =>
        {
            var data = HealthyData();
            data.Performance.CpuPercent = null;
            var summary = SupportSummary.Build(Assess(data));
            Require(summary.Contains("Статус: WARN; индекс: 100/100", StringComparison.Ordinal), "Summary incorrectly reports healthy status or penalizes missing data.");
            Require(summary.Contains("Покрытие: 85% (HIGH)", StringComparison.Ordinal), "Coverage band no longer reflects its numeric threshold.");
            Require(summary.Contains("Недоступные сигналы: CPU", StringComparison.Ordinal), "Missing CPU is absent from support summary.");
            Require(summary.Contains("Диагностика собрана не полностью", StringComparison.Ordinal), "Incomplete-data finding is absent from support summary.");
        });

        Console.WriteLine($"Diagnostic completeness regression: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 1;
    }

    private static ScanResult Assess(DiagnosticData data) => new AssessmentService().Assess(data);

    private static void AssertIncomplete(ScanResult scan, int expectedCoverage)
    {
        var a = scan.Assessment;
        Require(a.CoveragePercent == expectedCoverage, $"Expected coverage {expectedCoverage}, got {a.CoveragePercent}.");
        Require(a.Status == "WARN", $"Expected WARN, got {a.Status} at coverage {a.CoveragePercent}%.");
        Require(a.Score == 100, "Missing telemetry must not fabricate a health-score penalty.");
        Require(a.MissingSignals.Count == 1, "Expected exactly one missing weighted signal.");
        Require(a.Findings.Count(x => x.Category == "Данные" && x.Severity == "WARN") == 1, "Expected one explainable incomplete-data warning.");
        Require(!scan.Actions.Any(x => x.Preselected), "Missing telemetry must not preselect a remediation.");
        var automated = scan.Actions.Where(x => x.CanAutomate).Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal);
        Require(automated.SequenceEqual(new[] { "CleanTemp", "Dism", "FlushDns", "Sfc" }), "Automated remediation allow-list changed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static DiagnosticData HealthyData() => new()
    {
        System = new SystemInfo
        {
            ComputerName = "SYNTHETIC-PC", UserName = "SYNTHETIC\\user", Domain = "SYNTHETIC",
            OS = "Microsoft Windows 11 Enterprise", BuildNumber = "26100", UptimeDays = 1
        },
        Performance = new PerformanceSnapshot
        {
            CpuPercent = 10, MemoryAvailablePercent = 40, MemoryUsedPercent = 60,
            DiskBusyPercent = 5, DiskQueueLength = 0
        },
        LogicalDisks = [new LogicalDiskInfo { Drive = "C:", SizeGB = 256, FreeGB = 100, FreePercent = 39.1 }],
        PhysicalDisks = [new PhysicalDiskInfo { Name = "Synthetic SSD", HealthStatus = "Healthy" }],
        TopCpu = [new ProcessInfo { Pid = 1, Name = "Synthetic process" }],
        SecurityProducts = [new SecurityProductInfo { Name = "Synthetic security product", State = "Running" }]
    };
}
