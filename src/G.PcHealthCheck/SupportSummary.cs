using System.Text;

namespace G.PcHealthCheck;

internal static class SupportSummary
{
    public static string Build(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var d = scan.Data;
        var a = scan.Assessment;
        var triage = TriageSummary.Build(scan);
        var systemDrive = d.LogicalDisks.FirstOrDefault(x => string.Equals(x.Drive, "C:", StringComparison.OrdinalIgnoreCase));
        var sb = new StringBuilder();

        sb.AppendLine("G PC Health Check");
        sb.AppendLine($"Компьютер: {d.System.ComputerName}");
        sb.AppendLine($"Пользователь: {d.System.UserName}");
        sb.AppendLine($"Статус: {a.Status}; индекс: {a.Score}/100");
        sb.AppendLine($"Покрытие: {a.CoveragePercent}% ({a.CoverageStatus})");
        sb.AppendLine($"Triage: {triage.Title}");
        sb.AppendLine($"Следующий шаг: {triage.NextAction}");
        sb.AppendLine($"CPU: {Format(d.Performance.CpuPercent, "%")}; RAM: {Format(d.Performance.MemoryUsedPercent, "%")}; C: {(systemDrive is null ? "—" : $"{systemDrive.FreeGB:0.#} GB свободно")}; uptime: {d.System.UptimeDays:0.#} дн.");

        if (a.MissingSignals.Count > 0)
            sb.AppendLine("Недоступные сигналы: " + string.Join(", ", a.MissingSignals));
        if (d.CollectionWarnings.Count > 0)
            sb.AppendLine("Предупреждения сбора: " + string.Join(" | ", d.CollectionWarnings));

        var significant = a.Findings.Where(x => x.Severity is "CRIT" or "WARN").Take(8).ToList();
        if (significant.Count > 0)
        {
            sb.AppendLine("Наблюдения:");
            foreach (var finding in significant)
                sb.AppendLine($"- [{finding.Severity}] {finding.Title}: {finding.Value}");
        }

        var recommended = scan.Actions.Where(x => x.Preselected || x.Kind == "Рекомендуется").Take(8).ToList();
        if (recommended.Count > 0)
        {
            sb.AppendLine("Рекомендуемые действия:");
            foreach (var action in recommended)
                sb.AppendLine($"- {action.Title}{(action.RequiresAdmin ? " [Admin]" : "")}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string Format(double? value, string suffix)
        => value is null ? "—" : $"{value:0.#}{suffix}";
}
