using System.Text;

namespace G.PcHealthCheck;

// Evidence, not an authorization credential. Runtime actions recapture native facts.
public sealed record ExecutionContextInfo
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
    public string ProcessAccount { get; init; } = "";
    public string ProcessSid { get; init; } = "";
    public string ProcessProfile { get; init; } = "";
    public int SessionId { get; init; } = -1;
    public bool? AdministratorMember { get; init; }
    public bool? HasAdministratorToken { get; init; }
    public bool? IsElevated { get; init; }
    public int? ElevationType { get; init; }
    public string SessionAccount { get; init; } = "";
    public string SessionSid { get; init; } = "";
    public string SessionProfile { get; init; } = "";
    public string ProfileSource { get; init; } = "";
    public List<string> Warnings { get; init; } = [];
}

internal sealed record ActionAvailability(string State, string Reason, string Scope)
{
    public bool CanRequest => State is "Ready" or "NeedsUac";
}

internal static class ExecutionPolicy
{
    public static string Mode(ExecutionContextInfo context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.HasAdministratorToken == true)
            return context.ElevationType == 1 ? "AdministratorFullToken" : context.IsElevated == true ? "AdministratorElevated" : "Unknown";
        if (context.HasAdministratorToken != false || context.IsElevated != false) return "Unknown";
        return context.AdministratorMember switch { true => "AdministratorLimited", false => "Standard", _ => "Unknown" };
    }
    public static string ModeText(ExecutionContextInfo? context) => context is null ? "Контекст не сохранён" : Mode(context) switch
    {
        "Standard" => "Обычный пользователь — без повышения",
        "AdministratorLimited" => "Администратор — без повышения",
        "AdministratorElevated" => "Администратор — повышенный процесс",
        "AdministratorFullToken" => "Администратор — полный токен без связанной пары UAC",
        _ => "Права процесса определены не полностью"
    };
    public static bool SameUser(ExecutionContextInfo context)
        => !string.IsNullOrWhiteSpace(context.ProcessSid) && !string.IsNullOrWhiteSpace(context.SessionSid)
           && string.Equals(context.ProcessSid, context.SessionSid, StringComparison.Ordinal);

    internal static string? NormalizeProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile) || profile.Contains('%') || !Path.IsPathFullyQualified(profile)
            || profile.StartsWith(@"\\?\", StringComparison.Ordinal) || profile.StartsWith(@"\\.\", StringComparison.Ordinal)) return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile)); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return null; }
    }
    public static string? PreviewRoot(ExecutionContextInfo context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.SessionId <= 0 || string.IsNullOrWhiteSpace(context.SessionSid)) return null;
        var profile = NormalizeProfile(context.SessionProfile);
        return profile is null ? null : Path.Combine(profile, "AppData", "Local", "Temp");
    }
    public static string? CleanupRoot(ExecutionContextInfo context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.IsElevated != false || context.HasAdministratorToken != false || !SameUser(context)) return null;
        var own = NormalizeProfile(context.ProcessProfile); var subject = NormalizeProfile(context.SessionProfile);
        if (own is null || subject is null || !string.Equals(own, subject, StringComparison.OrdinalIgnoreCase)) return null;
        return PreviewRoot(context);
    }
    public static ActionAvailability For(string actionId, ExecutionContextInfo context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var id = actionId.ToLowerInvariant();
        if (id == "temppreview")
        {
            var root = PreviewRoot(context);
            return root is null ? new("Unavailable", "Пользователь/профиль текущего сеанса не подтверждён; чужой Temp не подставляется.", "Не определена")
                : new("Ready", "Только чтение метаданных; повышение не является основанием для запрета. Доступ к файлам определяет Windows.", root);
        }
        if (id == "cleantemp")
        {
            var root = CleanupRoot(context);
            return root is not null ? new("Ready", "Удаление старых обычных файлов только в собственном Temp подтверждённого пользователя сеанса.", root)
                : new("Unavailable", "Для удаления нужен обычный запуск от имени пользователя текущего сеанса и подтверждённый профиль. Предпросмотр разрешён отдельно.", PreviewRoot(context) ?? "Не определена");
        }
        if (id is "dism" or "sfc")
            return context.HasAdministratorToken switch
            {
                true => new("Ready", "Административный токен уже активен; повторный UAC не требуется.", "Windows на этом компьютере"),
                false => new("NeedsUac", "Отдельный обработчик запросит UAC. Обычному пользователю нужны административные учётные данные, если разрешено политикой.", "Windows на этом компьютере"),
                _ => new("Unavailable", "Не удалось подтвердить фактические права процесса.", "Windows на этом компьютере")
            };
        if (id == "flushdns") return new("Ready", "Запуск с текущими правами; Windows может отказать. В наборе с DISM/SFC выполняется в их обработчике. Автоповтора через UAC нет.", "DNS-кэш компьютера");
        if (id == "diagnostics") return new("Ready", "Сбор с текущими правами. Недоступные поля не повышают процесс автоматически; полнота зависит от поставщика данных и ACL.", "Компьютер; пользовательские источники имеют отдельную область");
        if (id == "startupreview") return new("Ready", "HKCU и личная Startup принадлежат аккаунту процесса, а не автоматически пользователю рабочего стола.", context.ProcessAccount);
        return new("Unavailable", "Автоматическое действие не определено в разрешённом перечне.", "—");
    }
    public static string StateText(string state) => state switch { "Ready" => "Доступно сейчас", "NeedsUac" => "Требуется UAC", "Manual" => "Вручную", _ => "Недоступно" };
    public static string Describe(ExecutionContextInfo? context)
    {
        if (context is null) return "Контекст выполнения не сохранён; права и аккаунт не восстанавливаются предположением.";
        var s = new StringBuilder();
        s.AppendLine(ModeText(context));
        s.AppendLine($"Аккаунт процесса: {Value(context.ProcessAccount)}; SID: {Value(context.ProcessSid)}.");
        s.AppendLine($"Пользователь сеанса {context.SessionId}: {Value(context.SessionAccount)}; SID: {Value(context.SessionSid)}.");
        s.AppendLine($"Повышение токена: {Flag(context.IsElevated)}; активные административные права: {Flag(context.HasAdministratorToken)}; SID Администраторов в токене: {Flag(context.AdministratorMember)}.");
        s.AppendLine($"Профиль сеанса: {Value(context.SessionProfile)}; источник: {Value(context.ProfileSource)}.");
        s.AppendLine($"Профиль процесса: {Value(context.ProcessProfile)}. HKCU/личная Startup относятся к аккаунту процесса.");
        if (!SameUser(context)) s.AppendLine("Аккаунт процесса и пользователь сеанса различаются либо соответствие не подтверждено. Их данные нельзя смешивать.");
        s.AppendLine("Диагностика не получает повышение автоматически. Повышение обработчика исправлений не повышает последующую диагностику в исходном окне.");
        s.AppendLine($"Контекст прочитан: {context.CapturedAt:O}. Изменение членства групп после входа не перечитывается из каталога.");
        foreach (var warning in context.Warnings) s.AppendLine("! " + warning);
        return s.ToString().TrimEnd();
    }
    internal static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "не определён" : value;
    internal static string Flag(bool? value) => value is null ? "неизвестно" : value.Value ? "да" : "нет";
}
