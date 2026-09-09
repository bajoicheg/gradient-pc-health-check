using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Gradient.PcHealthCheck;

public static class RemediationWorker
{
    private static readonly string[] Allowed = ["CleanTemp", "FlushDns", "Dism", "Sfc"];
    private const string PipePrefix = "GradientPcHealthCheck-";
    private const string InstalledExeName = "Gradient-PC-Health-Check.exe";

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

            var actions = ParseActions(actionsCsv);
            if (actions.Count == 0) return 21;

            var needsAdmin = actions.Any(a => a is "CleanTemp" or "Dism" or "Sfc");
            if (needsAdmin && !DiagnosticsService.IsAdministrator()) return 22;

            var profile = DiagnosticsService.GetInteractiveUserProfile();
            if (string.IsNullOrWhiteSpace(profile)) return 23;

            var days = int.TryParse(tempDaysText, out var parsed) ? Math.Clamp(parsed, 1, 30) : 3;

            // Connect before long-running remediation. This confirms elevation/bootstrap quickly,
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
    /// Elevated bootstrap used when the GUI was launched from a user-writable location such as Downloads.
    /// The same signed/hashed candidate EXE is copied into Program Files, verified byte-for-byte by SHA-256,
    /// and the trusted copy is then used as the actual remediation worker. The GUI still receives the result
    /// over the one-time named-pipe session.
    /// </summary>
    public static int RunBootstrap(string[] args)
    {
        try
        {
            if (!DiagnosticsService.IsAdministrator()) return 40;

            var session = Arg(args, "--session");
            var actionsCsv = Arg(args, "--actions");
            var tempDaysText = Arg(args, "--temp-days");
            var pipeName = Arg(args, "--pipe");
            var nonce = Arg(args, "--nonce");

            if (!Guid.TryParse(session, out _)) return 41;
            if (!IsValidPipeName(pipeName, session!)) return 42;
            if (!IsValidNonce(nonce)) return 43;
            var actions = ParseActions(actionsCsv);
            if (actions.Count == 0) return 44;

            var trustedExe = EnsureTrustedInstalledCopy();
            if (!IsTrustedElevationLocation(trustedExe)) return 45;

            var arguments = BuildWorkerArguments(
                "--worker",
                session!,
                string.Join(',', actions),
                int.TryParse(tempDaysText, out var days) ? Math.Clamp(days, 1, 30) : 3,
                pipeName!,
                nonce!);

            using var child = Process.Start(new ProcessStartInfo
            {
                FileName = trustedExe,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (child is null) return 46;
            if (!child.WaitForExit((int)TimeSpan.FromMinutes(125).TotalMilliseconds))
            {
                TryKill(child);
                return 47;
            }
            return child.ExitCode;
        }
        catch
        {
            return 49;
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
        var trusted = IsTrustedElevationLocation(exe);
        var mode = trusted ? "--worker" : "--bootstrap-worker";

        var session = Guid.NewGuid().ToString("D");
        var pipeName = PipePrefix + session;
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        progress?.Report(trusted
            ? "Запрашиваю права администратора через UAC…"
            : "Запрашиваю UAC и подготавливаю защищённую копию в Program Files…");

        await using var pipe = CreatePipeServer(pipeName);
        var waitForConnection = pipe.WaitForConnectionAsync();

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = BuildWorkerArguments(mode, session, string.Join(',', ids), Math.Clamp(tempDays, 1, 30), pipeName, nonce),
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
                throw new InvalidOperationException($"Elevated bootstrap/worker завершился до подключения к каналу результата. Код: {child.ExitCode}.");
            }
            if (first == connectionTimeout)
            {
                TryKill(child);
                throw new TimeoutException("Elevated worker не подключился к защищённому каналу результата за 2 минуты.");
            }

            await waitForConnection;
            progress?.Report(trusted
                ? "Административные действия выполняются…"
                : "Защищённая копия подготовлена. Административные действия выполняются…");

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

            progress?.Report("Административные действия завершены. Запускаю автопроверку…");
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

    private static string EnsureTrustedInstalledCopy()
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь текущего EXE.");
        if (IsTrustedElevationLocation(source)) return source;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
            throw new InvalidOperationException("Не удалось определить Program Files.");

        var directory = Path.Combine(programFiles, "Gradient", "PCHealthCheck");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, InstalledExeName);

        if (File.Exists(target) && FilesHaveSameSha256(source, target)) return target;

        var staging = target + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, staging, overwrite: true);
            if (!FilesHaveSameSha256(source, staging))
                throw new InvalidOperationException("SHA-256 установленной копии не совпадает с исходным EXE.");
            File.Move(staging, target, overwrite: true);
            if (!FilesHaveSameSha256(source, target))
                throw new InvalidOperationException("Контроль SHA-256 после установки не пройден.");
            return target;
        }
        finally
        {
            try { if (File.Exists(staging)) File.Delete(staging); } catch { }
        }
    }

    private static bool FilesHaveSameSha256(string a, string b)
    {
        var ah = HashFile(a);
        var bh = HashFile(b);
        return CryptographicOperations.FixedTimeEquals(ah, bh);
    }

    private static byte[] HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan);
        return SHA256.HashData(stream);
    }

    private static List<string> ParseActions(string? actionsCsv)
        => (actionsCsv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => Allowed.Contains(a, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string BuildWorkerArguments(string mode, string session, string actions, int tempDays, string pipeName, string nonce)
        => $"{mode} --session {Quote(session)} --actions {Quote(actions)} --temp-days {tempDays} --pipe {Quote(pipeName)} --nonce {Quote(nonce)}";

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static bool FixedTimeAsciiEquals(string? actual, string expected)
    {
        var a = Encoding.ASCII.GetBytes(actual ?? string.Empty);
        var b = Encoding.ASCII.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
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
