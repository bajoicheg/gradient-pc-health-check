using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Gradient.PcHealthCheck;

public sealed class ReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ReportsDirectory
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gradient", "PCHealthCheck", "Reports");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public (string Html, string Json) SaveScan(ScanResult scan)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeHost = SafeName(scan.Data.System.ComputerName);
        var basePath = Path.Combine(ReportsDirectory, $"PCHealth_{safeHost}_{stamp}");
        var jsonPath = basePath + ".json";
        var htmlPath = basePath + ".html";
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(scan, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(htmlPath, BuildScanHtml(scan), new UTF8Encoding(false));
        return (htmlPath, jsonPath);
    }

    public (string Html, string Json, string Zip) SaveVerification(VerificationResult verification)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeHost = SafeName(verification.After.Data.System.ComputerName);
        var basePath = Path.Combine(ReportsDirectory, $"PCHealth_{safeHost}_{stamp}_verification");
        var jsonPath = basePath + ".json";
        var htmlPath = basePath + ".html";
        var zipPath = basePath + ".zip";
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(verification, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(htmlPath, BuildVerificationHtml(verification), new UTF8Encoding(false));
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(htmlPath, Path.GetFileName(htmlPath), CompressionLevel.Optimal);
            zip.CreateEntryFromFile(jsonPath, Path.GetFileName(jsonPath), CompressionLevel.Optimal);
        }
        return (htmlPath, jsonPath, zipPath);
    }

    private static string BuildScanHtml(ScanResult scan)
    {
        var d = scan.Data;
        var a = scan.Assessment;
        var statusClass = a.Status.ToLowerInvariant();
        var sb = new StringBuilder();
        sb.Append(HtmlHead($"Gradient PC Health Check — {H(d.System.ComputerName)}"));
        sb.Append($"<header><div class='brand'>{ShieldSvg()}<div><h1>Gradient PC Health Check</h1><div class='muted'>Диагностика рабочего места Service Desk</div></div></div><div class='stamp'>{H(d.System.CollectedAt.ToString("dd.MM.yyyy HH:mm:ss"))}</div></header>");
        sb.Append("<main>");
        sb.Append($"<section class='hero'><div class='score {statusClass}'>{a.Score}<span>/100</span></div><div><h2>{StatusText(a.Status)}</h2><p>{H(d.System.ComputerName)} · {H(d.System.UserName)} · {H(d.System.Model)}</p><p class='muted'>Индекс — эвристика первичной диагностики, а не доказательство исправности.</p></div></section>");
        sb.Append(MetricCards(scan));
        sb.Append("<section><h2>Выводы</h2><table><thead><tr><th>Статус</th><th>Категория</th><th>Наблюдение</th><th>Значение</th><th>Что делать</th></tr></thead><tbody>");
        foreach (var f in a.Findings.OrderBy(x => SeverityOrder(x.Severity)))
            sb.Append($"<tr><td><span class='pill {f.Severity.ToLowerInvariant()}'>{H(f.Severity)}</span></td><td>{H(f.Category)}</td><td>{H(f.Title)}</td><td>{H(f.Value)}</td><td>{H(f.Recommendation)}</td></tr>");
        sb.Append("</tbody></table></section>");

        sb.Append("<section><h2>Действия Service Desk</h2><table><thead><tr><th>Тип</th><th>Действие</th><th>Причина</th><th>Автоматизация</th><th>Admin</th><th>Риск</th></tr></thead><tbody>");
        foreach (var x in scan.Actions)
            sb.Append($"<tr><td>{H(x.Kind)}</td><td>{H(x.Title)}</td><td>{H(x.Reason)}</td><td>{(x.CanAutomate ? "Да" : "Нет")}</td><td>{(x.RequiresAdmin ? "Да" : "Нет")}</td><td>{H(x.Risk)}</td></tr>");
        sb.Append("</tbody></table></section>");

        sb.Append(ProcessesHtml(d));
        sb.Append(EventsHtml(d));
        sb.Append(SystemHtml(d));
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static string BuildVerificationHtml(VerificationResult v)
    {
        var b = v.Before;
        var a = v.After;
        var sb = new StringBuilder();
        sb.Append(HtmlHead($"Проверка после remediation — {H(a.Data.System.ComputerName)}"));
        sb.Append($"<header><div class='brand'>{ShieldSvg()}<div><h1>Gradient PC Health Check</h1><div class='muted'>Автопроверка после remediation</div></div></div><div class='stamp'>{DateTime.Now:dd.MM.yyyy HH:mm:ss}</div></header><main>");
        sb.Append($"<section class='hero'><div class='score {a.Assessment.Status.ToLowerInvariant()}'>{a.Assessment.Score}<span>/100</span></div><div><h2>После применения выбранных действий</h2><p>{H(a.Data.System.ComputerName)} · было {b.Assessment.Score}/100 → стало {a.Assessment.Score}/100</p><p class='muted'>Кратковременные CPU/RAM значения не считаются сами по себе доказательством исправления.</p></div></section>");
        sb.Append("<section><h2>До / после</h2><table><thead><tr><th>Показатель</th><th>До</th><th>После</th><th>Изменение</th></tr></thead><tbody>");
        foreach (var row in CompareRows(b, a)) sb.Append($"<tr><td>{H(row.Name)}</td><td>{H(row.Before)}</td><td>{H(row.After)}</td><td>{H(row.Delta)}</td></tr>");
        sb.Append("</tbody></table></section>");
        sb.Append("<section><h2>Выполненные действия</h2><table><thead><tr><th>Действие</th><th>Результат</th><th>Код</th><th>Подробности</th></tr></thead><tbody>");
        foreach (var r in v.Remediation.Actions)
        {
            var details = r.Id == "CleanTemp" ? $"{r.Message}" : r.Message;
            sb.Append($"<tr><td>{H(r.Id)}</td><td><span class='pill {(r.Success ? "ok" : "crit")}'>{(r.Success ? "Успешно" : "Ошибка")}</span></td><td>{H(r.ExitCode?.ToString() ?? "—")}</td><td>{H(details)}</td></tr>");
        }
        sb.Append("</tbody></table></section>");
        sb.Append("<section><h2>Актуальные выводы после проверки</h2><table><thead><tr><th>Статус</th><th>Категория</th><th>Наблюдение</th><th>Значение</th><th>Что делать</th></tr></thead><tbody>");
        foreach (var f in a.Assessment.Findings.OrderBy(x => SeverityOrder(x.Severity)))
            sb.Append($"<tr><td><span class='pill {f.Severity.ToLowerInvariant()}'>{H(f.Severity)}</span></td><td>{H(f.Category)}</td><td>{H(f.Title)}</td><td>{H(f.Value)}</td><td>{H(f.Recommendation)}</td></tr>");
        sb.Append("</tbody></table></section></main></body></html>");
        return sb.ToString();
    }

    public static List<(string Name, string Before, string After, string Delta)> CompareRows(ScanResult before, ScanResult after)
    {
        var bd = SystemDisk(before.Data);
        var ad = SystemDisk(after.Data);
        return
        [
            ("Индекс", before.Assessment.Score.ToString(), after.Assessment.Score.ToString(), Signed(after.Assessment.Score - before.Assessment.Score)),
            ("CPU", F(before.Data.Performance.CpuPercent, "%"), F(after.Data.Performance.CpuPercent, "%"), Delta(before.Data.Performance.CpuPercent, after.Data.Performance.CpuPercent, "%")),
            ("RAM доступно", F(before.Data.Performance.MemoryAvailablePercent, "%"), F(after.Data.Performance.MemoryAvailablePercent, "%"), Delta(before.Data.Performance.MemoryAvailablePercent, after.Data.Performance.MemoryAvailablePercent, "%")),
            ("Свободно C:", bd is null ? "—" : $"{bd.FreeGB:0.0} GB", ad is null ? "—" : $"{ad.FreeGB:0.0} GB", bd is null || ad is null ? "—" : Signed(ad.FreeGB - bd.FreeGB, " GB")),
            ("Очередь диска", F(before.Data.Performance.DiskQueueLength, ""), F(after.Data.Performance.DiskQueueLength, ""), Delta(before.Data.Performance.DiskQueueLength, after.Data.Performance.DiskQueueLength, "")),
            ("Critical/Error за 24ч", (before.Data.Events.CriticalCount + before.Data.Events.ErrorCount).ToString(), (after.Data.Events.CriticalCount + after.Data.Events.ErrorCount).ToString(), Signed((after.Data.Events.CriticalCount + after.Data.Events.ErrorCount) - (before.Data.Events.CriticalCount + before.Data.Events.ErrorCount))),
            ("Pending reboot", before.Data.PendingReboot.Pending ? "Да" : "Нет", after.Data.PendingReboot.Pending ? "Да" : "Нет", before.Data.PendingReboot.Pending == after.Data.PendingReboot.Pending ? "без изменений" : "изменилось")
        ];
    }

    private static string MetricCards(ScanResult scan)
    {
        var d = scan.Data;
        var disk = SystemDisk(d);
        return $"<section class='cards'><div class='card'><b>CPU</b><strong>{F(d.Performance.CpuPercent, "%")}</strong><small>текущая загрузка</small></div><div class='card'><b>RAM</b><strong>{F(d.Performance.MemoryUsedPercent, "%")}</strong><small>использовано</small></div><div class='card'><b>Системный диск</b><strong>{(disk is null ? "—" : $"{disk.FreeGB:0.#} GB")}</strong><small>свободно</small></div><div class='card'><b>Uptime</b><strong>{d.System.UptimeDays:0.#} дн.</strong><small>{(d.PendingReboot.Pending ? "pending reboot" : "перезагрузка не ожидается")}</small></div></section>";
    }

    private static string ProcessesHtml(DiagnosticData d)
    {
        var sb = new StringBuilder("<section><h2>TOP процессов</h2><div class='grid3'>");
        sb.Append(ProcessTable("CPU", d.TopCpu));
        sb.Append(ProcessTable("RAM", d.TopMemory));
        sb.Append(ProcessTable("I/O", d.TopIo));
        sb.Append("</div></section>");
        return sb.ToString();
    }

    private static string ProcessTable(string title, List<ProcessInfo> items)
    {
        var sb = new StringBuilder($"<div><h3>{H(title)}</h3><table><thead><tr><th>Процесс</th><th>PID</th><th>CPU</th><th>RAM</th><th>I/O</th></tr></thead><tbody>");
        foreach (var p in items) sb.Append($"<tr><td>{H(p.Name)}</td><td>{p.Pid}</td><td>{p.CpuPercent:0.#}%</td><td>{p.MemoryMB:0.#} MB</td><td>{p.IoMBPerSec:0.##} MB/s</td></tr>");
        sb.Append("</tbody></table></div>");
        return sb.ToString();
    }

    private static string EventsHtml(DiagnosticData d)
    {
        var sb = new StringBuilder($"<section><h2>Ошибки Windows за {d.Events.Hours} часов</h2><p>Critical: <b>{d.Events.CriticalCount}</b> · Error: <b>{d.Events.ErrorCount}</b></p><table><thead><tr><th>Log</th><th>Provider</th><th>Event ID</th><th>Количество</th><th>Последнее</th></tr></thead><tbody>");
        foreach (var e in d.Events.Top) sb.Append($"<tr><td>{H(e.Log)}</td><td>{H(e.Provider)}</td><td>{e.EventId}</td><td>{e.Count}</td><td>{H(e.LastSeen?.ToString("dd.MM HH:mm:ss") ?? "")}</td></tr>");
        sb.Append("</tbody></table></section>");
        return sb.ToString();
    }

    private static string SystemHtml(DiagnosticData d)
    {
        var sb = new StringBuilder("<section><h2>Система</h2><table><tbody>");
        void Row(string k, string v) => sb.Append($"<tr><th>{H(k)}</th><td>{H(v)}</td></tr>");
        Row("ПК", d.System.ComputerName); Row("Пользователь", d.System.UserName); Row("Производитель / модель", d.System.Manufacturer + " " + d.System.Model);
        Row("Windows", d.System.OS + " " + d.System.OSVersion + " build " + d.System.BuildNumber); Row("CPU", d.System.Cpu); Row("RAM", $"{d.System.TotalMemoryGB:0.#} GB");
        Row("Последняя загрузка", d.System.LastBoot.ToString("dd.MM.yyyy HH:mm:ss")); Row("Права процесса", d.System.IsAdministrator ? "Administrator" : "Standard user");
        Row("Windows Update", $"wuauserv={d.Updates.Wuauserv}; BITS={d.Updates.Bits}");
        Row("Антивирус / EDR", d.SecurityProducts.Count == 0 ? "Не удалось определить" : string.Join("; ", d.SecurityProducts.Select(x => x.Name + " [" + x.State + "]")));
        sb.Append("</tbody></table>");
        if (d.CollectionWarnings.Count > 0) sb.Append("<h3>Предупреждения сбора</h3><ul>" + string.Join("", d.CollectionWarnings.Select(x => $"<li>{H(x)}</li>")) + "</ul>");
        sb.Append("</section>");
        return sb.ToString();
    }

    private static LogicalDiskInfo? SystemDisk(DiagnosticData d) => d.LogicalDisks.FirstOrDefault(x => string.Equals(x.Drive, "C:", StringComparison.OrdinalIgnoreCase));
    private static LogicalDiskInfo? SystemDisk(ScanResult d) => SystemDisk(d.Data);
    private static string F(double? v, string suffix) => v is null ? "—" : $"{v:0.#}{suffix}";
    private static string Delta(double? b, double? a, string suffix) => b is null || a is null ? "—" : Signed(a.Value - b.Value, suffix);
    private static string Signed(double v, string suffix = "") => (v > 0 ? "+" : "") + v.ToString("0.##") + suffix;
    private static string Signed(int v) => v > 0 ? "+" + v : v.ToString();
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static int SeverityOrder(string s) => s == "CRIT" ? 0 : s == "WARN" ? 1 : 2;
    private static string StatusText(string s) => s == "OK" ? "Состояние без явных критических признаков" : s == "WARN" ? "Есть признаки, требующие внимания" : "Есть критические признаки";
    private static string SafeName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static string ShieldSvg() => "<svg class='logo' viewBox='0 0 100 110' xmlns='http://www.w3.org/2000/svg'><path fill='#15344f' d='M50 2L94 17l-4 44c-3 20-18 37-40 47C28 98 13 81 10 61L6 17z'/><path fill='#2386c0' d='M50 8l37 13-4 38c-3 16-14 31-33 41-19-10-30-25-33-41l-4-38z'/><text x='50' y='68' text-anchor='middle' font-family='Segoe UI,Arial' font-size='52' font-weight='700' fill='white'>G</text><path d='M26 79h14l6-8 6 17 7-13 6 4h11' fill='none' stroke='#b4eaff' stroke-width='4' stroke-linejoin='round'/></svg>";

    private static string HtmlHead(string title) => $"""
<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{title}</title>
<style>
:root{{--bg:#f4f7fa;--surface:#fff;--ink:#172432;--muted:#687a8b;--line:#dce5ed;--blue:#2386c0;--navy:#15344f;--ok:#177a4b;--warn:#a56a00;--crit:#b53636}}
*{{box-sizing:border-box}}body{{margin:0;background:var(--bg);color:var(--ink);font:14px/1.45 'Segoe UI',Arial,sans-serif}}header{{display:flex;justify-content:space-between;align-items:center;padding:20px 32px;background:#fff;border-bottom:1px solid var(--line);position:sticky;top:0;z-index:3}}.brand{{display:flex;gap:14px;align-items:center}}.logo{{width:52px;height:58px}}h1{{margin:0;font-size:22px}}h2{{margin:0 0 14px;font-size:19px}}h3{{margin:14px 0 8px}}.muted,.stamp{{color:var(--muted)}}main{{max-width:1480px;margin:auto;padding:24px}}section{{background:var(--surface);border:1px solid var(--line);border-radius:14px;padding:20px;margin:0 0 18px;box-shadow:0 3px 12px rgba(27,55,79,.04)}}.hero{{display:flex;gap:22px;align-items:center}}.score{{width:96px;height:96px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:34px;font-weight:700;border:9px solid}}.score span{{font-size:12px;margin-top:18px}}.score.ok{{color:var(--ok);border-color:#bfe5d2}}.score.warn{{color:var(--warn);border-color:#f4d99c}}.score.crit{{color:var(--crit);border-color:#f0b8b8}}.cards{{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;background:none;border:0;padding:0;box-shadow:none}}.card{{background:#fff;border:1px solid var(--line);border-radius:14px;padding:18px;display:flex;flex-direction:column}}.card strong{{font-size:27px;margin:5px 0}}.card small{{color:var(--muted)}}table{{border-collapse:collapse;width:100%;font-size:13px}}th,td{{border-bottom:1px solid var(--line);padding:9px 10px;text-align:left;vertical-align:top}}th{{color:#4b6174;background:#f8fafc}}.pill{{display:inline-block;border-radius:999px;padding:3px 8px;font-size:11px;font-weight:700}}.pill.ok{{background:#dff4e9;color:var(--ok)}}.pill.warn{{background:#fff0c9;color:var(--warn)}}.pill.crit{{background:#ffe1e1;color:var(--crit)}}.grid3{{display:grid;grid-template-columns:repeat(3,1fr);gap:16px}}@media(max-width:1000px){{.cards,.grid3{{grid-template-columns:1fr 1fr}}}}@media(max-width:650px){{main{{padding:10px}}header{{padding:12px}}.cards,.grid3{{grid-template-columns:1fr}}.hero{{align-items:flex-start}}}}
</style></head><body>
""";
}
