using System.Net;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class ResourceProbeReport
{
    public const string Boundary = "Проверяется системное разрешение имени и прямое TCP-подключение. TLS, HTTP, вход пользователя и работоспособность приложения не проверяются. Системный HTTP-прокси не используется; маршруты/VPN выбирает ОС. Время TCP — время установления соединения, не ping и не скорость передачи.";
    public static string OutcomeText(string outcome) => outcome switch
    {
        "Connected" => "TCP-соединение установлено", "Partial" => "Частичный результат", "Failed" => "Проверка неуспешна",
        "Cancelled" => "Проверка отменена", "Resolved" => "Имя разрешено", "Skipped" => "Этап не требуется",
        "Refused" => "Соединение отклонено", "Timeout" => "Тайм-аут", "Unreachable" => "Нет доступного маршрута/узла",
        "Denied" => "Доступ запрещён ОС", _ => "Нет результата"
    };
    public static string Summary(ResourceProbeSnapshot current, ResourceProbeSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        var sb = new StringBuilder("G PC Health Check — проверка ресурса\n");
        AppendSummary(sb, current, "Текущая попытка");
        if (previous is not null)
        {
            sb.AppendLine(current.Target == previous.Target ? "Повторная проверка той же цели; изменение не доказывает устранение причины." : "Проверены разные цели; сравнение не является проверкой до/после ремонта.");
            AppendSummary(sb, previous, "Предыдущая попытка");
        }
        return sb.AppendLine(Boundary).ToString();
    }
    private static void AppendSummary(StringBuilder sb, ResourceProbeSnapshot s, string title)
    {
        sb.AppendLine($"{title}: {s.StartedAt:yyyy-MM-dd HH:mm:ss zzz} → {s.FinishedAt:HH:mm:ss zzz}");
        sb.AppendLine($"Цель: {s.Target.Host}; TCP-порт: {s.Target.Port}; результат: {OutcomeText(s.Outcome)} ({s.Outcome})");
        sb.AppendLine($"Лимиты: DNS {s.Options.DnsTimeoutMs} мс; TCP {s.Options.TcpTimeoutMs} мс/адрес; не более {s.Options.MaxAddresses} адресов.");
        sb.AppendLine("Адреса: " + (s.Addresses.Count == 0 ? "нет" : string.Join(", ", s.Addresses)));
        foreach (var x in s.Steps) sb.AppendLine($"{x.Stage} {x.Endpoint}: {OutcomeText(x.Outcome)}; {x.ElapsedMs:0.##} мс; {x.ErrorCode}; исходный IP: {x.LocalAddress}; {x.Detail}");
        foreach (var w in s.Warnings) sb.AppendLine("Примечание: " + w);
    }
    public static string Json(ResourceProbeSnapshot current, ResourceProbeSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        return JsonSerializer.Serialize(new { SchemaVersion = 1, Kind = "ResourceDiagnostics", Boundary, Current = current, Previous = previous }, new JsonSerializerOptions { WriteIndented = true });
    }
    public static string Html(ResourceProbeSnapshot current, ResourceProbeSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        var sb = new StringBuilder("<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>G PC Health Check — ресурс</title><style>body{font:15px/1.5 'Segoe UI',sans-serif;background:#f4f7fa;color:#172432;margin:24px}main{max-width:1280px;margin:auto}section{background:white;padding:20px;margin:16px 0;border:1px solid #dce5ed;border-radius:12px}table{width:100%;border-collapse:collapse}td,th{padding:9px;text-align:left;border-bottom:1px solid #dce5ed;vertical-align:top;overflow-wrap:anywhere}pre{white-space:pre-wrap;overflow-wrap:anywhere}small{color:#526475}</style></head><body><main><h1>G PC Health Check — проверка ресурса</h1>");
        sb.Append("<p>").Append(H(Boundary)).Append("</p>");
        Section(sb, current, "Текущая попытка");
        if (previous is not null)
        {
            sb.Append("<p>").Append(current.Target == previous.Target ? "Повтор той же цели; это не доказательство устранения причины." : "Проверены разные цели; это не сравнение до/после ремонта.").Append("</p>");
            Section(sb, previous, "Предыдущая попытка");
        }
        return sb.Append("</main></body></html>").ToString();
    }
    private static void Section(StringBuilder sb, ResourceProbeSnapshot s, string title)
    {
        sb.Append("<section><h2>").Append(H(title)).Append("</h2><p><b>").Append(H(s.Target.Host)).Append(" · TCP ").Append(s.Target.Port).Append(" · ").Append(H(OutcomeText(s.Outcome))).Append("</b></p>");
        sb.Append("<p>").Append(H($"{s.StartedAt:yyyy-MM-dd HH:mm:ss zzz} → {s.FinishedAt:yyyy-MM-dd HH:mm:ss zzz}")).Append("</p>");
        sb.Append("<p>").Append(H($"DNS: {s.Options.DnsTimeoutMs} мс; TCP: {s.Options.TcpTimeoutMs} мс; адресов не более {s.Options.MaxAddresses}.")).Append("</p>");
        sb.Append("<p>Полученные адреса: ").Append(H(string.Join(", ", s.Addresses))).Append("</p><table><thead><tr><th>Этап / адрес</th><th>Результат</th><th>мс</th><th>Исходный IP</th><th>Код / подробности</th></tr></thead><tbody>");
        foreach (var x in s.Steps) sb.Append("<tr><td>").Append(H(x.Stage + " " + x.Endpoint)).Append("</td><td>").Append(H(OutcomeText(x.Outcome))).Append("</td><td>").Append(H(x.ElapsedMs.ToString("0.##"))).Append("</td><td>").Append(H(x.LocalAddress)).Append("</td><td>").Append(H(x.ErrorCode + " " + x.Detail)).Append("</td></tr>");
        sb.Append("</tbody></table>"); foreach (var w in s.Warnings) sb.Append("<p>").Append(H(w)).Append("</p>"); sb.Append("</section>");
    }
    private static string H(string value) => WebUtility.HtmlEncode(value);
}
