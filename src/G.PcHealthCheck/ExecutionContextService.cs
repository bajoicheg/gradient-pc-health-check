using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace G.PcHealthCheck;

// Query-only context discovery. No LogonUser, impersonation, profile loading,
// privilege adjustment, ACL changes or physical-console fallback.
internal static class ExecutionContextService
{
    public static ExecutionContextInfo Capture()
    {
        var warnings = new List<string>();
        string account = "", sid = "", profile = "", sessionAccount = "", sessionSid = "", sessionProfile = "", source = "";
        bool? admin = null, member = null, elevated = null; int? type = null; var session = -1;
        try
        {
            using var process = Process.GetCurrentProcess(); session = process.SessionId;
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            account = identity.Name; sid = identity.User?.Value ?? "";
            try { admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
            catch (Exception ex) { warnings.Add(Error("Активные права", ex)); }
            try { type = TokenInt(identity.AccessToken, 18); }
            catch (Exception ex) { warnings.Add(Error("Тип токена", ex)); }
            try { elevated = TokenInt(identity.AccessToken, 20) != 0; }
            catch (Exception ex) { warnings.Add(Error("Повышение", ex)); }
            try { member = HasAdministratorsSid(identity.AccessToken); }
            catch (Exception ex) { warnings.Add(Error("Группы токена", ex)); }
            try { profile = ProfileFromToken(identity.AccessToken); }
            catch (Exception ex) { warnings.Add(Error("Профиль процесса", ex)); }
        }
        catch (Exception ex) { warnings.Add(Error("Идентификатор процесса", ex)); }
        try
        {
            if (session <= 0) throw new InvalidOperationException("Интерактивный сеанс отсутствует.");
            var user = SessionText(session, 5); var domain = SessionText(session, 7);
            if (string.IsNullOrWhiteSpace(user)) throw new InvalidOperationException("Пользователь текущего сеанса не найден.");
            sessionAccount = string.IsNullOrWhiteSpace(domain) ? user : domain + "\\" + user;
            sessionSid = ((SecurityIdentifier)new NTAccount(sessionAccount).Translate(typeof(SecurityIdentifier))).Value;
            if (sessionSid == sid && !string.IsNullOrWhiteSpace(profile)) { sessionProfile = profile; source = "Токен: SID процесса совпадает с SID пользователя WTS-сеанса"; }
            else { sessionProfile = ProfileFromRegistry(sessionSid); source = "ProfileList по SID пользователя текущего WTS-сеанса"; }
            if (ExecutionPolicy.NormalizeProfile(sessionProfile) is null) throw new InvalidOperationException("Профиль сеанса не разрешён однозначно.");
        }
        catch (Exception ex) { sessionProfile = ""; source = "Не определён"; warnings.Add(Error("Пользователь/профиль текущего сеанса", ex)); }
        return new()
        {
            ProcessAccount = account, ProcessSid = sid, ProcessProfile = profile, SessionId = session,
            AdministratorMember = member, HasAdministratorToken = admin, IsElevated = elevated, ElevationType = type,
            SessionAccount = sessionAccount, SessionSid = sessionSid, SessionProfile = sessionProfile, ProfileSource = source, Warnings = warnings
        };
    }
    private static int TokenInt(SafeAccessTokenHandle token, int kind)
    {
        if (!GetTokenInformation(token, kind, out int value, sizeof(int), out var returned) || returned != sizeof(int))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return value;
    }
    private static bool HasAdministratorsSid(SafeAccessTokenHandle token)
    {
        GetTokenInformationBuffer(token, 2, IntPtr.Zero, 0, out var length);
        if (length < 4 || length > 1048576) throw new InvalidOperationException("Недопустимый размер TokenGroups.");
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformationBuffer(token, 2, buffer, length, out var returned)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var count = Marshal.ReadInt32(buffer); var offset = Marshal.OffsetOf<TokenGroups>(nameof(TokenGroups.First)).ToInt32();
            var stride = Marshal.SizeOf<SidAndAttributes>();
            if (count < 0 || count > (returned - offset) / stride) throw new InvalidOperationException("Неполные данные TokenGroups.");
            for (var i = 0; i < count; i++)
            {
                var group = Marshal.PtrToStructure<SidAndAttributes>(IntPtr.Add(buffer, offset + i * stride));
                if (group.Sid != IntPtr.Zero && new SecurityIdentifier(group.Sid).IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)) return true;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static string ProfileFromToken(SafeAccessTokenHandle token)
    {
        uint length = 0; GetUserProfileDirectory(token, null, ref length);
        if (length is < 2 or > 32768) throw new Win32Exception(Marshal.GetLastWin32Error());
        var text = new StringBuilder((int)length);
        if (!GetUserProfileDirectory(token, text, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return ExecutionPolicy.NormalizeProfile(text.ToString()) ?? throw new InvalidOperationException("Путь профиля не определён.");
    }
    private static string ProfileFromRegistry(string sid)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid, false);
        var path = key?.GetValue("ProfileImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        // Expand only machine-derived variables; never substitute the technician's USERNAME/USERPROFILE.
        var root = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "";
        path = path.Replace("%SystemDrive%", root, StringComparison.OrdinalIgnoreCase)
            .Replace("%SystemRoot%", Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase);
        return ExecutionPolicy.NormalizeProfile(path) ?? throw new InvalidOperationException("ProfileList не содержит однозначного пути.");
    }
    private static string SessionText(int session, int kind)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, session, kind, out var buffer, out var bytes)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (buffer == IntPtr.Zero || bytes < 2 || bytes > 65536 || (bytes & 1) != 0) return "";
            return (Marshal.PtrToStringUni(buffer, bytes / 2) ?? "").TrimEnd('\0');
        }
        finally { WTSFreeMemory(buffer); }
    }
    private static string Error(string section, Exception ex) => $"{section}: {ex.GetType().Name}, 0x{ex.HResult:X8}.";
    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenGroups { public uint Count; public SidAndAttributes First; }
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int information, int length, out int returned);
    [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformationBuffer(SafeAccessTokenHandle token, int informationClass, IntPtr information, int length, out int returned);
    [DllImport("userenv.dll", EntryPoint = "GetUserProfileDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserProfileDirectory(SafeAccessTokenHandle token, StringBuilder? profile, ref uint length);
    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int session, int informationClass, out IntPtr buffer, out int bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr memory);
}
