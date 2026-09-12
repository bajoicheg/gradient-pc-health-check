namespace G.PcHealthCheck;

internal static class EndpointReviewService
{
    public static EndpointSnapshot Collect(IEndpointSource source, ExecutionContextInfo? context, CancellationToken ct, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source); ct.ThrowIfCancellationRequested();
        var snapshot = new EndpointSnapshot { ExecutionContext = context, Tables = EndpointReviewCore.TableNames.Select(x => new EndpointTable { Name = x }).ToList() };
        List<EndpointProcess> Processes()
        {
            try { return source.ReadProcesses(ct, snapshot.Warnings); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { snapshot.Warnings.Add("Сведения процессов: " + Describe(ex)); return []; }
        }
        try
        {
            progress?.Report("Читаю идентичности процессов перед таблицами…"); var before = Processes();
            for (var i = 0; i < snapshot.Tables.Count; i++)
            {
                ct.ThrowIfCancellationRequested(); var table = snapshot.Tables[i]; table.StartedAt = DateTimeOffset.Now;
                progress?.Report("Читаю локальную таблицу " + table.Name + "…");
                try
                {
                    var read = source.ReadTable(table.Name, ct);
                    if (read.Name != table.Name || read.Rows.Count > 5000 || read.Rows.Any(x => x.Table != table.Name)) throw new InvalidDataException("Источник вернул несогласованную таблицу.");
                    read.StartedAt = table.StartedAt; read.FinishedAt = DateTimeOffset.Now; snapshot.Tables[i] = read;
                }
                catch (OperationCanceledException) { table.FinishedAt = DateTimeOffset.Now; throw; }
                catch (Exception ex) { table.State = "Unavailable"; table.Message = Describe(ex); table.FinishedAt = DateTimeOffset.Now; }
            }
            ct.ThrowIfCancellationRequested(); progress?.Report("Сверяю процессы после чтения таблиц…"); var after = Processes();
            ct.ThrowIfCancellationRequested();
            foreach (var table in snapshot.Tables) table.Rows = EndpointReviewCore.Associate(table.Rows, before, after);
            var unknown = snapshot.Tables.Sum(x => x.Rows.Count(r => r.ProcessEvidence != "Stable"));
            if (unknown > 0) snapshot.Warnings.Add($"Для {unknown} записей имя процесса не подтверждено; исходный PID Windows сохранён.");
            snapshot.State = snapshot.Tables.All(x => x.State == "Complete") ? "Complete" : snapshot.Tables.All(x => x.State == "Unavailable") ? "Unavailable" : "Partial";
        }
        catch (OperationCanceledException) { snapshot.State = "Cancelled"; snapshot.Warnings.Add("Сбор остановлен. Завершённые таблицы сохранены; сопоставление процессов может быть не завершено."); }
        finally { snapshot.FinishedAt = DateTimeOffset.Now; }
        return snapshot;
    }
    internal static string Describe(Exception ex) => ex is System.ComponentModel.Win32Exception w
        ? $"{ex.GetType().Name}; Win32={w.NativeErrorCode}." : $"{ex.GetType().Name}; 0x{ex.HResult:X8}.";
}
