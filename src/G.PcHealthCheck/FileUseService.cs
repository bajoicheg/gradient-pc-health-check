using System.ComponentModel;

namespace G.PcHealthCheck;

internal static class FileUseService
{
    internal const int MaxProcesses = 1024;
    internal const int MaxAttempts = 4;
    public static FileUseSnapshot Collect(IFileUseSource source, string target, ExecutionContextInfo? context, CancellationToken ct, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source); ct.ThrowIfCancellationRequested();
        var snapshot = new FileUseSnapshot { TargetPath = FileUseCore.NormalizeTarget(target), ExecutionContext = context };
        uint session = 0; var started = false;
        void Stage(string value) { snapshot.Stage = value; progress?.Report(value); }
        void Check(uint code)
        {
            if (code == 0) return;
            if (code == 1223)
            {
                snapshot.ErrorCode = 1223;
                throw new OperationCanceledException("Windows отменила запрос Restart Manager.", ct);
            }
            throw new Win32Exception(unchecked((int)code));
        }
        try
        {
            Stage("Проверяю путь и метаданные файла…"); source.CheckFile(snapshot.TargetPath, ct); ct.ThrowIfCancellationRequested();
            Stage("Открываю сеанс Restart Manager…"); Check(source.StartSession(out session)); started = true;
            ct.ThrowIfCancellationRequested();
            Stage("Регистрирую выбранный файл для запроса…"); Check(source.RegisterFile(session, snapshot.TargetPath));
            var capacity = 0;
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested(); Stage("Получаю список приложений/служб…"); snapshot.QueryAttempts++;
                var batch = source.ReadList(session, capacity);
                if (batch.Code == 234)
                {
                    snapshot.ReportedCount = batch.Needed;
                    if (batch.Needed > MaxProcesses || batch.Needed <= capacity) throw new InvalidDataException("Список превышает ограничение 1024 записей либо размер ответа несогласован.");
                    capacity = checked((int)batch.Needed); continue;
                }
                Check(batch.Code);
                if (batch.Returned > (uint)capacity || batch.Returned > MaxProcesses || batch.Returned != batch.Processes.Count)
                    throw new InvalidDataException("Число записей Restart Manager не соответствует размеру буфера или полученным данным.");
                snapshot.Processes = batch.Processes.ToList(); snapshot.ListCompleted = true;
                snapshot.ReportedCount = batch.Returned; snapshot.RebootReasons = batch.RebootReasons; break;
            }
            if (!snapshot.ListCompleted) throw new Win32Exception(234, "Список менялся во время сбора; достигнут предел повторов.");
            ct.ThrowIfCancellationRequested(); Stage("Проверяю PID и время создания процессов…");
            var identities = new Dictionary<uint, FileUseIdentity?>();
            for (var i = 0; i < snapshot.Processes.Count; i++)
            {
                ct.ThrowIfCancellationRequested(); var row = snapshot.Processes[i];
                // A skipped invalid source record is not evidence that this PID is unreadable.
                if (row.Pid == 0 || FileUseCore.StartTime(row.StartFileTime) is null)
                {
                    snapshot.Processes[i] = FileUseCore.Associate(row, null); continue;
                }
                if (!identities.TryGetValue(row.Pid, out var identity))
                {
                    try { identity = source.ReadIdentity(row.Pid); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { identity = new(row.Pid, 0, "", ErrorCode(ex)); }
                    identities[row.Pid] = identity;
                }
                snapshot.Processes[i] = FileUseCore.Associate(row, identity);
            }
            ct.ThrowIfCancellationRequested();
            var missing = snapshot.Processes.Count(x => x.IdentityState != "Matched");
            if (missing > 0) snapshot.Warnings.Add($"Для {missing} записей EXE не подтверждён. Названия от Restart Manager сохранены отдельно и не подменяются текущим процессом с тем же PID.");
            snapshot.State = "Complete"; Stage("Список получен.");
        }
        catch (OperationCanceledException) { snapshot.State = "Cancelled"; snapshot.Warnings.Add("Сбор остановлен; завершённый список сохранён, если успел поступить. Отсутствие данных не означает отсутствие использования файла."); }
        catch (Exception ex)
        {
            snapshot.ErrorCode = ErrorCode(ex); snapshot.State = snapshot.ListCompleted ? "Partial" : "Unavailable";
            snapshot.Warnings.Add($"{ex.GetType().Name}: {ex.Message} (код {snapshot.ErrorCode}).");
        }
        finally
        {
            if (started)
            {
                try
                {
                    snapshot.EndSessionCode = source.EndSession(session);
                    if (snapshot.EndSessionCode != 0) snapshot.Warnings.Add($"Завершение сеанса Restart Manager не подтверждено: код {snapshot.EndSessionCode}.");
                }
                catch (Exception ex) { snapshot.Warnings.Add($"Ошибка завершения сеанса Restart Manager: {ex.GetType().Name}, код {ErrorCode(ex)}."); }
                if (snapshot.EndSessionCode != 0 && snapshot.State == "Complete") snapshot.State = "Partial";
            }
            snapshot.FinishedAt = DateTimeOffset.Now;
        }
        return snapshot;
    }
    internal static int ErrorCode(Exception ex) => ex is Win32Exception native ? native.NativeErrorCode : ex.HResult;
}
