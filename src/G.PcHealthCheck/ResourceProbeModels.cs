using System.Net;

namespace G.PcHealthCheck;

internal sealed record ResourceTarget(string Host, int Port);
internal sealed record ResourceProbeOptions(int DnsTimeoutMs = 5000, int TcpTimeoutMs = 3000, int MaxAddresses = 8);
internal sealed record ResourceProbeStep(string Stage, string Endpoint, string Outcome, double ElapsedMs, string ErrorCode, string Detail, string LocalAddress = "");
internal sealed class ResourceProbeSnapshot
{
    public ResourceTarget Target { get; init; } = new("", 443);
    public ResourceProbeOptions Options { get; init; } = new();
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset FinishedAt { get; set; }
    public string Outcome { get; set; } = "NotStarted";
    public List<string> Addresses { get; set; } = [];
    public List<ResourceProbeStep> Steps { get; } = [];
    public List<string> Warnings { get; } = [];
}
internal interface IResourceProbeNetwork
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct);
    Task<string> ConnectAsync(IPAddress address, int port, CancellationToken ct);
}
