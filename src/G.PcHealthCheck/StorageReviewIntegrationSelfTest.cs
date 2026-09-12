using System.Reflection;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class StorageReviewIntegrationSelfTest
{
    public static int Run()
    {
        var count = 0; var failures = new List<string>();
        void Test(string name, Action action)
        {
            count++;
            try { action(); } catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); }
        }
        static Type TypeNamed(string name) => typeof(AssessmentService).Assembly.GetType("G.PcHealthCheck." + name)
            ?? throw new InvalidOperationException(name + " not implemented.");
        static Form Window(bool folder) => (Form)Activator.CreateInstance(TypeNamed("StorageReviewForm"), [folder])!;
        Test("existing menu preserved and new tools attach once", () =>
        {
            using var main = new Form();
            PerformanceSessionMenu.Attach(main);
            var group = (ToolStripMenuItem)main.MainMenuStrip!.Items.Find("ReadOnlyInspections", false).Single();
            var before = group.DropDownItems.Count;
            var attach = TypeNamed("StorageReviewMenu").GetMethod("Attach")!;
            attach.Invoke(null, [main]); attach.Invoke(null, [main]);
            Require(group.DropDownItems.Count == before + 2, "Menu replaced existing tools or duplicated new ones.");
        });
        foreach (var folder in new[] { true, false })
            Test("window opens idle " + folder, () =>
            {
                using var form = Window(folder);
                var start = form.Controls.Find("StorageStart", true).Single();
                var cancel = form.Controls.Find("StorageCancel", true).Single();
                Require(start.Enabled && !cancel.Enabled, "Unexpected scan on open.");
                Require(form.MinimumSize.Width >= 850, "Minimum layout too small.");
            });
        Test("real folder metadata collection preserves bytes and includes hidden files", () =>
        {
            var source = (IFolderUsageSource)Activator.CreateInstance(TypeNamed("WindowsFolderUsageSource"))!;
            var path = Path.Combine(Path.GetTempPath(), "GPC-Storage-Test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(path, "child"));
            var a = Path.Combine(path, "a.bin"); var b = Path.Combine(path, "child", "hidden.bin");
            try
            {
                File.WriteAllBytes(a, new byte[17]); File.WriteAllBytes(b, new byte[29]); File.SetAttributes(b, FileAttributes.Hidden);
                var before = File.GetLastWriteTimeUtc(a);
                var snapshot = FolderUsageService.Collect(path, new(), source, default);
                Require(snapshot.Outcome == "Completed" && snapshot.Folders[0].Bytes == 46 && snapshot.Folders[0].Files == 2, "Real metadata totals differ.");
                Require(File.ReadAllBytes(a).Length == 17 && File.ReadAllBytes(b).Length == 29 && File.GetLastWriteTimeUtc(a) == before, "Scan modified sentinel files.");
            }
            finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        });
        Test("local WMI collection returns explicit data or source failure", () =>
        {
            var source = (IPhysicalDiskDetailsSource)Activator.CreateInstance(TypeNamed("WindowsDiskDetailsSource"))!;
            var snapshot = DiskDetailsService.Collect(source, default);
            Require(snapshot.FinishedAt >= snapshot.StartedAt && snapshot.Outcome != "NotStarted", "Storage query did not finish with explicit outcome.");
            Require(snapshot.Disks.Count > 0 || snapshot.Warnings.Count > 0, "Missing devices silently looked complete.");
            Require(snapshot.Disks.All(x => x.Reliability is null || x.Reliability.DeviceId == x.DeviceId), "Counter identity mismatch.");
        });
        Test("native source observes pre-cancellation", () =>
        {
            var source = (IPhysicalDiskDetailsSource)Activator.CreateInstance(TypeNamed("WindowsDiskDetailsSource"))!;
            var snapshot = DiskDetailsService.Collect(source, new CancellationToken(true));
            Require(snapshot.Outcome == "Stopped" && snapshot.Disks.Count == 0, "Cancelled call produced live data.");
        });
        Test("unique exports preserve full snapshot", () =>
        {
            var parent = Path.Combine(Path.GetTempPath(), "GPC-Storage-Export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(parent);
            try
            {
                var snapshot = new FolderUsageSnapshot { Root = "<test>", Outcome = "Partial", Folders = [new() { Path = "<a>", Bytes = 123 }, new() { Path = "<b>", Bytes = 456 }] };
                var a = StorageReviewReport.Save(snapshot, parent); var b = StorageReviewReport.Save(snapshot, parent);
                Require(a != b && Directory.Exists(a) && Directory.Exists(b), "Exports reused a path.");
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(a, "snapshot.json")));
                Require(json.RootElement.GetProperty("Snapshot").GetProperty("Folders").GetArrayLength() == 2, "Export lost collected rows.");
                Require(File.ReadAllText(Path.Combine(a, "report.html")).Contains("&lt;a&gt;", StringComparison.Ordinal), "Exported HTML is not encoded.");
            }
            finally { Directory.Delete(parent, true); }
        });
        Test("UI displays typed numeric sizes", () =>
        {
            using var form = Window(true);
            var snapshot = new FolderUsageSnapshot { Root = "C:\\Synthetic", Outcome = "Completed", Folders = [new() { Path = "C:\\Synthetic", Bytes = 10, Files = 1, Incomplete = false }] };
            TypeNamed("StorageReviewForm").GetMethod("DisplaySnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [snapshot]);
            var grid = (DataGridView)form.Controls.Find("StorageEvidence", true).Single();
            Require(grid.Rows.Count == 1 && grid.Columns.Cast<DataGridViewColumn>().Any(x => x.ValueType == typeof(decimal)), "Numeric sorting has only formatted strings.");
        });
        Console.WriteLine($"Storage review integration: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 231;
    }
    private static void Require(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
}
