from pathlib import Path


def read(path):
    return Path(path).read_text(encoding="utf-8")


def write(path, text):
    Path(path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(path, old, new):
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected exactly one match, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


support = "src/G.PcHealthCheck/SupportSummary.cs"
replace_once(
    support,
    r'''string.Equals(x.Drive, "C:\\", StringComparison.OrdinalIgnoreCase)''',
    r'''string.Equals(x.Drive, "C:", StringComparison.OrdinalIgnoreCase)'''
)

report = "src/G.PcHealthCheck/ReportService.cs"
replace_once(report, "        SystemBlock(sb, d);", "        SystemBlock(sb, scan);")
replace_once(
    report,
    '''    private static void SystemBlock(StringBuilder sb, DiagnosticData d)
    {
        sb.Append("<section><h2>Система</h2><table><tbody>");''',
    '''    private static void SystemBlock(StringBuilder sb, ScanResult scan)
    {
        var d = scan.Data;
        sb.Append("<section><h2>Система</h2><table><tbody>");'''
)
replace_once(
    report,
    '''        Row(sb, "Покрытие диагностики", $"{d.CollectionWarnings.Count} предупреждений сбора");
        Row(sb, "Pending reboot", d.PendingReboot.Pending ? "Да — " + string.Join("; ", d.PendingReboot.Reasons) : "Нет");''',
    '''        Row(sb, "Покрытие диагностики", $"{scan.Assessment.CoveragePercent}% ({scan.Assessment.CoverageStatus})");
        Row(sb, "Недоступные сигналы", scan.Assessment.MissingSignals.Count == 0 ? "Нет" : string.Join("; ", scan.Assessment.MissingSignals));
        Row(sb, "Pending reboot", d.PendingReboot.Pending ? "Да — " + string.Join("; ", d.PendingReboot.Reasons) : "Нет");'''
)

selftest = "src/G.PcHealthCheck/SelfTest.cs"
replace_once(
    selftest,
    '''        if (!summary.Contains("Покрытие: 100% (HIGH)", StringComparison.Ordinal)) return 3;
        return 0;''',
    '''        if (!summary.Contains("Покрытие: 100% (HIGH)", StringComparison.Ordinal)) return 3;
        if (!summary.Contains("C: 100 GB свободно", StringComparison.Ordinal)) return 4;
        return 0;'''
)

print("0.4.1 follow-up corrections applied")
