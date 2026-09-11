namespace G.PcHealthCheck;

internal static class SelfTest
{
    public static int Run()
    {
        try
        {
            if (TestExistingRemediationRules() != 0) return 11;
            if (TestHealthyCoverage() != 0) return 21;
            if (TestMissingTelemetryDoesNotLookHealthy() != 0) return 31;
            if (TestDiffuseEventNoiseDoesNotPenalize() != 0) return 41;
            if (TestRepeatedEventsAreActionable() != 0) return 51;
            if (TestTrustedElevationLocations() != 0) return 61;
            if (TestSupportSummary() != 0) return 71;
            return 0;
        }
        catch { return 99; }
    }

    private static int TestExistingRemediationRules()
    {
        var data = HealthyData();
        data.System.UptimeDays = 20;
        data.LogicalDisks = [new LogicalDiskInfo { Drive = "C:", SizeGB = 256, FreeGB = 8, FreePercent = 3.1 }];
        data.PendingReboot = new PendingRebootInfo { Pending = true, Reasons = ["Windows Update"] };

        var result = new AssessmentService().Assess(data);
        var clean = result.Actions.Single(x => x.Id == "CleanTemp");
        var restart = result.Actions.Single(x => x.Id == "ManualRestart");
        var dism = result.Actions.Single(x => x.Id == "Dism");
        var sfc = result.Actions.Single(x => x.Id == "Sfc");
        if (!clean.CanAutomate || clean.RequiresAdmin || !clean.Preselected || clean.Kind != "Рекомендуется") return 1;
        if (restart.CanAutomate) return 2;
        if (dism.Preselected || sfc.Preselected) return 3;
        if (result.Assessment.Score >= 100) return 4;
        return 0;
    }

    private static int TestHealthyCoverage()
    {
        var result = new AssessmentService().Assess(HealthyData());
        if (result.Assessment.CoveragePercent != 100) return 1;
        if (result.Assessment.CoverageStatus != "HIGH") return 2;
        if (result.Assessment.Status != "OK") return 3;
        if (result.Assessment.Score != 100) return 4;
        if (result.Assessment.MissingSignals.Count != 0) return 5;

        var clean = result.Actions.SingleOrDefault(x => x.Id == "CleanTemp");
        if (clean is null || !clean.CanAutomate || clean.RequiresAdmin) return 6;
        if (clean.Preselected || clean.Kind != "Дополнительно") return 7;
        return 0;
    }

    private static int TestMissingTelemetryDoesNotLookHealthy()
    {
        var data = new DiagnosticData
        {
            System = new SystemInfo { ComputerName = "MISSING", UptimeDays = 1 }
        };
        var result = new AssessmentService().Assess(data);
        if (result.Assessment.CoveragePercent >= 65) return 1;
        if (result.Assessment.CoverageStatus != "LOW") return 2;
        if (result.Assessment.Status == "OK") return 3;
        if (!result.Assessment.Findings.Any(x => x.Category == "Данные" && x.Severity == "WARN")) return 4;
        return 0;
    }

    private static int TestDiffuseEventNoiseDoesNotPenalize()
    {
        var data = HealthyData();
        data.Events = new EventSummary
        {
            Hours = 24,
            ErrorCount = 120,
            CriticalCount = 0,
            Top = Enumerable.Range(1, 12).Select(i => new EventGroup
            {
                Log = "Application",
                Provider = "Provider" + i,
                EventId = 1000 + i,
                Count = 1,
                LastSeen = DateTime.Now
            }).ToList()
        };

        var result = new AssessmentService().Assess(data);
        if (result.Assessment.Score != 100) return 1;
        if (result.Assessment.Findings.Any(x => x.Category == "События" && x.Severity != "OK")) return 2;
        if (result.Actions.Any(x => x.Id == "InspectEvents")) return 3;
        return 0;
    }

