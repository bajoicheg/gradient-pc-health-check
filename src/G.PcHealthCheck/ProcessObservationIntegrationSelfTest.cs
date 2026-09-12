using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class ProcessObservationIntegrationSelfTest
{
    public static int Run()
    {
        var n = 0; var errors = new List<string>();
        void Test(string name, Action a) { n++; try { a(); } catch (Exception ex) { errors.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); } }
        Test("native query reads current instance memory CPU and IO", () =>
        {
            using var p = Process.GetCurrentProcess(); using var source = Source(new((uint)p.Id, p.StartTime.ToUniversalTime(), p.ProcessName));
            var r = source.Read(CancellationToken.None);
            Require(r.State == "Live" && r.Kernel100ns is not null && r.User100ns is not null && r.WorkingSetBytes > 0 && r.PrivateBytes > 0 && r.ReadBytes is not null && r.WriteBytes is not null && r.LogicalProcessors > 0, "Native data unavailable: " + string.Join(";", r.Warnings));
        });
        Test("wrong creation time rejects even a current PID", () =>
        { using var p = Process.GetCurrentProcess(); using var s = Source(new((uint)p.Id, p.StartTime.ToUniversalTime().AddSeconds(-1), "Synthetic")); var r = s.Read(CancellationToken.None); Require(r.State == "IdentityChanged" && r.Kernel100ns is null && r.WorkingSetBytes is null, "Wrong instance read."); });
        Test("held handle detects fixture exit without rebinding", () =>
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("/d"); start.ArgumentList.Add("/c"); start.ArgumentList.Add("set /p GPCHC_FIXTURE_INPUT=");
            using var p = Process.Start(start)!;
            try
            {
                using var source = Source(new((uint)p.Id, p.StartTime.ToUniversalTime(), "Fixture")); Require(source.Read(default).State == "Live", "Fixture not live.");
                p.StandardInput.WriteLine("done"); p.StandardInput.Close(); Require(p.WaitForExit(5000), "Fixture did not exit.");
                var r = source.Read(default); Require(r.State == "Exited" && r.User100ns is null && r.WorkingSetBytes is null && source.Read(default).State == "Exited", "Exited fixture read/rebound.");
            }
            finally { if (!p.HasExited) { p.StandardInput.Close(); p.WaitForExit(5000); } }
        });
        Test("pre-cancelled native query throws before opening", () =>
        { using var p = Process.GetCurrentProcess(); using var s = Source(new((uint)p.Id, p.StartTime.ToUniversalTime(), "Current")); using var c = new CancellationTokenSource(); c.Cancel(); try { s.Read(c.Token); throw new InvalidOperationException("Read after cancellation."); } catch (OperationCanceledException) { } });
        Test("process list has disabled observe button before collection", () =>
        { using var form = new IncidentReviewForm(false); var button = form.Controls.Find("ObserveSelectedProcess", true).SingleOrDefault() as Button; Require(button is not null && !button.Enabled, "Observe button missing/not idle."); });
        Test("event view cannot observe an emitter PID as current process", () =>
        { using var form = new IncidentReviewForm(true); Require(form.Controls.Find("ObserveSelectedProcess", true).Length == 0, "Historical event can start wrong observation."); });
        Test("observation opens idle with both process and system timelines", () =>
        {
            var type = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.ProcessObservationForm"); Require(type is not null, "Observation form absent.");
            using var f = (Form)Activator.CreateInstance(type!, [ProcessObservationSelfTest.Target])!; f.PerformLayout();
            Require(f.Controls.Find("ObservationStart", true).Single().Enabled && !f.Controls.Find("ObservationExport", true).Single().Enabled && f.Controls.Find("ProcessTimeline", true).Length == 1 && f.Controls.Find("SystemTimeline", true).Length == 1, "Idle controls/charts absent.");
        });
        Test("exports never replace previous results", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "GpcProcessObs-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var s = new ProcessObservationSnapshot { Target = ProcessObservationSelfTest.Target };
                var a = ProcessObservationReport.Save(s, root); var b = ProcessObservationReport.Save(s, root);
                Require(a != b && Directory.GetFiles(a).Length == 2 && Directory.GetFiles(b).Length == 2, "Export incomplete/replaced.");
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(a, "observation.json"))); Require(json.RootElement.GetProperty("Target").GetProperty("Pid").GetUInt32() == 42, "Target lost.");
            }
            finally { Directory.Delete(root, true); }
        });
        Console.WriteLine($"Process observation integration: {n - errors.Count}/{n} passed."); foreach (var e in errors) Console.Error.WriteLine("FAIL: " + e); return errors.Count == 0 ? 0 : 248;
    }
    private static IProcessObservationSource Source(ProcessObservationTarget target)
    { var t = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.WindowsProcessObservationSource"); Require(t is not null, "Native source missing."); return (IProcessObservationSource)Activator.CreateInstance(t!, [target])!; }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
