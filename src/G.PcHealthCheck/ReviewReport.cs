using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace G.PcHealthCheck;

internal static class ReviewReport
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static string Title(object snapshot) => snapshot switch
    {
        TempPreviewSnapshot => "Предпросмотр очистки Temp",
        StartupReviewSnapshot => "Разбор автозагрузки",
        _ => throw new ArgumentException("Неизвестный тип снимка.", nameof(snapshot))
    };
    public static string State(ReviewCollectionState state) => state switch
    {
        ReviewCollectionState.Complete => "Сбор завершён в заявленной области",
        ReviewCollectionState.Partial => "Неполный сбор",
        ReviewCollectionState.Missing => "Каталог/источник отсутствует",
        _ => "Данные недоступны"
    };
    public static string Bytes(long bytes) => $"{bytes / 1048576d:N2} MiB ({bytes:N0} байт)";
    public static string Overview(object snapshot)
    {
        var sb = new StringBuilder(); sb.AppendLine(Title(snapshot));
        if (snapshot is TempPreviewSnapshot t)
        {
            sb.AppendLine($"{State(t.State)} · снимок {t.CompletedAt:dd.MM.yyyy HH:mm:ss zzz}");
            sb.AppendLine($"Чей Temp: {ExecutionPolicy.Value(t.TargetAccount)}; SID: {ExecutionPolicy.Value(t.TargetSid)}.");
            sb.AppendLine(ExecutionPolicy.Describe(t.ExecutionContext));
            sb.AppendLine("Область: " + (t.Root.Length == 0 ? "не определена" : t.Root));
            sb.AppendLine($"Правило: LastWriteTime < {t.Cutoff:dd.MM.yyyy HH:mm:ss zzz} (старше {t.OlderThanDays} дней).");
            if (t.State == ReviewCollectionState.Unavailable) sb.AppendLine("Кандидаты и объём: — (оценка недоступна).");
            else sb.AppendLine($"Кандидатов: {t.CandidateFiles:N0}; логическая оценка объёма: {Bytes(t.CandidateBytes)}.");
            sb.AppendLine($"Просмотрено записей: {t.VisitedEntries:N0}; пропущено ссылок: {t.SkippedLinks:N0}; ошибок: {t.Errors:N0}.");
            sb.AppendLine($"В таблице: {t.LargestFiles.Count} крупнейших из {t.CandidateFiles:N0} кандидатов. Лимиты: {t.MaxEntries:N0} записей / {t.TimeLimitSeconds:0.#} с.");
            sb.AppendLine("Это оценка, не обещание освободить указанный объём. Содержимое не читалось, файлы не удалялись. При очистке отбор выполняется заново; файл может измениться или оказаться заблокированным. Размеры логические, не занятое место на носителе.");
            sb.AppendLine(t.CleanupAvailability == "Ready"
                ? "Очистка доступна отдельным подтверждаемым действием CleanTemp в главном окне. Список крупнейших файлов не является планом удаления."
                : "Для удаления откройте программу обычным запуском от имени пользователя этого сеанса. Возможность чтения с повышением не означает возможность очистки в том же контексте.");
            foreach (var issue in t.Issues) sb.AppendLine("! " + issue);
            if (t.OmittedIssues > 0) sb.AppendLine($"Ещё сообщений: {t.OmittedIssues:N0}.");
        }
        else if (snapshot is StartupReviewSnapshot s)
        {
            sb.AppendLine($"{State(s.State)} · снимок {s.CollectedAt:dd.MM.yyyy HH:mm:ss zzz}");
            sb.AppendLine($"Аккаунт процесса: {s.Account}; записей: {s.Entries.Count}; источников: {s.Sources.Count}.");
            sb.AppendLine(ExecutionPolicy.Describe(s.ExecutionContext));
            sb.AppendLine(StartupReviewService.ScopeNote);
            sb.AppendLine("Команды показаны без запуска и подстановки переменных. Состояние включения: не определено; подпись, репутация и влияние на загрузку не проверялись.");
            foreach (var source in s.Sources) sb.AppendLine($"Источник: {source.Name} | {source.Scope} | {State(source.State)} | записей: {source.Items} | {source.Detail}");
            foreach (var issue in s.Issues) sb.AppendLine("! " + issue);
        }
        return sb.ToString().TrimEnd();
    }
    public static string Summary(object snapshot)
    {
        var sb = new StringBuilder("G PC Health Check\n"); sb.AppendLine(Overview(snapshot));
        if (snapshot is TempPreviewSnapshot t)
            foreach (var row in t.LargestFiles) sb.AppendLine($"{row.Bytes} байт | {row.LastWriteTime:O} | {row.Path}");
        else if (snapshot is StartupReviewSnapshot s)
            foreach (var row in s.Entries) sb.AppendLine($"{row.Name} | {row.Scope} | {row.Source} | {row.State}\n{row.Command}");
        return sb.ToString();
    }
    public static string Json(object snapshot)
    {
        _ = Title(snapshot);
        return JsonSerializer.Serialize(new { SchemaVersion = 1, Kind = snapshot is TempPreviewSnapshot ? "TempPreview" : "StartupReview", Snapshot = snapshot }, Options);
    }
    public static string Html(object snapshot)
    {
        var title = Title(snapshot);
        var sb = new StringBuilder("<!doctype html><html lang='ru'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>");
        sb.Append(H(title)).Append("</title><style>body{margin:0;background:#f4f7fa;color:#172432;font:14px/1.5 'Segoe UI',Arial,sans-serif}header{padding:24px;background:#15344f;color:white}main{padding:24px;max-width:1500px;margin:auto}section{background:white;border:1px solid #dce5ed;border-radius:12px;padding:18px;margin-bottom:18px}pre{white-space:pre-wrap;font:inherit;overflow-wrap:anywhere}table{width:100%;border-collapse:collapse}th,td{padding:10px;text-align:left;vertical-align:top;border-bottom:1px solid #dce5ed;white-space:pre-wrap;overflow-wrap:anywhere}th{background:#edf3f8}.table{overflow-x:auto}footer{padding:24px;color:#556}h1{margin:0;font-size:24px}</style></head><body><header><h1>G PC Health Check · ");
        sb.Append(H(title)).Append("</h1></header><main><section><pre>").Append(H(Overview(snapshot))).Append("</pre></section><section class='table'><table><thead><tr>");
        if (snapshot is TempPreviewSnapshot t)
        {
            foreach (var h in new[] { "Размер, байт", "Изменён", "Путь" }) sb.Append("<th>").Append(H(h)).Append("</th>");
            sb.Append("</tr></thead><tbody>");
            foreach (var row in t.LargestFiles) Row(sb, row.Bytes.ToString(), row.LastWriteTime.ToString("O"), row.Path);
        }
        else if (snapshot is StartupReviewSnapshot s)
        {
            foreach (var h in new[] { "Название", "Команда / ссылка на файл", "Область", "Источник", "Включение" }) sb.Append("<th>").Append(H(h)).Append("</th>");
            sb.Append("</tr></thead><tbody>");
            foreach (var row in s.Entries) Row(sb, row.Name, row.Command, row.Scope, row.Source, row.State);
        }
        return sb.Append("</tbody></table></section></main><footer>Экспорт содержит весь сохранённый снимок, независимо от поиска в окне. Пути и команды могут содержать чувствительные данные. Не публикуйте отчёт без проверки.</footer></body></html>").ToString();
    }
    private static void Row(StringBuilder sb, params string[] values)
    {
        sb.Append("<tr>"); foreach (var value in values) sb.Append("<td>").Append(H(value)).Append("</td>"); sb.Append("</tr>");
    }
    private static string H(string value) => WebUtility.HtmlEncode(value);
}
