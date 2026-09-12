using System.Diagnostics;

namespace G.PcHealthCheck;

internal static class PerformanceValues
{
    public static void Validate(PerformanceSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.DurationSeconds is < 1 or > 600 || options.IntervalSeconds is < 1 or > 10 || options.IntervalSeconds > options.DurationSeconds)
            throw new ArgumentException("Длительность: 1–600 секунд; интервал: 1–10 секунд, не больше длительности.", nameof(options));
    }

    public static double? Cpu(CpuTimeCounters? before, CpuTimeCounters after)
    {
        ArgumentNullException.ThrowIfNull(after);
        if (before is null || after.Idle < before.Idle || after.Kernel < before.Kernel || after.User < before.User) return null;
        // Kernel already includes idle. Convert deltas separately to avoid ulong addition overflow.
        var total = (double)(after.Kernel - before.Kernel) + (after.User - before.User);
        var idle = (double)(after.Idle - before.Idle);
        return total > 0 && idle <= total ? 100 * (total - idle) / total : null;
    }

    public static double? Memory(ulong total, ulong available)
        => total > 0 && available <= total ? 100d * (total - available) / total : null;

    public static PerformanceReading Normalize(PerformanceReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var warnings = reading.Warnings.ToList();
        double? Check(double? value, string name, bool percentage)
        {
            if (Valid(value, percentage)) return value;
            warnings.Add(name + ": значение недоступно или некорректно.");
            return null;
        }
        var cpu = Check(reading.CpuPercent, "CPU", true);
        var memory = Check(reading.MemoryUsedPercent, "RAM", true);
        var disk = Check(reading.DiskBusyPercent, "Занятость дисков", true);
        var queue = Check(reading.DiskQueueLength, "Очередь дисков", false);
        return new(cpu, memory, disk, queue, warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    public static double? Value(PerformanceReading reading, SessionMetric metric)
    {
        var value = metric switch
        {
            SessionMetric.Cpu => reading.CpuPercent,
            SessionMetric.Memory => reading.MemoryUsedPercent,
            SessionMetric.DiskBusy => reading.DiskBusyPercent,
            SessionMetric.DiskQueue => reading.DiskQueueLength,
            _ => throw new ArgumentOutOfRangeException(nameof(metric))
        };
        return Valid(value, metric != SessionMetric.DiskQueue) ? value : null;
    }

    private static bool Valid(double? value, bool percentage)
        => value is double v && double.IsFinite(v) && v >= 0 && (!percentage || v <= 100);
}

internal sealed class PerformanceSessionService
{
    // Caller supplies a fresh session clock and runs this service away from the GUI thread.
    public async Task<PerformanceSessionSnapshot> RunAsync(PerformanceSessionOptions options, IPerformanceSessionSource source,
        IPerformanceSessionClock clock, IProgress<PerformanceSample>? progress, CancellationToken ct)
    {
        PerformanceValues.Validate(options);
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(clock);
        var result = new PerformanceSessionSnapshot { Options = options, StartedAt = clock.Now.AddMilliseconds(-clock.ElapsedMs), Outcome = "Running" };
        var interval = options.IntervalSeconds * 1000L;
        var duration = options.DurationSeconds * 1000L;
        var next = interval;
        try
        {
            ct.ThrowIfCancellationRequested();
            while (next <= duration)
            {
                var remaining = next - clock.ElapsedMs;
                if (remaining > 0) await clock.DelayAsync((int)remaining, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (clock.ElapsedMs > duration)
                {
                    result.MissedSlots += (int)((duration - next) / interval + 1);
                    break;
                }
                var started = clock.ElapsedMs;
                PerformanceReading reading;
                try { reading = source.Read(ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    reading = new(null, null, null, null, [$"Сбор не выполнен: {ex.GetType().Name}, 0x{ex.HResult:X8}."]);
                }
                ct.ThrowIfCancellationRequested();
                var sample = new PerformanceSample(clock.ElapsedMs, Math.Max(0, clock.ElapsedMs - started), clock.Now, PerformanceValues.Normalize(reading));
                result.Samples.Add(sample);
                progress?.Report(sample);
                next += interval;
                // Skip elapsed slots: never overlap reads or launch a catch-up burst.
                while (next <= duration && next < clock.ElapsedMs) { result.MissedSlots++; next += interval; }
            }
            if (clock.ElapsedMs < duration) await clock.DelayAsync((int)(duration - clock.ElapsedMs), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            result.Outcome = "Completed";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            result.Outcome = "Stopped";
            result.Warnings.Add("Сеанс остановлен. Завершённые измерения сохранены; незавершённое измерение не добавлено.");
        }
        catch (Exception ex)
        {
            result.Outcome = "Failed";
            result.Warnings.Add($"Сеанс прерван: {ex.GetType().Name}, 0x{ex.HResult:X8}. Завершённые измерения сохранены.");
        }
        result.ElapsedMs = clock.ElapsedMs;
        result.FinishedAt = clock.Now;
        if (result.MissedSlots > 0) result.Warnings.Add($"Пропущено запланированных замеров из-за задержки: {result.MissedSlots}.");
        if (result.ElapsedMs > duration + 50) result.Warnings.Add("Фактическая длительность превысила заданную: системный вызов или планировщик завершился позже. Смотрите реальные временные отметки.");
        return result;
    }
}

internal sealed class MonotonicPerformanceClock : IPerformanceSessionClock
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    public long ElapsedMs => _watch.ElapsedMilliseconds;
    public DateTimeOffset Now => DateTimeOffset.Now;
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) => Task.Delay(milliseconds, cancellationToken);
}

internal static class PerformanceStatistics
{
    public static PerformanceMetricStats For(PerformanceSessionSnapshot snapshot, SessionMetric metric)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var values = snapshot.Samples.Select(x => PerformanceValues.Value(x.Reading, metric)).Where(x => x.HasValue).Select(x => x!.Value).Order().ToArray();
        var n = values.Length;
        if (n == 0) return new(metric, snapshot.Samples.Count, 0, null, null, null, null, 0);
        var median = n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2;
        return new(metric, snapshot.Samples.Count, n, values[0], median, values[(int)Math.Ceiling(n * .95) - 1], values[^1], values.Count(x => x >= Threshold(metric)));
    }

    public static double Threshold(SessionMetric metric) => metric switch { SessionMetric.Cpu or SessionMetric.Memory => 85, SessionMetric.DiskBusy => 80, SessionMetric.DiskQueue => 2, _ => throw new ArgumentOutOfRangeException(nameof(metric)) };

    public static List<List<PerformancePoint>> Segments(PerformanceSessionSnapshot snapshot, SessionMetric metric)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var result = new List<List<PerformancePoint>>();
        List<PerformancePoint>? current = null;
        long? previous = null;
        foreach (var sample in snapshot.Samples)
        {
            var value = PerformanceValues.Value(sample.Reading, metric);
            if (value is null || sample.OffsetMs < 0) { current = null; previous = null; continue; }
            if (previous is long last && (sample.OffsetMs <= last || sample.OffsetMs - last > snapshot.Options.IntervalSeconds * 1750L)) current = null;
            if (current is null) { current = []; result.Add(current); }
            current.Add(new(sample.OffsetMs, value.Value));
            previous = sample.OffsetMs;
        }
        return result;
    }

    public static bool AddMarker(List<PerformanceMarker> markers, long offsetMs, string note)
    {
        ArgumentNullException.ThrowIfNull(markers);
        if (offsetMs < 0 || markers.Count >= 100 || string.IsNullOrWhiteSpace(note) || note.Trim().Length > 160) return false;
        markers.Add(new(offsetMs, note.Trim()));
        return true;
    }

    public static string Completeness(PerformanceSessionSnapshot snapshot)
    {
        if (snapshot.Samples.Count == 0 || Enum.GetValues<SessionMetric>().All(m => For(snapshot, m).Valid == 0)) return "Нет доступных измерений";
        var full = snapshot.Outcome == "Completed" && snapshot.MissedSlots == 0 && snapshot.Samples.Count == snapshot.Options.DurationSeconds / snapshot.Options.IntervalSeconds
            && Enum.GetValues<SessionMetric>().All(m => For(snapshot, m).Valid == snapshot.Samples.Count);
        return full ? "Все запланированные замеры получены" : "Неполные данные — проверьте пропуски и предупреждения";
    }
}
