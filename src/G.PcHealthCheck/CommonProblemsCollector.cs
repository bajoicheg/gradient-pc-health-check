using System.Management;
using System.Net.NetworkInformation;
using System.ServiceProcess;

namespace G.PcHealthCheck;

internal sealed class CommonProblemsCollector
{
    // A provider that ignores cancellation must not accumulate workers after reopening the UI.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<CommonProblemSnapshot> CollectAsync(IProgress<string>? progress, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try { return await Task.Run(() => Collect(progress, ct), ct); }
        finally { Gate.Release(); }
    }

    private static CommonProblemSnapshot Collect(IProgress<string>? progress, CancellationToken ct)
    {
        var data = new CommonProblemSnapshot();
        ct.ThrowIfCancellationRequested();
        progress?.Report("Сеть: читаю локальную конфигурацию интерфейсов…");
        try
        {
            var adapters = new List<NetworkProbe>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                ct.ThrowIfCancellationRequested();
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (adapters.Count >= 256) throw new InvalidOperationException("Network enumeration limit exceeded.");
                var item = new NetworkProbe { Name = nic.Name, IsUp = nic.OperationalStatus == OperationalStatus.Up };
                if (item.IsUp)
                {
                    var properties = nic.GetIPProperties();
                    item.Addresses = properties.UnicastAddresses.Select(x => x.Address.ToString()).ToList();
                    item.DnsServers = properties.DnsAddresses.Select(x => x.ToString()).ToList();
                }
                adapters.Add(item);
            }
            data.Adapters = adapters;
            data.NetworkState = ProbeState.Complete;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { data.NetworkState = ProbeState.Failed; data.NetworkError = Error(ex); }

        ct.ThrowIfCancellationRequested();
        progress?.Report("Печать: читаю состояние установленных принтеров…");
        try
        {
            using var spooler = new ServiceController("Spooler");
            data.SpoolerState = spooler.Status.ToString();
        }
        catch { data.SpoolerState = "Unknown"; }
        try
        {
            data.Printers = Query("SELECT Name,Default,WorkOffline,PrinterStatus,DetectedErrorState FROM Win32_Printer", item => new PrinterProbe
            {
                Name = Convert.ToString(item["Name"]) ?? "",
                IsDefault = item["Default"] is not null && Convert.ToBoolean(item["Default"]),
                WorkOffline = Boolean(item["WorkOffline"]),
                PrinterStatus = Number(item["PrinterStatus"]), ErrorState = Number(item["DetectedErrorState"])
            }, 128, ct);
            data.PrinterState = ProbeState.Complete;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { data.PrinterState = ProbeState.Failed; data.PrinterError = Error(ex); }

        ct.ThrowIfCancellationRequested();
        progress?.Report("Устройства: читаю коды ошибок Plug and Play…");
        try
        {
            data.Devices = Query("SELECT Name,Present,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0", item => new DeviceProbe
            {
                Name = Convert.ToString(item["Name"]) ?? "", Present = Boolean(item["Present"]), ErrorCode = Number(item["ConfigManagerErrorCode"])
            }, 1024, ct);
            data.DeviceState = ProbeState.Complete;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { data.DeviceState = ProbeState.Failed; data.DeviceError = Error(ex); }
        ct.ThrowIfCancellationRequested();
        data.CollectedAt = DateTimeOffset.Now;
        return data;
    }

    private static List<T> Query<T>(string query, Func<ManagementObject, T> map, int limit, CancellationToken ct)
    {
        var options = new EnumerationOptions
        {
            ReturnImmediately = true, Rewindable = false, BlockSize = 16, Timeout = TimeSpan.FromSeconds(5)
        };
        using var searcher = new ManagementObjectSearcher(new ManagementScope("root\\CIMV2"), new ObjectQuery(query), options);
        using var results = searcher.Get();
        var output = new List<T>();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                ct.ThrowIfCancellationRequested();
                if (output.Count >= limit) throw new InvalidOperationException("WMI enumeration limit exceeded.");
                output.Add(map(item));
            }
        }
        return output;
    }
    private static int? Number(object? value) => value is null ? null : Convert.ToInt32(value);
    private static bool? Boolean(object? value) => value is null ? null : Convert.ToBoolean(value);
    private static string Error(Exception ex) => $"{ex.GetType().Name}; код 0x{ex.HResult:X8}";
}
