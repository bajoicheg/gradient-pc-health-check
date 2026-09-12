using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using NativeFileTime = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace G.PcHealthCheck;

// Query-only diagnostic adapter. RM owns temporary registration/session state.
// No shutdown/restart, process termination, privilege adjustment or file-content access.
internal sealed class FileUseWindowsSource : IFileUseSource
{
    public void CheckFile(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var root = Path.GetPathRoot(path) ?? throw new ArgumentException("Не определён локальный диск.");
        if (new DriveInfo(root).DriveType == DriveType.Network) throw new NotSupportedException("Сетевой диск не поддерживается: локальный Restart Manager не определяет удалённых держателей файла.");
        var current = root; var attributes = File.GetAttributes(current);
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new NotSupportedException("Путь через ссылку не поддерживается.");
        foreach (var segment in path[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested(); current = Path.Combine(current, segment); attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new NotSupportedException("Выберите обычный файл без ссылок или точек повторного анализа в пути.");
        }
        if ((attributes & FileAttributes.Directory) != 0) throw new NotSupportedException("Выбран каталог. Нужен один файл; обход каталога не выполняется.");
    }
    public uint StartSession(out uint handle)
    {
        // DWORD + FILETIME(2 DWORDs), WCHAR[256], WCHAR[64], four DWORDs = 668.
        if (Marshal.SizeOf<NativeProcessInfo>() != 668 || Marshal.OffsetOf<NativeProcessInfo>(nameof(NativeProcessInfo.ApplicationType)).ToInt32() != 652)
            throw new InvalidDataException("Неожиданная разметка структуры Restart Manager.");
        return RmStartSession(out handle, 0, new StringBuilder(33));
    }
    public uint RegisterFile(uint handle, string path) => RmRegisterResources(handle, 1, [path], 0, IntPtr.Zero, 0, IntPtr.Zero);
    public FileUseBatch ReadList(uint handle, int capacity)
    {
        if (capacity is < 0 or > FileUseService.MaxProcesses) throw new ArgumentOutOfRangeException(nameof(capacity));
        var buffer = capacity == 0 ? null : new NativeProcessInfo[capacity];
        var count = (uint)capacity;
        var code = RmGetList(handle, out var needed, ref count, buffer, out var reasons);
        if (code != 0) return new(code, needed, count, reasons, []);
        if (count > capacity) throw new InvalidDataException("Restart Manager сообщил больше записей, чем в буфере.");
        var rows = new List<FileUseProcess>();
        for (var i = 0; i < count; i++)
        {
            var r = buffer![i];
            rows.Add(new FileUseProcess
            {
                Pid = r.Process.Pid, StartFileTime = FileTime(r.Process.StartTime),
                ApplicationName = r.ApplicationName ?? "", ServiceName = r.ServiceName ?? "",
                ApplicationType = r.ApplicationType, ApplicationStatus = r.ApplicationStatus,
                SessionId = r.SessionId, Restartable = r.Restartable
            });
        }
        return new(0, needed, count, reasons, rows);
    }
    public uint EndSession(uint handle) => RmEndSession(handle);
    public FileUseIdentity? ReadIdentity(uint pid)
    {
        const uint QueryLimitedInformation = 0x1000;
        using var process = OpenProcess(QueryLimitedInformation, false, pid);
        if (process.IsInvalid) return new(pid, 0, "", Marshal.GetLastWin32Error());
        if (!GetProcessTimes(process, out var created, out _, out _, out _)) return new(pid, 0, "", Marshal.GetLastWin32Error());
        var text = new StringBuilder(32768); var length = (uint)text.Capacity;
        if (!QueryFullProcessImageNameW(process, 0, text, ref length)) return new(pid, FileTime(created), "", Marshal.GetLastWin32Error());
        // Both fields belong to this query-only handle, not two independent PID lookups.
        return new(pid, FileTime(created), text.ToString());
    }
    private static ulong FileTime(NativeFileTime value) => ((ulong)(uint)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeUniqueProcess { public uint Pid; public NativeFileTime StartTime; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeProcessInfo
    {
        public NativeUniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string? ApplicationName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? ServiceName;
        public uint ApplicationType;
        public uint ApplicationStatus;
        public uint SessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint RmStartSession(out uint handle, uint flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint RmRegisterResources(uint handle, uint files, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] paths, uint applications, IntPtr processes, uint services, IntPtr names);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint RmGetList(uint handle, out uint needed, ref uint count, [In, Out] NativeProcessInfo[]? processes, out uint reasons);
    [DllImport("rstrtmgr.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint RmEndSession(uint handle);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out NativeFileTime creation, out NativeFileTime exit, out NativeFileTime kernel, out NativeFileTime user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);
}
