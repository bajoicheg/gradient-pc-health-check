using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace G.PcHealthCheck;

// Reads local owner-PID tables only. No DNS, remote socket connections,
// packet capture, privilege adjustment or endpoint/process modifications.
internal sealed class EndpointWindowsSource : IEndpointSource
{
    public EndpointTable ReadTable(string name, CancellationToken ct)
    {
        if (!EndpointReviewCore.TableNames.Contains(name)) throw new ArgumentException("Unknown endpoint table.", nameof(name));
        ct.ThrowIfCancellationRequested();
        var tcp = name.StartsWith("TCP", StringComparison.Ordinal); var family = name.EndsWith('6') ? 23u : 2u;
        uint Call(IntPtr buffer, ref uint size) => tcp ? GetExtendedTcpTable(buffer, ref size, false, family, 5, 0) : GetExtendedUdpTable(buffer, ref size, false, family, 1, 0);
        uint length = 0; var result = Call(IntPtr.Zero, ref length);
        if (result != 0 && result != 122) throw new Win32Exception(unchecked((int)result));
        for (var attempt = 0; attempt < 4; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (length is < 4 or > 16 * 1024 * 1024) throw new InvalidDataException("Endpoint table exceeds the allowed buffer size.");
            var capacity = length; var pointer = Marshal.AllocHGlobal(checked((int)capacity));
            try
            {
                result = Call(pointer, ref length);
                if (result == 122) continue; // Concurrent endpoint growth; bounded retry, no stale rows.
                if (result != 0) throw new Win32Exception(unchecked((int)result));
                ct.ThrowIfCancellationRequested();
                if (length < 4 || length > capacity) throw new InvalidDataException("Endpoint table returned an invalid length.");
                var bytes = new byte[checked((int)length)]; Marshal.Copy(pointer, bytes, 0, bytes.Length);
                return EndpointReviewCore.Decode(bytes, name);
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }
        throw new IOException("Endpoint table changed size repeatedly; retry collection.");
    }
    public List<EndpointProcess> ReadProcesses(CancellationToken ct, List<string> warnings)
    {
        ct.ThrowIfCancellationRequested();
        var processes = Process.GetProcesses(); var output = new List<EndpointProcess>(); var unavailable = 0;
        try
        {
            if (processes.Length > 8192) warnings.Add("Список процессов ограничен 8192 объектами; часть имён может остаться неизвестной.");
            foreach (var process in processes.Take(8192))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var pid = process.Id;
                    if (pid <= 0) continue;
                    var started = new DateTimeOffset(process.StartTime.ToUniversalTime());
                    var name = process.ProcessName;
                    if (string.IsNullOrWhiteSpace(name)) { unavailable++; continue; }
                    output.Add(new((uint)pid, name, started));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
                { unavailable++; }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        if (unavailable > 0) warnings.Add($"Имя/время запуска не прочитаны для {unavailable} процессов (завершение или ограничение доступа).");
        return output;
    }
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order, uint family, uint tableClass, uint reserved);
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order, uint family, uint tableClass, uint reserved);
}
