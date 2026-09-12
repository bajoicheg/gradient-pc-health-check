using System.Reflection;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class ExecutionContextSelfTest
{
    internal static ExecutionContextInfo User() => new()
    {
        ProcessAccount = "SYNTHETIC\\user", ProcessSid = "S-1-5-21-1-2-3-1001", ProcessProfile = @"C:\Users\Synthetic",
        SessionAccount = "SYNTHETIC\\user", SessionSid = "S-1-5-21-1-2-3-1001", SessionProfile = @"C:\Users\Synthetic",
        SessionId = 3, ProfileSource = "Synthetic", AdministratorMember = false, HasAdministratorToken = false, IsElevated = false, ElevationType = 1
    };
    public static int Run()
    {
        var failures = new List<string>(); var count = 0;
        void Test(string name, Action test) { count++; try { test(); } catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); } }
        var user = User();
        var limited = user with { AdministratorMember = true, ElevationType = 3 };
        var elevated = user with { AdministratorMember = true, HasAdministratorToken = true, IsElevated = true, ElevationType = 2 };
        foreach (var pair in new[] { (user, "Standard"), (limited, "AdministratorLimited"), (elevated, "AdministratorElevated"),
            (elevated with { ElevationType = 1 }, "AdministratorFullToken"), (new ExecutionContextInfo(), "Unknown") })
            Test("classifies token " + pair.Item2, () => Require(ExecutionPolicy.Mode(pair.Item1) == pair.Item2, "Incorrect effective mode."));
        foreach (var ctx in new[] { user, limited, elevated, elevated with { ProcessAccount = "SYNTHETIC\\tech", ProcessSid = "S-1-5-21-1-2-3-1002", ProcessProfile = @"C:\Users\Technician" } })
            Test("preview is read-only in token " + ctx.ElevationType + " " + ctx.ProcessAccount, () =>
            {
                Require(ExecutionPolicy.PreviewRoot(ctx) == @"C:\Users\Synthetic\AppData\Local\Temp", "Preview selected the wrong profile or refused elevation.");
                Require(ExecutionPolicy.For("TempPreview", ctx).State == "Ready", "Read-only preview incorrectly requires a normal token.");
            });
        foreach (var ctx in new[] { user with { SessionSid = "" }, user with { SessionProfile = "" }, user with { SessionId = 0 },
            user with { SessionProfile = "relative" }, user with { SessionProfile = @"\\?\C:\Users\Synthetic" } })
            Test("missing or unsupported subject is not guessed", () =>
            {
                Require(ExecutionPolicy.PreviewRoot(ctx) is null && !ExecutionPolicy.For("TempPreview", ctx).CanRequest, "Preview fell back to another user's directory.");
            });
        foreach (var ctx in new[] { user, limited }) Test("same-user normal cleanup", () =>
            Require(ExecutionPolicy.CleanupRoot(ctx) == ExecutionPolicy.PreviewRoot(ctx) && ExecutionPolicy.For("CleanTemp", ctx).State == "Ready", "Normal same-user cleanup lost availability."));
        foreach (var ctx in new[] { elevated, user with { ProcessSid = "S-1-5-21-1-2-3-1002" }, user with { ProcessProfile = @"C:\Users\Other" },
            user with { IsElevated = null }, user with { HasAdministratorToken = null }, user with { SessionSid = "" } })
            Test("cleanup refuses elevated or ambiguous identity", () => Require(ExecutionPolicy.CleanupRoot(ctx) is null && !ExecutionPolicy.For("CleanTemp", ctx).CanRequest, "Unsafe cleanup remained available."));
        foreach (var id in new[] { "Dism", "Sfc" }) Test("machine action independent of user profile " + id, () =>
        {
            Require(ExecutionPolicy.For(id, user with { SessionProfile = "", SessionSid = "" }).State == "NeedsUac", "Machine repair was coupled to an unrelated profile.");
            Require(ExecutionPolicy.For(id, elevated with { SessionProfile = "" }).State == "Ready", "Elevated action requests duplicate elevation.");
            Require(!ExecutionPolicy.For(id, new ExecutionContextInfo()).CanRequest, "Unknown privilege was assumed.");
        });
        Test("DNS stays a current-token action", () => Require(ExecutionPolicy.For("FlushDns", user).State == "Ready", "Existing DNS policy silently changed."));
        Test("unknown action is unavailable", () => Require(!ExecutionPolicy.For("AnythingElse", user).CanRequest, "Unknown action accepted."));
        Test("description distinguishes technician and subject", () =>
        {
            var text = ExecutionPolicy.Describe(elevated with { ProcessAccount = "SYNTHETIC\\tech", ProcessSid = "S-1-5-21-1-2-3-1002" });
            Require(text.Contains("SYNTHETIC\\tech") && text.Contains("SYNTHETIC\\user") && text.Contains("3") && text.Contains("HKCU"), "Identity/scope explanation is incomplete.");
        });
        Test("absent legacy context remains unknown", () => Require(ExecutionPolicy.Describe(null).Contains("не сохранён"), "Old report context was fabricated."));
        Test("context JSON preserves unknown token", () =>
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(new ExecutionContextInfo()));
            Require(json.RootElement.GetProperty("IsElevated").ValueKind == JsonValueKind.Null, "Unknown elevation became false.");
        });
        Test("elevated real metadata preview and context export", () =>
        {
            var method = typeof(TempPreviewService).GetMethod("CollectForContext", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Require(method is not null, "Elevated preview entry is missing.");
            var root = Path.Combine(Path.GetTempPath(), "GPcHealthCheck-ContextTest-" + Guid.NewGuid().ToString("N"));
            var temp = Path.Combine(root, "AppData", "Local", "Temp"); Directory.CreateDirectory(temp);
            var sentinel = Path.Combine(temp, "old.txt"); File.WriteAllText(sentinel, "untouched"); File.SetLastWriteTime(sentinel, DateTime.Now.AddDays(-10));
            try
            {
                var ctx = elevated with { SessionProfile = root, ProcessProfile = root };
                var scan = (TempPreviewSnapshot)method!.Invoke(null, [3, ctx, CancellationToken.None, null])!;
                Require(scan.CandidateFiles == 1 && File.ReadAllText(sentinel) == "untouched", "Preview failed or changed the fixture.");
                var json = ReviewReport.Json(scan); var text = ReviewReport.Overview(scan);
                Require(json.Contains("ExecutionContext") && text.Contains("SYNTHETIC\\user"), "Preview lost actor/subject evidence.");
                Require(text.Contains("обычным запуском"), "Elevated preview directs the user to an unavailable cleanup.");
            }
            finally { Directory.Delete(root, true); }
        });
        Console.WriteLine($"Execution context regression: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 240;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
