using System.Reflection;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class ExecutionContextActionSelfTest
{
    public static int Run()
    {
        var failures = new List<string>(); var count = 0;
        void Test(string name, Action test)
        { count++; try { test(); } catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); } }
        var user = ExecutionContextSelfTest.User();
        var root = Path.Combine(Path.GetTempPath(), "GPcHealthCheck-ContextAction-" + Guid.NewGuid().ToString("N"));
        var temp = Path.Combine(root, "AppData", "Local", "Temp");
        Directory.CreateDirectory(temp);
        var sentinel = Path.Combine(temp, "old-sentinel.txt");
        File.WriteAllText(sentinel, "must-remain"); File.SetLastWriteTime(sentinel, DateTime.Now.AddDays(-10));
        var context = user with { ProcessProfile = root, SessionProfile = root };
        try
        {
            foreach (var blocked in new[]
            {
                context with { IsElevated = true, HasAdministratorToken = true },
                context with { ProcessSid = "S-1-5-21-1-2-3-9999" },
                context with { IsElevated = null }, context with { SessionSid = "" }
            }) Test("cleanup refuses unverified/elevated context before file work", () =>
            {
                var result = Cleanup(blocked);
                Require(!result.Success && File.ReadAllText(sentinel) == "must-remain", "Forbidden cleanup modified the synthetic fixture.");
            });
            Test("root that is a file is a failure not successful empty cleanup", () =>
            {
                var other = Path.Combine(root, "not-a-temp-folder");
                var otherTemp = Path.Combine(other, "AppData", "Local", "Temp");
                Directory.CreateDirectory(Path.GetDirectoryName(otherTemp)!); File.WriteAllText(otherTemp, "not-a-directory");
                var result = Cleanup(context with { ProcessProfile = other, SessionProfile = other });
                Require(!result.Success && File.ReadAllText(otherTemp) == "not-a-directory", "Directory.Exists false was misclassified as successful cleanup.");
            });
            Test("different actor same session preview preserves read-only target", () =>
            {
                var elevated = context with { ProcessSid = "S-1-5-21-1-2-3-9999", ProcessAccount = "SYNTHETIC\\tech", ProcessProfile = @"C:\Users\Tech", HasAdministratorToken = true, IsElevated = true };
                var result = TempPreviewService.CollectForContext(3, elevated, default, null);
                Require(result.CandidateFiles == 1 && result.Root == temp && File.ReadAllText(sentinel) == "must-remain", "Preview read another profile or changed contents.");
                Require(result.CleanupAvailability == "Unavailable", "Preview was confused with permission to delete.");
            });
            Test("main identity stamping does not retain a console account", () =>
            {
                var data = new DiagnosticData { System = new SystemInfo { UserName = "SYNTHETIC\\console" } };
                MainForm.StampExecutionContext(data, context);
                Require(data.System.UserName == user.SessionAccount && ReferenceEquals(data.System.ExecutionContext, context), "Main identity did not use current-session evidence.");
            });
            Test("legacy action context is null in JSON", () =>
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(new RemediationActionResult { Id = "Dism" }));
                Require(json.RootElement.GetProperty("ExecutionContext").ValueKind == JsonValueKind.Null, "Old action received fabricated identity.");
            });
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"Execution context action review: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 242;
    }
    private static RemediationActionResult Cleanup(ExecutionContextInfo context)
        => (RemediationActionResult)typeof(RemediationWorker).GetMethod("CleanTemp", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [context, 3])!;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
