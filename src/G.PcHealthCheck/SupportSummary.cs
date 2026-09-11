using System.Text;

namespace G.PcHealthCheck;

internal static class SupportSummary
{
    private const int MaxFindings = 8;

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
        sb.AppendLine($"Диск I/O: busy {Format(d.Performance.DiskBusyPercent, "%")}; queue {Format(d.Performance.DiskQueueLength, "")}");

        if (a.MissingSignals.Count > 0)
            sb.AppendLine("Недоступные сигналы: " + string.Join(", ", a.MissingSignals));
        if (d.CollectionWarnings.Count > 0)
            sb.AppendLine("Предупреждения сбора: " + string.Join(" | ", d.CollectionWarnings));

        var significant = TriageSummary.SignificantFindings(scan);
        if (significant.Count > 0)
        {
            sb.AppendLine("Наблюдения:");
            foreach (var finding in significant.Take(MaxFindings))
                sb.AppendLine($"- [{finding.Severity}] {finding.Title}: {finding.Value}");
            if (significant.Count > MaxFindings)
                sb.AppendLine($"Ещё наблюдений: {significant.Count - MaxFindings}. Полный список — в отчёте HTML/JSON.");
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
