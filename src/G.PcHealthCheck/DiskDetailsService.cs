using System.Diagnostics;
using System.Text;

namespace G.PcHealthCheck;

// Interpret Windows-supplied evidence only. No drive operations are performed here.
internal static class DiskDetailsService
{
    public static DiskDetailsSnapshot Collect(IPhysicalDiskDetailsSource source, CancellationToken ct,
        int maxDisks = 64, Func<long>? elapsedMs = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maxDisks is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maxDisks));
        var watch = Stopwatch.StartNew(); elapsedMs ??= () => watch.ElapsedMilliseconds;
        var result = new DiskDetailsSnapshot { Outcome = "Running" };
        try
        {
            ct.ThrowIfCancellationRequested();
            using var items = source.Read(ct).GetEnumerator();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (elapsedMs() >= 30000)
                { result.Outcome = "Partial"; result.Warnings.Add("Достигнут 30-секундный бюджет сбора; поставщик может завершать вызовы позже."); break; }
                if (!items.MoveNext())
                {
                    ct.ThrowIfCancellationRequested();
                    result.Outcome = elapsedMs() >= 30000 ? "Partial" : "Completed";
                    if (result.Outcome == "Partial") result.Warnings.Add("Последний вызов поставщика завершился после бюджета времени.");
                    break;
                }
                ct.ThrowIfCancellationRequested();
                if (result.Disks.Count == maxDisks)
                { result.Outcome = "Partial"; result.Warnings.Add($"Показаны первые {maxDisks} накопителей; список неполный."); break; }
                result.Disks.Add(items.Current);
            }
            if (result.Outcome == "Completed" && result.Disks.Count == 0)
            { result.Outcome = "Unavailable"; result.Warnings.Add("Поставщик не вернул дисков. Это не подтверждает отсутствие устройств или неисправностей."); }
            else if (result.Outcome == "Completed" && result.Disks.Any(x => !CompleteCounters(x) || x.Warnings.Count > 0))
            { result.Outcome = "Partial"; result.Warnings.Add("Часть дополнительных показателей недоступна. Смотрите пустые поля и предупреждения каждой строки."); }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { result.Outcome = "Stopped"; result.Warnings.Add("Сбор остановлен; завершённые записи сохранены."); }
        catch (Exception ex)
        { result.Outcome = result.Disks.Count == 0 ? "Failed" : "Partial"; result.Warnings.Add($"Поставщик хранения: {ex.GetType().Name}, 0x{ex.HResult:X8}."); }
        result.FinishedAt = DateTimeOffset.Now; return result;
    }

    public static void BindCounters(PhysicalDiskDetail disk, IEnumerable<DiskReliability> candidates)
    {
        ArgumentNullException.ThrowIfNull(disk); ArgumentNullException.ThrowIfNull(candidates);
        disk.Reliability = null; disk.CounterState = "Unavailable";
        var rows = candidates.Take(2).ToArray();
        if (rows.Length > 1)
        { disk.CounterState = "Ambiguous"; disk.Warnings.Add("Связь вернула несколько наборов счётчиков; выбор не выполнялся."); return; }
        if (rows.Length == 0) { disk.Warnings.Add("Связанный набор счётчиков недоступен."); return; }
        var row = rows[0];
        if (string.IsNullOrWhiteSpace(disk.DeviceId) || string.IsNullOrWhiteSpace(row.DeviceId)
            || !string.Equals(disk.DeviceId, row.DeviceId, StringComparison.Ordinal))
        { disk.Warnings.Add("Идентификатор счётчиков не совпал с диском; показатели не присвоены."); return; }
        int? ByteValue(int? value, string name)
        {
            if (value is null or >= 0 and <= 255) return value;
            disk.Warnings.Add(name + ": недопустимое значение поставщика."); return null;
        }
        disk.Reliability = row with
        {
            Temperature = ByteValue(row.Temperature, "Температура"),
            TemperatureMax = ByteValue(row.TemperatureMax, "Предел температуры"),
            Wear = ByteValue(row.Wear, "Износ")
        };
        disk.CounterState = "Available";
    }

    private static bool CompleteCounters(PhysicalDiskDetail disk)
        => disk.CounterState == "Available" && disk.Reliability is
        { Temperature: not null, Wear: not null, PowerOnHours: not null, ReadErrorsUncorrected: not null, WriteErrorsUncorrected: not null };

    public static string Attention(PhysicalDiskDetail disk)
    {
        ArgumentNullException.ThrowIfNull(disk);
        if (disk.Health == 2) return "CRIT";
        var r = disk.Reliability;
        if (disk.Health == 1 || r?.Wear >= 100 || r?.ReadErrorsUncorrected > 0 || r?.WriteErrorsUncorrected > 0
            || (r is { Temperature: int t, TemperatureMax: int limit } && limit > 0 && t > limit)) return "WARN";
        if (disk.Health != 0 || !CompleteCounters(disk)) return "UNKNOWN";
        return "INFO";
    }

    public static string Describe(PhysicalDiskDetail disk)
    {
        ArgumentNullException.ThrowIfNull(disk);
        var r = disk.Reliability; var s = new StringBuilder();
        s.AppendLine($"{disk.Name} · DeviceId={disk.DeviceId}");
        s.AppendLine($"HealthStatus: {HealthText(disk.Health)}; внимание: {Attention(disk)}.");
        s.AppendLine($"Тип: {MediaText(disk.MediaType)}; шина: {BusText(disk.BusType)}; размер: {disk.Size?.ToString() ?? "—"} байт.");
        s.AppendLine($"Прошивка: {(string.IsNullOrWhiteSpace(disk.Firmware) ? "—" : disk.Firmware)}; OperationalStatus: {(disk.OperationalStatus.Length == 0 ? "—" : string.Join(", ", disk.OperationalStatus))}.");
        s.AppendLine($"Связанные счётчики: {disk.CounterState}. Неизвестные поля — «—», не нули.");
        s.AppendLine($"Температура: {r?.Temperature?.ToString() ?? "—"} °C; предел от устройства: {r?.TemperatureMax?.ToString() ?? "—"} °C.");
        s.AppendLine($"Износ: {r?.Wear?.ToString() ?? "—"}%; наработка: {r?.PowerOnHours?.ToString() ?? "—"} ч.");
        s.AppendLine($"Неисправленные ошибки чтения / записи: {r?.ReadErrorsUncorrected?.ToString() ?? "—"} / {r?.WriteErrorsUncorrected?.ToString() ?? "—"}.");
        if (Attention(disk) is "CRIT" or "WARN") s.AppendLine("Проверьте резервную копию и наблюдение средствами производителя. Счётчики ошибок могут быть историческими и сами по себе не устанавливают причину текущего сбоя.");
        s.AppendLine("Износ — использованный ресурс, не процент оставшегося здоровья; 100% означает достижение оценочного предела. Отсутствие счётчика не заменяется оценкой.");
        s.AppendLine("Это Windows Storage WMI, не полный SMART, не тест поверхности и не соответствие букве тома. Значения зависят от драйвера, USB/RAID и прав.");
        foreach (var warning in disk.Warnings) s.AppendLine("Предупреждение: " + warning);
        return s.ToString().TrimEnd();
    }
    public static string HealthText(int? value) => value switch
    { 0 => "Healthy (0), сообщает Windows", 1 => "Warning (1)", 2 => "Unhealthy (2)", 5 => "Unknown (5)", null => "—", _ => "Unknown (" + value + ")" };
    public static string MediaText(int? value) => value switch
    { 3 => "HDD (3)", 4 => "SSD (4)", 5 => "SCM (5)", null => "—", _ => "Не определён (" + value + ")" };
    public static string BusText(int? value) => value switch
    { 1 => "SCSI (1)", 3 => "ATA (3)", 7 => "USB (7)", 8 => "RAID (8)", 9 => "iSCSI (9)", 10 => "SAS (10)", 11 => "SATA (11)", 15 => "File backed virtual (15)", 16 => "Storage Spaces (16)", 17 => "NVMe (17)", null => "—", _ => "Код " + value };
}
