using System.Globalization;

namespace G.PcHealthCheck;

internal static class FileUseCore
{
    public static string NormalizeTarget(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var path = target.Trim();
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"') path = path[1..^1];
        path = path.Replace('/', '\\');
        if (path.Length is < 4 or > 32760 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\'
            || path.AsSpan(2).Contains(':') || path.Any(char.IsControl) || path.IndexOfAny(['<', '>', '"', '|', '*', '?']) >= 0)
            throw new ArgumentException("Укажите полный путь к одному локальному файлу. URL, UNC, устройства, маски и альтернативные потоки не поддерживаются.", nameof(target));
        // Validate input components before normalizing away dot/parent segments.
        foreach (var part in path[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part is "." or "..") continue;
            if (part.EndsWith(' ') || part.EndsWith('.') || ReservedComponent(part))
                throw new ArgumentException("Путь содержит зарезервированное имя устройства либо неоднозначную точку/пробел в конце компонента.", nameof(target));
        }
        var full = Path.GetFullPath(path);
        if (full.Length <= 3) throw new ArgumentException("Нужен файл, не корень диска.", nameof(target));
        return full;
    }
    private static bool ReservedComponent(string part)
    {
        var stem = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") return true;
        return stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
            && "123456789¹²³".Contains(stem[3]);
    }
    public static DateTimeOffset? StartTime(ulong fileTime)
    {
        if (fileTime == 0 || fileTime > long.MaxValue) return null;
        try { return new DateTimeOffset(DateTime.FromFileTimeUtc((long)fileTime)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
    public static FileUseProcess Associate(FileUseProcess row, FileUseIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(row);
        var clean = row with { IdentityState = "Unavailable", ProcessName = "", ImagePath = "", IdentityError = identity?.ErrorCode };
        if (row.Pid == 0 || StartTime(row.StartFileTime) is null) return clean with { IdentityState = "Invalid" };
        if (identity is null || identity.ErrorCode is not null || StartTime(identity.StartFileTime) is null) return clean;
        if (row.Pid != identity.Pid || row.StartFileTime != identity.StartFileTime) return clean with { IdentityState = "Changed" };
        if (string.IsNullOrWhiteSpace(identity.ImagePath)) return clean;
        return clean with { IdentityState = "Matched", ImagePath = identity.ImagePath, ProcessName = Path.GetFileName(identity.ImagePath) };
    }
    public static bool Matches(FileUseProcess row, string query)
    {
        var q = query.Trim(); if (q.Length == 0) return true;
        return new[] { row.ApplicationName, row.ServiceName, row.ProcessName, row.ImagePath, row.Pid.ToString(CultureInfo.InvariantCulture), TypeText(row.ApplicationType), IdentityText(row.IdentityState) }
            .Any(x => x.Contains(q, StringComparison.OrdinalIgnoreCase));
    }
    public static string Verdict(FileUseSnapshot snapshot)
    {
        if (!snapshot.ListCompleted) return snapshot.State == "Cancelled" ? "Сбор остановлен; список приложений не получен." : "Не удалось получить список использующих файл приложений.";
        var prefix = snapshot.State == "Cancelled" ? "Сбор остановлен после получения списка. " : snapshot.State == "Partial" ? "Проверка завершена с предупреждениями. " : "";
        return prefix + (snapshot.Processes.Count > 0
            ? $"Restart Manager сообщил приложений/служб: {snapshot.Processes.Count}. Это сведения об использовании файла, не доказательство причины отказа конкретной операции."
            : "Restart Manager не сообщил использующих файл приложений. Это не доказывает отсутствие блокировки или возможность удаления/переименования.");
    }
    public static string Session(FileUseProcess row) => row.ApplicationType is 3 or 1000 || row.SessionId == uint.MaxValue ? "—" : row.SessionId.ToString(CultureInfo.InvariantCulture);
    public static string TypeText(uint type) => type switch
    { 0 => "Не определён", 1 => "Приложение", 2 => "Окно приложения", 3 => "Служба", 4 => "Проводник", 5 => "Консоль", 1000 => "Критический процесс", _ => $"Неизвестный тип ({type})" };
    public static string IdentityText(string state) => state switch
    {
        "Matched" => "PID и время создания совпали", "Changed" => "PID уже относится к другому процессу",
        "Invalid" => "PID/время создания не определены", "NotChecked" => "Проверка EXE не завершена", _ => "Метаданные EXE недоступны"
    };
    public static string StateText(string state) => state switch
    { "Complete" => "Собрано", "Partial" => "С предупреждениями", "Cancelled" => "Остановлено", "Unavailable" => "Недоступно", _ => "Не собиралось" };
}
