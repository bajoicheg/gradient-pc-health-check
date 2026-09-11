using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace G.PcHealthCheck;

internal static class CommonProblemsReport
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private const string Scope = "Дополнительные проверки сети, печати и устройств. Они не изменяют основной Health Score. INFO не подтверждает исправность, UNKNOWN означает недостаток данных. Повторный снимок и успешный код команды не доказывают устранение причины.";

    public static string Summary(CommonProblemSnapshot after, CommonProblemSnapshot? before, RemediationBatchResult? lastCommand)
    {
        ArgumentNullException.ThrowIfNull(after);
        var sb = new StringBuilder($"G PC Health Check {Application.ProductVersion} — типовые проблемы\n");
        sb.AppendLine(Scope);
        sb.AppendLine($"Текущий снимок: {after.CollectedAt:O}");
        if (before is not null) sb.AppendLine($"Предыдущий снимок: {before.CollectedAt:O}");
        foreach (var row in CommonProblemsAssessment.Assess(after))
        {
            sb.AppendLine($"[{row.Status}] {row.Title}");
            sb.AppendLine(row.Evidence);
            sb.AppendLine("Что делать: " + row.Resolution);
        }
        if (lastCommand is not null)
            foreach (var action in lastCommand.Actions)
                sb.AppendLine($"Последняя команда: {action.Id}; success={action.Success}; code={action.ExitCode}; {action.Message}");
        return sb.ToString();
    }

    public static string Json(CommonProblemSnapshot after, CommonProblemSnapshot? before, RemediationBatchResult? lastCommand)
    {
        ArgumentNullException.ThrowIfNull(after);
        return JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, Product = "G PC Health Check", Version = Application.ProductVersion, Scope,
            PreviousSnapshot = before, CurrentSnapshot = after,
            PreviousFindings = before is null ? null : CommonProblemsAssessment.Assess(before),
            CurrentFindings = CommonProblemsAssessment.Assess(after), LastCommand = lastCommand
        }, Options);
    }

    public static string Html(CommonProblemSnapshot after, CommonProblemSnapshot? before, RemediationBatchResult? lastCommand)
    {
        ArgumentNullException.ThrowIfNull(after);
        var sb = new StringBuilder("<!doctype html><html lang='ru'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'><title>G PC Health Check — типовые проблемы</title><style>body{font:16px system-ui,sans-serif;max-width:1200px;margin:32px auto;padding:0 20px;color:#17334d}table{border-collapse:collapse;width:100%;margin:20px 0}th,td{border:1px solid #ccd6df;padding:12px;vertical-align:top;text-align:left;white-space:pre-wrap;overflow-wrap:anywhere}th{background:#edf3f7}p{line-height:1.6}h2{margin-top:32px}</style></head><body><h1>Типовые проблемы рабочих ПК</h1>");
        sb.Append("<p>G PC Health Check ").Append(H(Application.ProductVersion)).Append("</p><p>").Append(H(Scope)).Append("</p>");
        if (before is not null) Table(sb, "Предыдущий снимок", before);
        Table(sb, "Текущий снимок", after);
        if (lastCommand is not null)
        {
            sb.Append("<h2>Последняя выполненная команда</h2><p>Это журнал выполнения, не подтверждение устранения симптома.</p><table><tr><th>Действие</th><th>Результат команды</th><th>Подробности</th></tr>");
            foreach (var a in lastCommand.Actions)
                sb.Append("<tr><td>").Append(H(a.Id)).Append("</td><td>").Append(a.Success ? "Успех" : "Ошибка").Append("; code=").Append(H(a.ExitCode?.ToString() ?? "—")).Append("</td><td>").Append(H(a.Message)).Append("</td></tr>");
            sb.Append("</table>");
        }
        return sb.Append("<p>Отчёт может содержать имена устройств и сетевые адреса. Не публикуйте его без проверки.</p></body></html>").ToString();
    }

    private static void Table(StringBuilder sb, string title, CommonProblemSnapshot snapshot)
    {
        sb.Append("<h2>").Append(H(title)).Append("</h2><p>").Append(H(snapshot.CollectedAt.ToString("O"))).Append("</p><table><tr><th>Статус</th><th>Проверка</th><th>Факты</th><th>Шаги инженера</th></tr>");
        foreach (var row in CommonProblemsAssessment.Assess(snapshot))
            sb.Append("<tr><td>").Append(H(row.Status)).Append("</td><td>").Append(H(row.Title)).Append("</td><td>").Append(H(row.Evidence)).Append("</td><td>").Append(H(row.Resolution)).Append("</td></tr>");
        sb.Append("</table>");
    }
    private static string H(string value) => WebUtility.HtmlEncode(value);
}