    private static int TestRepeatedEventsAreActionable()
    {
        var data = HealthyData();
        data.Events = new EventSummary
        {
            Hours = 24,
            ErrorCount = 120,
            CriticalCount = 0,
            Top =
            [
                new EventGroup { Log = "System", Provider = "Disk", EventId = 153, Count = 25, LastSeen = DateTime.Now },
                new EventGroup { Log = "Application", Provider = "Other", EventId = 1, Count = 2, LastSeen = DateTime.Now }
            ]
        };

        var result = new AssessmentService().Assess(data);
        if (!result.Assessment.Findings.Any(x => x.Category == "События" && x.Severity == "CRIT")) return 1;
        if (!result.Actions.Any(x => x.Id == "InspectEvents")) return 2;
        if (result.Assessment.Score >= 100) return 3;
        return 0;
    }

    private static int TestTrustedElevationLocations()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(pf))
        {
            var trusted = Path.Combine(pf, "G", "PCHealthCheck", "G-PC-Health-Check.exe");
            if (!RemediationWorker.IsTrustedElevationLocation(trusted)) return 1;

            var arbitraryProgramFiles = Path.Combine(pf, "OtherApp", "G-PC-Health-Check.exe");
            if (RemediationWorker.IsTrustedElevationLocation(arbitraryProgramFiles)) return 2;
        }

        var tempCandidate = Path.Combine(Path.GetTempPath(), "G-PC-Health-Check.exe");
        if (RemediationWorker.IsTrustedElevationLocation(tempCandidate)) return 3;

        if (RemediationWorker.RunBootstrap(["--bootstrap-worker"]) != 48) return 4;
        return 0;
    }

    private static int TestSupportSummary()
    {
        var result = new AssessmentService().Assess(HealthyData());
        var summary = SupportSummary.Build(result);
        if (!summary.Contains("Компьютер: TEST", StringComparison.Ordinal)) return 1;
        if (!summary.Contains("Статус: OK; индекс: 100/100", StringComparison.Ordinal)) return 2;
        if (!summary.Contains("Покрытие: 100% (HIGH)", StringComparison.Ordinal)) return 3;
        return 0;
    }

    private static DiagnosticData HealthyData()
        => new()
        {
            System = new SystemInfo
            {
                ComputerName = "TEST",
                UserName = "TEST\\user",
                Manufacturer = "Test",
                Model = "Model",
                OS = "Microsoft Windows 11 Enterprise",
                OSVersion = "10.0.26100",
                BuildNumber = "26100",
                Cpu = "Test CPU",
                LogicalProcessors = 8,
                TotalMemoryGB = 16,
                UptimeDays = 1,
                LastBoot = DateTime.Now.AddDays(-1)
            },
            Performance = new PerformanceSnapshot
            {
                CpuPercent = 10,
                MemoryAvailablePercent = 40,
                MemoryUsedPercent = 60,
                MemoryAvailableGB = 6.4,
                DiskBusyPercent = 5,
                DiskQueueLength = 0
            },
            LogicalDisks = [new LogicalDiskInfo { Drive = "C:", SizeGB = 256, FreeGB = 100, FreePercent = 39.1 }],
            PhysicalDisks = [new PhysicalDiskInfo { Name = "Test SSD", HealthStatus = "Healthy", OperationalStatus = "OK", SizeGB = 256 }],
            TopCpu = [new ProcessInfo { Pid = 1, Name = "System", CpuPercent = 1, MemoryMB = 100, IoMBPerSec = 0.1 }],
            TopMemory = [new ProcessInfo { Pid = 1, Name = "System", CpuPercent = 1, MemoryMB = 100, IoMBPerSec = 0.1 }],
            TopIo = [new ProcessInfo { Pid = 1, Name = "System", CpuPercent = 1, MemoryMB = 100, IoMBPerSec = 0.1 }],
            PendingReboot = new PendingRebootInfo { Pending = false },
            Events = new EventSummary { Hours = 24, CriticalCount = 0, ErrorCount = 0 },
            StartupItems = [],
            SecurityProducts = [new SecurityProductInfo { Name = "Kaspersky Endpoint Security", State = "Running" }]
        };
}
