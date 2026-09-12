using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

public static class RemediationWorker
{
    private static readonly string[] Allowed = ["CleanTemp", "FlushDns", "Dism", "Sfc"];
    private static readonly string[] WorkerAllowed = ["FlushDns", "Dism", "Sfc"];
    private const string PipePrefix = "GPcHealthCheck-";

    public static int Run(string[] args)
    {
        try
        {
            // Portable mode: location and filename are not authorization checks.
            var session = Arg(args, "--session");
            var actionsCsv = Arg(args, "--actions");
            var tempDaysText = Arg(args, "--temp-days");
            var pipeName = Arg(args, "--pipe");
            var nonce = Arg(args, "--nonce");

            if (!Guid.TryParse(session, out _)) return 20;
            if (!IsValidPipeName(pipeName, session!)) return 24;
            if (!IsValidNonce(nonce)) return 25;
            if (!TryParseWorkerActions(actionsCsv, out var actions)) return 21;
            if (actions.Any(RequiresAdministrator) && !DiagnosticsService.IsAdministrator()) return 22;
            var days = int.TryParse(tempDaysText, out var parsed) ? Math.Clamp(parsed, 1, 30) : 3;

            // Machine repairs do not depend on finding a user's profile.
            using var client = new NamedPipeClientStream(".", pipeName!, PipeDirection.Out, PipeOptions.None);
            client.Connect((int)TimeSpan.FromSeconds(45).TotalMilliseconds);
            var batch = Execute(session!, actions, days);
            WriteResult(client, new WorkerEnvelope { Nonce = nonce!, Result = batch });
            return batch.Actions.All(x => x.Success) ? 0 : 2;
        }
        catch { return 99; }
    }

    public static int RunBootstrap(string[] args)
    {
        _ = args;
        return 48;
    }

