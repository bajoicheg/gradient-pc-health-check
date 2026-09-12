using System.Diagnostics;

namespace G.PcHealthCheck;

// Read-only aggregation. The source supplies metadata; this service never opens or deletes files.
internal static class FolderUsageService
{
    public static string Validate(string root, FolderUsageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxEntries is < 1 or > 1000000 || options.MaxDirectories is < 1 or > 100000
            || options.MaxSeconds is < 1 or > 600 || options.TopFiles is < 1 or > 1000)
            throw new ArgumentException("Недопустимые ограничения анализа папок.", nameof(options));
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)
            || root.StartsWith(@"\\?\", StringComparison.Ordinal) || root.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new ArgumentException("Укажите полный путь к папке, не относительный путь или путь устройства.", nameof(root));
        return Canonical(root);
    }

    public static FolderUsageSnapshot Collect(string root, FolderUsageOptions options, IFolderUsageSource source,
        CancellationToken ct, IProgress<string>? progress = null, Func<long>? elapsedMs = null)
    {
        root = Validate(root, options);
        ArgumentNullException.ThrowIfNull(source);
        var watch = Stopwatch.StartNew();
        elapsedMs ??= () => watch.ElapsedMilliseconds;
        var result = new FolderUsageSnapshot { Root = root, Options = options, Outcome = "Running" };
        result.Folders.Add(new() { Path = root });
        var pending = new Stack<int>(); pending.Push(0);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var largest = new SortedSet<LargeFolderFile>(Comparer<LargeFolderFile>.Create((a, b) =>
        {
            var size = b.Bytes.CompareTo(a.Bytes);
            return size != 0 ? size : StringComparer.Ordinal.Compare(a.Path, b.Path);
        }));
        void Warn(string message) { if (result.Warnings.Count < 100) result.Warnings.Add(message); }
        void Error(FolderUsageRow row, string message)
        { result.Errors++; row.Incomplete = true; Warn(message); }
        void Budget()
        {
            ct.ThrowIfCancellationRequested();
            if (elapsedMs() >= options.MaxSeconds * 1000L) throw new FolderLimitException("Достигнут лимит времени обхода.");
        }
        try
        {
            ct.ThrowIfCancellationRequested();
            // Check initial ancestors as well as each visited directory. These metadata
            // checks are not an atomic filesystem snapshot or a concurrent replacement guarantee.
            for (string? ancestor = root; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            {
                Budget();
                var attributes = source.GetAttributes(ancestor);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Папка или её предок является ссылкой. Выберите обычную папку назначения непосредственно.");
                if ((attributes & FileAttributes.Directory) == 0) throw new IOException("Выбранный путь не является папкой.");
            }
            while (pending.Count > 0)
            {
                Budget();
                var index = pending.Pop(); var row = result.Folders[index];
                try
                {
                    var attributes = source.GetAttributes(row.Path);
                    Budget();
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    { result.SkippedLinks++; Warn("Пропущена ссылка после повторной проверки: " + row.Path); continue; }
                    if ((attributes & FileAttributes.Directory) == 0) throw new IOException("Папка исчезла или была заменена.");
                    row.Incomplete = false;
                    foreach (var entry in source.ReadDirectory(row.Path))
                    {
                        Budget();
                        if (result.EntriesVisited >= options.MaxEntries) throw new FolderLimitException("Достигнут лимит числа записей.");
                        result.EntriesVisited++;
                        if ((result.EntriesVisited & 1023) == 0)
                            progress?.Report($"Просмотрено {result.EntriesVisited:N0} записей; папок {result.Folders.Count:N0}…");
                        if (!string.IsNullOrWhiteSpace(entry.Error)) { Error(row, entry.Path + ": " + entry.Error); continue; }
                        string path;
                        try
                        {
                            if (!Path.IsPathFullyQualified(entry.Path)) throw new IOException("Относительный путь от поставщика данных.");
                            path = Canonical(entry.Path);
                            if (!string.Equals(Path.GetDirectoryName(path), row.Path, StringComparison.OrdinalIgnoreCase))
                                throw new IOException("Запись не является непосредственным потомком проверяемой папки.");
                            if (!seen.Add(path)) throw new IOException("Повторная или неоднозначная запись пути.");
                        }
                        catch (Exception ex) { Error(row, entry.Path + ": " + ex.Message); continue; }
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        { result.SkippedLinks++; row.Incomplete = true; Warn("Пропущена ссылка: " + path); continue; }
                        if ((entry.Attributes & FileAttributes.Directory) != 0)
                        {
                            if (result.Folders.Count >= options.MaxDirectories) throw new FolderLimitException("Достигнут лимит числа папок.");
                            result.Folders.Add(new() { Path = path, ParentIndex = index }); pending.Push(result.Folders.Count - 1);
                        }
                        else if (entry.Bytes is long bytes && bytes >= 0)
                        {
                            row.OwnBytes += bytes; row.OwnFiles++;
                            largest.Add(new(path, bytes, entry.Modified));
                            if (largest.Count > options.TopFiles) largest.Remove(largest.Max!);
                        }
                        else Error(row, path + ": размер файла недоступен или некорректен.");
                    }
                    // The final MoveNext may itself be delayed or cancelled even for an empty folder.
                    Budget();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { row.Incomplete = true; throw; }
                catch (FolderLimitException) { row.Incomplete = true; throw; }
                catch (Exception ex) { Error(row, row.Path + $": {ex.GetType().Name}, 0x{ex.HResult:X8}."); }
            }
            ct.ThrowIfCancellationRequested(); result.Outcome = "Completed";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { result.Outcome = "Stopped"; result.Folders[0].Incomplete = true; Warn("Обход остановлен. Обработанные записи сохранены; объём неполный."); }
        catch (FolderLimitException ex)
        { result.Outcome = "Partial"; result.Folders[0].Incomplete = true; Warn(ex.Message + " Объём неполный."); }
        catch (Exception ex)
        { result.Outcome = "Failed"; Error(result.Folders[0], "Не удалось начать обход: " + ex.Message); }
        // Each directly contained file contributes once. Nested aggregate rows overlap.
        for (var i = result.Folders.Count - 1; i >= 0; i--)
        {
            var row = result.Folders[i]; row.Bytes += row.OwnBytes; row.Files += row.OwnFiles;
            if (row.ParentIndex < 0) continue;
            var parent = result.Folders[row.ParentIndex];
            parent.Bytes += row.Bytes; parent.Files += row.Files; parent.Incomplete |= row.Incomplete;
        }
        if (result.Outcome == "Completed" && (result.Folders[0].Incomplete || result.Errors > 0 || result.SkippedLinks > 0)) result.Outcome = "Partial";
        result.LargestFiles = largest.ToList(); result.FinishedAt = DateTimeOffset.Now;
        if (result.Warnings.Count == 100) result.Warnings.Add("Показаны первые 100 предупреждений; полные счётчики пропусков сохранены.");
        return result;
    }
    private static string Canonical(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private sealed class FolderLimitException(string message) : Exception(message);
}
