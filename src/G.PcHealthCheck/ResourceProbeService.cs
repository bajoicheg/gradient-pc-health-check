namespace G.PcHealthCheck;

internal static class ResourceTargetParser
{
    public static ResourceTarget Parse(string input, int port) => throw new NotImplementedException("Resource target validation is not implemented.");
}
internal sealed class ResourceProbeService(IResourceProbeNetwork network)
{
    private readonly IResourceProbeNetwork _network = network;
    public Task<ResourceProbeSnapshot> RunAsync(ResourceTarget target, ResourceProbeOptions options, IProgress<string>? progress, CancellationToken ct)
    { _ = _network; throw new NotImplementedException("Resource diagnostics are not implemented."); }
}
