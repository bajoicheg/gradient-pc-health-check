namespace G.PcHealthCheck;

public enum ProbeState { NotCollected, Complete, Failed }

public sealed class CommonProblemSnapshot
{
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.Now;
    public ProbeState NetworkState { get; set; }
    public string NetworkError { get; set; } = "";
    public List<NetworkProbe> Adapters { get; set; } = [];
    public ProbeState PrinterState { get; set; }
    public string PrinterError { get; set; } = "";
    public List<PrinterProbe> Printers { get; set; } = [];
    public string SpoolerState { get; set; } = "Unknown";
    public ProbeState DeviceState { get; set; }
    public string DeviceError { get; set; } = "";
    public List<DeviceProbe> Devices { get; set; } = [];
}

public sealed class NetworkProbe
{
    public string Name { get; set; } = "";
    public bool IsUp { get; set; }
    public List<string> Addresses { get; set; } = [];
    public List<string> DnsServers { get; set; } = [];
}

public sealed class PrinterProbe
{
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool? WorkOffline { get; set; }
    public int? PrinterStatus { get; set; }
    public int? ErrorState { get; set; }
}

public sealed class DeviceProbe
{
    public string Name { get; set; } = "";
    public bool? Present { get; set; }
    public int? ErrorCode { get; set; }
}

public sealed class CommonProblemFinding
{
    public string Id { get; init; } = "";
    public string Status { get; init; } = "INFO";
    public string Topic { get; init; } = "";
    public string Title { get; init; } = "";
    public string Evidence { get; init; } = "";
    public string Resolution { get; init; } = "";
}
