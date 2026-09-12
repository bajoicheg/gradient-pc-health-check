using System.Net;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class IncidentReport
{
    public const string Boundary = "Совпадение событий по времени не доказывает причину сбоя. PID в событии — идентификатор издателя, не обязательно сбойного приложения; его нельзя автоматически связывать с текущим процессом. Процессы — отдельный текущий снимок, не история. Полнота сбора не является оценкой здоровья. Поиск ограничен полученным снимком; отсутствующие или удалённые ранее события восстановить нельзя.";
    public static string StateText(string state) => state switch
    { "Complete" => "Сбор завершён", "Partial" => "Неполные данные", "Unavailable" => "Данные недоступны", "Cancelled" => "Отменено", "Verified" => "Владелец подтверждён", "Stale" => "Процесс изменился / завершён", "Unverified" => "Идентичность не подтверждена", _ => "Не проверено" };
    public static string LevelText(int? level) => level switch { 0 => "Не задан", 1 => "Критическое", 2 => "Ошибка", 3 => "Предупреждение", 4 => "Информация", 5 => "Подробно", _ => "Неизвестно" };
    public static string Json(object snapshot, object filter)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(filter);
        var visible = Count(snapshot, filter);
        return JsonSerializer.Serialize(new { SchemaVersion = 1, Snapshot = snapshot, Filter = filter, VisibleCount = visible, Boundary }, new JsonSerializerOptions { WriteIndented = true });
    }
    public static string Summary(object snapshot, object filter)
    {
        var visible = Count(snapshot, filter); var sb = new StringBuilder("G PC Health Check — разбор сбоя\n");
        if (snapshot is IncidentSnapshot events)
        {
            sb.AppendLine($"Сбор: {events.StartedAt:O} — {events.FinishedAt:O}; {StateText(events.State)}");
            sb.AppendLine($"Интервал: {events.Window.From:O} — {events.Window.To:O}; лимит {events.Window.MaxPerLog} на журнал.");
            sb.AppendLine($"Сохранено {events.Logs.Sum(x => x.Events.Count)} событий; по текущему фильтру {visible}.");
            foreach (var log in events.Logs)
            {
                sb.AppendLine($"{log.Log}: {StateText(log.State)}, записей {log.Events.Count}; замечаний {log.Warnings.Count}.");
                foreach (var warning in log.Warnings.Take(10)) sb.AppendLine("  " + warning);
            }
            foreach (var e in IncidentQueries.Events(events, (IncidentFilter)filter).Take(12)) sb.AppendLine($"{e.Timestamp:O} | {e.Log} | {e.Provider} | ID {e.EventId} | {LevelText(e.Level)}");
        }
        else if (snapshot is ProcessReviewSnapshot processes)
        {
            sb.AppendLine($"Сбор: {processes.StartedAt:O} — {processes.FinishedAt:O}; {StateText(processes.State)}");
            sb.AppendLine($"Сохранено {processes.Processes.Count} процессов; по фильтру {visible}; записей с замечаниями {processes.Processes.Count(x => x.Warnings.Count > 0)}.");
            foreach (var w in processes.Warnings) sb.AppendLine(w);
            foreach (var p in IncidentQueries.Processes(processes, (string)filter).Take(12)) sb.AppendLine($"PID {p.Pid} | {p.Name} | начало {p.CreatedAt:O} | working set {p.WorkingSetBytes?.ToString() ?? "—"} байт");
            foreach (var o in processes.OwnerChecks.TakeLast(10)) sb.AppendLine($"{o.CheckedAt:O} PID {o.Pid}: {StateText(o.State)}; {o.Owner}; {o.Detail}");
            sb.AppendLine("Пустой путь/команда или неизвестный владелец не являются признаком вредоносности. Команды только отображаются; запуск, завершение и изменение процессов не выполняются.");
        }
        sb.AppendLine("Полные записи и замечания — в HTML/JSON; отчёт содержит весь снимок, а не только отфильтрованные строки.");
        sb.AppendLine(Boundary); return sb.ToString();
    }
    public static string Html(object snapshot, object filter)
    {
        var summary = Summary(snapshot, filter); var sb = new StringBuilder("<!doctype html><html lang='ru'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>G PC Health Check — разбор сбоя</title><style>body{font:14px/1.5 'Segoe UI',sans-serif;margin:24px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #bbb;padding:8px;text-align:left;vertical-align:top;overflow-wrap:anywhere}pre{white-space:pre-wrap;overflow-wrap:anywhere}h2{margin-top:28px}</style><h1>G PC Health Check — разбор сбоя</h1><pre>");
        sb.Append(H(summary)).Append("</pre><h2>Полный сохранённый снимок</h2>");
        if (snapshot is IncidentSnapshot events)
        {
            sb.Append("<table><tr><th>Время / журнал</th><th>ID / источник</th><th>Уровень / PID издателя</th><th>Сообщение</th></tr>");
            foreach (var e in IncidentQueries.Events(events, new())) sb.Append("<tr><td>").Append(H($"{e.Timestamp:O}\n{e.Log}\nRecord {e.RecordId}")).Append("</td><td>").Append(H($"{e.EventId}\n{e.Provider}")).Append("</td><td>").Append(H($"{LevelText(e.Level)}\n{e.EmitterPid}")).Append("</td><td><pre>").Append(H(e.Message)).Append("</pre>").Append(H(e.MessageState)).Append("</td></tr>");
            sb.Append("</table>");
            foreach (var log in events.Logs) { sb.Append("<h2>").Append(H(log.Log)).Append(" — замечания</h2><pre>").Append(H(string.Join("\n", log.Warnings))).Append("</pre>"); }
        }
        else if (snapshot is ProcessReviewSnapshot processes)
        {
            sb.Append("<table><tr><th>PID / процесс</th><th>Создан / PPID / сеанс</th><th>Путь / команда</th><th>Память / потоки / дескрипторы</th><th>Замечания</th></tr>");
            foreach (var p in IncidentQueries.Processes(processes, "")) sb.Append("<tr><td>").Append(H($"{p.Pid}\n{p.Name}")).Append("</td><td>").Append(H($"{p.CreatedAt:O}\n{p.CreationKey}\nPPID {p.ParentPid}; session {p.SessionId}")).Append("</td><td><pre>").Append(H(p.Executable + "\n" + p.CommandLine)).Append("</pre></td><td>").Append(H($"{p.WorkingSetBytes?.ToString() ?? "—"} байт; threads {p.Threads}; handles {p.Handles}")).Append("</td><td>").Append(H(string.Join("; ", p.Warnings))).Append("</td></tr>");
            sb.Append("</table><h2>Проверки владельца</h2><pre>");
            foreach (var o in processes.OwnerChecks) sb.Append(H($"{o.CheckedAt:O} | PID {o.Pid} | {o.CreationKey} | {StateText(o.State)} | {o.Owner} | {o.Detail}\n"));
            sb.Append("</pre>");
        }
        return sb.Append("</html>").ToString();
    }
    private static int Count(object snapshot, object filter) => snapshot switch
    {
        IncidentSnapshot s when filter is IncidentFilter f => IncidentQueries.Events(s, f).Count,
        ProcessReviewSnapshot s when filter is string f => IncidentQueries.Processes(s, f).Count,
        _ => throw new ArgumentException("Неподдерживаемый снимок или фильтр.")
    };
    private static string H(string value) => WebUtility.HtmlEncode(value);
}
