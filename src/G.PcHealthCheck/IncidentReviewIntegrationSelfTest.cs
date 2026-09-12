using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class IncidentReviewIntegrationSelfTest
{
    public static int Run()
    {
        var failures = new List<string>(); var count = 0;
        void Test(string name, Action action) { count++; try { action(); } catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); } }
        Test("menu attaches once without removing previous analysis tools", () =>
        {
            using var main = new Form(); ResourceProbeMenu.Attach(main);
            var group = (ToolStripMenuItem)main.MainMenuStrip!.Items.Find("ReadOnlyInspections", false).Single(); var before = group.DropDownItems.Count;
            var attach = Type("IncidentReviewMenu").GetMethod("Attach", BindingFlags.Public | BindingFlags.Static)!;
            attach.Invoke(null, [main]); attach.Invoke(null, [main]);
            Require(group.DropDownItems.Count == before + 2 && main.MainMenuStrip.Items.Find("ResourceProbe", true).Length == 1, "Duplicate or lost menu entries.");
        });
        foreach (var events in new[] { true, false })
            Test("idle typed window " + events, () =>
            {
                using var form = (Form)Activator.CreateInstance(Type("IncidentReviewForm"), [events])!;
                var run = (Button)form.Controls.Find("IncidentRun", true).Single(); var cancel = (Button)form.Controls.Find("IncidentCancel", true).Single();
                var grid = (DataGridView)form.Controls.Find("IncidentGrid", true).Single();
                Require(run.Enabled && !cancel.Enabled && grid.Rows.Count == 0, "Window opens busy or starts provider collection.");
                var column = grid.Columns[events ? "EventId" : "Pid"]!;
                Require(column.ValueType == (events ? typeof(int) : typeof(uint)), "Numeric field uses string sorting.");
                Require(form.Controls.Find("IncidentSearch", true).Length == 1 && form.MinimumSize.Width >= 850, "Missing search or minimum layout.");
            });
        Test("Windows event adapter rejects Security channel", () =>
        {
            var source = (IIncidentEventSource)Activator.CreateInstance(Type("WindowsIncidentEventSource"))!;
            try { source.Read("Security", new(DateTimeOffset.Now.AddMinutes(-1), DateTimeOffset.Now), default).FirstOrDefault(); }
            catch (ArgumentException) { return; }
            throw new InvalidOperationException("Unexpected channel accepted.");
        });
        Test("actual local Application query is read-only and bounded", () =>
        {
            var source = (IIncidentEventSource)Activator.CreateInstance(Type("WindowsIncidentEventSource"))!;
            var w = new IncidentWindow(DateTimeOffset.Now.AddMinutes(-2), DateTimeOffset.Now);
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            foreach (var e in source.Read("Application", w, ct.Token).Take(2)) Require(e.Log == "Application" && e.Timestamp >= w.From && e.Timestamp <= w.To, "Provider returned a different scope.");
        });
        Test("actual WMI metadata for this test process", () =>
        {
            var source = (IProcessReviewSource)Activator.CreateInstance(Type("WindowsProcessReviewSource"))!;
            using var process = Process.GetCurrentProcess(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var row = source.Lookup((uint)process.Id, ct.Token);
            Require(row is not null && row.Pid == process.Id && row.CreationKey.Length > 0 && row.CreatedAt is not null && row.WorkingSetBytes > 0, "Own process metadata unavailable.");
            var owner = new ProcessReview(source).Owner(row!, ct.Token);
            Require(owner.State == "Verified" && owner.Owner.EndsWith(Environment.UserName, StringComparison.OrdinalIgnoreCase), "Own process owner did not round-trip identity checks.");
        });
        Test("export has unique directory and never drops filtered rows", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "G-Incident-Test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var snapshot = new ProcessReviewSnapshot { State = "Partial", Processes = [new ProcessReviewEntry { Pid = 1, Name = "<literal>", CommandLine = "raw & text" }] };
                var save = Type("IncidentExport").GetMethod("Save", BindingFlags.Public | BindingFlags.Static)!;
                var one = (string)save.Invoke(null, [snapshot, "no match", root])!; var two = (string)save.Invoke(null, [snapshot, "", root])!;
                Require(one != two && File.Exists(Path.Combine(one, "report.html")), "Export overwrote a prior report.");
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(one, "snapshot.json")));
                Require(json.RootElement.GetProperty("Snapshot").GetProperty("Processes").GetArrayLength() == 1 && json.RootElement.GetProperty("VisibleCount").GetInt32() == 0, "Search filter destroyed report evidence.");
                Require(File.ReadAllText(Path.Combine(two, "report.html")).Contains("&lt;literal&gt;", StringComparison.Ordinal), "Export HTML not encoded.");
            }
            finally { Directory.Delete(root, true); }
        });
        Console.WriteLine($"Incident integration regression: {count - failures.Count}/{count} passed."); foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 211;
    }
    private static Type Type(string name) => typeof(AssessmentService).Assembly.GetType("G.PcHealthCheck." + name) ?? throw new InvalidOperationException(name + " is not implemented.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
