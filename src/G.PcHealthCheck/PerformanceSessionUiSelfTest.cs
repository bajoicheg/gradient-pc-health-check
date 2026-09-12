using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class PerformanceSessionUiSelfTest
{
    public static int Run()
    {
        var count = 0; var failures = new List<string>();
        void Test(string name, Action test) { count++; try { test(); } catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); } }
        Test("menu keeps all previous analysis tools and attaches once", () =>
        {
            using var main = new Form();
            IncidentReviewMenu.Attach(main);
            var before = main.MainMenuStrip!.Items.Find("ReadOnlyInspections", false).OfType<ToolStripMenuItem>().Single().DropDownItems.Count;
            var menu = TypeOf("PerformanceSessionMenu").GetMethod("Attach", BindingFlags.Public | BindingFlags.Static)!;
            menu.Invoke(null, [main]); menu.Invoke(null, [main]);
            var group = main.MainMenuStrip.Items.Find("ReadOnlyInspections", false).OfType<ToolStripMenuItem>().Single();
            Require(group.DropDownItems.Count == before + 1 && group.DropDownItems.Find("PerformanceSession", false).Length == 1, "Duplicate or replaced menu.");
        });
        Test("window opens idle", () =>
        {
            using var form = (Form)Activator.CreateInstance(TypeOf("PerformanceSessionForm"))!;
            Require(form.Controls.Find("SessionStart", true).Single().Enabled, "Start unavailable.");
            Require(!form.Controls.Find("SessionStop", true).Single().Enabled && !form.Controls.Find("SessionMark", true).Single().Enabled, "Idle window already running.");
            Require(!form.Controls.Find("SessionExport", true).Single().Enabled, "Uncollected session export enabled.");
        });
        Test("memory native structure matches Windows 64-byte contract", () =>
        {
            var type = TypeOf("WindowsPerformanceSessionSource").GetNestedType("MemoryStatus", BindingFlags.NonPublic)!;
            Require(Marshal.SizeOf(type) == 64, "Incorrect native memory structure size.");
        });
        Test("native counters and local disk read are non-destructive", () =>
        {
            using var source = (IPerformanceSessionSource)Activator.CreateInstance(TypeOf("WindowsPerformanceSessionSource"))!;
            Thread.Sleep(30);
            var r = source.Read(default);
            Require(r.MemoryUsedPercent is >= 0 and <= 100, "GlobalMemoryStatusEx did not produce valid physical memory.");
            Require(r.CpuPercent is null or (>= 0 and <= 100), "Invalid CPU percentage.");
            Require(r.DiskBusyPercent is null or (>= 0 and <= 100), "Invalid disk percentage.");
            Require(r.CpuPercent.HasValue || r.Warnings.Count > 0, "Missing CPU unexplained.");
        });
        Test("source pre-cancellation returns without a sample", () =>
        {
            using var source = (IPerformanceSessionSource)Activator.CreateInstance(TypeOf("WindowsPerformanceSessionSource"))!;
            using var c = new CancellationTokenSource(); c.Cancel();
            try { source.Read(c.Token); } catch (OperationCanceledException) { return; }
            throw new InvalidOperationException("Cancelled source returned data.");
        });
        foreach (var metric in Enum.GetValues<SessionMetric>())
            Test("render graph with missing data " + metric, () =>
            {
                using var chart = (Control)Activator.CreateInstance(TypeOf("PerformanceTimeline"))!;
                chart.Size = new Size(700, 220);
                var s = PerformanceSessionSelfTest.Snapshot(10, null, 90);
                s.Markers.Add(new(1500, "Симптом"));
                chart.GetType().GetMethod("Display")!.Invoke(chart, [s, metric]);
                using var bitmap = new Bitmap(chart.Width, chart.Height); chart.DrawToBitmap(bitmap, chart.ClientRectangle);
            });
        Test("exports use distinct paths and preserve complete observations", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "GPcHealthCheck-Session-Test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root); var s = PerformanceSessionSelfTest.Snapshot(10, null, 30);
                var save = TypeOf("PerformanceSessionExport").GetMethod("Save", BindingFlags.Public | BindingFlags.Static)!;
                var first = (string)save.Invoke(null, [s, root])!; var second = (string)save.Invoke(null, [s, root])!;
                Require(first != second && File.Exists(Path.Combine(first, "report.html")), "Report overwritten.");
                using var j = JsonDocument.Parse(File.ReadAllText(Path.Combine(first, "session.json")));
                Require(j.RootElement.GetProperty("Snapshot").GetProperty("Samples").GetArrayLength() == 3, "Missing samples discarded.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        Console.WriteLine($"Performance session integration: {count - failures.Count}/{count} passed.");
        foreach (var f in failures) Console.Error.WriteLine("FAIL: " + f);
        return failures.Count == 0 ? 0 : 221;
    }

    private static Type TypeOf(string name) => typeof(AssessmentService).Assembly.GetType("G.PcHealthCheck." + name) ?? throw new InvalidOperationException(name + " not implemented.");
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
