using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gradient.PcHealthCheck;

public static class RemediationWorker
{
    private static readonly string[] Allowed = ["CleanTemp", "FlushDns", "Dism", "Sfc"];
    private const string PipePrefix = "GradientPcHealthCheck-";

    public static int Run(string[] args)
    {
        try
        {
            var session = Arg(args, "--session");
            var actionsCsv = Arg(args, "--actions");
            var tempDaysText = Arg(args, "--temp-days");
            var pipeName = Arg(args, "--pipe");
            var nonce = Arg(args, "--nonce");

            if (!Guid.TryParse(session, out _)) return 20;
            if (!IsValidPipeName(pipeName, session!)) return 24;
            if (!IsValidNonce(nonce)) return 25;

            var actions = (actionsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(a => Allowed.Contains(a, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (actions.Count == 0) return 21;

            var needsAdmin = actions.Any(a => a is "CleanTemp" or "Dism" or "Sfc");
            if (needsAdmin && !DiagnosticsService.IsAdministrator()) return 22;

            var profile = DiagnosticsService.GetInteractiveUserProfile();
            if (string.IsNullOrWhiteSpace(profile)) return 23;

            var days = int.TryParse(tempDaysText, out var parsed) ? Math.Clamp(parsed, 1, 30) : 3;
            var batch = Execute(session!, actions, profile, days);
            var envelope = new WorkerEnvelope { Nonce = nonce!, Result = batch };
            SendResult(pipeName!, envelope);
            return batch.Actions.All(x => x.Success) ? 0 : 2;
        }
        catch
        {
            return 99;
        }
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

        var requiresAdmin = selected.Any(x => x.CanAutomate && x.RequiresAdmin);
        if (!requiresAdmin || DiagnosticsService.IsAdministrator())
        {
            progress?.Report(requiresAdmin ? "Процесс уже запущен с правами администратора. Выполняю выбранные действия…" : "Выполняю выбранные действия…");
            var localSession = Guid.NewGuid().ToString("D");
            return await Task.Run(() => Execute(localSession, ids, profile, tempDays));
        }

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь к EXE.");
        if (!IsTrustedElevationLocation(exe))
            throw new InvalidOperationException("Для безопасного запроса UAC приложение должно быть запущено из Program Files. Разверните Gradient-PC-Health-Check.exe в %ProgramFiles%\\Gradient\\PCHealthCheck или запустите текущую копию уже с правами администратора.");

        var session = Guid.NewGuid().ToString("D");
        var pipeName = PipePrefix + session;
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        progress?.Report("Запрашиваю права администратора через UAC…");
        var actionArg = string.Join(',', ids);

        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            64 * 1024,
            64 * 1024);

        var waitForConnection = pipe.WaitForConnectionAsync();
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"--worker --session \"{session}\" --actions \"{actionArg}\" --temp-days {Math.Clamp(tempDays, 1, 30)} --pipe \"{pipeName}\" --nonce \"{nonce}\"",
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
            var timeout = Task.Delay(TimeSpan.FromMinutes(125));
            var connected = await Task.WhenAny(waitForConnection, timeout);
            if (connected != waitForConnection)
            {
                TryKill(child);
                throw new TimeoutException("Elevated worker не подключился к защищённому каналу результата.");
            }
            await waitForConnection;

            progress?.Report("Административные действия выполняются…");
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 16 * 1024, leaveOpen: true);
            var readTask = reader.ReadToEndAsync();
            var exitTask = child.WaitForExitAsync();
            var completed = await Task.WhenAny(Task.WhenAll(readTask, exitTask), timeout);
            if (completed == timeout)
            {
                TryKill(child);
                throw new TimeoutException("Превышено общее время выполнения административных действий.");
            }

            await exitTask;
            var json = await readTask;
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException($"Elevated worker завершился с кодом {child.ExitCode}, но не вернул результат.");

            var envelope = JsonSerializer.Deserialize<WorkerEnvelope>(json, JsonOptions())
                ?? throw new InvalidOperationException("Некорректный результат elevated worker.");
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(envelope.Nonce ?? string.Empty),
                    Encoding.ASCII.GetBytes(nonce)))
                throw new InvalidOperationException("Не удалось подтвердить подлинность результата elevated worker.");
            if (!string.Equals(envelope.Result.SessionId, session, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Session ID результата elevated worker не совпадает с запросом.");

            progress?.Report("Административные действия завершены. Запускаю автопроверку…");
            return envelope.Result;
        }
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
        var roots = new[]
        {
            Path.Combine(profile, "AppData", "Local", "Temp"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")
        };
        var cutoff = DateTime.Now.AddDays(-Math.Abs(olderThanDays));
        long files = 0, bytes = 0;
        var errors = 0;

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            if (IsReparsePoint(rootFull))
            {
                errors++;
                continue;
            }

            var stack = new Stack<string>();
            stack.Push(rootFull);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
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
        }

        return new RemediationActionResult
        {
            Id = "CleanTemp",
            Success = true,
            FreedMB = Math.Round(bytes / 1024d / 1024d, 1),
            DeletedFiles = files,
            Message = $"Удалено файлов: {files}; освобождено {bytes / 1024d / 1024d:0.0} MB; пропущено/ошибок: {errors}."
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

    private static void SendResult(string pipeName, WorkerEnvelope envelope)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.None);
        client.Connect((int)TimeSpan.FromSeconds(30).TotalMilliseconds);
        var json = JsonSerializer.Serialize(envelope, JsonOptions());
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 16 * 1024, leaveOpen: true) { AutoFlush = true };
        writer.Write(json);
    }

    private static bool IsTrustedElevationLocation(string exePath)
    {
        try
        {
            var full = Path.GetFullPath(exePath);
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                     }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
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
