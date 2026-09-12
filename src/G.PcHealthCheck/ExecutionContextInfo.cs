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
    public static string Mode(ExecutionContextInfo context) => throw new NotImplementedException("Context mode is not implemented.");
    public static string? PreviewRoot(ExecutionContextInfo context) => throw new NotImplementedException("Session preview target is not implemented.");
    public static string? CleanupRoot(ExecutionContextInfo context) => throw new NotImplementedException("Session cleanup target is not implemented.");
    public static ActionAvailability For(string actionId, ExecutionContextInfo context) => throw new NotImplementedException("Action availability is not implemented.");
    public static string Describe(ExecutionContextInfo? context) => throw new NotImplementedException("Context description is not implemented.");
}
