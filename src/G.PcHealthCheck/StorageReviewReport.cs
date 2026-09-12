using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class StorageReviewReport
{
    public const string FolderScope =
        "Сумма логических размеров файлов, не физически занятое место и не план удаления. " +
        "Жёсткие ссылки учитываются по именам; сжатие, sparse-файлы, альтернативные потоки и недоступные данные не оцениваются. " +
        "Итоговые размеры вложенных папок пересекаются: не складывайте все строки таблицы. " +
        "Обход не является атомарным снимком; файлы могут меняться. Ссылки/reparse points исключаются.";
    public const string DiskScope =
        "Источник: локальный Windows Storage WMI. Связанные показатели зависят от драйвера, устройства и прав. " +
        "Это не полный SMART, не тест поверхности и не гарантия исправности. Буквы томов не выводятся из номера диска. " +
        "— означает недоступные данные. Износ — использованный ресурс, не остаток здоровья.";

    public static string Summary(object snapshot)
    {
        Validate(snapshot);
        var b = new StringBuilder("G PC Health Check — анализ хранения\n");
        if (snapshot is FolderUsageSnapshot f)
        {
            b.AppendLine($"Папка: {f.Root}");
            b.AppendLine($"Начало: {f.StartedAt:O}; окончание: {f.FinishedAt:O}");
            b.AppendLine($"Результат сбора: {OutcomeText(f.Outcome)} ({f.Outcome})");
            var root = f.Folders.FirstOrDefault();
            b.AppendLine($"Учтено: {Bytes(root?.Bytes ?? 0)}; файлов: {root?.Files ?? 0:N0}; папок в снимке: {f.Folders.Count:N0}.");
            b.AppendLine($"Записей просмотрено: {f.EntriesVisited:N0}; пропущенных ссылок: {f.SkippedLinks:N0}; ошибок: {f.Errors:N0}.");
            b.AppendLine($"Крупнейших файлов сохранено: {f.LargestFiles.Count:N0}. При неполном обходе рейтинг относится только к просмотренной части.");
            b.AppendLine($"Лимиты: {f.Options.MaxEntries:N0} записей, {f.Options.MaxDirectories:N0} папок, {f.Options.MaxSeconds} с между вызовами поставщика.");
            b.AppendLine(FolderScope);
            foreach (var warning in f.Warnings) b.AppendLine("Предупреждение: " + warning);
        }
        else if (snapshot is DiskDetailsSnapshot d)
        {
            b.AppendLine($"Начало: {d.StartedAt:O}; окончание: {d.FinishedAt:O}");
            b.AppendLine($"Результат сбора: {OutcomeText(d.Outcome)} ({d.Outcome}); накопителей: {d.Disks.Count}.");
            b.AppendLine(DiskScope);
            foreach (var disk in d.Disks.OrderBy(x => Rank(DiskDetailsService.Attention(x))))
            {
                b.AppendLine();
                b.AppendLine(DiskDetailsService.Describe(disk));
            }
            foreach (var warning in d.Warnings) b.AppendLine("Предупреждение: " + warning);
        }
        return b.ToString().TrimEnd();
    }

    public static string Json(object snapshot)
    {
        Validate(snapshot);
        return JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Kind = snapshot is FolderUsageSnapshot ? "FolderUsage" : "PhysicalDiskDetails",
            Scope = snapshot is FolderUsageSnapshot ? FolderScope : DiskScope,
            Snapshot = snapshot
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string Html(object snapshot)
    {
        Validate(snapshot);
        var b = new StringBuilder("<!doctype html><html lang='ru'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
        b.Append("<title>G PC Health Check — анализ хранения</title><style>");
        b.Append("body{margin:0;background:#f4f7fa;color:#172432;font:14px/1.5 'Segoe UI',Arial,sans-serif}main{max-width:1350px;margin:auto;padding:24px}");
        b.Append("section{background:white;border:1px solid #dce5ed;border-radius:12px;padding:20px;margin-bottom:18px}h1,h2{color:#15344f}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:inherit}");
        b.Append("table{border-collapse:collapse;width:100%;font-size:13px}th,td{padding:8px;border-bottom:1px solid #dce5ed;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{background:#f0f5f8}");
        b.Append(".table{overflow-x:auto}.bar{display:block;height:12px;background:#2386c0;border-radius:3px}.muted{color:#607285}@media(max-width:650px){main{padding:8px}}");
        b.Append("</style></head><body><main><h1>G PC Health Check — анализ хранения</h1><section><pre>");
        b.Append(H(Summary(snapshot))).Append("</pre></section>");
        if (snapshot is FolderUsageSnapshot f)
        {
            var total = f.Folders.FirstOrDefault()?.Bytes ?? 0;
            b.Append("<section><h2>Крупнейшие папки первого уровня</h2><p class='muted'>Доли от учтённых логических байтов; это не карта свободного места диска.</p><table><tr><th>Папка</th><th>Размер</th><th>Доля</th></tr>");
            foreach (var row in f.Folders.Where(x => x.ParentIndex == 0).OrderByDescending(x => x.Bytes).ThenBy(x => x.Path, StringComparer.Ordinal).Take(15))
            {
                var percentage = total > 0 ? Math.Clamp(row.Bytes / total * 100m, 0m, 100m) : 0m;
                b.Append("<tr><td>").Append(H(row.Path)).Append("</td><td>").Append(H(Bytes(row.Bytes))).Append("</td><td>")
                    .Append(percentage.ToString("0.0", CultureInfo.InvariantCulture)).Append("%<span class='bar' style='width:")
                    .Append(percentage.ToString("0.0", CultureInfo.InvariantCulture)).Append("%'></span></td></tr>");
            }
            b.Append("</table></section><section><h2>Все собранные папки</h2><p>Размер включает подпапки; строки пересекаются. Результат обхода указан выше.</p><div class='table'><table><tr><th>Путь</th><th>Всего байт</th><th>Файлов</th><th>Собственные байты</th><th>Полнота</th></tr>");
            foreach (var row in f.Folders.OrderByDescending(x => x.Bytes).ThenBy(x => x.Path, StringComparer.Ordinal))
                TableRow(b, row.Path, row.Bytes, row.Files, row.OwnBytes, row.Incomplete ? "Неполная" : "Собрано в заявленной области");
            b.Append("</table></div></section><section><h2>Крупнейшие файлы просмотренной части</h2><div class='table'><table><tr><th>Путь</th><th>Байт</th><th>Изменён</th></tr>");
            foreach (var row in f.LargestFiles) TableRow(b, row.Path, row.Bytes, row.Modified?.ToString("O") ?? "—");
            b.Append("</table></div></section>");
        }
        else if (snapshot is DiskDetailsSnapshot d)
        {
            b.Append("<section><h2>Сводка по накопителям</h2><div class='table'><table><tr><th>Внимание</th><th>DeviceId</th><th>Имя</th><th>Размер, байт</th><th>Прошивка</th><th>HealthStatus</th><th>Температура, °C</th><th>Износ, %</th><th>Наработка, ч</th><th>Ошибки чтения / записи</th></tr>");
            foreach (var disk in d.Disks.OrderBy(x => Rank(DiskDetailsService.Attention(x))))
                TableRow(b, DiskDetailsService.Attention(disk), disk.DeviceId, disk.Name, disk.Size, disk.Firmware,
                    DiskDetailsService.HealthText(disk.Health), disk.Reliability?.Temperature, disk.Reliability?.Wear,
                    disk.Reliability?.PowerOnHours, $"{disk.Reliability?.ReadErrorsUncorrected?.ToString() ?? "—"} / {disk.Reliability?.WriteErrorsUncorrected?.ToString() ?? "—"}");
            b.Append("</table></div></section>");
        }
        return b.Append("<p class='muted'>Экспорт включает весь собранный снимок независимо от фильтров окна. Проверьте чувствительные пути и идентификаторы перед передачей.</p></main></body></html>").ToString();
    }

    public static string Save(object snapshot, string parent)
    {
        var json = Json(snapshot);
        var html = Html(snapshot);
        if (!Path.IsPathFullyQualified(parent)) throw new ArgumentException("Нужен полный путь к папке отчётов.", nameof(parent));
        var folder = Path.Combine(parent, $"G-PC-Storage_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        WriteNew(Path.Combine(folder, "snapshot.json"), json);
        WriteNew(Path.Combine(folder, "report.html"), html);
        return folder;
    }
    private static void WriteNew(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false, true));
        writer.Write(content);
    }
    private static void Validate(object snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot is not FolderUsageSnapshot and not DiskDetailsSnapshot)
            throw new ArgumentException("Неподдерживаемый тип снимка.", nameof(snapshot));
    }
    private static void TableRow(StringBuilder b, params object?[] values)
    {
        b.Append("<tr>");
        foreach (var value in values) b.Append("<td>").Append(H(value is null ? "—" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "—")).Append("</td>");
        b.Append("</tr>");
    }
    private static string H(string value) => WebUtility.HtmlEncode(value);
    public static int Rank(string status) => status switch { "CRIT" => 0, "WARN" => 1, "UNKNOWN" => 2, _ => 3 };
    public static string OutcomeText(string value) => value switch
    {
        "Completed" => "Сбор завершён в указанной области", "Partial" => "Неполные данные", "Stopped" => "Остановлено — данные неполные",
        "Failed" => "Сбор не выполнен полностью", "Unavailable" => "Данные недоступны", "Running" => "Сбор выполняется", _ => "Снимок не собран"
    };
    public static string Bytes(decimal value)
    {
        if (value >= 1099511627776m) return $"{value / 1099511627776m:0.##} TiB ({value:N0} байт)";
        if (value >= 1073741824m) return $"{value / 1073741824m:0.##} GiB ({value:N0} байт)";
        if (value >= 1048576m) return $"{value / 1048576m:0.##} MiB ({value:N0} байт)";
        if (value >= 1024m) return $"{value / 1024m:0.##} KiB ({value:N0} байт)";
        return $"{value:N0} байт";
    }
}
