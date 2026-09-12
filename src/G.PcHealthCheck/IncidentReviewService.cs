using System.Diagnostics;
using System.Globalization;

namespace G.PcHealthCheck;

internal static class IncidentQueries
{
    public static void Validate(IncidentWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.To <= window.From || window.To - window.From > TimeSpan.FromDays(7)) throw new ArgumentException("Интервал должен быть положительным и не превышать 7 суток.");
        if (window.MaxPerLog is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(window), "Допустимо 1–5000 записей на журнал.");
    }
    public static string XPath(IncidentWindow window)
    {
        Validate(window);
        static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        return $"*[System[TimeCreated[@SystemTime >= '{Utc(window.From)}' and @SystemTime <= '{Utc(window.To)}']]]";
    }
    public static List<IncidentEvent> Events(IncidentSnapshot snapshot, IncidentFilter filter)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(filter);
        if (filter.EventId is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(filter));
        var text = filter.Text.Trim();
        return snapshot.Logs.SelectMany(x => x.Events)
            .Where(x => (filter.Log.Length == 0 || x.Log.Equals(filter.Log, StringComparison.OrdinalIgnoreCase))
                && (!filter.EventId.HasValue || x.EventId == filter.EventId)
                && (!filter.WarningsOnly || x.Level is >= 1 and <= 3)
                && (text.Length == 0 || new[] { x.Provider, x.Message, x.EventId.ToString(CultureInfo.InvariantCulture), x.EmitterPid?.ToString(CultureInfo.InvariantCulture) ?? "" }.Any(v => v.Contains(text, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(x => x.Timestamp).ThenBy(x => x.Log, StringComparer.Ordinal).ThenByDescending(x => x.RecordId).ToList();
    }
    public static List<ProcessReviewEntry> Processes(ProcessReviewSnapshot snapshot, string text)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(text); text = text.Trim();
        return snapshot.Processes.Where(x => text.Length == 0 || new[] { x.Name, x.Executable, x.CommandLine,
            x.Pid.ToString(CultureInfo.InvariantCulture), x.ParentPid?.ToString(CultureInfo.InvariantCulture) ?? "", x.SessionId?.ToString(CultureInfo.InvariantCulture) ?? "" }
            .Any(v => v.Contains(text, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Pid).ToList();
    }
    public static string Error(Exception ex) => (ex is UnauthorizedAccessException ? "Доступ запрещён. " : "") + $"{ex.GetType().Name}; 0x{ex.HResult:X8}";
}

internal sealed class IncidentEvents(IIncidentEventSource source)
{
    public IncidentSnapshot Collect(IncidentWindow window, CancellationToken ct)
    {
        IncidentQueries.Validate(window);
        var snapshot = new IncidentSnapshot { Window = window };
        foreach (var log in new[] { "Application", "System" })
        {
            var result = new IncidentLogResult { Log = log }; snapshot.Logs.Add(result);
            if (ct.IsCancellationRequested) { result.State = "Cancelled"; continue; }
            var watch = Stopwatch.StartNew(); var observed = 0;
            try
            {
                using var iterator = source.Read(log, window, ct).GetEnumerator();
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    if (watch.Elapsed >= TimeSpan.FromSeconds(20)) { result.Warnings.Add("Достигнуто 20 секунд между вызовами поставщика; журнал просмотрен не полностью."); break; }
                    if (!iterator.MoveNext()) break;
                    ct.ThrowIfCancellationRequested();
                    if (++observed > window.MaxPerLog) { result.Warnings.Add($"Достигнут лимит {window.MaxPerLog} записей; более старые результаты не прочитаны."); break; }
                    var e = iterator.Current;
                    if (e.Timestamp is null || e.Timestamp < window.From || e.Timestamp > window.To)
                    { result.Warnings.Add("Пропущена запись без подтверждённого времени внутри интервала."); continue; }
                    result.Events.Add(new IncidentEvent { Log = log, RecordId = e.RecordId, Timestamp = e.Timestamp, EventId = e.EventId, Level = e.Level,
                        Provider = e.Provider, Message = e.Message, MessageState = e.MessageState, EmitterPid = e.EmitterPid });
                    if (e.MessageState != "Available") result.Warnings.Add($"Record ID {e.RecordId}: текст события недоступен полностью ({e.MessageState}); метаданные сохранены.");
                }
                ct.ThrowIfCancellationRequested();
                result.State = result.Warnings.Count == 0 ? "Complete" : "Partial";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { result.State = "Cancelled"; result.Warnings.Add("Сбор отменён; показаны только уже полученные записи."); }
            catch (Exception ex)
            {
                result.State = ct.IsCancellationRequested ? "Cancelled" : result.Events.Count > 0 || observed > 0 ? "Partial" : "Unavailable";
                result.Warnings.Add(IncidentQueries.Error(ex));
            }
        }
        snapshot.State = ct.IsCancellationRequested || snapshot.Logs.Any(x => x.State == "Cancelled") ? "Cancelled"
            : snapshot.Logs.All(x => x.State == "Unavailable") ? "Unavailable"
            : snapshot.Logs.All(x => x.State == "Complete") ? "Complete" : "Partial";
        snapshot.FinishedAt = DateTimeOffset.Now; return snapshot;
    }
}

internal sealed class ProcessReview(IProcessReviewSource source)
{
    public ProcessReviewSnapshot Collect(int maximum, CancellationToken ct)
    {
        if (maximum is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(maximum));
        var snapshot = new ProcessReviewSnapshot(); var watch = Stopwatch.StartNew();
        try
        {
            ct.ThrowIfCancellationRequested(); using var iterator = source.Read(ct).GetEnumerator();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (watch.Elapsed >= TimeSpan.FromSeconds(20)) { snapshot.Warnings.Add("Достигнуто 20 секунд между вызовами WMI; список неполный."); break; }
                if (!iterator.MoveNext()) break;
                ct.ThrowIfCancellationRequested();
                if (snapshot.Processes.Count >= maximum) { snapshot.Warnings.Add($"Достигнут лимит {maximum} процессов; список неполный."); break; }
                snapshot.Processes.Add(iterator.Current);
            }
            ct.ThrowIfCancellationRequested();
            snapshot.State = snapshot.Warnings.Count > 0 || snapshot.Processes.Any(x => x.Warnings.Count > 0) ? "Partial" : "Complete";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { snapshot.State = "Cancelled"; snapshot.Warnings.Add("Сбор отменён; список может быть неполным."); }
        catch (Exception ex) { snapshot.State = ct.IsCancellationRequested ? "Cancelled" : snapshot.Processes.Count == 0 ? "Unavailable" : "Partial"; snapshot.Warnings.Add(IncidentQueries.Error(ex)); }
        snapshot.FinishedAt = DateTimeOffset.Now; return snapshot;
    }
    public ProcessOwnerEvidence Owner(ProcessReviewEntry selected, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ProcessOwnerEvidence Result(string state, string owner, string detail) => new(selected.Pid, selected.CreationKey, state, owner, detail, DateTimeOffset.Now);
        bool Same(ProcessReviewEntry? row) => row is not null && row.Pid == selected.Pid && row.CreationKey == selected.CreationKey;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(selected.CreationKey)) return Result("Unverified", "", "Нет времени создания: нельзя подтвердить, что PID всё ещё принадлежит выбранному процессу.");
            if (!Same(source.Lookup(selected.Pid, ct))) return Result("Stale", "", "Процесс завершён или PID уже принадлежит другому процессу. Обновите список.");
            ct.ThrowIfCancellationRequested(); var owner = source.ReadOwner(selected.Pid, ct); ct.ThrowIfCancellationRequested();
            if (!Same(source.Lookup(selected.Pid, ct))) return Result("Stale", "", "Идентичность изменилась во время проверки; полученный владелец отброшен.");
            ct.ThrowIfCancellationRequested();
            return string.IsNullOrWhiteSpace(owner) ? Result("Unavailable", "", "WMI не сообщил владельца.")
                : Result("Verified", owner, "PID и CreationDate совпали до и после GetOwner. Это наблюдение на момент проверки, не постоянная связь.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Result("Cancelled", "", "Проверка владельца отменена."); }
        catch (Exception ex) { return Result(ct.IsCancellationRequested ? "Cancelled" : "Unavailable", "", IncidentQueries.Error(ex)); }
    }
}
