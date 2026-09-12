using Microsoft.Win32;
using System.Security.Principal;

namespace G.PcHealthCheck;

/// <summary>Read registrations, never execute/expand their commands or resolve shortcut targets.</summary>
internal static class StartupReviewService
{
    public const string ScopeNote = "Проверены Run/RunOnce HKCU, Run/RunOnce HKLM (32/64-bit на x64) и папки Startup текущего аккаунта и всех пользователей. Задачи, службы, расширения и другие механизмы не перечисляются. Наличие записи не доказывает включённый запуск, влияние на скорость или вредоносность. Ярлыки показаны как файлы; их цели не разрешаются.";
    private const int SourceLimit = 2000;

    public static List<StartupReviewEntry> Filter(StartupReviewSnapshot snapshot, string? query)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var text = query?.Trim() ?? "";
        return snapshot.Entries.Where(x => text.Length == 0 || new[] { x.Name, x.Command, x.Scope, x.Source, x.State }
            .Any(value => value.Contains(text, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public static StartupReviewSnapshot Collect(CancellationToken ct, IProgress<string>? progress = null)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var account = identity.Name;
        var providers = new List<Func<CancellationToken, (ReviewSource Source, List<StartupReviewEntry> Entries)>>();
        foreach (var keyName in new[] { "Run", "RunOnce" })
        {
            var key = keyName;
            providers.Add(token => RegistrySource(RegistryHive.CurrentUser, RegistryView.Default, key, account, token));
            foreach (var view in Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry64, RegistryView.Registry32 } : new[] { RegistryView.Default })
            {
                var capturedView = view;
                providers.Add(token => RegistrySource(RegistryHive.LocalMachine, capturedView, key, "Все пользователи", token));
            }
        }
        providers.Add(token => FolderSource(Environment.SpecialFolder.Startup, account, token));
        providers.Add(token => FolderSource(Environment.SpecialFolder.CommonStartup, "Все пользователи", token));
        var result = CollectSources(providers.Select(provider => (Func<CancellationToken, (ReviewSource, List<StartupReviewEntry>)>)(token =>
        {
            progress?.Report("Читаю источник автозагрузки…"); return provider(token);
        })), ct);
        result.Account = account;
        if (DiagnosticsService.IsAdministrator())
            result.Issues.Add("Приложение повышено. HKCU и личная Startup относятся к указанному аккаунту процесса, который может отличаться от пользователя рабочего стола.");
        return result;
    }

    public static StartupReviewSnapshot CollectSources(IEnumerable<Func<CancellationToken, (ReviewSource Source, List<StartupReviewEntry> Entries)>> sources, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var result = new StartupReviewSnapshot();
        foreach (var collect in sources)
        {
            ct.ThrowIfCancellationRequested();
            if (result.Sources.Count >= 32) { result.Issues.Add("Достигнут предел 32 источников."); break; }
            ReviewSource source; List<StartupReviewEntry> entries;
            try { (source, entries) = collect(ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                source = new ReviewSource { Name = $"Источник #{result.Sources.Count + 1}", State = ReviewCollectionState.Unavailable, Detail = Describe(ex) };
                entries = [];
            }
            ct.ThrowIfCancellationRequested();
            if (entries.Count > SourceLimit)
            {
                entries = entries.Take(SourceLimit).ToList(); source.State = ReviewCollectionState.Partial;
                source.Detail += $" Ограничение: первые {SourceLimit} записей источника.";
            }
            source.Items = entries.Count;
            result.Sources.Add(source); result.Entries.AddRange(entries);
            if (source.State is ReviewCollectionState.Partial or ReviewCollectionState.Unavailable)
                result.Issues.Add(source.Name + ": " + source.Detail);
        }
        result.Entries = result.Entries.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Source, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Command, StringComparer.Ordinal).ToList();
        result.State = result.Sources.Count == 0 || result.Sources.All(x => x.State == ReviewCollectionState.Unavailable)
            ? ReviewCollectionState.Unavailable : result.Issues.Count > 0 ? ReviewCollectionState.Partial : ReviewCollectionState.Complete;
        result.CollectedAt = DateTime.Now;
        return result;
    }

    private static (ReviewSource Source, List<StartupReviewEntry> Entries) RegistrySource(RegistryHive hive, RegistryView view, string keyName, string scope, CancellationToken ct)
    {
        var path = @"Software\Microsoft\Windows\CurrentVersion\" + keyName;
        var source = new ReviewSource { Name = $"{(hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\\{path} [{view}]", Scope = scope, State = ReviewCollectionState.Complete };
        var entries = new List<StartupReviewEntry>();
        try
        {
            ct.ThrowIfCancellationRequested();
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(path, writable: false);
            if (key is null) { source.State = ReviewCollectionState.Missing; source.Detail = "Ключ отсутствует."; return (source, entries); }
            var names = key.GetValueNames();
            if (names.Length > SourceLimit) { source.State = ReviewCollectionState.Partial; source.Detail = $"Предел {SourceLimit} записей."; }
            foreach (var name in names.Take(SourceLimit))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    var command = value as string;
                    if (command is null)
                    {
                        source.State = ReviewCollectionState.Partial;
                        source.Detail = "Есть исчезнувшие или нестроковые значения; команда для них неизвестна.";
                    }
                    entries.Add(new StartupReviewEntry { Name = name.Length == 0 ? "(по умолчанию)" : name,
                        Command = command ?? "[строковая команда недоступна]", Scope = scope, Source = source.Name });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    source.State = ReviewCollectionState.Partial; source.Detail = Describe(ex);
                    entries.Add(new StartupReviewEntry { Name = name, Source = source.Name, Scope = scope, Command = "[значение недоступно]" });
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { source.State = entries.Count > 0 ? ReviewCollectionState.Partial : ReviewCollectionState.Unavailable; source.Detail = Describe(ex); }
        return (source, entries);
    }

    private static (ReviewSource Source, List<StartupReviewEntry> Entries) FolderSource(Environment.SpecialFolder folder, string scope, CancellationToken ct)
    {
        var source = new ReviewSource { Name = folder.ToString(), Scope = scope, State = ReviewCollectionState.Complete };
        var entries = new List<StartupReviewEntry>();
        try
        {
            ct.ThrowIfCancellationRequested();
            var path = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrWhiteSpace(path)) throw new IOException("Startup path is unavailable.");
            source.Name = "Startup: " + path;
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Startup root is a reparse point; not traversed.");
            using var iterator = Directory.EnumerateFileSystemEntries(path, "*", new System.IO.EnumerationOptions
                { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0 }).GetEnumerator();
            var visited = 0;
            while (iterator.MoveNext())
            {
                ct.ThrowIfCancellationRequested();
                if (visited++ >= SourceLimit) { source.State = ReviewCollectionState.Partial; source.Detail = $"Предел {SourceLimit} записей каталога."; break; }
                var item = iterator.Current;
                var attr = File.GetAttributes(item);
                if ((attr & FileAttributes.Directory) != 0) continue;
                if (Path.GetFileName(item).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                entries.Add(new StartupReviewEntry { Name = Path.GetFileName(item), Command = item, Scope = scope,
                    Source = source.Name + ((attr & FileAttributes.ReparsePoint) != 0 ? " [ссылка, цель не открывалась]" : " [путь, цель ярлыка не разрешалась]") });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { source.State = entries.Count == 0 ? ReviewCollectionState.Missing : ReviewCollectionState.Partial; source.Detail = "Каталог/запись отсутствует или исчезла во время сбора."; }
        catch (Exception ex) { source.State = entries.Count > 0 ? ReviewCollectionState.Partial : ReviewCollectionState.Unavailable; source.Detail = Describe(ex); }
        return (source, entries);
    }
    private static string Describe(Exception ex) => $"{ex.GetType().Name} (0x{ex.HResult:X8})";
}
