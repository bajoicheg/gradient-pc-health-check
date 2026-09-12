namespace G.PcHealthCheck;

// Test-first contracts, not a functioning feature until the implementation is tested.
internal static class FileUseCore
{
    public static string NormalizeTarget(string target) => throw new NotImplementedException();
    public static FileUseProcess Associate(FileUseProcess row, FileUseIdentity? identity) => throw new NotImplementedException();
    public static bool Matches(FileUseProcess row, string query) => throw new NotImplementedException();
    public static string Verdict(FileUseSnapshot snapshot) => throw new NotImplementedException();
    public static DateTimeOffset? StartTime(ulong fileTime) => throw new NotImplementedException();
    public static string Session(FileUseProcess row) => throw new NotImplementedException();
}
internal static class FileUseService
{
    public static FileUseSnapshot Collect(IFileUseSource source, string target, ExecutionContextInfo? context, CancellationToken ct, IProgress<string>? progress = null) => throw new NotImplementedException();
}
internal static class FileUseReport
{
    public static string Summary(FileUseSnapshot current, FileUseSnapshot? previous) => throw new NotImplementedException();
    public static string Html(FileUseSnapshot current, FileUseSnapshot? previous) => throw new NotImplementedException();
    public static string Json(FileUseSnapshot current, FileUseSnapshot? previous) => throw new NotImplementedException();
    public static string Save(FileUseSnapshot current, FileUseSnapshot? previous, string parent) => throw new NotImplementedException();
}
