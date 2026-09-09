namespace Gradient.PcHealthCheck;

internal static class SelfTest
{
    public static int Run()
    {
        try
        {
            var data = new DiagnosticData
            {
                System = new SystemInfo { ComputerName = "TEST", UptimeDays = 20, LogicalProcessors = 8, TotalMemoryGB = 16 },
                Performance = new PerformanceSnapshot { CpuPercent = 10, MemoryAvailablePercent = 40, MemoryUsedPercent = 60, DiskQueueLength = 0 },
                LogicalDisks = [new LogicalDiskInfo { Drive = "C:", SizeGB = 256, FreeGB = 8, FreePercent = 3.1 }],
                PhysicalDisks = [new PhysicalDiskInfo { Name = "Test SSD", HealthStatus = "Healthy" }],
                PendingReboot = new PendingRebootInfo { Pending = true, Reasons = ["Windows Update"] },
                Events = new EventSummary { Hours = 24, CriticalCount = 0, ErrorCount = 2 },
                StartupItems = []
            };
            var result = new AssessmentService().Assess(data);
            var clean = result.Actions.Single(x => x.Id == "CleanTemp");
            var restart = result.Actions.Single(x => x.Id == "ManualRestart");
            var dism = result.Actions.Single(x => x.Id == "Dism");
            var sfc = result.Actions.Single(x => x.Id == "Sfc");
            if (!clean.CanAutomate || !clean.RequiresAdmin || !clean.Preselected) return 11;
            if (restart.CanAutomate) return 12;
            if (dism.Preselected || sfc.Preselected) return 13;
            if (result.Assessment.Score >= 100) return 14;
            return 0;
        }
        catch { return 99; }
    }
}
