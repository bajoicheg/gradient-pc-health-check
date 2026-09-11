namespace G.PcHealthCheck;

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
