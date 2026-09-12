using System.Reflection;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class FileUseIntegrationSelfTest
{
    public static int Run()
    {
        var n = 0; var failures = new List<string>();
        void Test(string name, Action test) { n++; try { test(); } catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); } }
        Test("real file-use query reports fixture process without changing file", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "GpcFileUse-" + Guid.NewGuid().ToString("N") + ".txt"); var bytes = Encoding.UTF8.GetBytes("Synthetic file-use fixture — never a user file."); File.WriteAllBytes(path, bytes);
            try
            {
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var s = FileUseService.Collect(Source(), path, null, CancellationToken.None);
                    Require(s.ListCompleted && s.EndSessionCode == 0, "Native query or cleanup failed: " + s.Stage + "/" + s.ErrorCode);
                    Require(s.Processes.Any(x => x.Pid == Environment.ProcessId && x.IdentityState == "Matched"), "Current fixture holder not matched.");
                    Require(held.CanRead && held.ReadByte() == bytes[0], "Fixture handle was closed/modified.");
                }
                Require(File.ReadAllBytes(path).SequenceEqual(bytes), "Query changed the selected file.");
                var after = FileUseService.Collect(Source(), path, null, CancellationToken.None);
                Require(after.ListCompleted && after.EndSessionCode == 0 && !after.Processes.Any(x => x.Pid == Environment.ProcessId), "Own released handle still reported or query failed.");
            }
            finally { File.Delete(path); }
        });
        Test("directory cannot be registered as file", () => { var s = FileUseService.Collect(Source(), Path.GetTempPath(), null, CancellationToken.None); Require(!s.ListCompleted && s.State == "Unavailable", "Directory accepted."); });
        Test("missing file not reported unlocked", () => { var s = FileUseService.Collect(Source(), Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".missing"), null, CancellationToken.None); Require(!s.ListCompleted && s.State == "Unavailable", "Missing file accepted."); });
        Test("window opens idle", () =>
        {
            var type = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.FileUseForm"); Require(type is not null, "File-use window missing.");
            using var form = (Form)Activator.CreateInstance(type!)!; form.PerformLayout();
            Require(form.Controls.Find("FileUseEvidence", true).Single() is DataGridView g && g.Rows.Count == 0 && !form.Controls.Find("FileUseExport", true).Single().Enabled, "Window queried or exported without target.");
            Require(form.Controls.Find("FileUseTarget", true).Single() is TextBox && form.Controls.Find("FileUseSearch", true).Single() is TextBox, "Target/search missing.");
        });
        Test("menu attaches once", () =>
        {
            var type = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.FileUseMenu"); Require(type is not null, "File-use menu missing.");
            using var form = new Form(); var method = type!.GetMethod("Attach", BindingFlags.Public | BindingFlags.Static)!; method.Invoke(null, [form]); method.Invoke(null, [form]);
            var group = (ToolStripMenuItem)form.MainMenuStrip!.Items.Find("ReadOnlyInspections", false).Single(); Require(group.DropDownItems.Find("FileUseOpen", false).Length == 1, "Menu duplicate.");
        });
        Test("separate full exports with UTF-8", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "GpcFileUseExport-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var s = FileUseSelfTest.Snapshot(FileUseSelfTest.Row()); var a = FileUseReport.Save(s, null, root); var b = FileUseReport.Save(s, s, root);
                Require(a != b && Directory.GetFiles(a).Length == 2 && Directory.GetFiles(b).Length == 2, "Export overwrote.");
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(b, "file-use.json"), new UTF8Encoding(false, true))); Require(json.RootElement.GetProperty("Previous").ValueKind == JsonValueKind.Object, "Previous attempt missing.");
            }
            finally { Directory.Delete(root, true); }
        });
        Console.WriteLine($"File-use integration: {n - failures.Count}/{n} passed."); foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 246;
    }
    private static IFileUseSource Source()
    {
        var type = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.FileUseWindowsSource"); Require(type is not null, "Native file-use source missing."); return (IFileUseSource)Activator.CreateInstance(type!)!;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
