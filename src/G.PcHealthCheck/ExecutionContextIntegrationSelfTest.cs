using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class ExecutionContextIntegrationSelfTest
{
    public static int Run()
    {
        var failures = new List<string>(); var count = 0;
        void Test(string name, Action test) { count++; try { test(); } catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); } }
        var user = ExecutionContextSelfTest.User();
        var elevated = user with { ProcessAccount = "SYNTHETIC\\tech", ProcessSid = "S-1-5-21-1-2-3-1002", AdministratorMember = true, HasAdministratorToken = true, IsElevated = true, ElevationType = 2 };
        Test("native capture agrees with current identity and role", () =>
        {
            using var identity = WindowsIdentity.GetCurrent(); using var process = Process.GetCurrentProcess();
            var context = ExecutionContextService.Capture();
            Require(context.ProcessSid == identity.User?.Value && context.SessionId == process.SessionId, "Wrong process token or session.");
            Require(context.HasAdministratorToken == new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), "Native role query did not preserve effective rights.");
            Require(context.IsElevated is not null && context.ElevationType is >= 1 and <= 3 && context.AdministratorMember is not null, "Native token facts are missing.");
            Require(context.ProcessProfile.Length > 0, "Token profile is missing.");
            if (context.SessionSid.Length == 0) Require(context.SessionProfile.Length == 0 && context.Warnings.Count > 0, "Unresolved session guessed a profile.");
        });
        Test("main displays context and blocks only unavailable actions", () =>
        {
            var scan = new ScanResult { Data = new DiagnosticData { System = new SystemInfo { ExecutionContext = elevated } }, Actions =
                [new() { Id = "CleanTemp", CanAutomate = true, Preselected = true }, new() { Id = "Dism", CanAutomate = true, RequiresAdmin = true }] };
            using var form = new MainForm();
            typeof(MainForm).GetMethod("Populate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [scan]);
            Require(form.Controls.Find("ExecutionContextBanner", true).Length == 1, "Main context banner absent.");
            var grid = (DataGridView)typeof(MainForm).GetField("_actions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            Require(grid.Rows[0].Cells[0].ReadOnly && !Convert.ToBoolean(grid.Rows[0].Cells[0].Value), "Unavailable cleanup still preselected.");
            Require(!grid.Rows[1].Cells[0].ReadOnly, "Ready administrative action blocked.");
        });
        Test("clipboard main summary retains actor and subject", () =>
        {
            var scan = new ScanResult { Data = new DiagnosticData { System = new SystemInfo { ExecutionContext = elevated } } };
            var summary = SupportSummary.Build(scan);
            Require(summary.Contains("SYNTHETIC\\tech") && summary.Contains("SYNTHETIC\\user"), "Clipboard loses actor or subject.");
        });
        Test("HTML carries per-action context and escapes account names", () =>
        {
            var v = new VerificationResult { Remediation = new RemediationBatchResult { Actions =
                [new() { Id = "Dism", Success = true, ExecutionContext = elevated with { ProcessAccount = "<script>tech</script>" }, TargetScope = "Windows" },
                 new() { Id = "CleanTemp", Success = true, ExecutionContext = user, TargetScope = @"C:\Users\Synthetic\AppData\Local\Temp" }] } };
            var html = (string)typeof(ReportService).GetMethod("BuildVerificationHtml", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [v])!;
            Require(!html.Contains("<script>") && html.Contains("&lt;script&gt;tech"), "Action account missing or unescaped.");
            Require(html.Contains("SYNTHETIC\\user") && html.Contains("AppData"), "Normal action context or scope is absent.");
        });
        Test("mixed batch preserves both contexts through serialization", () =>
        {
            var batch = new RemediationBatchResult { Actions = [new() { Id = "Dism", ExecutionContext = elevated }, new() { Id = "CleanTemp", ExecutionContext = user }] };
            Require(batch.MixedExecutionContexts == true, "Mixed contexts collapsed.");
            var copy = JsonSerializer.Deserialize<RemediationBatchResult>(JsonSerializer.Serialize(batch))!;
            Require(copy.Actions[0].ExecutionContext!.IsElevated == true && copy.Actions[1].ExecutionContext!.IsElevated == false, "Per-action privilege lost in IPC/JSON.");
            Require(new RemediationBatchResult { Actions = [new() { Id = "Legacy" }] }.MixedExecutionContexts is null, "Legacy context guessed.");
        });
        Test("same-account elevated and normal actions are still mixed", () =>
        {
            var batch = new RemediationBatchResult { Actions = [new() { ExecutionContext = user }, new() { ExecutionContext = elevated with { ProcessSid = user.ProcessSid } }] };
            Require(batch.MixedExecutionContexts == true, "Same SID hid different token rights.");
        });
        Test("context window shows a non-executing availability matrix", () =>
        {
            var type = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.ExecutionContextForm");
            Require(type is not null, "Context window absent.");
            using var form = (Form)Activator.CreateInstance(type!, [user])!;
            Require(form.Controls.Find("Availability", true).Single() is DataGridView g && g.Rows.Count >= 7, "Availability matrix incomplete.");
        });
        Console.WriteLine($"Execution context integration: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 241;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