    public static async Task<RemediationBatchResult> ExecuteFromGuiAsync(
        IReadOnlyCollection<ActionRecommendation> selected,
        int tempDays,
        IProgress<string>? progress = null)
    {
        var ids = selected.Where(x => x.CanAutomate)
            .Select(x => x.Id)
            .Where(x => Allowed.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) throw new InvalidOperationException("Не выбрано ни одного автоматизируемого действия.");
        NormalizeOrder(ids);
        tempDays = Math.Clamp(tempDays, 1, 30);

        // Never authorize from the GUI's saved snapshot or caller-supplied RequiresAdmin.
        var context = await Task.Run(ExecutionContextService.Capture);
        var unavailable = ids.Select(id => (Id: id, Availability: ExecutionPolicy.For(id, context)))
            .Where(x => !x.Availability.CanRequest).ToList();
        if (unavailable.Count > 0)
            throw new InvalidOperationException("Набор не выполнен: " + string.Join("; ", unavailable.Select(x => x.Id + ": " + x.Availability.Reason)));
        var requiresAdmin = ids.Any(RequiresAdministrator);
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь к EXE.");
        if (!requiresAdmin || context.HasAdministratorToken == true)
        {
            progress?.Report(requiresAdmin ? "Административные права уже активны. Выполняю выбранные действия…" : "Выполняю выбранные действия…");
            return await Task.Run(() => Execute(Guid.NewGuid().ToString("D"), ids, tempDays));
        }

        // CleanTemp never enters the elevated worker; the original parent rechecks its
        // current-session subject before deletion, after the worker returns a result.
        var workerIds = ids.Where(x => !x.Equals("CleanTemp", StringComparison.OrdinalIgnoreCase)).ToList();
        if (workerIds.Count == 0) throw new InvalidOperationException("Не выбрано административных действий для elevated worker.");
        NormalizeOrder(workerIds);
        var session = Guid.NewGuid().ToString("D");
        var pipeName = PipePrefix + session;
        var nonce = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        progress?.Report("Запрашиваю права администратора через UAC…");

        await using var pipe = CreatePipeServer(pipeName);
        var waitForConnection = pipe.WaitForConnectionAsync();
        var psi = CreateElevationStartInfo(exe,
            BuildWorkerArguments("--worker", session, string.Join(',', workerIds), tempDays, pipeName, nonce));
        Process child;
        try { child = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить elevated-процесс."); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new OperationCanceledException("Запрос UAC отменён."); }

        using (child)
        {
            var childExitTask = child.WaitForExitAsync();
            var connectionTimeout = Task.Delay(TimeSpan.FromMinutes(2));
            var first = await Task.WhenAny(waitForConnection, childExitTask, connectionTimeout);
            if (first == childExitTask)
            {
                await childExitTask;
                throw new InvalidOperationException($"Elevated worker завершился до подключения к каналу результата. Код: {child.ExitCode}.");
            }
            if (first == connectionTimeout)
            {
                TryKill(child);
                throw new TimeoutException("Elevated worker не подключился к защищённому каналу результата за 2 минуты.");
            }
            await waitForConnection;
            progress?.Report("Административные действия выполняются…");
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 16 * 1024, leaveOpen: true);
            var readTask = reader.ReadToEndAsync();
            var operationTimeout = Task.Delay(TimeSpan.FromMinutes(125));
            var all = Task.WhenAll(readTask, childExitTask);
            if (await Task.WhenAny(all, operationTimeout) == operationTimeout)
            {
                TryKill(child);
                throw new TimeoutException("Превышено общее время выполнения административных действий.");
            }
            await all;
            var json = await readTask;
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException($"Elevated worker завершился с кодом {child.ExitCode}, но не вернул результат.");
            var envelope = JsonSerializer.Deserialize<WorkerEnvelope>(json, JsonOptions())
                ?? throw new InvalidOperationException("Некорректный результат elevated worker.");
            if (!FixedTimeAsciiEquals(envelope.Nonce, nonce)) throw new InvalidOperationException("Не удалось подтвердить подлинность результата elevated worker.");
            if (!string.Equals(envelope.Result.SessionId, session, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Session ID результата elevated worker не совпадает с запросом.");

            if (ids.Contains("CleanTemp", StringComparer.OrdinalIgnoreCase))
            {
                progress?.Report("Административные действия завершены. Проверяю пользовательский контекст перед очисткой Temp…");
                var localClean = await Task.Run(() => Execute(Guid.NewGuid().ToString("D"), ["CleanTemp"], tempDays));
                MergeInto(envelope.Result, localClean);
            }
            progress?.Report("Выбранные действия завершены. Запускаю автопроверку…");
            return envelope.Result;
        }
    }

    private static NamedPipeServerStream CreatePipeServer(string pipeName)
    {
        var security = new PipeSecurity();
        using var identity = WindowsIdentity.GetCurrent();
        var currentSid = identity.User;
        if (currentSid is not null) security.AddAccessRule(new PipeAccessRule(currentSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new PipeAccessRule(admins, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 64 * 1024, 64 * 1024, security);
    }

    private static bool TryParseWorkerActions(string? actionsCsv, out List<string> actions)
    {
        actions = [];
        var requested = (actionsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (requested.Length == 0 || requested.Any(a => !WorkerAllowed.Contains(a, StringComparer.OrdinalIgnoreCase))) return false;
        actions = requested.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return actions.Count > 0;
    }
    private static bool RequiresAdministrator(string actionId)
        => actionId.Equals("Dism", StringComparison.OrdinalIgnoreCase) || actionId.Equals("Sfc", StringComparison.OrdinalIgnoreCase);
    private static string BuildWorkerArguments(string mode, string session, string actions, int tempDays, string pipeName, string nonce)
        => $"{mode} --session {Quote(session)} --actions {Quote(actions)} --temp-days {tempDays} --pipe {Quote(pipeName)} --nonce {Quote(nonce)}";
    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    private static bool FixedTimeAsciiEquals(string? actual, string expected)
    {
        var a = Encoding.ASCII.GetBytes(actual ?? string.Empty); var b = Encoding.ASCII.GetBytes(expected);
        return a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static RemediationBatchResult Execute(string session, IReadOnlyCollection<string> requested, int tempDays)
    {
        var ids = requested.Where(x => Allowed.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
        NormalizeOrder(ids);
        var batch = new RemediationBatchResult { SessionId = session, StartedAt = DateTime.Now };
        foreach (var id in ids)
        {
            var started = DateTime.Now;
            var context = ExecutionContextService.Capture();
            var availability = ExecutionPolicy.For(id, context);
            RemediationActionResult result;
            try
            {
                if (availability.State != "Ready")
                    result = new() { Id = id, Success = false, Message = "Действие не запущено: " + availability.Reason };
                else result = id.ToLowerInvariant() switch
                {
                    "cleantemp" => CleanTemp(context, tempDays),
                    "flushdns" => RunCommand("FlushDns", Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/flushdns", TimeSpan.FromMinutes(2)),
                    "dism" => RunCommand("Dism", Path.Combine(Environment.SystemDirectory, "dism.exe"), "/Online /Cleanup-Image /RestoreHealth", TimeSpan.FromMinutes(60)),
                    "sfc" => RunCommand("Sfc", Path.Combine(Environment.SystemDirectory, "sfc.exe"), "/scannow", TimeSpan.FromMinutes(60)),
                    _ => new() { Id = id, Success = false, Message = "Действие не разрешено." }
                };
            }
            catch (Exception ex) { result = new() { Id = id, Success = false, Message = ex.Message }; }
            result.StartedAt = started; result.FinishedAt = DateTime.Now;
            result.ExecutionContext = context; result.TargetScope = availability.Scope;
            batch.Actions.Add(result);
            batch.Elevated |= context.IsElevated == true || context.HasAdministratorToken == true;
        }
        batch.FinishedAt = DateTime.Now;
        return batch;
    }

    private static RemediationActionResult CleanTemp(ExecutionContextInfo context, int olderThanDays)
    {
        var root = ExecutionPolicy.CleanupRoot(context);
        if (root is null)
            return new() { Id = "CleanTemp", Success = false, Message = "Очистка не запущена: нужен неповышенный процесс того же пользователя и подтверждённый профиль текущего сеанса." };
        var cutoff = DateTime.Now.AddDays(-Math.Clamp(olderThanDays, 1, 30));
        long files = 0, bytes = 0; var errors = 0;
        if (!Directory.Exists(root))
            return new() { Id = "CleanTemp", Success = true, Message = "Каталог Temp текущего пользователя отсутствует или недоступен для проверки; файлы не удалялись." };
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (IsReparsePoint(rootFull))
            return new() { Id = "CleanTemp", Success = false, Message = "Корень пользовательского Temp является reparse point или недоступен; очистка отменена." };
        var stack = new Stack<string>(); stack.Push(rootFull);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (IsReparsePoint(current)) { errors++; continue; }
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(current).ToArray(); }
            catch { errors++; continue; }
            foreach (var entry in entries)
            {
                try
                {
                    var attr = File.GetAttributes(entry);
                    if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attr & FileAttributes.Directory) != 0) { stack.Push(entry); continue; }
                    var info = new FileInfo(entry);
                    if (info.LastWriteTime >= cutoff) continue;
                    var len = info.Length;
                    File.Delete(entry); files++; bytes += len;
                }
                catch { errors++; }
            }
        }
        return new()
        {
            Id = "CleanTemp", Success = true, FreedMB = Math.Round(bytes / 1024d / 1024d, 1), DeletedFiles = files,
            Message = $"Temp текущего пользователя: удалено файлов {files}; освобождено {bytes / 1024d / 1024d:0.0} MB; пропущено/ошибок: {errors}."
        };
    }

    private static RemediationActionResult RunCommand(string id, string fileName, string arguments, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo { FileName = fileName, Arguments = arguments, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Не удалось запустить {fileName}.");
        var stdoutTask = p.StandardOutput.ReadToEndAsync(); var stderrTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds)))
        {
            TryKill(p);
            return new() { Id = id, Success = false, ExitCode = null, Message = $"Превышено время выполнения {timeout.TotalMinutes:0} мин." };
        }
        Task.WaitAll(stdoutTask, stderrTask);
        var output = (stdoutTask.Result + Environment.NewLine + stderrTask.Result).Trim();
        if (output.Length > 12000) output = output[^12000..];
        return new() { Id = id, Success = p.ExitCode == 0, ExitCode = p.ExitCode, Message = p.ExitCode == 0 ? "Команда завершена успешно." : $"Команда завершена с кодом {p.ExitCode}.", Output = output };
    }
    private static void MergeInto(RemediationBatchResult target, RemediationBatchResult addition)
    {
        target.Actions.AddRange(addition.Actions); target.Elevated |= addition.Elevated;
        if (addition.StartedAt < target.StartedAt) target.StartedAt = addition.StartedAt;
        if (addition.FinishedAt > target.FinishedAt) target.FinishedAt = addition.FinishedAt;
    }
    private static void WriteResult(Stream stream, WorkerEnvelope envelope)
    {
        var json = JsonSerializer.Serialize(envelope, JsonOptions());
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 16 * 1024, leaveOpen: true) { AutoFlush = true };
        writer.Write(json);
    }
    internal static ProcessStartInfo CreateElevationStartInfo(string executablePath, string arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath)) throw new ArgumentException("Не удалось определить полный путь к текущему EXE.", nameof(executablePath));
        return new ProcessStartInfo { FileName = executablePath, Arguments = arguments, UseShellExecute = true, Verb = "runas", WorkingDirectory = Environment.SystemDirectory, WindowStyle = ProcessWindowStyle.Hidden };
    }
    private static bool IsValidPipeName(string? pipeName, string session)
        => !string.IsNullOrWhiteSpace(pipeName) && pipeName.Length <= 128 && string.Equals(pipeName, PipePrefix + session, StringComparison.OrdinalIgnoreCase);
    private static bool IsValidNonce(string? nonce) => nonce is { Length: 64 } && nonce.All(Uri.IsHexDigit);
    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }
    }
    private static void NormalizeOrder(List<string> ids)
    {
        if (ids.Contains("Dism", StringComparer.OrdinalIgnoreCase) && ids.Contains("Sfc", StringComparer.OrdinalIgnoreCase))
        {
            ids.RemoveAll(x => x.Equals("Dism", StringComparison.OrdinalIgnoreCase) || x.Equals("Sfc", StringComparison.OrdinalIgnoreCase));
            ids.Add("Dism"); ids.Add("Sfc");
        }
    }
    private static void TryKill(Process process) { try { if (!process.HasExited) process.Kill(true); } catch { } }
    private static string? Arg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++) if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = false, PropertyNameCaseInsensitive = true };
    private sealed class WorkerEnvelope
    {
        public string Nonce { get; set; } = "";
        public RemediationBatchResult Result { get; set; } = new();
    }
}
