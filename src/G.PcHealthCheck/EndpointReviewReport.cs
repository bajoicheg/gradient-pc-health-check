using System.Net;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class EndpointReviewReport
{
    public const string Meaning = "Это последовательные снимки локальных таблиц, не непрерывная трассировка и не оценка безопасности. UDP показывает привязанные локальные сокеты, а не удалённые разговоры. LISTEN/BOUND не доказывает доступность порта извне: действуют брандмауэр, маршруты и политики. Обратный DNS, проверка портов и захват пакетов не выполняются. Между снимками могут возникать и исчезать незафиксированные соединения; одинаковый набор адресов/портов не доказывает тот же сокет.";
    public static string Summary(EndpointSnapshot current, EndpointSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        var rows = current.Tables.SelectMany(x => x.Rows).ToList(); var s = new StringBuilder();
        s.AppendLine("G PC Health Check — сетевые соединения и порты");
        s.AppendLine($"ПК: {current.ComputerName}; снимок: {current.StartedAt:O} — {current.FinishedAt:O}.");
        s.AppendLine($"Таблицы: {EndpointReviewCore.CollectionText(current.State)}. Записей: {rows.Count}; TCP LISTEN: {rows.Count(x => x.State == "LISTEN")}; TCP ESTABLISHED: {rows.Count(x => x.State == "ESTABLISHED")}; UDP: {rows.Count(x => x.Protocol == "UDP")}.");
        s.AppendLine($"Имя процесса сопоставлено: {rows.Count(x => x.ProcessEvidence == "Stable")}/{rows.Count}.");
        foreach (var table in current.Tables) s.AppendLine($"{table.Name}: {EndpointReviewCore.CollectionText(table.State)}; сохранено {table.Rows.Count}, Windows сообщила {table.ReportedCount?.ToString() ?? "—"}. {table.Message}");
        if (previous is null) s.AppendLine("Первый снимок: сравнение отсутствует.");
        else
        {
            s.AppendLine($"Предыдущий снимок: {previous.StartedAt:O} — {previous.FinishedAt:O}.");
            s.AppendLine("Сравниваются только полные парные таблицы при совпадении ПК, аккаунта, сеанса и прав; недоступность данных не означает исчезновение соединений.");
            foreach (var group in EndpointReviewCore.Compare(current, previous).GroupBy(x => x.Change)) s.AppendLine($"{EndpointReviewCore.ChangeText(group.Key)}: {group.Count()}.");
        }
        s.AppendLine(Meaning);
        foreach (var warning in current.Warnings) s.AppendLine("! " + warning);
        s.AppendLine(ExecutionPolicy.Describe(current.ExecutionContext));
        s.AppendLine("Экспорт содержит оба полных собранных снимка, независимо от фильтров; имена процессов и сетевые адреса могут быть чувствительными.");
        return s.ToString().TrimEnd();
    }
    public static string Json(EndpointSnapshot current, EndpointSnapshot? previous)
        => JsonSerializer.Serialize(new { SchemaVersion = 1, Current = current, Previous = previous, Observations = EndpointReviewCore.Compare(current, previous), Interpretation = Meaning }, new JsonSerializerOptions { WriteIndented = true });
    public static string Html(EndpointSnapshot current, EndpointSnapshot? previous)
    {
        var s = new StringBuilder("<!doctype html><html lang='ru'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>G PC Health Check — сетевые соединения</title><style>body{font:14px/1.5 'Segoe UI',sans-serif;margin:24px}table{border-collapse:collapse;width:100%;margin-bottom:20px}td,th{border:1px solid #ddd;padding:7px;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{background:#f3f5f7}pre{white-space:pre-wrap;overflow-wrap:anywhere}section{overflow:auto}small{color:#555}</style></head><body><h1>Сетевые соединения и порты</h1><pre>");
        s.Append(H(Summary(current, previous))).Append("</pre><section><h2>Наблюдения между снимками</h2>");
        Rows(s, EndpointReviewCore.Compare(current, previous)); s.Append("</section>");
        Raw(s, current, "Текущий снимок"); if (previous is not null) Raw(s, previous, "Предыдущий снимок");
        return s.Append("</body></html>").ToString();
    }
    private static void Raw(StringBuilder s, EndpointSnapshot snapshot, string title)
    {
        s.Append("<section><h2>").Append(H(title)).Append("</h2><pre>").Append(H($"{snapshot.StartedAt:O} — {snapshot.FinishedAt:O}\n" + ExecutionPolicy.Describe(snapshot.ExecutionContext))).Append("</pre>");
        foreach (var table in snapshot.Tables)
        {
            s.Append("<h3>").Append(H(table.Name)).Append("</h3><p>").Append(H($"{EndpointReviewCore.CollectionText(table.State)}; {table.StartedAt:O} — {table.FinishedAt:O}; сохранено {table.Rows.Count}; сообщено {table.ReportedCount?.ToString() ?? "—"}. {table.Message}")).Append("</p>");
            Rows(s, table.Rows.Select(x => new EndpointObservation(x, "Baseline")));
        }
        s.Append("<pre>").Append(H(string.Join("\n", snapshot.Warnings))).Append("</pre></section>");
    }
    private static void Rows(StringBuilder s, IEnumerable<EndpointObservation> rows)
    {
        s.Append("<table><thead><tr><th>Наблюдение</th><th>Протокол</th><th>PID</th><th>Процесс / привязка</th><th>Локальный адрес</th><th>Удалённый адрес</th><th>Состояние</th></tr></thead><tbody>");
        foreach (var item in rows)
        {
            var r = item.Row;
            s.Append("<tr><td>").Append(H(EndpointReviewCore.ChangeText(item.Change))).Append("</td><td>").Append(H(r.Table))
                .Append("</td><td>").Append(r.Pid).Append("</td><td>").Append(H((r.ProcessName.Length == 0 ? "—" : r.ProcessName) + "\n" + EndpointReviewCore.ProcessText(r.ProcessEvidence) + (r.ProcessStartedAt is null ? "" : $"; {r.ProcessStartedAt:O}")))
                .Append("</td><td>").Append(H(Endpoint(r.LocalAddress, r.LocalPort))).Append("</td><td>").Append(H(Endpoint(r.RemoteAddress, r.RemotePort)))
                .Append("</td><td>").Append(H(item.PreviousState is not null && item.PreviousState != r.State ? item.PreviousState + " → " + r.State : r.State)).Append("</td></tr>");
        }
        s.Append("</tbody></table>");
    }
    public static string Detail(EndpointObservation item)
    {
        var r = item.Row;
        return $"{EndpointReviewCore.ChangeText(item.Change)}\r\n{r.Table}; PID Windows: {r.Pid}\r\nПроцесс: {(r.ProcessName.Length == 0 ? "—" : r.ProcessName)}\r\n{EndpointReviewCore.ProcessText(r.ProcessEvidence)}\r\nВремя запуска: {r.ProcessStartedAt:O}\r\nЛокально: {Endpoint(r.LocalAddress, r.LocalPort)}\r\nУдалённо: {Endpoint(r.RemoteAddress, r.RemotePort)}\r\nСостояние: {r.State}; исходный код: {r.StateCode?.ToString() ?? "—"}\r\nПредыдущее состояние: {item.PreviousState ?? "—"}\r\n\r\n" + Meaning;
    }
    public static string Endpoint(string? address, int? port) => address is null || port is null ? "—" : address.Contains(':') ? $"[{address}]:{port}" : $"{address}:{port}";
    public static string Save(EndpointSnapshot current, EndpointSnapshot? previous, string parent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        var html = Html(current, previous); var json = Json(current, previous);
        var root = Path.Combine(parent, $"Endpoints_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        Write(Path.Combine(root, "endpoints.html"), html); Write(Path.Combine(root, "endpoints.json"), json); return root;
    }
    private static void Write(string path, string content)
    { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); using var writer = new StreamWriter(stream, new UTF8Encoding(false)); writer.Write(content); }
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
}
