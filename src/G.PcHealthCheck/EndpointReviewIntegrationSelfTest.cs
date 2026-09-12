using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class EndpointReviewIntegrationSelfTest
{
    public static int Run()
    {
        var count = 0; var failures = new List<string>();
        void Test(string name, Action action) { count++; try { action(); } catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); } }
        Test("native TCP4 listener and established loopback endpoints", () => Tcp(false));
        Test("native UDP4 bound endpoint has no remote peer", () => Udp(false));
        if (Socket.OSSupportsIPv6)
        {
            Test("native TCP6 listener and established loopback endpoints", () => Tcp(true));
            Test("native UDP6 bound endpoint has no remote peer", () => Udp(true));
        }
        else Console.WriteLine("Endpoint integration: IPv6 socket tests not run; OS reports no IPv6 support.");
        Test("native collector associates current process across table reads", () =>
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var result = EndpointReviewService.Collect(Source(), ExecutionContextService.Capture(), CancellationToken.None);
            Require(result.Tables.Count == 4 && result.FinishedAt >= result.StartedAt, "Collection missing source/time evidence.");
            var row = result.Tables.Single(x => x.Name == "TCP4").Rows.Single(x => x.LocalPort == port && x.Pid == Environment.ProcessId && x.State == "LISTEN");
            using var process = Process.GetCurrentProcess();
            Require(row.ProcessEvidence == "Stable" && row.ProcessName == process.ProcessName && row.ProcessStartedAt is not null, "Current process not matched across sampling.");
            Require(result.ExecutionContext?.ProcessSid.Length > 0, "Collection lost actor.");
        });
        Test("new window opens idle with searchable evidence grid", () =>
        {
            var type = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.EndpointReviewForm"); Require(type is not null, "Endpoint window missing.");
            using var form = (Form)Activator.CreateInstance(type!)!; form.PerformLayout();
            Require(form.Controls.Find("EndpointEvidence", true).Single() is DataGridView grid && grid.Rows.Count == 0, "Window not idle.");
            Require(form.Controls.Find("EndpointSearch", true).Single() is TextBox && form.Controls.Find("EndpointStart", true).Single().Enabled, "Search/start absent.");
            Require(!form.Controls.Find("EndpointExport", true).Single().Enabled, "Export active without snapshot.");
        });
        Test("analysis menu attaches exactly once", () =>
        {
            var type = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.EndpointReviewMenu"); Require(type is not null, "Endpoint menu missing.");
            using var form = new Form(); var attach = type!.GetMethod("Attach", BindingFlags.Public | BindingFlags.Static)!;
            attach.Invoke(null, [form]); attach.Invoke(null, [form]);
            var analysis = (ToolStripMenuItem)form.MainMenuStrip!.Items.Find("ReadOnlyInspections", false).Single();
            Require(analysis.DropDownItems.Find("EndpointReviewOpen", false).Length == 1, "Menu duplicated.");
        });
        Test("local exports preserve snapshots and never overwrite", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "GPcHealthCheck-Endpoints-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var save = typeof(EndpointReviewReport).GetMethod("Save", BindingFlags.Public | BindingFlags.Static); Require(save is not null, "Export method absent.");
                var data = EndpointReviewSelfTest.Snapshot(EndpointReviewSelfTest.Row());
                var a = (string)save!.Invoke(null, [data, null, root])!; var b = (string)save.Invoke(null, [data, data, root])!;
                Require(a != b && Directory.GetFiles(a).Length == 2 && Directory.GetFiles(b).Length == 2, "Export overwrote or is incomplete.");
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(b, "endpoints.json")));
                Require(json.RootElement.GetProperty("Previous").ValueKind == JsonValueKind.Object, "Previous snapshot lost.");
            }
            finally { Directory.Delete(root, true); }
        });
        Console.WriteLine($"Endpoint integration: {count - failures.Count}/{count} passed."); foreach (var f in failures) Console.Error.WriteLine("FAIL: " + f);
        return failures.Count == 0 ? 0 : 244;
    }
    private static IEndpointSource Source()
    {
        var type = typeof(MainForm).Assembly.GetType("G.PcHealthCheck.EndpointWindowsSource"); Require(type is not null, "Windows endpoint source absent.");
        return (IEndpointSource)Activator.CreateInstance(type!)!;
    }
    private static void Tcp(bool ipv6)
    {
        var address = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback; var key = ipv6 ? "TCP6" : "TCP4";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(address, 0); if (ipv6) listener.Server.DualMode = false; listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var table = Source().ReadTable(key, timeout.Token);
        Require(table.State == "Complete" && table.Rows.Any(x => x.LocalPort == port && x.Pid == Environment.ProcessId && x.State == "LISTEN" && x.RemotePort is null), "Native listener/PID/port not decoded.");
        using var client = new TcpClient(address.AddressFamily);
        client.ConnectAsync(address, port, timeout.Token).AsTask().GetAwaiter().GetResult();
        using var server = listener.AcceptTcpClientAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
        table = Source().ReadTable(key, timeout.Token);
        Require(table.Rows.Any(x => x.LocalPort == port && x.Pid == Environment.ProcessId && x.State == "ESTABLISHED" && x.RemotePort == ((IPEndPoint)client.Client.LocalEndPoint!).Port), "Native connection not decoded.");
        Require(client.Available == 0 && server.Available == 0, "Unexpected application payload.");
    }
    private static void Udp(bool ipv6)
    {
        var address = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        using var client = new UdpClient(new IPEndPoint(address, 0)); var port = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
        var table = Source().ReadTable(ipv6 ? "UDP6" : "UDP4", CancellationToken.None);
        Require(table.State == "Complete" && table.Rows.Any(x => x.LocalPort == port && x.Pid == Environment.ProcessId && x.RemotePort is null && x.State == "BOUND"), "Native UDP binding not decoded.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
