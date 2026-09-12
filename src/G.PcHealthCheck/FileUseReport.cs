using System.Net;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class FileUseReport
{
    public const string Meaning = "Restart Manager сообщает приложения/службы, использующие выбранный путь. Это не полный перечень дескрипторов и не проверка прав удаления. Пустой список не доказывает отсутствие блокировки: повторите именно ту операцию, которая не выполнялась. Названия от Restart Manager и дополнительно проверенные метаданные EXE — разные источники. Результат относится ко времени запроса; файл и процессы могут измениться.";
    public const string NextSteps = "Сохраните работу. Проверьте найденное приложение и при необходимости закройте его обычным способом, затем повторите проверку и исходную операцию. Не завершайте системные процессы и службы по одной строке результата. Если список пуст или недоступен, проверьте права на файл, его атрибуты, область пути и фактическое сообщение Windows; не отключайте защитное ПО для проверки.";
    public static string Summary(FileUseSnapshot current, FileUseSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        var s = new StringBuilder();
        s.AppendLine("G PC Health Check — кто использует файл");
        s.AppendLine("Проверенный путь: " + current.TargetPath);
        s.AppendLine($"ПК: {current.ComputerName}; {current.StartedAt:O} — {current.FinishedAt:O}.");
        s.AppendLine($"Сбор: {FileUseCore.StateText(current.State)}; этап: {current.Stage}.");
        s.AppendLine(FileUseCore.Verdict(current));
        s.AppendLine($"Записей: {current.Processes.Count}; EXE подтверждён: {current.Processes.Count(x => x.IdentityState == "Matched")}; запросов списка: {current.QueryAttempts}.");
        s.AppendLine($"Ошибка запроса: {current.ErrorCode?.ToString() ?? "—"}; код завершения RM-сеанса: {current.EndSessionCode?.ToString() ?? "—"}.");
        foreach (var warning in current.Warnings) s.AppendLine("! " + warning);
        if (previous is not null)
        {
            s.AppendLine($"Предыдущая попытка: {previous.TargetPath}; {previous.StartedAt:O}; {FileUseCore.StateText(previous.State)}.");
            s.AppendLine("Попытки сохранены независимо со своими путями и контекстом. Разница списков не является доказательством снятия блокировки.");
        }
        s.AppendLine(Meaning); s.AppendLine(NextSteps);
        s.AppendLine(ExecutionPolicy.Describe(current.ExecutionContext));
        s.AppendLine("Сеансы и регистрации Restart Manager временно управляются Windows. Программа не изменяет выбранный файл, не читает его содержимое и не закрывает приложения.");
        s.AppendLine("Экспорт содержит обе попытки целиком, независимо от поиска. Пути, имена приложений и аккаунтов могут быть чувствительными.");
        return s.ToString().TrimEnd();
    }
    public static string Json(FileUseSnapshot current, FileUseSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        return JsonSerializer.Serialize(new { SchemaVersion = 1, Current = current, Previous = previous, Interpretation = Meaning, NextSteps }, new JsonSerializerOptions { WriteIndented = true });
    }
    public static string Html(FileUseSnapshot current, FileUseSnapshot? previous)
    {
        var s = new StringBuilder("<!doctype html><html lang='ru'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>G PC Health Check — кто использует файл</title><style>body{font:14px/1.5 'Segoe UI',sans-serif;margin:24px}pre{white-space:pre-wrap;overflow-wrap:anywhere}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ddd;text-align:left;padding:8px;vertical-align:top;overflow-wrap:anywhere}th{background:#f3f5f7}section{overflow:auto;margin-bottom:24px}</style></head><body><h1>Кто использует файл</h1><pre>");
        s.Append(H(Summary(current, previous))).Append("</pre>");
        Snapshot(s, current, "Текущая попытка");
        if (previous is not null) Snapshot(s, previous, "Предыдущая попытка");
        return s.Append("</body></html>").ToString();
    }
    private static void Snapshot(StringBuilder s, FileUseSnapshot data, string title)
    {
        s.Append("<section><h2>").Append(H(title)).Append("</h2><pre>").Append(H(Summary(data, null))).Append("</pre>");
        s.Append("<table><thead><tr><th>PID / создание</th><th>Приложение (RM)</th><th>Служба (RM)</th><th>Тип / сеанс</th><th>Проверенный EXE</th><th>Подробности</th></tr></thead><tbody>");
        foreach (var row in data.Processes)
        {
            s.Append("<tr><td>").Append(row.Pid).Append("<br>").Append(H(Time(row.StartFileTime)))
                .Append("</td><td>").Append(H(Value(row.ApplicationName))).Append("</td><td>").Append(H(Value(row.ServiceName)))
                .Append("</td><td>").Append(H(FileUseCore.TypeText(row.ApplicationType))).Append("<br>").Append(H(FileUseCore.Session(row)))
                .Append("</td><td>").Append(H(Value(row.ImagePath))).Append("</td><td><pre>").Append(H(Detail(row))).Append("</pre></td></tr>");
        }
        s.Append("</tbody></table><p>").Append(H($"Исходные флаги RM RebootReasons: {data.RebootReasons?.ToString() ?? "—"}. Это не рекомендация перезагрузки."))
            .Append("</p></section>");
    }
    public static string Detail(FileUseProcess row)
        => $"Источник: Windows Restart Manager\r\nPID: {row.Pid}; время создания: {Time(row.StartFileTime)}\r\nПриложение (RM): {Value(row.ApplicationName)}\r\nСлужба (RM): {Value(row.ServiceName)}\r\nТип (RM): {FileUseCore.TypeText(row.ApplicationType)} ({row.ApplicationType})\r\nСеанс (RM): {FileUseCore.Session(row)}\r\n\r\nEXE: {Value(row.ImagePath)}\r\n{FileUseCore.IdentityText(row.IdentityState)}\r\nОшибка чтения EXE: {row.IdentityError?.ToString() ?? "—"}\r\n\r\nИсходный AppStatus: 0x{row.ApplicationStatus:X8}; Restartable (RM): {row.Restartable}. Эти флаги не являются предложением завершить или перезапустить процесс.\r\n\r\n" + NextSteps;
    public static string Save(FileUseSnapshot current, FileUseSnapshot? previous, string parent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        var html = Html(current, previous); var json = Json(current, previous);
        var folder = Path.Combine(parent, $"FileUse_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"); Directory.CreateDirectory(folder);
        Write(Path.Combine(folder, "file-use.html"), html); Write(Path.Combine(folder, "file-use.json"), json); return folder;
    }
    private static void Write(string path, string content)
    { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); using var writer = new StreamWriter(stream, new UTF8Encoding(false)); writer.Write(content); }
    private static string Time(ulong fileTime) => FileUseCore.StartTime(fileTime)?.ToString("O") ?? "—";
    private static string Value(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
}
