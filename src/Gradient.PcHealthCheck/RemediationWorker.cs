using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Gradient.PcHealthCheck;

public static class RemediationWorker
{
    private static readonly string[] Allowed = ["CleanTemp", "FlushDns", "Dism", "Sfc"];
    private static readonly string[] WorkerAllowed = ["FlushDns", "Dism", "Sfc"];
    private const string PipePrefix = "GradientPcHealthCheck-";
    private const string InstalledExeName = "Gradient-PC-Health-Check.exe";

    public static int Run(string[] args)
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExe) || !IsTrustedElevationLocation(currentExe)) return 26;

            var session = Arg(args, "--session");
            var actionsCsv = Arg(args, "--actions");
            var tempDaysText = Arg(args, "--temp-days");
            var pipeName = Arg(args, "--pipe");
            var nonce = Arg(args, "--nonce");

            if (!Guid.TryParse(session, out _)) return 20;
            if (!IsValidPipeName(pipeName, session!)) return 24;
            if (!IsValidNonce(nonce)) return 25;
            if (!TryParseWorkerActions(actionsCsv, out var actions)) return 21;

            var needsAdmin = actions.Any(RequiresAdministrator);
            if (needsAdmin && !DiagnosticsService.IsAdministrator()) return 22;

            var profile = DiagnosticsService.GetInteractiveUserProfile();
            if (string.IsNullOrWhiteSpace(profile)) return 23;

            var days = int.TryParse(tempDaysText, out var parsed) ? Math.Clamp(parsed, 1, 30) : 3;

            // Connect before long-running remediation. This confirms elevation quickly,
            // while the pipe remains open until the final result is written.
            using var client = new NamedPipeClientStream(".", pipeName!, PipeDirection.Out, PipeOptions.None);
            client.Connect((int)TimeSpan.FromSeconds(45).TotalMilliseconds);

            var batch = Execute(session!, actions, profile, days);
            var envelope = new WorkerEnvelope { Nonce = nonce!, Result = batch };
            WriteResult(client, envelope);
            return batch.Actions.All(x => x.Success) ? 0 : 2;
        }
        catch
        {
            return 99;
        }
    }

    /// <summary>
    /// Bootstrap from a user-writable location was intentionally disabled before pilot.
    /// Elevating an EXE directly from Downloads leaves an unavoidable pre-UAC path-swap boundary
    /// until the product has Authenticode/trusted deployment. Privileged remediation is allowed
    /// only from the canonical Program Files location.
    /// </summary>
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
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0) throw new InvalidOperationException("Не выбрано ни одного автоматизируемого действия.");
        NormalizeOrder(ids);

        var profile = DiagnosticsService.GetInteractiveUserProfile();
        if (string.IsNullOrWhiteSpace(profile))
            throw new InvalidOperationException("Не удалось безопасно определить профиль интерактивного пользователя. Действия отменены.");

        // Privilege is a security property of the hardcoded action ID, not trusted UI/model metadata.
        var requiresAdmin = ids.Any(RequiresAdministrator);
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь к EXE.");

        if (requiresAdmin && !IsTrustedElevationLocation(exe))
        {
            throw new InvalidOperationException(
                "Административные действия разрешены только для проверенной копии Gradient PC Health Check в Program Files. " +
                "Запуск диагностики и очистки Temp пользователя из Downloads разрешён, но для DISM/SFC сначала разверните EXE в " +
                "%ProgramFiles%\\Gradient\\PCHealthCheck средствами корпоративного управления ПО.");
        }

        if (!requiresAdmin || DiagnosticsService.IsAdministrator())
        {
            progress?.Report(requiresAdmin ? "Процесс уже запущен с правами администратора. Выполняю выбранные действия…" : "Выполняю выбранные действия…");
            var localSession = Guid.NewGuid().ToString("D");
            return await Task.Run(() => Execute(localSession, ids, profile, tempDays));
        }

        // User-controlled Temp is intentionally excluded from the elevated worker. It is cleaned
        // later by the original standard-user process, after privileged actions finish successfully.
        var workerIds = ids.Where(x => !x.Equals("CleanTemp", StringComparison.OrdinalIgnoreCase)).ToList();
        if (workerIds.Count == 0)
            throw new InvalidOperationException("Не выбрано административных действий для elevated worker.");
        NormalizeOrder(workerIds);

        var session = Guid.NewGuid().ToString("D");
        var pipeName = PipePrefix + session;
        var nonce = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        progress?.Report("Запрашиваю права администратора через UAC…");

        await using var pipe = CreatePipeServer(pipeName);
        var waitForConnection = pipe.WaitForConnectionAsync();

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = BuildWorkerArguments("--worker", session, string.Join(',', workerIds), Math.Clamp(tempDays, 1, 30), pipeName, nonce),
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process child;
        try
        {
            child = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить elevated-процесс.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Запрос UAC отменён.");
        }

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
            var completed = await Task.WhenAny(all, operationTimeout);
            if (completed == operationTimeout)
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
            if (!FixedTimeAsciiEquals(envelope.Nonce, nonce))
                throw new InvalidOperationException("Не удалось подтвердить подлинность результата elevated worker.");
            if (!string.Equals(envelope.Result.SessionId, session, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Session ID результата elevated worker не совпадает с запросом.");

            if (ids.Contains("CleanTemp", StringComparer.OrdinalIgnoreCase))
            {
                progress?.Report("Административные действия завершены. Очищаю Temp в контексте пользователя…");
                var localClean = await Task.Run(() => Execute(
                    Guid.NewGuid().ToString("D"), ["CleanTemp"], profile, tempDays));
                MergeInto(envelope.Result, localClean);
            }

            progress?.Report("Выбранные действия завершены. Запускаю автопроверку…");
            return envelope.Result;
        }
    }

    private static NamedPipeServerStream CreatePipeServer(string pipeName)
    {
        var security = new PipeSecurity();
        var currentSid = WindowsIdentity.GetCurrent().User;
        if (currentSid is not null)
            security.AddAccessRule(new PipeAccessRule(currentSid, PipeAccessRights.FullControl, AccessControlType.Allow));

        // A Service Desk engineer may enter a different local/domain admin credential in the UAC dialog.
        // Permit the local Administrators group to connect while the one-time nonce/session still authenticates the result.
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new PipeAccessRule(admins, PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            64 * 1024,
            64 * 1024,
            security);
    }

    private static bool TryParseWorkerActions(string? actionsCsv, out List<string> actions)
    {
        actions = [];
        var requested = (actionsCsv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (requested.Length == 0) return false;
        if (requested.Any(a => !WorkerAllowed.Contains(a, StringComparer.OrdinalIgnoreCase))) return false;
        actions = requested.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return actions.Count > 0;
    }

    private static bool RequiresAdministrator(string actionId)
        => actionId.Equals("Dism", StringComparison.OrdinalIgnoreCase)
           || actionId.Equals("Sfc", StringComparison.OrdinalIgnoreCase);

    private static string BuildWorkerArguments(string mode, string session, string actions, int tempDays, string pipeName, string nonce)
        => $"{mode} --session {Quote(session)} --actions {Quote(actions)} --temp-days {tempDays} --pipe {Quote(pipeName)} --nonce {Quote(nonce)}";

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static bool FixedTimeAsciiEquals(string? actual, string expected)
    {
        var a = Encoding.ASCII.GetBytes(actual ?? string.Empty);
        var b = Encoding.ASCII.GetBytes(expected);
        return a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static RemediationBatchResult Execute(string session, IReadOnlyCollection<string> requested, string profile, int tempDays)
    {
        var ids = requested.Where(x => Allowed.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
        NormalizeOrder(ids);
        var batch = new RemediationBatchResult
        {
            SessionId = session,
            StartedAt = DateTime.Now,
            Elevated = DiagnosticsService.IsAdministrator()
        };

        foreach (var id in ids)
        {
            var started = DateTime.Now;
            try
            {
                var result = id.ToLowerInvariant() switch
                {
                    "cleantemp" => CleanTemp(profile, tempDays),
                    "flushdns" => RunCommand("FlushDns", Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/flushdns", TimeSpan.FromMinutes(2)),
                    "dism" => RunCommand("Dism", Path.Combine(Environment.SystemDirectory, "dism.exe"), "/Online /Cleanup-Image /RestoreHealth", TimeSpan.FromMinutes(60)),
                    "sfc" => RunCommand("Sfc", Path.Combine(Environment.SystemDirectory, "sfc.exe"), "/scannow", TimeSpan.FromMinutes(60)),
                    _ => new RemediationActionResult { Id = id, Success = false, Message = "Действие не разрешено." }
                };
                result.StartedAt = started;
                result.FinishedAt = DateTime.Now;
                batch.Actions.Add(result);
            }
            catch (Exception ex)
            {
                batch.Actions.Add(new RemediationActionResult
                {
                    Id = id,
                    Success = false,
                    Message = ex.Message,
                    StartedAt = started,
                    FinishedAt = DateTime.Now
                });
            }
        }

        batch.FinishedAt = DateTime.Now;
        return batch;
    }

    private static RemediationActionResult CleanTemp(string profile, int olderThanDays)
    {
        if (DiagnosticsService.IsAdministrator())
        {
            return new RemediationActionResult
            {
                Id = "CleanTemp",
                Success = false,
                Message = "Очистка пользовательского Temp намеренно не выполняется из elevated-процесса. Перезапустите приложение обычным пользователем."
            };
        }

        var root = Path.Combine(profile, "AppData", "Local", "Temp");
        var cutoff = DateTime.Now.AddDays(-Math.Abs(olderThanDays));
        long files = 0, bytes = 0;
        var errors = 0;

        if (!Directory.Exists(root))
        {
            return new RemediationActionResult
            {
                Id = "CleanTemp",
                Success = true,
                Message = "Каталог Temp текущего пользователя отсутствует; очистка не требуется."
            };
        }

        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (IsReparsePoint(rootFull))
        {
            return new RemediationActionResult
            {
                Id = "CleanTemp",
                Success = false,
                Message = "Корень пользовательского Temp является reparse point; очистка отменена."
            };
        }

        var stack = new Stack<string>();
        stack.Push(rootFull);
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
                    if ((attr & FileAttributes.Directory) != 0)
                    {
                        stack.Push(entry);
                        continue;
                    }

                    var info = new FileInfo(entry);
                    if (info.LastWriteTime >= cutoff) continue;
                    var len = info.Length;
                    File.Delete(entry);
                    files++;
                    bytes += len;
                }
                catch { errors++; }
            }
        }

        return new RemediationActionResult
        {
            Id = "CleanTemp",
            Success = true,
            FreedMB = Math.Round(bytes / 1024d / 1024d, 1),
            DeletedFiles = files,
            Message = $"Temp текущего пользователя: удалено файлов {files}; освобождено {bytes / 1024d / 1024d:0.0} MB; пропущено/ошибок: {errors}."
        };
    }

    private static RemediationActionResult RunCommand(string id, string fileName, string arguments, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Не удалось запустить {fileName}.");
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds)))
        {
            TryKill(p);
            return new RemediationActionResult
            {
                Id = id,
                Success = false,
                ExitCode = null,
                Message = $"Превышено время выполнения {timeout.TotalMinutes:0} мин."
            };
        }

        Task.WaitAll(stdoutTask, stderrTask);
        var output = (stdoutTask.Result + Environment.NewLine + stderrTask.Result).Trim();
        if (output.Length > 12000) output = output[^12000..];
        return new RemediationActionResult
        {
            Id = id,
            Success = p.ExitCode == 0,
            ExitCode = p.ExitCode,
            Message = p.ExitCode == 0 ? "Команда завершена успешно." : $"Команда завершена с кодом {p.ExitCode}.",
            Output = output
        };
    }

    private static void MergeInto(RemediationBatchResult target, RemediationBatchResult addition)
    {
        target.Actions.AddRange(addition.Actions);
        if (addition.StartedAt < target.StartedAt) target.StartedAt = addition.StartedAt;
        if (addition.FinishedAt > target.FinishedAt) target.FinishedAt = addition.FinishedAt;
    }

    private static void WriteResult(Stream stream, WorkerEnvelope envelope)
    {
        var json = JsonSerializer.Serialize(envelope, JsonOptions());
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 16 * 1024, leaveOpen: true) { AutoFlush = true };
        writer.Write(json);
    }

    public static bool IsTrustedElevationLocation(string exePath)
    {
        try
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrWhiteSpace(programFiles)) return false;

            var full = Path.GetFullPath(exePath).TrimEnd(Path.DirectorySeparatorChar);
            var expected = Path.GetFullPath(Path.Combine(programFiles, "Gradient", "PCHealthCheck", InstalledExeName))
                .TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(full, expected, StringComparison.OrdinalIgnoreCase)) return false;

            if (File.Exists(full) && IsReparsePoint(full)) return false;

            var root = Path.GetFullPath(programFiles).TrimEnd(Path.DirectorySeparatorChar);
            var current = Path.GetDirectoryName(full);
            while (!string.IsNullOrWhiteSpace(current)
                   && current.Length > root.Length
                   && current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(current) && IsReparsePoint(current)) return false;
                current = Path.GetDirectoryName(current);
            }

            return true;
        }
        catch { return false; }
    }

    private static bool IsValidPipeName(string? pipeName, string session)
        => !string.IsNullOrWhiteSpace(pipeName)
           && pipeName.Length <= 128
           && string.Equals(pipeName, PipePrefix + session, StringComparison.OrdinalIgnoreCase);

    private static bool IsValidNonce(string? nonce)
        => nonce is { Length: 64 } && nonce.All(Uri.IsHexDigit);

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
            ids.Add("Dism");
            ids.Add("Sfc");
        }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch { }
    }

    private static string? Arg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = false, PropertyNameCaseInsensitive = true };

    private sealed class WorkerEnvelope
    {
        public string Nonce { get; set; } = "";
        public RemediationBatchResult Result { get; set; } = new();
    }
}
