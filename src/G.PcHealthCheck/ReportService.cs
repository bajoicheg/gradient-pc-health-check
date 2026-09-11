using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

public sealed class ReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ReportsDirectory
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "G", "PCHealthCheck", "Reports");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public (string Html, string Json) SaveScan(ScanResult scan)
    {
        var basePath = BasePath(scan.Data.System.ComputerName, "");
        var json = basePath + ".json";
        var html = basePath + ".html";
        File.WriteAllText(json, JsonSerializer.Serialize(scan, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(html, BuildScanHtml(scan), new UTF8Encoding(false));
        return (html, json);
    }

    public (string Html, string Json, string Zip) SaveVerification(VerificationResult verification)
    {
        var basePath = BasePath(verification.After.Data.System.ComputerName, "_verification");
        var json = basePath + ".json";
        var html = basePath + ".html";
        var zip = basePath + ".zip";
        File.WriteAllText(json, JsonSerializer.Serialize(verification, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(html, BuildVerificationHtml(verification), new UTF8Encoding(false));
        if (File.Exists(zip)) File.Delete(zip);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(html, Path.GetFileName(html), CompressionLevel.Optimal);
            archive.CreateEntryFromFile(json, Path.GetFileName(json), CompressionLevel.Optimal);
        }
        return (html, json, zip);
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

    private string BasePath(string host, string suffix)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(ReportsDirectory, $"PCHealth_{SafeName(host)}_{stamp}{suffix}");
    }

    private static string BuildScanHtml(ScanResult scan)
    {
        var d = scan.Data;
        var sb = Begin("G PC Health Check — " + d.System.ComputerName);
        Header(sb, "Диагностика рабочего места Service Desk", d.System.CollectedAt);
        Hero(sb, scan.Assessment.Score, scan.Assessment.Status, d.System.ComputerName + " · " + d.System.UserName + " · " + d.System.Model);
        Metrics(sb, scan);
        Findings(sb, scan.Assessment.Findings, "Выводы");
        sb.Append("<section><h2>Действия Service Desk</h2><table><thead><tr><th>Тип</th><th>Действие</th><th>Причина</th><th>Авто</th><th>Admin</th><th>Риск</th></tr></thead><tbody>");
        foreach (var a in scan.Actions)
            sb.Append("<tr><td>").Append(H(a.Kind)).Append("</td><td>").Append(H(a.Title)).Append("</td><td>").Append(H(a.Reason)).Append("</td><td>").Append(a.CanAutomate ? "Да" : "Нет").Append("</td><td>").Append(a.RequiresAdmin ? "Да" : "Нет").Append("</td><td>").Append(H(a.Risk)).Append("</td></tr>");
        sb.Append("</tbody></table></section>");
        Processes(sb, d);
        Events(sb, d);
        SystemBlock(sb, d);
        return End(sb);
    }

    private static string BuildVerificationHtml(VerificationResult v)
    {
        var sb = Begin("G PC Health Check — автопроверка");
        Header(sb, "Автопроверка после remediation", DateTime.Now);
        Hero(sb, v.After.Assessment.Score, v.After.Assessment.Status, $"{v.After.Data.System.ComputerName} · было {v.Before.Assessment.Score}/100 → стало {v.After.Assessment.Score}/100");
        sb.Append("<section><h2>До / после</h2><table><thead><tr><th>Показатель</th><th>До</th><th>После</th><th>Изменение</th></tr></thead><tbody>");
        foreach (var r in CompareRows(v.Before, v.After))
            sb.Append("<tr><td>").Append(H(r.Name)).Append("</td><td>").Append(H(r.Before)).Append("</td><td>").Append(H(r.After)).Append("</td><td>").Append(H(r.Delta)).Append("</td></tr>");
        sb.Append("</tbody></table></section>");
        sb.Append("<section><h2>Выполненные действия</h2><table><thead><tr><th>Действие</th><th>Результат</th><th>Код</th><th>Подробности</th></tr></thead><tbody>");
        foreach (var r in v.Remediation.Actions)
            sb.Append("<tr><td>").Append(H(r.Id)).Append("</td><td><span class='pill ").Append(r.Success ? "ok" : "crit").Append("'>").Append(r.Success ? "Успешно" : "Ошибка").Append("</span></td><td>").Append(H(r.ExitCode?.ToString() ?? "—")).Append("</td><td>").Append(H(r.Message)).Append("</td></tr>");
        sb.Append("</tbody></table></section>");
        Findings(sb, v.After.Assessment.Findings, "Актуальные выводы после проверки");
        return End(sb);
    }

    private static StringBuilder Begin(string title)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang='ru'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>").Append(H(title)).Append("</title><style>");
        sb.Append("body{margin:0;background:#f4f7fa;color:#172432;font:14px/1.45 'Segoe UI',Arial,sans-serif}header{display:flex;justify-content:space-between;align-items:center;padding:20px 32px;background:white;border-bottom:1px solid #dce5ed}.brand{display:flex;gap:14px;align-items:center}.logo{width:52px;height:58px}main{max-width:1480px;margin:auto;padding:24px}section{background:white;border:1px solid #dce5ed;border-radius:14px;padding:20px;margin:0 0 18px}.hero{display:flex;gap:22px;align-items:center}.score{width:96px;height:96px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:34px;font-weight:700;border:9px solid}.score.ok{color:#177a4b;border-color:#bfe5d2}.score.warn{color:#a56a00;border-color:#f4d99c}.score.crit{color:#b53636;border-color:#f0b8b8}.cards{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;background:none;border:0;padding:0}.card{background:white;border:1px solid #dce5ed;border-radius:14px;padding:18px;display:flex;flex-direction:column}.card strong{font-size:27px;margin:5px 0}.muted,.stamp,.card small{color:#687a8b}table{border-collapse:collapse;width:100%;font-size:13px}th,td{border-bottom:1px solid #dce5ed;padding:9px 10px;text-align:left;vertical-align:top}th{color:#4b6174;background:#f8fafc}.pill{display:inline-block;border-radius:999px;padding:3px 8px;font-size:11px;font-weight:700}.pill.ok{background:#dff4e9;color:#177a4b}.pill.warn{background:#fff0c9;color:#a56a00}.pill.crit{background:#ffe1e1;color:#b53636}.grid3{display:grid;grid-template-columns:repeat(3,1fr);gap:16px}@media(max-width:900px){.cards,.grid3{grid-template-columns:1fr 1fr}}@media(max-width:600px){.cards,.grid3{grid-template-columns:1fr}main{padding:10px}}");
        sb.Append("</style></head><body>");
        return sb;
    }

    private static string End(StringBuilder sb) { sb.Append("</main></body></html>"); return sb.ToString(); }

    private static void Header(StringBuilder sb, string subtitle, DateTime stamp)
    {
        sb.Append("<header><div class='brand'>").Append(ShieldSvg()).Append("<div><h1>G PC Health Check</h1><div class='muted'>").Append(H(subtitle)).Append("</div></div></div><div class='stamp'>").Append(H(stamp.ToString("dd.MM.yyyy HH:mm:ss"))).Append("</div></header><main>");
    }

    private static void Hero(StringBuilder sb, int score, string status, string detail)
    {
        var cls = status.ToLowerInvariant();
        sb.Append("<section class='hero'><div class='score ").Append(cls).Append("'>").Append(score).Append("<span style='font-size:12px'>/100</span></div><div><h2>").Append(H(StatusText(status))).Append("</h2><p>").Append(H(detail)).Append("</p><p class='muted'>Индекс — эвристика первичной диагностики, а не доказательство исправности.</p></div></section>");
    }

    private static void Metrics(StringBuilder sb, ScanResult scan)
    {
        var d = scan.Data; var disk = SystemDisk(d);
        sb.Append("<section class='cards'>");
        Card(sb, "CPU", F(d.Performance.CpuPercent, "%"), "текущая загрузка");
        Card(sb, "RAM", F(d.Performance.MemoryUsedPercent, "%"), "использовано");
        Card(sb, "Системный диск", disk is null ? "—" : $"{disk.FreeGB:0.#} GB", "свободно");
        Card(sb, "Uptime", $"{d.System.UptimeDays:0.#} дн.", d.PendingReboot.Pending ? "pending reboot" : "перезагрузка не ожидается");
        sb.Append("</section>");
    }

    private static void Card(StringBuilder sb, string name, string value, string note) => sb.Append("<div class='card'><b>").Append(H(name)).Append("</b><strong>").Append(H(value)).Append("</strong><small>").Append(H(note)).Append("</small></div>");

    private static void Findings(StringBuilder sb, IEnumerable<Finding> findings, string title)
    {
        sb.Append("<section><h2>").Append(H(title)).Append("</h2><table><thead><tr><th>Статус</th><th>Категория</th><th>Наблюдение</th><th>Значение</th><th>Что делать</th></tr></thead><tbody>");
        foreach (var f in findings.OrderBy(x => SeverityOrder(x.Severity)))
            sb.Append("<tr><td><span class='pill ").Append(f.Severity.ToLowerInvariant()).Append("'>").Append(H(f.Severity)).Append("</span></td><td>").Append(H(f.Category)).Append("</td><td>").Append(H(f.Title)).Append("</td><td>").Append(H(f.Value)).Append("</td><td>").Append(H(f.Recommendation)).Append("</td></tr>");
        sb.Append("</tbody></table></section>");
    }

    private static void Processes(StringBuilder sb, DiagnosticData d)
    {
        sb.Append("<section><h2>TOP процессов</h2><div class='grid3'>"); ProcessTable(sb, "CPU", d.TopCpu); ProcessTable(sb, "RAM", d.TopMemory); ProcessTable(sb, "I/O", d.TopIo); sb.Append("</div></section>");
    }

    private static void ProcessTable(StringBuilder sb, string title, IEnumerable<ProcessInfo> items)
    {
        sb.Append("<div><h3>").Append(H(title)).Append("</h3><table><thead><tr><th>Процесс</th><th>PID</th><th>CPU</th><th>RAM</th><th>I/O</th></tr></thead><tbody>");
        foreach (var p in items) sb.Append("<tr><td>").Append(H(p.Name)).Append("</td><td>").Append(p.Pid).Append("</td><td>").Append(p.CpuPercent.ToString("0.#")).Append("%</td><td>").Append(p.MemoryMB.ToString("0.#")).Append(" MB</td><td>").Append(p.IoMBPerSec.ToString("0.##")).Append(" MB/s</td></tr>");
        sb.Append("</tbody></table></div>");
    }

    private static void Events(StringBuilder sb, DiagnosticData d)
    {
        sb.Append("<section><h2>Ошибки Windows за ").Append(d.Events.Hours).Append(" часов</h2><p>Critical: <b>").Append(d.Events.CriticalCount).Append("</b> · Error: <b>").Append(d.Events.ErrorCount).Append("</b></p><table><thead><tr><th>Log</th><th>Provider</th><th>Event ID</th><th>Количество</th><th>Последнее</th></tr></thead><tbody>");
        foreach (var e in d.Events.Top) sb.Append("<tr><td>").Append(H(e.Log)).Append("</td><td>").Append(H(e.Provider)).Append("</td><td>").Append(e.EventId).Append("</td><td>").Append(e.Count).Append("</td><td>").Append(H(e.LastSeen?.ToString("dd.MM HH:mm:ss") ?? "")).Append("</td></tr>");
        sb.Append("</tbody></table></section>");
    }

    private static void SystemBlock(StringBuilder sb, DiagnosticData d)
    {
        sb.Append("<section><h2>Система</h2><table><tbody>");
        Row(sb, "ПК", d.System.ComputerName); Row(sb, "Пользователь", d.System.UserName); Row(sb, "Производитель / модель", d.System.Manufacturer + " " + d.System.Model); Row(sb, "Windows", d.System.OS + " " + d.System.OSVersion + " build " + d.System.BuildNumber); Row(sb, "CPU", d.System.Cpu); Row(sb, "RAM", $"{d.System.TotalMemoryGB:0.#} GB"); Row(sb, "Последняя загрузка", d.System.LastBoot.ToString("dd.MM.yyyy HH:mm:ss")); Row(sb, "Права процесса", d.System.IsAdministrator ? "Administrator" : "Standard user"); Row(sb, "Windows Update", $"wuauserv={d.Updates.Wuauserv}; BITS={d.Updates.Bits}"); Row(sb, "Антивирус / EDR", d.SecurityProducts.Count == 0 ? "Не удалось определить" : string.Join("; ", d.SecurityProducts.Select(x => x.Name + " [" + x.State + "]")));
        sb.Append("</tbody></table>");
        if (d.CollectionWarnings.Count > 0) sb.Append("<h3>Предупреждения сбора</h3><ul>").Append(string.Join("", d.CollectionWarnings.Select(x => "<li>" + H(x) + "</li>"))).Append("</ul>");
        sb.Append("</section>");
    }

    private static void Row(StringBuilder sb, string k, string v) => sb.Append("<tr><th>").Append(H(k)).Append("</th><td>").Append(H(v)).Append("</td></tr>");
    private static LogicalDiskInfo? SystemDisk(DiagnosticData d) => d.LogicalDisks.FirstOrDefault(x => string.Equals(x.Drive, "C:", StringComparison.OrdinalIgnoreCase));
    private static string F(double? v, string suffix) => v is null ? "—" : $"{v:0.#}{suffix}";
    private static string Delta(double? b, double? a, string suffix) => b is null || a is null ? "—" : Signed(a.Value - b.Value, suffix);
    private static string Signed(double v, string suffix = "") => (v > 0 ? "+" : "") + v.ToString("0.##") + suffix;
    private static string Signed(int v) => v > 0 ? "+" + v : v.ToString();
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static int SeverityOrder(string s) => s == "CRIT" ? 0 : s == "WARN" ? 1 : 2;
    private static string StatusText(string s) => s == "OK" ? "Состояние без явных критических признаков" : s == "WARN" ? "Есть признаки, требующие внимания" : "Есть критические признаки";
    private static string SafeName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    private static string ShieldSvg() => "<svg class='logo' viewBox='0 0 100 110' xmlns='http://www.w3.org/2000/svg'><path fill='#15344f' d='M50 2L94 17l-4 44c-3 20-18 37-40 47C28 98 13 81 10 61L6 17z'/><path fill='#2386c0' d='M50 8l37 13-4 38c-3 16-14 31-33 41-19-10-30-25-33-41l-4-38z'/><text x='50' y='68' text-anchor='middle' font-family='Segoe UI,Arial' font-size='52' font-weight='700' fill='white'>G</text><path d='M26 79h14l6-8 6 17 7-13 6 4h11' fill='none' stroke='#b4eaff' stroke-width='4' stroke-linejoin='round'/></svg>";
}
