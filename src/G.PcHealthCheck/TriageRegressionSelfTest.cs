namespace G.PcHealthCheck;

internal static class TriageRegressionSelfTest
{
    public static int Run()
    {
        var service = new AssessmentService();

        var healthy = service.Assess(HealthyData());
        var healthyTriage = TriageSummary.Build(healthy);
        if (healthyTriage.SignificantCount != 0 || healthyTriage.Title != "Проблем не обнаружено") return 1;
        if (!healthyTriage.NextAction.Contains("не требуются", StringComparison.OrdinalIgnoreCase)) return 2;
        var healthySummary = SupportSummary.Build(healthy);
        if (!healthySummary.Contains("C: 100 GB свободно", StringComparison.Ordinal)) return 3;
        if (!healthySummary.Contains("Triage: Проблем не обнаружено", StringComparison.Ordinal)) return 4;

        var criticalData = HealthyData();
        criticalData.LogicalDisks = [new LogicalDiskInfo { Drive = "C:", SizeGB = 256, FreeGB = 5, FreePercent = 2 }];
        var critical = service.Assess(criticalData);
        var criticalTriage = TriageSummary.Build(critical);
        if (criticalTriage.CriticalCount < 1 || !criticalTriage.Title.StartsWith("Критично:", StringComparison.Ordinal)) return 5;
        if (!criticalTriage.NextAction.Contains("временные файлы", StringComparison.OrdinalIgnoreCase)) return 6;

        var missing = service.Assess(new DiagnosticData { System = new SystemInfo { ComputerName = "MISSING", UptimeDays = 1 } });
        var missingTriage = TriageSummary.Build(missing);
        if (missingTriage.WarningCount < 1) return 7;
        if (!missingTriage.Detail.Contains("покрытие", StringComparison.OrdinalIgnoreCase)) return 8;
        if (!missingTriage.NextAction.Contains("Повторите диагностику", StringComparison.OrdinalIgnoreCase)) return 9;
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
