using System.Net;
using System.Net.Sockets;

namespace G.PcHealthCheck;

internal sealed class SystemResourceProbeNetwork : IResourceProbeNetwork
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Dns.GetHostAddressesAsync(host, ct);

    public async Task<string> ConnectAsync(IPAddress address, int port, CancellationToken ct)
    {
        // No shell, HTTP client, credentials, TLS handshake or application payload.
        using var client = new TcpClient(address.AddressFamily);
        await client.ConnectAsync(address, port, ct).ConfigureAwait(false);
        return (client.Client.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "";
    }
}
