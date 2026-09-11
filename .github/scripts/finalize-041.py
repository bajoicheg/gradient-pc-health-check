from pathlib import Path


def read(path):
    return Path(path).read_text(encoding="utf-8")


def write(path, text):
    Path(path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(path, old, new):
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one match, found {count}: {old[:100]!r}")
    write(path, text.replace(old, new, 1))


selftest = "src/G.PcHealthCheck/SelfTest.cs"
replace_once(
    selftest,
    "            if (TestSupportSummary() != 0) return 71;\n            return 0;",
    "            if (TestSupportSummary() != 0) return 71;\n            if (TriageRegressionSelfTest.Run() != 0) return 81;\n            return 0;"
)

tests = r'''namespace G.PcHealthCheck;

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
'''
write("src/G.PcHealthCheck/TriageRegressionSelfTest.cs", tests)

readme = "README.md"
text = read(readme)
text = text.replace("Current project version: **0.4.0**.", "Current project version: **0.4.1**.", 1)
text = text.replace("## What 0.4.0 adds", "## What 0.4.1 adds", 1)
old = "Version 0.4.0 returns focus to the Service Desk product experience while preserving the 0.3.8 security and supply-chain boundary. It makes diagnostic coverage visible in the main dashboard, records scan duration, adds a copy-ready support summary, and removes the form-wide busy cursor that could remain visually stuck over DataGridView regions after asynchronous diagnostics."
new = "Version 0.4.1 adds an explicit Service Desk triage layer on top of the existing health assessment. The UI identifies the highest-priority finding, counts CRIT/WARN observations, proposes the next step, exposes missing diagnostic signals in the System view, and carries the same triage context into clipboard and HTML reports."
if old not in text:
    raise SystemExit("README 0.4.0 intro not found")
text = text.replace(old, new, 1)
marker = "- shows diagnostic coverage directly on the dashboard and colors degraded coverage;"
if marker not in text:
    raise SystemExit("README coverage bullet not found")
text = text.replace(marker, marker + "\n- summarizes the primary issue, CRIT/WARN counts and next Service Desk step in a dedicated triage card;\n- exposes missing diagnostic signals in the System view and report outputs;", 1)
write(readme, text)

changelog = "CHANGELOG.md"
text = read(changelog)
entry = '''## 0.4.1 — Service Desk triage clarity

- Added a dedicated triage card above recommendations: primary finding, CRIT/WARN counts, diagnostic coverage and the next Service Desk step.
- Prioritizes critical findings before warnings and prefers an explicitly recommended/preselected remediation as the next step when one exists.
- Highlights CRIT/WARN finding rows for faster visual scanning.
- Shows diagnostic coverage and missing signals directly in the System tab and HTML reports.
- Adds the same triage context to clipboard summaries.
- Fixed clipboard summary system-drive lookup (`C:` rather than `C:\\`), so free-space data is no longer lost.
- Added healthy, critical-disk, incomplete-telemetry and system-drive regression self-tests.
- Application version: `0.4.1`.

'''
if not text.startswith("# Changelog\n\n"):
    raise SystemExit("Unexpected changelog header")
text = "# Changelog\n\n" + entry + text[len("# Changelog\n\n"):]
write(changelog, text)

print("0.4.1 regression tests and docs finalized")
