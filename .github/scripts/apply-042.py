from pathlib import Path


def read(path):
    return Path(path).read_text(encoding="utf-8")


def write(path, text):
    Path(path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(path, old, new):
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one match, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


project = "src/G.PcHealthCheck/G.PcHealthCheck.csproj"
text = read(project)
for old, new in [
    ("<Version>0.4.1</Version>", "<Version>0.4.2</Version>"),
    ("<FileVersion>0.4.1.0</FileVersion>", "<FileVersion>0.4.2.0</FileVersion>"),
    ("<AssemblyVersion>0.4.1.0</AssemblyVersion>", "<AssemblyVersion>0.4.2.0</AssemblyVersion>"),
]:
    if text.count(old) != 1:
        raise SystemExit(f"{project}: missing {old}")
    text = text.replace(old, new, 1)
write(project, text)

models = "src/G.PcHealthCheck/Models.cs"
replace_once(
    models,
    "    public double DiskQueueWarn { get; init; } = 2;\n    public double DiskQueueCritical { get; init; } = 5;",
    "    public double DiskQueueWarn { get; init; } = 2;\n    public double DiskQueueCritical { get; init; } = 5;\n    public double DiskBusyWarnPercent { get; init; } = 80;\n    public double DiskBusyCriticalPercent { get; init; } = 95;"
)

assessment = "src/G.PcHealthCheck/AssessmentService.cs"
replace_once(
    assessment,
    '''        if (p.DiskQueueLength is double queue)
        {
            if (queue >= Thresholds.DiskQueueCritical) Add("CRIT", "Диск", "Высокая очередь диска", $"{queue:0.#}", "Проверьте TOP процессов по I/O и состояние накопителя.", 12);
            else if (queue >= Thresholds.DiskQueueWarn) Add("WARN", "Диск", "Повышенная очередь диска", $"{queue:0.#}", "Проверьте TOP процессов по I/O.", 5);
        }''',
    '''        if (p.DiskQueueLength is double queue && p.DiskBusyPercent is double busy)
        {
            if (queue >= Thresholds.DiskQueueCritical && busy >= Thresholds.DiskBusyCriticalPercent)
                Add("CRIT", "Диск", "Подтверждённая перегрузка диска", $"busy {busy:0.#}%; queue {queue:0.#}", "Проверьте TOP процессов по I/O, задержки и состояние накопителя. Высокая очередь учитывается только вместе с высокой занятостью диска.", 12);
            else if (queue >= Thresholds.DiskQueueWarn && busy >= Thresholds.DiskBusyWarnPercent)
                Add("WARN", "Диск", "Повышенная нагрузка на диск", $"busy {busy:0.#}%; queue {queue:0.#}", "Проверьте TOP процессов по I/O и подтвердите устойчивость нагрузки повторным замером.", 5);
        }'''
)
replace_once(
    assessment,
    '''        if (data.Performance.DiskQueueLength >= Thresholds.DiskQueueWarn)
            Add(new ActionRecommendation { Id = "InspectIo", Kind = "Вручную", Title = "Разобрать TOP процессов по I/O", Reason = "Устойчивая очередь диска выше порога.", CanAutomate = false, Risk = "Низкий", Verification = "Подтвердить повторными замерами и проверить состояние накопителя." });''',
    '''        if (IsDiskPressureAtLeastWarn(data.Performance))
            Add(new ActionRecommendation { Id = "InspectIo", Kind = "Вручную", Title = "Разобрать TOP процессов по I/O", Reason = "Одновременно повышены медианная занятость диска и очередь I/O.", CanAutomate = false, Risk = "Низкий", Verification = "Подтвердить повторными замерами, проверить задержки и состояние накопителя." });'''
)
replace_once(
    assessment,
    '''        return actions;
    }

    private (int Percent, string Status, List<string> Missing) CalculateCoverage(DiagnosticData data)''',
    '''        return actions;
    }

    private bool IsDiskPressureAtLeastWarn(PerformanceSnapshot p)
        => p.DiskQueueLength is double queue && p.DiskBusyPercent is double busy
           && queue >= Thresholds.DiskQueueWarn && busy >= Thresholds.DiskBusyWarnPercent;

    private (int Percent, string Status, List<string> Missing) CalculateCoverage(DiagnosticData data)'''
)
replace_once(
    assessment,
    '''        Signal(data.Performance.DiskQueueLength.HasValue, 10, "очередь диска");''',
    '''        Signal(data.Performance.DiskQueueLength.HasValue && data.Performance.DiskBusyPercent.HasValue, 10, "нагрузка диска (busy + queue)");'''
)

diagnostics = "src/G.PcHealthCheck/DiagnosticsService.cs"
replace_once(
    diagnostics,
    '''                    diskBusySamples.Add(Convert.ToDouble(item?["PercentDiskTime"] ?? 0));''',
    '''                    diskBusySamples.Add(Math.Clamp(Convert.ToDouble(item?["PercentDiskTime"] ?? 0), 0, 100));'''
)

support = "src/G.PcHealthCheck/SupportSummary.cs"
replace_once(
    support,
    '''        sb.AppendLine($"CPU: {Format(d.Performance.CpuPercent, "%")}; RAM: {Format(d.Performance.MemoryUsedPercent, "%")}; C: {(systemDrive is null ? "—" : $"{systemDrive.FreeGB:0.#} GB свободно")}; uptime: {d.System.UptimeDays:0.#} дн.");''',
    '''        sb.AppendLine($"CPU: {Format(d.Performance.CpuPercent, "%")}; RAM: {Format(d.Performance.MemoryUsedPercent, "%")}; C: {(systemDrive is null ? "—" : $"{systemDrive.FreeGB:0.#} GB свободно")}; uptime: {d.System.UptimeDays:0.#} дн.");
        sb.AppendLine($"Диск I/O: busy {Format(d.Performance.DiskBusyPercent, "%")}; queue {Format(d.Performance.DiskQueueLength, "")}");'''
)

report = "src/G.PcHealthCheck/ReportService.cs"
replace_once(
    report,
    '''            ("Очередь диска", F(before.Data.Performance.DiskQueueLength, ""), F(after.Data.Performance.DiskQueueLength, ""), Delta(before.Data.Performance.DiskQueueLength, after.Data.Performance.DiskQueueLength, "")),''',
    '''            ("Занятость диска", F(before.Data.Performance.DiskBusyPercent, "%"), F(after.Data.Performance.DiskBusyPercent, "%"), Delta(before.Data.Performance.DiskBusyPercent, after.Data.Performance.DiskBusyPercent, "%")),
            ("Очередь диска", F(before.Data.Performance.DiskQueueLength, ""), F(after.Data.Performance.DiskQueueLength, ""), Delta(before.Data.Performance.DiskQueueLength, after.Data.Performance.DiskQueueLength, "")),'''
)

selftest = "src/G.PcHealthCheck/SelfTest.cs"
replace_once(
    selftest,
    '''            if (TriageRegressionSelfTest.Run() != 0) return 81;
            return 0;''',
    '''            if (TriageRegressionSelfTest.Run() != 0) return 81;
            if (DiskPressureRegressionSelfTest.Run() != 0) return 91;
            return 0;'''
)

tests = r'''namespace G.PcHealthCheck;

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
'''
write("src/G.PcHealthCheck/DiskPressureRegressionSelfTest.cs", tests)

readme = "README.md"
text = read(readme)
text = text.replace("Current project version: **0.4.1**.", "Current project version: **0.4.2**.", 1)
text = text.replace("## What 0.4.1 adds", "## What 0.4.2 adds", 1)
old = "Version 0.4.1 adds an explicit Service Desk triage layer on top of the existing health assessment. The UI identifies the highest-priority finding, counts CRIT/WARN observations, proposes the next step, exposes missing diagnostic signals in the System view, and carries the same triage context into clipboard and HTML reports."
new = "Version 0.4.2 improves diagnostic confidence for disk bottlenecks. A high disk queue no longer creates WARN/CRIT by itself: the assessment now requires sustained queue pressure together with high median disk busy percentage, and incomplete busy/queue telemetry reduces diagnostic coverage instead of being treated as evidence of a bottleneck."
if old not in text:
    raise SystemExit("README 0.4.1 intro not found")
text = text.replace(old, new, 1)
text = text.replace("- samples CPU and disk queue repeatedly and uses the median to reduce transient false positives;", "- samples CPU, disk busy and disk queue repeatedly and uses medians to reduce transient false positives;\n- classifies disk pressure only when both queue depth and disk busy are elevated;", 1)
write(readme, text)

changelog = "CHANGELOG.md"
text = read(changelog)
entry = '''## 0.4.2 — Disk-pressure confidence

- Disk queue depth no longer produces WARN/CRIT on its own.
- Disk-pressure findings now require both elevated median queue depth and elevated median disk busy percentage.
- Added `DiskBusyWarnPercent` (80%) and `DiskBusyCriticalPercent` (95%) alongside existing queue thresholds.
- `InspectIo` is suggested only for the combined busy+queue signal.
- Diagnostic coverage now requires both disk busy and queue telemetry for the disk-load signal.
- Clamps sampled `% Disk Time` to 0–100 before taking the median.
- Clipboard and before/after HTML output now expose both disk busy and queue values.
- Added regression tests for queue-only false positives, WARN/CRIT combined pressure and incomplete disk telemetry.
- Application version: `0.4.2`.

'''
if not text.startswith("# Changelog\n\n"):
    raise SystemExit("Unexpected changelog header")
text = "# Changelog\n\n" + entry + text[len("# Changelog\n\n"):]
write(changelog, text)

print("0.4.2 disk-pressure confidence applied")
