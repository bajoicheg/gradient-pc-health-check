namespace G.PcHealthCheck;

internal static class GuidanceRegressionSelfTest
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

        var competingCases = new (string Name, string Category, Action<DiagnosticData> Change)[]
        {
            ("physical disk failure", "Накопитель", d => d.PhysicalDisks[0].HealthStatus = "Unhealthy"),
            ("critical CPU", "CPU", d => d.Performance.CpuPercent = 99),
            ("critical RAM", "RAM", d => { d.Performance.MemoryAvailablePercent = 3; d.Performance.MemoryUsedPercent = 97; }),
            ("critical disk pressure", "Диск", d => { d.Performance.DiskBusyPercent = 99; d.Performance.DiskQueueLength = 8; }),
            ("critical repeated events", "События", d => d.Events = new EventSummary
            {
                ErrorCount = 150,
                Top = [new EventGroup { Provider = "Synthetic", EventId = 1, Count = 30 }]
            })
        };
        foreach (var scenario in competingCases)
        {
            Test($"{scenario.Name} guidance is not replaced by Temp cleanup", () =>
            {
                var data = HealthyData();
                data.LogicalDisks[0].FreeGB = 15;
                data.LogicalDisks[0].FreePercent = 12;
                scenario.Change(data);
                var scan = new AssessmentService().Assess(data);
                var primary = scan.Assessment.Findings.Single(x => x.Severity == "CRIT");
                var clean = scan.Actions.Single(x => x.Id == "CleanTemp");
                Require(primary.Category == scenario.Category, "Incorrect scenario fixture.");
                Require(clean.Preselected, "The competing cleanup recommendation was not reproduced.");
                var insight = TriageSummary.Build(scan);
                Require(insight.NextAction == primary.Recommendation, "Primary guidance was replaced by an unrelated action.");
                Require(insight.Title.Contains(primary.Title, StringComparison.Ordinal), "Primary title and action refer to different findings.");
                Require(SupportSummary.Build(scan).Contains("Следующий шаг: " + primary.Recommendation, StringComparison.Ordinal), "Clipboard summary diverges from primary guidance.");
            });
        }

        Test("isolated low space keeps its actual guidance and cleanup action", () =>
        {
            var data = HealthyData();
            data.LogicalDisks[0].FreeGB = 5;
            data.LogicalDisks[0].FreePercent = 2;
            var scan = new AssessmentService().Assess(data);
            var finding = scan.Assessment.Findings.Single(x => x.Severity == "CRIT");
            Require(TriageSummary.Build(scan).NextAction == finding.Recommendation, "Low-space guidance was replaced by an action title.");
            Require(scan.Actions.Single(x => x.Id == "CleanTemp").Preselected, "The available cleanup action was removed.");
        });

        Test("missing data takes precedence over an unrelated selected action", () =>
        {
            var data = HealthyData();
            data.Performance.CpuPercent = null;
            var scan = new AssessmentService().Assess(data);
            scan.Actions.Single(x => x.Id == "FlushDns").Preselected = true;
            var finding = scan.Assessment.Findings.Single(x => x.Severity == "WARN");
            Require(TriageSummary.Build(scan).NextAction == finding.Recommendation, "Missing-data guidance was replaced by DNS cleanup.");
        });

        foreach (var absent in new[] { "", "   " })
        {
            Test($"absent primary guidance [{absent}] has a safe fallback", () =>
            {
                var scan = WithFindings(new Finding { Severity = "CRIT", Title = "SYNTHETIC-PRIMARY", Recommendation = absent });
                scan.Actions.Add(new ActionRecommendation { Id = "Unrelated", Kind = "Рекомендуется", Title = "UNRELATED-ACTION" });
                var next = TriageSummary.Build(scan).NextAction;
                Require(next == "Изучить основное наблюдение и подтвердить причину до изменения системы.", "Fallback selected an unrelated action.");
            });
        }

        Test("no significant finding does not invent a required action", () =>
        {
            var scan = WithFindings(new Finding { Severity = "OK", Title = "Synthetic OK" });
            scan.Actions.Add(new ActionRecommendation { Id = "Optional", Preselected = true, Title = "OPTIONAL-ACTION" });
            var insight = TriageSummary.Build(scan);
            Require(insight.NextAction == "Автоматические действия не требуются." && insight.SignificantCount == 0, "Healthy guidance changed.");
        });

        Test("severity outranks penalty and count totals are preserved", () =>
        {
            var scan = WithFindings(
                new Finding { Severity = "WARN", Title = "Warning", Penalty = 100, Recommendation = "WARN-GUIDANCE" },
                new Finding { Severity = "CRIT", Title = "Critical", Penalty = 1, Recommendation = "CRIT-GUIDANCE" },
                new Finding { Severity = "OK", Title = "Healthy", Penalty = 0 });
            var insight = TriageSummary.Build(scan);
            Require(insight.NextAction == "CRIT-GUIDANCE", "Penalty overrode severity.");
            Require(insight.CriticalCount == 1 && insight.WarningCount == 1 && insight.SignificantCount == 2, "Counts include OK or lose significant findings.");
        });

        Test("clipboard shows a late critical finding before warnings", () =>
        {
            var scan = NoisyScan(10);
            var summary = SupportSummary.Build(scan);
            var lines = ObservationLines(summary);
            Require(lines.Length == 8, "Unexpected clipboard finding limit.");
            Require(lines[0].StartsWith("- [CRIT] LATE-CRITICAL:", StringComparison.Ordinal), "Critical finding was hidden behind earlier warnings.");
        });

        Test("clipboard explicitly counts omitted findings", () =>
        {
            var summary = SupportSummary.Build(NoisyScan(10));
            Require(summary.Contains("Ещё наблюдений: 3.", StringComparison.Ordinal), "Truncation is silent or the omitted count is wrong.");
        });

        Test("exactly eight findings are not marked truncated", () =>
        {
            var summary = SupportSummary.Build(NoisyScan(7));
            Require(ObservationLines(summary).Length == 8, "Exactly-eight list was shortened.");
            Require(!summary.Contains("Ещё наблюдений:", StringComparison.Ordinal), "False truncation marker.");
        });

        Test("healthy clipboard contains no significant findings or omission marker", () =>
        {
            var summary = SupportSummary.Build(new AssessmentService().Assess(HealthyData()));
            Require(ObservationLines(summary).Length == 0, "OK findings entered the significant list.");
            Require(!summary.Contains("Ещё наблюдений:", StringComparison.Ordinal), "Healthy summary has a truncation marker.");
        });

        Test("clipboard and triage use the same penalty order within severity", () =>
        {
            var scan = WithFindings(
                new Finding { Severity = "CRIT", Title = "LESS-URGENT", Penalty = 5, Recommendation = "LESS-GUIDANCE" },
                new Finding { Severity = "CRIT", Title = "MORE-URGENT", Penalty = 20, Recommendation = "MORE-GUIDANCE" });
            var insight = TriageSummary.Build(scan);
            var lines = ObservationLines(SupportSummary.Build(scan));
            Require(insight.NextAction == "MORE-GUIDANCE", "Wrong primary finding.");
            Require(lines[0].Contains("MORE-URGENT", StringComparison.Ordinal), "Clipboard disagrees with primary finding.");
        });

        Test("equal-priority findings keep source order", () =>
        {
            var scan = WithFindings(
                new Finding { Severity = "WARN", Title = "FIRST", Penalty = 5, Recommendation = "FIRST-GUIDANCE" },
                new Finding { Severity = "WARN", Title = "SECOND", Penalty = 5, Recommendation = "SECOND-GUIDANCE" });
            var lines = ObservationLines(SupportSummary.Build(scan));
            Require(TriageSummary.Build(scan).NextAction == "FIRST-GUIDANCE" && lines[0].Contains("FIRST", StringComparison.Ordinal), "Equal-priority ordering is unstable.");
        });

        Test("building guidance never changes findings or remediation selection", () =>
        {
            var scan = NoisyScan(10);
            scan.Actions.Add(new ActionRecommendation { Id = "Optional", Preselected = false, RequiresAdmin = true });
            var findings = scan.Assessment.Findings.ToArray();
            TriageSummary.Build(scan);
            SupportSummary.Build(scan);
            Require(scan.Assessment.Findings.SequenceEqual(findings), "Presentation reordered source findings in place.");
            Require(scan.Actions.Count == 1 && !scan.Actions[0].Preselected && scan.Actions[0].RequiresAdmin, "Presentation changed remediation state.");
        });

        Test("null input is rejected explicitly", () =>
        {
            try { TriageSummary.Build(null!); throw new InvalidOperationException("Triage accepted null."); }
            catch (ArgumentNullException) { }
            try { SupportSummary.Build(null!); throw new InvalidOperationException("Summary accepted null."); }
            catch (ArgumentNullException) { }
        });

        Console.WriteLine($"Primary guidance regression: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 1;
    }

    private static string[] ObservationLines(string summary)
        => summary.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.StartsWith("- [CRIT]", StringComparison.Ordinal) || x.StartsWith("- [WARN]", StringComparison.Ordinal)).ToArray();

    private static ScanResult WithFindings(params Finding[] findings)
        => new() { Data = HealthyData(), Assessment = new Assessment { Score = 100, CoveragePercent = 100, Findings = findings.ToList() } };

    private static ScanResult NoisyScan(int warningCount)
    {
        var scan = WithFindings(Enumerable.Range(1, warningCount).Select(i => new Finding
        {
            Severity = "WARN", Title = "EARLY-WARNING-" + i, Penalty = 4, Recommendation = "Synthetic warning guidance"
        }).ToArray());
        scan.Assessment.Findings.Add(new Finding { Severity = "CRIT", Title = "LATE-CRITICAL", Penalty = 20, Recommendation = "Synthetic critical guidance" });
        return scan;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static DiagnosticData HealthyData() => new()
    {
        System = new SystemInfo { ComputerName = "SYNTHETIC-PC", UserName = "SYNTHETIC-USER", OS = "Windows 11", BuildNumber = "26100", UptimeDays = 1 },
        Performance = new PerformanceSnapshot { CpuPercent = 10, MemoryAvailablePercent = 40, MemoryUsedPercent = 60, DiskBusyPercent = 5, DiskQueueLength = 0 },
        LogicalDisks = [new LogicalDiskInfo { Drive = "C:", SizeGB = 128, FreeGB = 50, FreePercent = 39.1 }],
        PhysicalDisks = [new PhysicalDiskInfo { Name = "Synthetic SSD", HealthStatus = "Healthy" }],
        TopCpu = [new ProcessInfo { Pid = 1, Name = "Synthetic process" }],
        SecurityProducts = [new SecurityProductInfo { Name = "Synthetic security product" }]
    };
}
