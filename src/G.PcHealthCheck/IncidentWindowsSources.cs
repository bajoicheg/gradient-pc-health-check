using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Management;

namespace G.PcHealthCheck;

internal sealed class WindowsIncidentEventSource : IIncidentEventSource
{
    public IEnumerable<IncidentEvent> Read(string log, IncidentWindow window, CancellationToken ct)
    {
        if (log is not ("Application" or "System")) throw new ArgumentException("Разрешены только локальные Application и System.", nameof(log));
        IncidentQueries.Validate(window); ct.ThrowIfCancellationRequested();
        var query = new EventLogQuery(log, PathType.LogName, IncidentQueries.XPath(window)) { ReverseDirection = true, TolerateQueryErrors = false };
        using var reader = new EventLogReader(query) { BatchSize = 16 };
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using var record = reader.ReadEvent(TimeSpan.FromSeconds(1));
            if (record is null) yield break;
            ct.ThrowIfCancellationRequested();
            var item = new IncidentEvent { Log = log, RecordId = record.RecordId, EventId = record.Id, Level = record.Level,
                Timestamp = record.TimeCreated is DateTime time ? new DateTimeOffset(time) : null,
                Provider = record.ProviderName ?? "", EmitterPid = record.ProcessId };
            try
            {
                var text = record.FormatDescription();
                if (text is null) { item.MessageState = "Unavailable"; item.Message = "Поставщик не вернул текст сообщения."; }
                else if (text.Length > 16384) { item.MessageState = "Truncated"; item.Message = text[..(char.IsHighSurrogate(text[16383]) ? 16383 : 16384)]; }
                else item.Message = text;
            }
            catch (Exception ex) { item.MessageState = "Unavailable"; item.Message = "Не удалось форматировать текст: " + IncidentQueries.Error(ex); }
            ct.ThrowIfCancellationRequested(); yield return item;
        }
    }
}

internal sealed class WindowsProcessReviewSource : IProcessReviewSource
{
    private const string Fields = "ProcessId,ParentProcessId,CreationDate,Name,ExecutablePath,CommandLine,SessionId,ThreadCount,HandleCount,WorkingSetSize";
    public IEnumerable<ProcessReviewEntry> Read(CancellationToken ct) => Query("SELECT " + Fields + " FROM Win32_Process", ct);
    public ProcessReviewEntry? Lookup(uint pid, CancellationToken ct)
    {
        var rows = Query("SELECT " + Fields + " FROM Win32_Process WHERE ProcessId=" + pid.ToString(CultureInfo.InvariantCulture), ct).Take(2).ToArray();
        return rows.Length == 1 ? rows[0] : null;
    }
    public string ReadOwner(uint pid, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var process = new ManagementObject(new ManagementScope("root\\CIMV2"),
            new ManagementPath("Win32_Process.Handle=\"" + pid.ToString(CultureInfo.InvariantCulture) + "\""), new ObjectGetOptions { Timeout = TimeSpan.FromSeconds(5) });
        // Only the read-only GetOwner method is used; no arbitrary method name or input.
        using var result = process.InvokeMethod("GetOwner", null, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(5) });
        ct.ThrowIfCancellationRequested();
        if (result is null || result["ReturnValue"] is null) throw new InvalidOperationException("GetOwner не вернул результат.");
        var code = Convert.ToUInt32(result["ReturnValue"], CultureInfo.InvariantCulture);
        if (code != 0) throw new InvalidOperationException("GetOwner: код " + code.ToString(CultureInfo.InvariantCulture));
        var user = Convert.ToString(result["User"], CultureInfo.InvariantCulture) ?? "";
        var domain = Convert.ToString(result["Domain"], CultureInfo.InvariantCulture) ?? "";
        return user.Length == 0 ? "" : domain.Length == 0 ? user : domain + "\\" + user;
    }
    private static IEnumerable<ProcessReviewEntry> Query(string query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var options = new System.Management.EnumerationOptions { ReturnImmediately = true, Rewindable = false, BlockSize = 16, Timeout = TimeSpan.FromSeconds(5) };
        using var searcher = new ManagementObjectSearcher(new ManagementScope("root\\CIMV2"), new ObjectQuery(query), options);
        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                ct.ThrowIfCancellationRequested();
                var entry = new ProcessReviewEntry();
                var pid = Number<uint>(item, "ProcessId", entry.Warnings, Convert.ToUInt32);
                if (pid is null) throw new InvalidOperationException("WMI вернул процесс без PID.");
                entry.Pid = pid.Value; entry.ParentPid = Number<uint>(item, "ParentProcessId", entry.Warnings, Convert.ToUInt32);
                entry.Name = Text(item, "Name", entry.Warnings); entry.Executable = Text(item, "ExecutablePath", entry.Warnings);
                entry.CommandLine = Text(item, "CommandLine", entry.Warnings); entry.CreationKey = Text(item, "CreationDate", entry.Warnings);
                if (entry.CreationKey.Length > 0)
                {
                    try { entry.CreatedAt = new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(entry.CreationKey)); }
                    catch (Exception ex) { entry.CreationKey = ""; entry.Warnings.Add("Время создания: " + IncidentQueries.Error(ex)); }
                }
                entry.SessionId = Number<uint>(item, "SessionId", entry.Warnings, Convert.ToUInt32);
                entry.Threads = Number<uint>(item, "ThreadCount", entry.Warnings, Convert.ToUInt32);
                entry.Handles = Number<uint>(item, "HandleCount", entry.Warnings, Convert.ToUInt32);
                entry.WorkingSetBytes = Number<ulong>(item, "WorkingSetSize", entry.Warnings, Convert.ToUInt64);
                yield return entry;
            }
        }
    }
    private static string Text(ManagementBaseObject item, string name, List<string> warnings)
    {
        try
        {
            var value = item[name]; if (value is null) { warnings.Add(name + ": не предоставлено WMI (возможны ограничения доступа)."); return ""; }
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            if (text.Length <= 32768) return text;
            warnings.Add(name + ": текст сокращён до 32768 символов."); return text[..(char.IsHighSurrogate(text[32767]) ? 32767 : 32768)];
        }
        catch (Exception ex) { warnings.Add(name + ": " + IncidentQueries.Error(ex)); return ""; }
    }
    private static T? Number<T>(ManagementBaseObject item, string name, List<string> warnings, Func<object, T> convert) where T : struct
    {
        try { var value = item[name]; if (value is not null) return convert(value); warnings.Add(name + ": не предоставлено WMI."); }
        catch (Exception ex) { warnings.Add(name + ": " + IncidentQueries.Error(ex)); }
        return null;
    }
}
