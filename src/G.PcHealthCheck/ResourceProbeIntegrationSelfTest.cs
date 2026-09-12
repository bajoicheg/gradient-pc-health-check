using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class ResourceProbeIntegrationSelfTest
{
    public static int Run()
    {
        var failures = new List<string>(); var count = 0;
        void Test(string name, Action action) { count++; try { action(); } catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); } }
        Test("actual loopback TCP handshake with no application bytes", () =>
        {
            var network = (IResourceProbeNetwork)Activator.CreateInstance(TypeFor("SystemResourceProbeNetwork"))!;
            Loopback(network).GetAwaiter().GetResult();
        });
        Test("menu integration is idempotent and preserves existing items", () =>
        {
            using var form = new Form(); ReadOnlyReviewMenu.Attach(form); var countBefore = form.MainMenuStrip!.Items.Count;
            var attach = TypeFor("ResourceProbeMenu").GetMethod("Attach", BindingFlags.Public | BindingFlags.Static)!;
            attach.Invoke(null, [form]); attach.Invoke(null, [form]);
            Require(form.MainMenuStrip.Items.Count == countBefore && form.MainMenuStrip.Items.Find("ResourceProbe", true).Length == 1, "Menu missing or duplicated.");
        });
        Test("form opens idle without contacting any endpoint", () =>
        {
            var network = new CountingNetwork(); using var form = NewForm(network); form.Show(); Application.DoEvents();
            Require(network.Calls == 0 && !Find<Button>(form, "ProbeRun").Enabled && !Find<CheckBox>(form, "ProbeConsent").Checked, "Opening form caused a request or preapproved consent."); form.Close();
        });
        Test("valid target and explicit consent both required", () =>
        {
            using var form = NewForm(new CountingNetwork()); var host = Find<TextBox>(form, "ProbeHost"); var consent = Find<CheckBox>(form, "ProbeConsent"); var run = Find<Button>(form, "ProbeRun");
            host.Text = "example.invalid"; Require(!run.Enabled, "Valid target bypassed consent."); consent.Checked = true; Require(run.Enabled, "Valid consent did not enable check.");
            host.Text = "different.invalid"; Require(!consent.Checked && !run.Enabled, "New target inherited old consent.");
            host.Text = "http://bad"; consent.Checked = true; Require(!run.Enabled, "Invalid input enabled network action.");
        });
        Test("port change invalidates target consent", () =>
        {
            using var form = NewForm(new CountingNetwork()); Find<TextBox>(form, "ProbeHost").Text = "host"; Find<CheckBox>(form, "ProbeConsent").Checked = true;
            Find<NumericUpDown>(form, "ProbePort").Value = 80; Require(!Find<CheckBox>(form, "ProbeConsent").Checked, "Consent reused for a different port.");
        });
        Test("export keeps cancelled current attempt and previous result", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "GPcHealthCheck-ResourceExport-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var save = TypeFor("ResourceProbeExport").GetMethod("Save", BindingFlags.Public | BindingFlags.Static)!;
                var current = new ResourceProbeSnapshot { Target = new("example.invalid", 443), Outcome = "Cancelled" };
                var previous = new ResourceProbeSnapshot { Target = current.Target, Outcome = "Connected" };
                var p1 = (string)save.Invoke(null, [current, previous, root])!; var p2 = (string)save.Invoke(null, [current, previous, root])!;
                Require(p1 != p2 && File.Exists(Path.Combine(p1, "report.html")), "Export overwrote earlier evidence.");
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(p1, "snapshot.json")));
                Require(json.RootElement.GetProperty("Current").GetProperty("Outcome").GetString() == "Cancelled" && json.RootElement.GetProperty("Previous").GetProperty("Outcome").GetString() == "Connected", "Latest cancellation lost.");
            }
            finally { Directory.Delete(root, true); }
        });
        Console.WriteLine($"Resource integration regression: {count - failures.Count}/{count} passed.");
        foreach (var f in failures) Console.Error.WriteLine("FAIL: " + f);
        return failures.Count == 0 ? 0 : 201;
    }
    private static async Task Loopback(IResourceProbeNetwork network)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var accept = listener.AcceptTcpClientAsync(deadline.Token).AsTask();
        var local = await network.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, deadline.Token).ConfigureAwait(false);
        using var peer = await accept.ConfigureAwait(false); var bytes = new byte[1];
        var read = await peer.GetStream().ReadAsync(bytes, deadline.Token).ConfigureAwait(false);
        Require(local == "127.0.0.1" && read == 0, "Transport sent application payload, did not close or lost local source.");
    }
    private static Type TypeFor(string name) => typeof(AssessmentService).Assembly.GetType("G.PcHealthCheck." + name) ?? throw new InvalidOperationException(name + " is not implemented.");
    private static Form NewForm(IResourceProbeNetwork network) => (Form)Activator.CreateInstance(TypeFor("ResourceProbeForm"), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [network], null)!;
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private sealed class CountingNetwork : IResourceProbeNetwork
    {
        public int Calls { get; private set; }
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) { Calls++; return Task.FromResult(Array.Empty<IPAddress>()); }
        public Task<string> ConnectAsync(IPAddress address, int port, CancellationToken ct) { Calls++; return Task.FromResult(""); }
    }
}
