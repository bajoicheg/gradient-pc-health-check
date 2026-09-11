namespace G.PcHealthCheck;

internal static class DiskPressureRegressionSelfTest
{
    public static int Run()
    {
        var service = new AssessmentService();

        var queueOnly = BaseData();
        queueOnly.Performance.DiskQueueLength = 7;
        queueOnly.Performance.DiskBusyPercent = 25;
        var queueOnlyResult = service.Assess(queueOnly);
        if (queueOnlyResult.Assessment.Findings.Any(IsDiskPressureFinding)) return 1;
        if (queueOnlyResult.Actions.Any(x => x.Id == "InspectIo")) return 2;

        var warning = BaseData();
        warning.Performance.DiskQueueLength = 3;
        warning.Performance.DiskBusyPercent = 85;
        var warningResult = service.Assess(warning);
        if (!warningResult.Assessment.Findings.Any(x => x.Category == "Диск" && x.Severity == "WARN" && x.Title.Contains("нагрузка", StringComparison.OrdinalIgnoreCase))) return 3;
        if (!warningResult.Actions.Any(x => x.Id == "InspectIo")) return 4;

        var critical = BaseData();
        critical.Performance.DiskQueueLength = 6;
        critical.Performance.DiskBusyPercent = 99;
        var criticalResult = service.Assess(critical);
        if (!criticalResult.Assessment.Findings.Any(x => x.Category == "Диск" && x.Severity == "CRIT" && x.Title.Contains("перегрузка", StringComparison.OrdinalIgnoreCase))) return 5;
        if (!criticalResult.Actions.Any(x => x.Id == "InspectIo")) return 6;

        var incomplete = BaseData();
        incomplete.Performance.DiskBusyPercent = null;
        incomplete.Performance.DiskQueueLength = 7;
        var incompleteResult = service.Assess(incomplete);
        if (!incompleteResult.Assessment.MissingSignals.Any(x => x.Contains("busy + queue", StringComparison.OrdinalIgnoreCase))) return 7;
        if (incompleteResult.Actions.Any(x => x.Id == "InspectIo")) return 8;
        return 0;
    }

    private static bool IsDiskPressureFinding(Finding f)
        => f.Category == "Диск" && (f.Title.Contains("нагрузка", StringComparison.OrdinalIgnoreCase) || f.Title.Contains("перегрузка", StringComparison.OrdinalIgnoreCase));

    private static DiagnosticData BaseData()
        => new()
        {
            System = new SystemInfo
            {
                ComputerName = "DISK-TEST", UserName = "TEST\\user", Manufacturer = "Test", Model = "Model",
                OS = "Microsoft Windows 11 Enterprise", OSVersion = "10.0.26100", BuildNumber = "26100",
                Cpu = "Test CPU", LogicalProcessors = 8, TotalMemoryGB = 16, UptimeDays = 1, LastBoot = DateTime.Now.AddDays(-1)
            },
            Performance = new PerformanceSnapshot
            {
                CpuPercent = 10, MemoryAvailablePercent = 40, MemoryUsedPercent = 60, MemoryAvailableGB = 6.4,
                DiskBusyPercent = 5, DiskQueueLength = 0
            },
            LogicalDisks = [new LogicalDiskInfo { Drive = "C:", SizeGB = 256, FreeGB = 100, FreePercent = 39.1 }],
            PhysicalDisks = [new PhysicalDiskInfo { Name = "Test SSD", HealthStatus = "Healthy", OperationalStatus = "OK", SizeGB = 256 }],
            TopCpu = [new ProcessInfo { Pid = 1, Name = "System", CpuPercent = 1, MemoryMB = 100, IoMBPerSec = 0.1 }],
            TopMemory = [new ProcessInfo { Pid = 1, Name = "System", CpuPercent = 1, MemoryMB = 100, IoMBPerSec = 0.1 }],
            TopIo = [new ProcessInfo { Pid = 1, Name = "System", CpuPercent = 1, MemoryMB = 100, IoMBPerSec = 0.1 }],
            PendingReboot = new PendingRebootInfo { Pending = false },
            Events = new EventSummary { Hours = 24, CriticalCount = 0, ErrorCount = 0 },
            SecurityProducts = [new SecurityProductInfo { Name = "Test EDR", State = "Running" }]
        };
}
