from pathlib import Path


def read(path):
    return Path(path).read_text(encoding="utf-8")


def write(path, text):
    Path(path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(path, old, new):
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected exactly one match, found {count}: {old[:100]!r}")
    write(path, text.replace(old, new, 1))


project = "src/G.PcHealthCheck/G.PcHealthCheck.csproj"
text = read(project)
for old, new in [
    ("<Version>0.4.0</Version>", "<Version>0.4.1</Version>"),
    ("<FileVersion>0.4.0.0</FileVersion>", "<FileVersion>0.4.1.0</FileVersion>"),
    ("<AssemblyVersion>0.4.0.0</AssemblyVersion>", "<AssemblyVersion>0.4.1.0</AssemblyVersion>"),
]:
    if text.count(old) != 1:
        raise SystemExit(f"{project}: version token missing: {old}")
    text = text.replace(old, new, 1)
write(project, text)

triage = r'''namespace G.PcHealthCheck;

internal sealed class TriageInsight
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string NextAction { get; init; } = "";
    public int CriticalCount { get; init; }
    public int WarningCount { get; init; }
    public int SignificantCount => CriticalCount + WarningCount;
}

internal static class TriageSummary
{
    public static TriageInsight Build(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);

        var significant = scan.Assessment.Findings
            .Where(x => x.Severity is "CRIT" or "WARN")
            .OrderBy(x => x.Severity == "CRIT" ? 0 : 1)
            .ThenByDescending(x => x.Penalty)
            .ToList();

        var critical = significant.Count(x => x.Severity == "CRIT");
        var warning = significant.Count(x => x.Severity == "WARN");
        var primary = significant.FirstOrDefault();
        var recommended = scan.Actions.FirstOrDefault(x => x.Kind == "Рекомендуется" || x.Preselected);

        if (primary is null)
        {
            const string next = "Автоматические действия не требуются.";
            return new TriageInsight
            {
                Title = "Проблем не обнаружено",
                Detail = $"Индекс {scan.Assessment.Score}/100 · покрытие {scan.Assessment.CoveragePercent}% ({scan.Assessment.CoverageStatus}). {next}",
                NextAction = next,
                CriticalCount = 0,
                WarningCount = 0
            };
        }

        var nextAction = recommended?.Title;
        if (string.IsNullOrWhiteSpace(nextAction))
            nextAction = primary.Recommendation;
        if (string.IsNullOrWhiteSpace(nextAction))
            nextAction = "Изучить основное наблюдение и подтвердить причину до изменения системы.";

        var title = primary.Severity == "CRIT"
            ? $"Критично: {primary.Title}"
            : $"Требует внимания: {primary.Title}";

        return new TriageInsight
        {
            Title = title,
            Detail = $"CRIT: {critical} · WARN: {warning} · покрытие {scan.Assessment.CoveragePercent}% ({scan.Assessment.CoverageStatus}) · следующий шаг: {nextAction}",
            NextAction = nextAction,
            CriticalCount = critical,
            WarningCount = warning
        };
    }
}
'''
write("src/G.PcHealthCheck/TriageSummary.cs", triage)

main = "src/G.PcHealthCheck/MainForm.cs"
replace_once(
    main,
    "    private readonly Label _coverage = new();\n    private readonly Label _status = new();",
    "    private readonly Label _coverage = new();\n    private readonly Label _triageTitle = new();\n    private readonly Label _triageDetail = new();\n    private readonly Label _status = new();"
)
replace_once(
    main,
    '''        var page = Page("Рекомендации");
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };''',
    '''        var page = Page("Рекомендации");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var triage = Card();
        triage.Margin = new Padding(0, 0, 0, 8);
        _triageTitle.Font = new Font("Segoe UI Semibold", 11F);
        _triageTitle.ForeColor = Navy;
        _triageTitle.AutoSize = true;
        _triageTitle.Location = new Point(14, 12);
        triage.Controls.Add(_triageTitle);
        _triageDetail.ForeColor = Muted;
        _triageDetail.AutoEllipsis = true;
        _triageDetail.Location = new Point(14, 39);
        _triageDetail.Size = new Size(900, 22);
        _triageDetail.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        triage.Controls.Add(_triageDetail);
        triage.Resize += (_, _) => _triageDetail.Width = Math.Max(100, triage.ClientSize.Width - 28);
        layout.Controls.Add(triage, 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };'''
)
replace_once(
    main,
    '''        split.Panel2.Controls.Add(_findings);
        page.Controls.Add(split);
        return page;''',
    '''        split.Panel2.Controls.Add(_findings);
        layout.Controls.Add(split, 0, 1);
        page.Controls.Add(layout);
        return page;'''
)
replace_once(
    main,
    '''        _coverage.Text = $"{scan.Assessment.CoveragePercent}%";
        _coverage.ForeColor = scan.Assessment.CoverageStatus == "HIGH" ? Ok : scan.Assessment.CoverageStatus == "MEDIUM" ? Warn : Crit;

        _actions.Rows.Clear();''',
    '''        _coverage.Text = $"{scan.Assessment.CoveragePercent}%";
        _coverage.ForeColor = scan.Assessment.CoverageStatus == "HIGH" ? Ok : scan.Assessment.CoverageStatus == "MEDIUM" ? Warn : Crit;
        var triage = TriageSummary.Build(scan);
        _triageTitle.Text = triage.Title;
        _triageTitle.ForeColor = scan.Assessment.Status == "OK" ? Ok : scan.Assessment.Status == "WARN" ? Warn : Crit;
        _triageDetail.Text = triage.Detail;
        _tabs.TabPages[0].Text = triage.SignificantCount > 0 ? $"Рекомендации ({triage.SignificantCount})" : "Рекомендации";

        _actions.Rows.Clear();'''
)
replace_once(
    main,
    '''            var i = _findings.Rows.Add(f.Severity, f.Category, f.Title, f.Value, f.Recommendation);
            _findings.Rows[i].Cells[0].Style.ForeColor = f.Severity == "CRIT" ? Crit : f.Severity == "WARN" ? Warn : Ok;
            _findings.Rows[i].Cells[0].Style.Font = new Font(Font, FontStyle.Bold);''',
    '''            var i = _findings.Rows.Add(f.Severity, f.Category, f.Title, f.Value, f.Recommendation);
            var row = _findings.Rows[i];
            row.Cells[0].Style.ForeColor = f.Severity == "CRIT" ? Crit : f.Severity == "WARN" ? Warn : Ok;
            row.Cells[0].Style.Font = new Font(Font, FontStyle.Bold);
            if (f.Severity == "CRIT") row.DefaultCellStyle.BackColor = Color.FromArgb(255, 242, 242);
            else if (f.Severity == "WARN") row.DefaultCellStyle.BackColor = Color.FromArgb(255, 249, 235);'''
)
replace_once(main, "        PopulateSystem(d);", "        PopulateSystem(scan);")
replace_once(
    main,
    '''    private void PopulateSystem(DiagnosticData d)
    {
        _system.Rows.Clear(); void Add(string k, string v) => _system.Rows.Add(k, v);''',
    '''    private void PopulateSystem(ScanResult scan)
    {
        var d = scan.Data;
        _system.Rows.Clear(); void Add(string k, string v) => _system.Rows.Add(k, v);'''
)
replace_once(
    main,
    '''        Add("Последняя загрузка", d.System.LastBoot.ToString("dd.MM.yyyy HH:mm:ss")); Add("Права процесса", d.System.IsAdministrator ? "Administrator" : "Standard user");
        Add("Pending reboot", d.PendingReboot.Pending ? "Да — " + string.Join("; ", d.PendingReboot.Reasons) : "Нет");''',
    '''        Add("Последняя загрузка", d.System.LastBoot.ToString("dd.MM.yyyy HH:mm:ss")); Add("Права процесса", d.System.IsAdministrator ? "Administrator" : "Standard user");
        Add("Покрытие диагностики", $"{scan.Assessment.CoveragePercent}% ({scan.Assessment.CoverageStatus})");
        Add("Недоступные сигналы", scan.Assessment.MissingSignals.Count == 0 ? "Нет" : string.Join("; ", scan.Assessment.MissingSignals));
        Add("Pending reboot", d.PendingReboot.Pending ? "Да — " + string.Join("; ", d.PendingReboot.Reasons) : "Нет");'''
)

support = "src/G.PcHealthCheck/SupportSummary.cs"
replace_once(
    support,
    '''        var a = scan.Assessment;
        var systemDrive = d.LogicalDisks.FirstOrDefault''',
    '''        var a = scan.Assessment;
        var triage = TriageSummary.Build(scan);
        var systemDrive = d.LogicalDisks.FirstOrDefault'''
)
replace_once(
    support,
    '''        sb.AppendLine($"Покрытие: {a.CoveragePercent}% ({a.CoverageStatus})");
        sb.AppendLine($"CPU: {Format(d.Performance.CpuPercent, "%")}; RAM: {Format(d.Performance.MemoryUsedPercent, "%")}; C: {(systemDrive is null ? "—" : $"{systemDrive.FreeGB:0.#} GB свободно")}; uptime: {d.System.UptimeDays:0.#} дн.");''',
    '''        sb.AppendLine($"Покрытие: {a.CoveragePercent}% ({a.CoverageStatus})");
        sb.AppendLine($"Triage: {triage.Title}");
        sb.AppendLine($"Следующий шаг: {triage.NextAction}");
        sb.AppendLine($"CPU: {Format(d.Performance.CpuPercent, "%")}; RAM: {Format(d.Performance.MemoryUsedPercent, "%")}; C: {(systemDrive is null ? "—" : $"{systemDrive.FreeGB:0.#} GB свободно")}; uptime: {d.System.UptimeDays:0.#} дн.");'''
)

report = "src/G.PcHealthCheck/ReportService.cs"
replace_once(
    report,
    '''        Hero(sb, scan.Assessment.Score, scan.Assessment.Status, d.System.ComputerName + " · " + d.System.UserName + " · " + d.System.Model);
        Metrics(sb, scan);''',
    '''        Hero(sb, scan.Assessment.Score, scan.Assessment.Status, d.System.ComputerName + " · " + d.System.UserName + " · " + d.System.Model);
        Triage(sb, scan);
        Metrics(sb, scan);'''
)
replace_once(
    report,
    '''        Hero(sb, v.After.Assessment.Score, v.After.Assessment.Status, $"{v.After.Data.System.ComputerName} · было {v.Before.Assessment.Score}/100 → стало {v.After.Assessment.Score}/100");
        sb.Append("<section><h2>До / после</h2>''',
    '''        Hero(sb, v.After.Assessment.Score, v.After.Assessment.Status, $"{v.After.Data.System.ComputerName} · было {v.Before.Assessment.Score}/100 → стало {v.After.Assessment.Score}/100");
        Triage(sb, v.After);
        sb.Append("<section><h2>До / после</h2>'''
)
replace_once(report, "grid-template-columns:repeat(4,1fr)", "grid-template-columns:repeat(5,1fr)")
replace_once(
    report,
    '''    private static void Metrics(StringBuilder sb, ScanResult scan)
    {
        var d = scan.Data; var disk = SystemDisk(d);
        sb.Append("<section class='cards'>");''',
    '''    private static void Triage(StringBuilder sb, ScanResult scan)
    {
        var triage = TriageSummary.Build(scan);
        sb.Append("<section><h2>Triage</h2><p><b>").Append(H(triage.Title)).Append("</b></p><p>").Append(H(triage.Detail)).Append("</p></section>");
    }

    private static void Metrics(StringBuilder sb, ScanResult scan)
    {
        var d = scan.Data; var disk = SystemDisk(d);
        sb.Append("<section class='cards'>");
        Card(sb, "Покрытие", $"{scan.Assessment.CoveragePercent}%", scan.Assessment.CoverageStatus);'''
)
replace_once(
    report,
    '''        Row(sb, "Pending reboot", d.PendingReboot.Pending ? "Да — " + string.Join("; ", d.PendingReboot.Reasons) : "Нет");''',
    '''        Row(sb, "Покрытие диагностики", $"{d.CollectionWarnings.Count} предупреждений сбора");
        Row(sb, "Pending reboot", d.PendingReboot.Pending ? "Да — " + string.Join("; ", d.PendingReboot.Reasons) : "Нет");'''
)

selftest = "src/G.PcHealthCheck/SelfTest.cs"
replace_once(
    selftest,
    '''            if (TestSupportSummary() != 0) return 71;
            return 0;''',
    '''            if (TestSupportSummary() != 0) return 71;
            if (TestTriageSummary() != 0) return 81;
            return 0;'''
)
replace_once(
    selftest,
    '''    private static DiagnosticData HealthyData()
''',
    '''    private static int TestTriageSummary()
    {
        var healthy = new AssessmentService().Assess(HealthyData());
        var healthyTriage = TriageSummary.Build(healthy);
        if (healthyTriage.SignificantCount != 0 || healthyTriage.Title != "Проблем не обнаружено") return 1;
        if (!healthyTriage.NextAction.Contains("не требуются", StringComparison.OrdinalIgnoreCase)) return 2;

        var criticalData = HealthyData();
        criticalData.LogicalDisks = [new LogicalDiskInfo { Drive = "C:", SizeGB = 256, FreeGB = 5, FreePercent = 2 }];
        var critical = new AssessmentService().Assess(criticalData);
        var criticalTriage = TriageSummary.Build(critical);
        if (criticalTriage.CriticalCount < 1 || !criticalTriage.Title.StartsWith("Критично:", StringComparison.Ordinal)) return 3;
        if (!criticalTriage.NextAction.Contains("временные файлы", StringComparison.OrdinalIgnoreCase)) return 4;

        var missing = new AssessmentService().Assess(new DiagnosticData { System = new SystemInfo { ComputerName = "MISSING", UptimeDays = 1 } });
        var missingTriage = TriageSummary.Build(missing);
        if (missingTriage.WarningCount < 1) return 5;
        if (!missingTriage.Detail.Contains("покрытие", StringComparison.OrdinalIgnoreCase)) return 6;
        if (!missingTriage.NextAction.Contains("Повторите диагностику", StringComparison.OrdinalIgnoreCase)) return 7;
        return 0;
    }

    private static DiagnosticData HealthyData()
'''
)

readme = "README.md"
text = read(readme)
text = text.replace("Current project version: **0.4.0**.", "Current project version: **0.4.1**.", 1)
text = text.replace("## What 0.4.0 adds", "## What 0.4.1 adds", 1)
old = "Version 0.4.0 returns focus to the Service Desk product experience while preserving the 0.3.8 security and supply-chain boundary. It makes diagnostic coverage visible in the main dashboard, records scan duration, adds a copy-ready support summary, and removes the form-wide busy cursor that could remain visually stuck over DataGridView regions after asynchronous diagnostics."
new = "Version 0.4.1 adds an explicit Service Desk triage layer on top of the existing health assessment. The UI now identifies the highest-priority finding, counts CRIT/WARN observations, proposes the next step, exposes missing diagnostic signals in the System view, and carries the same triage context into clipboard and HTML reports."
if old not in text:
    raise SystemExit("README 0.4.0 intro not found")
text = text.replace(old, new, 1)
marker = "- shows diagnostic coverage directly on the dashboard and colors degraded coverage;"
text = text.replace(marker, marker + "\n- summarizes the primary issue, CRIT/WARN counts and the next Service Desk step in a dedicated triage card;\n- exposes missing diagnostic signals directly in the System view and HTML/clipboard output;", 1)
write(readme, text)

changelog = "CHANGELOG.md"
text = read(changelog)
entry = '''## 0.4.1 — Service Desk triage clarity

- Added a dedicated triage card above recommendations: primary finding, CRIT/WARN counts, diagnostic coverage and the next Service Desk step.
- Prioritizes critical findings before warnings and prefers an explicitly recommended/preselected remediation as the next step when one exists.
- Highlights CRIT/WARN finding rows for faster visual scanning.
- Shows diagnostic coverage and missing signals directly in the System tab.
- Adds the same triage context to clipboard summaries and HTML scan/verification reports.
- Adds healthy, critical-disk and incomplete-telemetry self-tests for the triage contract.
- Application version: `0.4.1`.

'''
if not text.startswith("# Changelog\n\n"):
    raise SystemExit("Unexpected changelog header")
text = "# Changelog\n\n" + entry + text[len("# Changelog\n\n"):]
write(changelog, text)

print("0.4.1 transform applied")
