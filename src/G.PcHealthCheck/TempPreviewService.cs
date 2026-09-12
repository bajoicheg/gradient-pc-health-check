using System.Diagnostics;

namespace G.PcHealthCheck;

/// <summary>Metadata-only preview. No file contents, deletes, attribute changes or commands.</summary>
internal static class TempPreviewService
{
    private static readonly IComparer<TempCandidate> LargestFirst = Comparer<TempCandidate>.Create((a, b) =>
    {
        var size = b.Bytes.CompareTo(a.Bytes);
        if (size != 0) return size;
        var path = StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path);
        return path != 0 ? path : StringComparer.Ordinal.Compare(a.Path, b.Path);
    });

    public static TempPreviewSnapshot CollectUser(int days, CancellationToken ct, IProgress<string>? progress = null)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Report("Определяю пользователя и профиль текущего сеанса…");
        var context = ExecutionContextService.Capture();
        return CollectForContext(days, context, ct, progress);
    }

    internal static TempPreviewSnapshot CollectForContext(int days, ExecutionContextInfo context, CancellationToken ct, IProgress<string>? progress)
    {
        if (days is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(days));
        ct.ThrowIfCancellationRequested();
        var root = ExecutionPolicy.PreviewRoot(context);
        var result = root is null
            ? Unavailable(days, DateTime.Now, ExecutionPolicy.For("TempPreview", context).Reason)
            : Scan(root, days, DateTime.Now, ct, progress: progress);
        result.ExecutionContext = context;
        result.TargetAccount = context.SessionAccount;
        result.TargetSid = context.SessionSid;
        result.CleanupAvailability = ExecutionPolicy.For("CleanTemp", context).State;
        return result;
    }

    private static TempPreviewSnapshot Unavailable(int days, DateTime now, string reason) => new()
    {
        OlderThanDays = days, StartedAt = now, CompletedAt = now, Cutoff = now.AddDays(-days),
        State = ReviewCollectionState.Unavailable, Issues = [reason]
    };

    public static TempPreviewSnapshot Scan(string root, int olderThanDays, DateTime now,
        CancellationToken ct = default, int maxEntries = 100000, int maxRows = 200, TimeSpan? timeLimit = null,
        IProgress<string>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Нужен полный путь к Temp.", nameof(root));
        if (olderThanDays is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(olderThanDays));
        if (maxEntries is < 1 or > 1000000) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (maxRows is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(maxRows));
        var limit = timeLimit ?? TimeSpan.FromSeconds(30);
        if (limit < TimeSpan.Zero || limit > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(timeLimit));
        ct.ThrowIfCancellationRequested();
        var result = new TempPreviewSnapshot
        {
            Root = Path.GetFullPath(root), OlderThanDays = olderThanDays, StartedAt = now,
            Cutoff = now.AddDays(-olderThanDays), MaxEntries = maxEntries, MaxRows = maxRows,
            TimeLimitSeconds = limit.TotalSeconds, State = ReviewCollectionState.Complete
        };
        var timer = Stopwatch.StartNew();
        void Issue(string text)
        {
            if (result.Issues.Count < 100) result.Issues.Add(text); else result.OmittedIssues++;
        }
        bool TimeExpired()
        {
            ct.ThrowIfCancellationRequested();
            if (timer.Elapsed < limit) return false;
            result.State = ReviewCollectionState.Partial;
            Issue($"Достигнуто ограничение времени {limit.TotalSeconds:0.#} с. Итоги относятся только к просмотренным записям.");
            return true;
        }
        TempPreviewSnapshot Finish() { ct.ThrowIfCancellationRequested(); result.CompletedAt = DateTime.Now; return result; }
        try
        {
            var attributes = File.GetAttributes(result.Root);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
            {
                result.State = ReviewCollectionState.Unavailable;
                Issue("Корень не является обычным каталогом или является ссылкой/reparse point; обход не начат.");
                return Finish();
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            result.State = ReviewCollectionState.Missing; Issue("Каталог Temp отсутствует. Оценка очистки не требуется."); return Finish();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            result.State = ReviewCollectionState.Unavailable; result.Errors++;
            Issue("Корень недоступен: " + Describe(ex)); return Finish();
        }

        var stack = new Stack<string>(); stack.Push(result.Root);
        while (stack.Count > 0)
        {
            if (TimeExpired()) return Finish();
            var directory = stack.Pop();
            try
            {
                // Read-only observation, not a filesystem transaction or a deletion plan.
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { result.SkippedLinks++; continue; }
                using var iterator = Directory.EnumerateFileSystemEntries(directory, "*", new System.IO.EnumerationOptions
                { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false }).GetEnumerator();
                while (true)
                {
                    if (TimeExpired()) return Finish();
                    if (!iterator.MoveNext()) break;
                    if (result.VisitedEntries >= maxEntries)
                    {
                        result.State = ReviewCollectionState.Partial;
                        Issue($"Достигнут предел {maxEntries} записей. Объём является частичной оценкой.");
                        return Finish();
                    }
                    result.VisitedEntries++;
                    if (result.VisitedEntries % 500 == 0)
                        progress?.Report($"Просмотрено записей: {result.VisitedEntries:N0}; кандидатов: {result.CandidateFiles:N0}…");
                    var path = iterator.Current;
                    try
                    {
                        var attr = File.GetAttributes(path);
                        if ((attr & FileAttributes.ReparsePoint) != 0) { result.SkippedLinks++; continue; }
                        if ((attr & FileAttributes.Directory) != 0) { stack.Push(path); continue; }
                        var info = new FileInfo(path);
                        var modified = info.LastWriteTime;
                        if (modified >= result.Cutoff) continue;
                        var bytes = info.Length;
                        result.CandidateBytes = checked(result.CandidateBytes + bytes);
                        result.CandidateFiles++;
                        var row = new TempCandidate { Path = path, Bytes = bytes, LastWriteTime = modified };
                        var index = result.LargestFiles.BinarySearch(row, LargestFirst);
                        if (index < 0) index = ~index;
                        if (index < maxRows)
                        {
                            result.LargestFiles.Insert(index, row);
                            if (result.LargestFiles.Count > maxRows) result.LargestFiles.RemoveAt(maxRows);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or OverflowException)
                    {
                        result.Errors++; result.State = ReviewCollectionState.Partial; Issue(path + ": " + Describe(ex));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                result.Errors++; result.State = ReviewCollectionState.Partial; Issue(directory + ": " + Describe(ex));
            }
        }
        return Finish();
    }
    private static string Describe(Exception ex) => $"{ex.GetType().Name} (0x{ex.HResult:X8})";
}
