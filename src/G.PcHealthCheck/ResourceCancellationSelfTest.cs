using System.Net;
using System.Net.Sockets;

namespace G.PcHealthCheck;

internal static class ResourceCancellationSelfTest
{
    public static int Run()
    {
        var failures = new List<string>(); var count = 0;
        void Test(string name, Action action) { count++; try { action(); } catch (Exception ex) { failures.Add(name + ": " + ex.Message); } }
        Test("DNS socket abort caused by user cancellation remains cancellation", () =>
        {
            using var c = new CancellationTokenSource(); var n = new Network
            {
                Resolve = _ => { c.Cancel(); return Task.FromException<IPAddress[]>(new SocketException((int)SocketError.OperationAborted)); }
            };
            var s = Run(n, c.Token); Require(s.Outcome == "Cancelled" && s.Steps.Single().Stage == "DNS" && s.Steps[0].Outcome == "Cancelled", "DNS cancellation was reported as a network failure.");
        });
        Test("cancelled DNS keeps in-flight stage in evidence", () =>
        {
            using var c = new CancellationTokenSource(); var n = new Network { Resolve = _ => { c.Cancel(); return Task.FromCanceled<IPAddress[]>(c.Token); } };
            var s = Run(n, c.Token); Require(s.Outcome == "Cancelled" && s.Steps.Any(x => x.Stage == "DNS" && x.Outcome == "Cancelled"), "Cancelled DNS stage disappeared.");
        });
        Test("cancelled TCP keeps in-flight address in evidence", () =>
        {
            using var c = new CancellationTokenSource(); var n = new Network { Connect = _ => { c.Cancel(); return Task.FromCanceled<string>(c.Token); } };
            var s = Run(n, c.Token); Require(s.Outcome == "Cancelled" && s.Steps.Any(x => x.Stage == "TCP" && x.Outcome == "Cancelled" && x.Endpoint.Contains("192.0.2.1")), "Cancelled TCP address disappeared.");
        });
        Test("empty DNS answer is not marked resolved", () =>
        {
            var n = new Network { Resolve = _ => Task.FromResult(Array.Empty<IPAddress>()) }; var s = Run(n, default);
            Require(s.Outcome == "Failed" && s.Steps.Single().Outcome == "Failed" && s.Steps[0].ErrorCode == "NoAddresses", "Empty resolver answer marked successful.");
        });
        Console.WriteLine($"Resource cancellation/evidence regression: {count - failures.Count}/{count} passed.");
        foreach (var f in failures) Console.Error.WriteLine("FAIL: " + f);
        return failures.Count == 0 ? 0 : 202;
    }
    private static ResourceProbeSnapshot Run(Network network, CancellationToken ct) => new ResourceProbeService(network).RunAsync(new("example.invalid", 443), new(), null, ct).GetAwaiter().GetResult();
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private sealed class Network : IResourceProbeNetwork
    {
        public Func<CancellationToken, Task<IPAddress[]>> Resolve { get; init; } = _ => Task.FromResult(new[] { IPAddress.Parse("192.0.2.1") });
        public Func<CancellationToken, Task<string>> Connect { get; init; } = _ => Task.FromResult("192.0.2.200");
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Resolve(ct);
        public Task<string> ConnectAsync(IPAddress address, int port, CancellationToken ct) => Connect(ct);
    }
}
