using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class ResourceProbeSelfTest
{
    public static int Run()
    {
        var failed = new List<string>(); var count = 0;
        void Test(string name, Action test) { count++; try { test(); } catch (Exception ex) { failed.Add(name + ": " + ex.Message); } }
        foreach (var (input, expected) in new[] { (" Example.COM ", "example.com"), ("host", "host"), ("example.com.", "example.com."), ("127.0.0.1", "127.0.0.1"), ("[::1]", "::1"), ("2001:db8::1", "2001:db8::1"), ("пример.рф", "xn--e1afmkfd.xn--p1ai") })
            Test("accept target " + input, () => Require(ResourceTargetParser.Parse(input, 443) == new ResourceTarget(expected, 443), "Target not preserved/normalized."));
        foreach (var input in new[] { "", " ", "https://example.com/path", "user:pass@example.com", "a/b", "a;b", "*.example.com", "a b", "a\nb", "-host", "host-", "a..b", "127.1", "0x7f000001", "0.0.0.0", "255.255.255.255", "224.0.0.1", "::", "ff02::1", "10.0.0.1/24", "a:443" })
            Test("reject target " + input, () => ThrowsArgument(() => ResourceTargetParser.Parse(input, 443)));
        foreach (var port in new[] { -1, 0, 65536 }) Test("reject port " + port, () => ThrowsArgument(() => ResourceTargetParser.Parse("example.com", port)));
        Test("boundary ports supported", () => { Require(ResourceTargetParser.Parse("host", 1).Port == 1, "Port 1 rejected."); Require(ResourceTargetParser.Parse("host", 65535).Port == 65535, "Port 65535 rejected."); });
        Test("DNS and TCP recorded separately", () =>
        {
            var n = new FakeNetwork(); var s = Run(n);
            Require(n.DnsCalls == 1 && n.Ports.SequenceEqual(new[] { 443 }), "Unexpected network requests.");
            Require(s.Outcome == "Connected" && s.Steps.Select(x => x.Stage).SequenceEqual(new[] { "DNS", "TCP" }), "Stages/status incorrect.");
            Require(s.Steps[1].LocalAddress == "192.0.2.200" && s.FinishedAt >= s.StartedAt, "Evidence/timestamps missing.");
        });
        Test("IP literal bypasses resolver", () => { var n = new FakeNetwork(); var s = Run(n, new("127.0.0.1", 1234)); Require(n.DnsCalls == 0 && n.Ports.Single() == 1234 && s.Steps[0].Outcome == "Skipped", "IP triggered DNS or wrong port."); });
        Test("DNS failure does not connect", () =>
        {
            var n = new FakeNetwork { Resolve = _ => Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound)) }; var s = Run(n);
            Require(n.Ports.Count == 0 && s.Outcome == "Failed" && s.Steps.Single().ErrorCode.Contains("HostNotFound"), "DNS failure misreported.");
        });
        Test("DNS timeout is not user cancellation", () =>
        {
            var n = new FakeNetwork { Resolve = async ct => { await Task.Delay(Timeout.Infinite, ct); return []; } }; var s = Run(n, options: new(20, 20, 8));
            Require(s.Outcome == "Failed" && s.Steps.Single().Outcome == "Timeout" && n.Ports.Count == 0, "DNS timeout misreported.");
        });
        Test("empty resolver answer is unavailable", () => { var n = new FakeNetwork { Resolve = _ => Task.FromResult(Array.Empty<IPAddress>()) }; var s = Run(n); Require(s.Outcome == "Failed" && n.Ports.Count == 0, "Empty answer became success."); });
        Test("deduplicates resolved addresses", () =>
        {
            var n = new FakeNetwork { Resolve = _ => Task.FromResult(new[] { IPAddress.Parse("192.0.2.1"), IPAddress.Parse("192.0.2.1") }) }; var s = Run(n);
            Require(s.Addresses.Count == 1 && n.Ports.Count == 1, "Duplicate probes issued.");
        });
        Test("maximum addresses explicitly limits attempts", () =>
        {
            var n = new FakeNetwork { Resolve = _ => Task.FromResult(new[] { IPAddress.Parse("192.0.2.1"), IPAddress.Parse("192.0.2.2"), IPAddress.Parse("192.0.2.3") }) }; var s = Run(n, options: new(1000, 1000, 2));
            Require(n.Ports.Count == 2 && s.Addresses.Count == 3 && s.Outcome == "Partial" && s.Warnings.Count > 0, "Truncation hidden.");
        });
        Test("rejects unspecified/multicast resolver answers without probing", () =>
        {
            var n = new FakeNetwork { Resolve = _ => Task.FromResult(new[] { IPAddress.Any, IPAddress.Parse("224.0.0.1") }) }; var s = Run(n);
            Require(n.Ports.Count == 0 && s.Outcome == "Failed" && s.Warnings.Count > 0, "Invalid addresses probed.");
        });
        Test("mixed address results are partial not healthy", () =>
        {
            var n = new FakeNetwork { Resolve = _ => Task.FromResult(new[] { IPAddress.Parse("192.0.2.1"), IPAddress.IPv6Loopback }), Connect = (a, _) => a.AddressFamily == AddressFamily.InterNetwork ? Task.FromException<string>(new SocketException((int)SocketError.ConnectionRefused)) : Task.FromResult("::1") };
            var s = Run(n); Require(s.Outcome == "Partial" && s.Steps.Count(x => x.Outcome == "Connected") == 1 && s.Steps.Any(x => x.Outcome == "Refused"), "One success concealed another failure.");
        });
        foreach (var (error, outcome) in new[] { (SocketError.ConnectionRefused, "Refused"), (SocketError.NetworkUnreachable, "Unreachable"), (SocketError.HostUnreachable, "Unreachable"), (SocketError.AccessDenied, "Denied"), (SocketError.TimedOut, "Timeout") })
            Test("socket outcome " + error, () => { var n = new FakeNetwork { Connect = (_, _) => Task.FromException<string>(new SocketException((int)error)) }; var s = Run(n); Require(s.Outcome == "Failed" && s.Steps.Last().Outcome == outcome, "Socket evidence lost."); });
        Test("TCP timeout bounded independently", () =>
        {
            var n = new FakeNetwork { Connect = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return ""; } }; var s = Run(n, options: new(1000, 20, 8));
            Require(s.Steps.Last().Outcome == "Timeout" && s.Outcome == "Failed", "TCP timeout not recorded.");
        });
        Test("cancel before run causes no requests", () => { var n = new FakeNetwork(); using var c = new CancellationTokenSource(); c.Cancel(); var s = Run(n, ct: c.Token); Require(s.Outcome == "Cancelled" && n.DnsCalls == 0 && n.Ports.Count == 0, "Cancelled attempt performed network requests."); });
        Test("cancel after a success never retains overall success", () =>
        {
            using var c = new CancellationTokenSource(); var n = new FakeNetwork { Resolve = _ => Task.FromResult(new[] { IPAddress.Parse("192.0.2.1"), IPAddress.Parse("192.0.2.2") }), Connect = (_, _) => { c.Cancel(); return Task.FromResult("192.0.2.200"); } };
            var s = Run(n, ct: c.Token); Require(s.Outcome == "Cancelled" && n.Ports.Count == 1, "Cancellation hidden or extra connection attempted.");
        });
        Test("invalid options do not invoke network", () => { var n = new FakeNetwork(); ThrowsArgument(() => Run(n, options: new(0, 1000, 8))); ThrowsArgument(() => Run(n, options: new(1000, 1000, 0))); Require(n.DnsCalls == 0, "Invalid options performed DNS."); });
        Test("service revalidates targets", () => { var n = new FakeNetwork(); ThrowsArgument(() => Run(n, new("http://bad", 443))); Require(n.DnsCalls == 0, "UI bypass allowed bad target."); });
        Test("summary qualifies TCP-only result", () => { var s = Run(new FakeNetwork()); var text = ResourceProbeReport.Summary(s, null); Require(text.Contains("TLS") && text.Contains("HTTP") && text.Contains("DNS") && text.Contains("443"), "Summary omits measurement boundary."); });
        Test("HTML encodes all evidence", () =>
        {
            var s = new ResourceProbeSnapshot { Target = new("<script>x</script>", 443), Outcome = "Failed" }; s.Steps.Add(new("TCP", "<b>x</b>", "Failed", 1, "x", "<img src=x>"));
            var html = ResourceProbeReport.Html(s, null); Require(!html.Contains("<script>") && !html.Contains("<img src=x>") && html.Contains("&lt;"), "Evidence became markup.");
        });
        Test("JSON records distinct attempts and options", () =>
        {
            var first = Run(new FakeNetwork()); var second = Run(new FakeNetwork()); using var json = JsonDocument.Parse(ResourceProbeReport.Json(second, first));
            Require(json.RootElement.GetProperty("SchemaVersion").GetInt32() == 1 && json.RootElement.GetProperty("Current").GetProperty("Options").GetProperty("MaxAddresses").GetInt32() == 8 && json.RootElement.GetProperty("Previous").ValueKind == JsonValueKind.Object, "Incomplete snapshot envelope.");
        });
        Test("different targets are not presented as before-after repair", () =>
        {
            var first = Run(new FakeNetwork(), new("first.invalid", 443)); var second = Run(new FakeNetwork(), new("second.invalid", 80));
            Require(ResourceProbeReport.Summary(second, first).Contains("разные цели", StringComparison.OrdinalIgnoreCase), "Unrelated endpoints compared as improvement.");
        });
        Console.WriteLine($"Resource diagnostics regression: {count - failed.Count}/{count} passed.");
        foreach (var f in failed) Console.Error.WriteLine("FAIL: " + f);
        return failed.Count == 0 ? 0 : 200;
    }
    private static ResourceProbeSnapshot Run(FakeNetwork n, ResourceTarget? target = null, ResourceProbeOptions? options = null, CancellationToken ct = default)
        => new ResourceProbeService(n).RunAsync(target ?? new("example.invalid", 443), options ?? new(), null, ct).GetAwaiter().GetResult();
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void ThrowsArgument(Action action) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Expected argument rejection."); }
    private sealed class FakeNetwork : IResourceProbeNetwork
    {
        public int DnsCalls { get; private set; }
        public List<int> Ports { get; } = [];
        public Func<CancellationToken, Task<IPAddress[]>> Resolve { get; init; } = _ => Task.FromResult(new[] { IPAddress.Parse("192.0.2.1") });
        public Func<IPAddress, CancellationToken, Task<string>> Connect { get; init; } = (_, _) => Task.FromResult("192.0.2.200");
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) { DnsCalls++; return Resolve(ct); }
        public Task<string> ConnectAsync(IPAddress address, int port, CancellationToken ct) { Ports.Add(port); return Connect(address, ct); }
    }
}
