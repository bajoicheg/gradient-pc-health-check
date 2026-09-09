using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Gradient.PcHealthCheck;

public static class RemediationWorker
{
    private static readonly string[] Allowed = ["CleanTemp", "FlushDns", "Dism", "Sfc"];

    public static int Run(string[] args)
    {
        try
        {
            var session = Arg(args, "--session");
            var actionsCsv = Arg(args, "--actions");
            var tempDaysText = Arg(args, "--temp-days");
            if (!Guid.TryParse(session, out _)) return 20;
            var actions = (actionsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(a => Allowed.Contains(a, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (actions.Count == 0) return 21;
            var needsAdmin = actions.Any(a => a is "CleanTemp" or "Dism" or "Sfc");
            if (needsAdmin && !DiagnosticsService.IsAdministrator()) return 22;

            var profile = DiagnosticsService.GetInteractiveUserProfile();
            if (string.IsNullOrWhiteSpace(profile)) return 23;
            var resultPath = GetResultPath(profile, session!);
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);

            var days = int.TryParse(tempDaysText, out var parsed) ? Math.Clamp(parsed, 1, 30) : 3;
            var batch = Execute(session!, actions, profile, days);
            var json = JsonSerializer.Serialize(batch, JsonOptions());
            File.WriteAllText(resultPath, json);
            return batch.Actions.All(x => x.Success) ? 0 : 2;
        }
        catch { return 99; }
    }

    public static async Task<RemediationBatchResult> ExecuteFromGuiAsync(IReadOnlyCollection<ActionRecommendation> selected, int tempDays, IProgress<string>? progress = null)
    {
        var ids = selected.Where(x => x.CanAutomate).Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) throw new InvalidOperationException("Не выбрано ни одного автоматизируемого действия.");
        if (ids.Contains("Dism", StringComparer.OrdinalIgnoreCase) && ids.Contains("Sfc", StringComparer.OrdinalIgnoreCase))
        {
            ids.RemoveAll(x => x.Equals("Dism", StringComparison.OrdinalIgnoreCase) || x.Equals("Sfc", StringComparison.OrdinalIgnoreCase));
            ids.Add("Dism"); ids.Add("Sfc");
        }

        var profile = DiagnosticsService.GetInteractiveUserProfile();
        if (string.IsNullOrWhiteSpace(profile)) throw new InvalidOperationException("Не удалось безопасно определить профиль интерактивного пользователя. Административные действия отменены.");
        var session = Guid.NewGuid().ToString("D");
        var resultPath = GetResultPath(profile, session);
        try { if (File.Exists(resultPath)) File.Delete(resultPath); } catch { }

        var requiresAdmin = selected.Any(x => x.CanAutomate && x.RequiresAdmin);
        if (!requiresAdmin)
        {
            progress?.Report("Выполняю выбранные действия…");
            return await Task.Run(() => Execute(session, ids, profile, tempDays));
        }

        progress?.Report("Запрашиваю права администратора через UAC…");
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь к EXE.");
        var actionArg = string.Join(',', ids);
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"--worker --session \"{session}\" --actions \"{actionArg}\" --temp-days {tempDays}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Normal
        };

        Process child;
        try { child = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить elevated-процесс."); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new OperationCanceledException("Запрос UAC отменён."); }
        using (child)
        {
            await Task.Run(() => child.WaitForExit());
            progress?.Report("Административные действия завершены. Читаю результат…");
            for (var i = 0; i < 20 && !File.Exists(resultPath); i++) await Task.Delay(100);
            if (!File.Exists(resultPath)) throw new InvalidOperationException($"Elevated-процесс завершился с кодом {child.ExitCode}, но файл результата не создан.");
        }

        var json = await File.ReadAllTextAsync(resultPath);
        return JsonSerializer.Deserialize<RemediationBatchResult>(json, JsonOptions()) ?? throw new InvalidOperationException("Некорректный результат remediation.");
    }

    private static RemediationBatchResult Execute(string session, IReadOnlyCollection<string> requested, string profile, int tempDays)
    {
        var ids = requested.ToList();
        if (ids.Contains("Dism", StringComparer.OrdinalIgnoreCase) && ids.Contains("Sfc", StringComparer.OrdinalIgnoreCase))
        {
            ids.RemoveAll(x => x.Equals("Dism", StringComparison.OrdinalIgnoreCase) || x.Equals("Sfc", StringComparison.OrdinalIgnoreCase));
            ids.Add("Dism"); ids.Add("Sfc");
        }
        var batch = new RemediationBatchResult { SessionId = session, StartedAt = DateTime.Now, Elevated = DiagnosticsService.IsAdministrator() };
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
                batch.Actions.Add(new RemediationActionResult { Id = id, Success = false, Message = ex.Message, StartedAt = started, FinishedAt = DateTime.Now });
            }
        }
        batch.FinishedAt = DateTime.Now;
        return batch;
    }

    private static RemediationActionResult CleanTemp(string profile, int olderThanDays)
    {
        var roots = new[] { Path.Combine(profile, "AppData", "Local", "Temp"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") };
        var cutoff = DateTime.Now.AddDays(-Math.Abs(olderThanDays));
        long files = 0, bytes = 0;
        var errors = 0;
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            var stack = new Stack<string>();
            stack.Push(rootFull);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                IEnumerable<string> entries;
                try { entries = Directory.EnumerateFileSystemEntries(current).ToArray(); } catch { errors++; continue; }
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
            Id = "CleanTemp", Success = true, FreedMB = Math.Round(bytes / 1024d / 1024d, 1), DeletedFiles = files,
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
            try { p.Kill(true); } catch { }
            return new RemediationActionResult { Id = id, Success = false, ExitCode = null, Message = $"Превышено время выполнения {timeout.TotalMinutes:0} мин." };
        }
        Task.WaitAll(stdoutTask, stderrTask);
        var output = (stdoutTask.Result + Environment.NewLine + stderrTask.Result).Trim();
        if (output.Length > 12000) output = output[^12000..];
        return new RemediationActionResult { Id = id, Success = p.ExitCode == 0, ExitCode = p.ExitCode, Message = p.ExitCode == 0 ? "Команда завершена успешно." : $"Команда завершена с кодом {p.ExitCode}.", Output = output };
    }

    private static string GetResultPath(string profile, string session)
    {
        if (!Guid.TryParse(session, out _)) throw new ArgumentException("Некорректный session id.");
        var dir = Path.Combine(profile, "AppData", "Local", "Temp", "Gradient-PCHealthCheck", "Remediation");
        var fullDir = Path.GetFullPath(dir);
        var expected = Path.GetFullPath(profile).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullDir.StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Небезопасный путь результата.");
        return Path.Combine(fullDir, session + ".json");
    }

    private static string? Arg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++) if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
}
