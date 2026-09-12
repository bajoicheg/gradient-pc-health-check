using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace G.PcHealthCheck;

internal static class ResourceTargetParser
{
    public static ResourceTarget Parse(string input, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port), "TCP-порт: от 1 до 65535.");
        var host = input.Trim();
        if (host.Length > 255 || host.Any(char.IsControl)) throw Invalid();
        if (host.StartsWith('[') && host.EndsWith(']')) host = host[1..^1];
        if (IPAddress.TryParse(host, out var ip))
        {
            // Reject legacy shorthand/hex/octal IPv4 syntax rather than probing an unexpected IP.
            if (ip.AddressFamily == AddressFamily.InterNetwork && host != ip.ToString()) throw Invalid();
            if (!IsUnicast(ip)) throw Invalid();
            return new(ip.ToString(), port);
        }
        if (host.Any(c => char.IsWhiteSpace(c) || "/\\:;@*?#[]%".Contains(c))) throw Invalid();
        var ascii = new IdnMapping().GetAscii(host).ToLowerInvariant();
        var name = ascii.EndsWith('.') ? ascii[..^1] : ascii;
        if (name.Length is < 1 or > 253 || name.All(c => char.IsAsciiDigit(c) || c == '.')) throw Invalid();
        foreach (var label in name.Split('.'))
            if (label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-' || label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw Invalid();
        return new(ascii, port);
    }

    internal static bool IsUnicast(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetworkV6) return !ip.Equals(IPAddress.IPv6Any) && !ip.IsIPv6Multicast;
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes(); return b[0] is > 0 and < 224;
    }
    private static ArgumentException Invalid() => new("Введите одно имя узла или IPv4/IPv6 без URL, пути, учётных данных и диапазона. Порт задаётся отдельно.");
}

internal sealed class ResourceProbeService(IResourceProbeNetwork network)
{
    private readonly IResourceProbeNetwork _network = network ?? throw new ArgumentNullException(nameof(network));

    public async Task<ResourceProbeSnapshot> RunAsync(ResourceTarget target, ResourceProbeOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(options);
        target = ResourceTargetParser.Parse(target.Host, target.Port);
        if (options.DnsTimeoutMs is < 1 or > 10000 || options.TcpTimeoutMs is < 1 or > 10000 || options.MaxAddresses is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(options), "Тайм-ауты 1–10000 мс, адресов 1–16.");
        var result = new ResourceProbeSnapshot { Target = target, Options = options };
        try
        {
            ct.ThrowIfCancellationRequested();
            IPAddress[] addresses = [];
            if (IPAddress.TryParse(target.Host, out var literal))
            {
                addresses = [literal];
                result.Steps.Add(new("DNS", target.Host, "Skipped", 0, "", "Задан IP-адрес: разрешение имени не требуется."));
            }
            else
            {
                progress?.Report("DNS: системное разрешение имени " + target.Host + "…");
                var dns = await Attempt("DNS", target.Host, options.DnsTimeoutMs, ct, async token =>
                {
                    addresses = await _network.ResolveAsync(target.Host, token).ConfigureAwait(false);
                    return "Использован системный резолвер (возможны кэш, hosts и суффиксы поиска).";
                }).ConfigureAwait(false);
                result.Steps.Add(dns);
                if (dns.Outcome != "Resolved") { result.Outcome = "Failed"; return result; }
            }
            ct.ThrowIfCancellationRequested();
            var distinct = addresses.Distinct().ToList();
            result.Addresses = distinct.Select(x => x.ToString()).ToList();
            var valid = distinct.Where(ResourceTargetParser.IsUnicast).ToList();
            if (valid.Count != distinct.Count) result.Warnings.Add("Неадресуемые или multicast-адреса исключены из TCP-проверки.");
            if (valid.Count == 0)
            {
                result.Warnings.Add("Нет пригодных адресов для TCP-подключения. Соединения не выполнялись.");
                result.Outcome = "Failed"; return result;
            }
            if (valid.Count > options.MaxAddresses) result.Warnings.Add($"Получено пригодных адресов: {valid.Count}; проверяются только первые {options.MaxAddresses} в порядке системного резолвера.");
            foreach (var address in valid.Take(options.MaxAddresses))
            {
                ct.ThrowIfCancellationRequested();
                var endpoint = address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]:{target.Port}" : $"{address}:{target.Port}";
                progress?.Report("TCP: проверяю " + endpoint + "…");
                result.Steps.Add(await Attempt("TCP", endpoint, options.TcpTimeoutMs, ct,
                    token => _network.ConnectAsync(address, target.Port, token)).ConfigureAwait(false));
            }
            ct.ThrowIfCancellationRequested();
            var tcp = result.Steps.Where(x => x.Stage == "TCP").ToList();
            var success = tcp.Count(x => x.Outcome == "Connected");
            result.Outcome = success == 0 ? "Failed" : success == tcp.Count && result.Warnings.Count == 0 ? "Connected" : "Partial";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            result.Outcome = "Cancelled";
            result.Warnings.Add("Проверка отменена. Уже выполненные обращения не отменяются задним числом; неполный результат не подтверждает доступность всех адресов.");
        }
        finally { result.FinishedAt = DateTimeOffset.Now; }
        return result;
    }

    private static async Task<ResourceProbeStep> Attempt(string stage, string endpoint, int timeoutMs, CancellationToken ct, Func<CancellationToken, Task<string>> operation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(timeoutMs);
        var watch = Stopwatch.StartNew();
        try
        {
            var value = await operation(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            return new(stage, endpoint, stage == "DNS" ? "Resolved" : "Connected", watch.Elapsed.TotalMilliseconds, "",
                stage == "DNS" ? value : "TCP-соединение установлено и закрыто без прикладных данных.", stage == "TCP" ? value : "");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        { return new(stage, endpoint, "Timeout", watch.Elapsed.TotalMilliseconds, "DeadlineExceeded", $"Не завершено за {timeoutMs} мс. Это не доказывает блокировку межсетевым экраном."); }
        catch (SocketException ex)
        {
            var outcome = ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => "Refused",
                SocketError.TimedOut => "Timeout",
                SocketError.NetworkUnreachable or SocketError.HostUnreachable => "Unreachable",
                SocketError.AccessDenied => "Denied",
                _ => "Failed"
            };
            return new(stage, endpoint, outcome, watch.Elapsed.TotalMilliseconds, $"{ex.SocketErrorCode} ({ex.NativeErrorCode})", Explain(outcome, stage));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { return new(stage, endpoint, "Failed", watch.Elapsed.TotalMilliseconds, $"{ex.GetType().Name} (0x{ex.HResult:X8})", "Операция не завершена. Проверьте поддержку протокола, настройки и доступность системного провайдера."); }
    }
    private static string Explain(string outcome, string stage) => outcome switch
    {
        "Refused" => "Соединение отклонено. Проверьте порт и слушающую службу; отказ также может исходить от сетевого устройства.",
        "Timeout" => "Ответ не получен в срок. Проверьте маршрут, VPN, сервер и правила фильтрации; причина не установлена автоматически.",
        "Unreachable" => "ОС сообщает недоступность сети или узла. Проверьте подключение, маршрут и VPN.",
        "Denied" => "ОС отказала в доступе к сокету. Проверьте политики с ответственным инженером, не отключая защиту.",
        _ when stage == "DNS" => "Имя не удалось разрешить. Проверьте написание, корпоративный DNS/VPN и суффиксы поиска. Не подменяйте DNS публичным сервером.",
        _ => "TCP-подключение не выполнено. Сопоставьте код ошибки с симптомом и конфигурацией сервиса."
    };
}
