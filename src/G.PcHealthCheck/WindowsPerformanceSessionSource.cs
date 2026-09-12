using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace G.PcHealthCheck;

internal sealed class WindowsPerformanceSessionSource : IPerformanceSessionSource
{
    private CpuTimeCounters? _previous;
    private readonly bool _singleGroup;
    private bool _disposed;

    public WindowsPerformanceSessionSource()
    {
        _singleGroup = GetActiveProcessorGroupCount() == 1;
        if (_singleGroup && GetSystemTimes(out var idle, out var kernel, out var user)) _previous = new(idle.Value, kernel.Value, user.Value);
    }

    public PerformanceReading Read(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var warnings = new List<string>();
        double? cpu = null, memory = null, busy = null, queue = null;
        if (!_singleGroup)
            warnings.Add("CPU: одна группа процессоров не подтверждена; GetSystemTimes не выдаётся за измерение всей многогрупповой системы.");
        else if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var current = new CpuTimeCounters(idle.Value, kernel.Value, user.Value);
            cpu = PerformanceValues.Cpu(_previous, current); _previous = current;
            if (cpu is null) warnings.Add("CPU: недостаточно двух корректных отсчётов счётчика; требуется следующий замер.");
        }
        else { _previous = null; warnings.Add($"CPU: GetSystemTimes, Win32 {Marshal.GetLastWin32Error()}."); }

        cancellationToken.ThrowIfCancellationRequested();
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref status)) memory = PerformanceValues.Memory(status.TotalPhys, status.AvailPhys);
        else warnings.Add($"RAM: GlobalMemoryStatusEx, Win32 {Marshal.GetLastWin32Error()}.");

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var scope = new ManagementScope("root\\CIMV2", new ConnectionOptions { Timeout = TimeSpan.FromSeconds(2) });
            var query = new ObjectQuery("SELECT PercentIdleTime,CurrentDiskQueueLength FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'");
            var options = new System.Management.EnumerationOptions { ReturnImmediately = true, Rewindable = false, BlockSize = 1, Timeout = TimeSpan.FromSeconds(2) };
            using var searcher = new ManagementObjectSearcher(scope, query, options);
            using var results = searcher.Get();
            var count = 0;
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++count > 1) { busy = null; queue = null; warnings.Add("Диски: неоднозначный экземпляр _Total."); break; }
                    var idlePercent = Number(item["PercentIdleTime"]);
                    if (idlePercent is >= 0 and <= 100) busy = 100 - idlePercent.Value;
                    queue = Number(item["CurrentDiskQueueLength"]);
                }
            }
            if (count == 0) warnings.Add("Диски: счётчики PhysicalDisk _Total не получены.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { warnings.Add($"Диски: {ex.GetType().Name}, 0x{ex.HResult:X8}."); }
        cancellationToken.ThrowIfCancellationRequested();
        return PerformanceValues.Normalize(new(cpu, memory, busy, queue, warnings));
    }

    public void Dispose() { _disposed = true; }
    private static double? Number(object? value)
    {
        if (value is null || value == DBNull.Value) return null;
        try { var n = Convert.ToDouble(value, CultureInfo.InvariantCulture); return double.IsFinite(n) ? n : null; }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern ushort GetActiveProcessorGroupCount();
}
