using System.Net;
using System.Net.Sockets;

namespace G.PcHealthCheck;

internal static class CommonProblemsAssessment
{
    public static List<CommonProblemFinding> Assess(CommonProblemSnapshot data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return [Network(data), Printing(data), Devices(data)];
    }

    public static bool CanOfferDnsFlush(CommonProblemSnapshot data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.NetworkState == ProbeState.Complete && data.Adapters.Any(x => x.IsUp
            && x.Addresses.Any(UsableAddress) && x.DnsServers.Any(ResolverAddress));
    }

    private static CommonProblemFinding Network(CommonProblemSnapshot d)
    {
        const string id = "NET.CONFIG";
        const string topic = "Network";
        if (d.NetworkState != ProbeState.Complete)
            return Missing(id, topic, "Сеть: данных недостаточно", d.NetworkError);
        var up = d.Adapters.Where(x => x.IsUp).ToList();
        var evidence = d.Adapters.Count == 0 ? "Сетевые интерфейсы не обнаружены." : string.Join("\n", d.Adapters.Select(x =>
            $"{x.Name}: {(x.IsUp ? "активен" : "не активен")}; IP: {Values(x.Addresses)}; DNS: {Values(x.DnsServers)}"));
        if (up.Count == 0)
            return Row(id, topic, "WARN", "Нет активного сетевого интерфейса", evidence,
                "Проверьте кабель, Wi-Fi и режим полёта. Для офлайн-работы сеть может быть не нужна. Не сбрасывайте VPN и сетевые настройки автоматически.");
        var configured = up.Where(x => x.Addresses.Any(UsableAddress)).ToList();
        if (configured.Count == 0)
            return Row(id, topic, "WARN", "Не найден пригодный IP-адрес", evidence,
                "Проверьте подключение и получение адреса от DHCP, если он используется. Адреса 169.254.x.x и link-local IPv6 сами по себе не обеспечивают связь за пределами сегмента. Не выполнять release/reset во время удалённой поддержки; очистка DNS не исправляет выдачу IP.");
        if (!configured.Any(x => x.DnsServers.Any(ResolverAddress)))
            return Row(id, topic, "WARN", "Не найден настроенный DNS на интерфейсе с IP", evidence,
                "Проверьте DNS, полученные от DHCP/VPN, и требования корпоративной сети. Отдельные приложения могут использовать собственный DNS. Не подменяйте DNS публичными адресами; очистка кэша не добавит отсутствующий сервер.");
        return Row(id, topic, "INFO", "IP/DNS настроены; доступность не проверялась", evidence,
            "Проверьте именно проблемный ресурс и его имя в нужной сети/VPN. Наличие IP и DNS не доказывает доступность ресурса или интернета. Очистка DNS-кэша — только отдельное подтверждаемое действие по симптому, а не универсальный ремонт.");
    }

    private static CommonProblemFinding Printing(CommonProblemSnapshot d)
    {
        const string id = "PRINT.CONFIG";
        const string topic = "Printing";
        if (d.PrinterState != ProbeState.Complete)
            return Missing(id, topic, "Печать: данных недостаточно", d.PrinterError + $"; Spooler: {d.SpoolerState}");
        if (d.Printers.Count == 0)
            return Row(id, topic, "INFO", "Установленных принтеров не найдено", $"Принтеров: 0; Spooler: {d.SpoolerState}",
                "Если печать нужна, подключите согласованный принтер через параметры Windows или корпоративный каталог. Не запускайте отключённую политикой службу без согласования.");
        var evidence = $"Spooler: {d.SpoolerState}\n" + string.Join("\n", d.Printers.Select(x =>
            $"{x.Name}; default={x.IsDefault}; WorkOffline={x.WorkOffline?.ToString() ?? "Unknown"}; PrinterStatus={x.PrinterStatus?.ToString() ?? "Unknown"}; ErrorState={x.ErrorState?.ToString() ?? "Unknown"}"));
        const string resolution = "Откройте параметры печати, выберите нужный принтер и проверьте очередь, питание и подключение. При остановленной службе выясните причину/GPO; перезапуск — только инженером после согласования. Не удалять задания и не переустанавливать драйвер автоматически. Состояние от драйвера не гарантирует физическую доступность: подтвердите пробной печатью.";
        if (d.SpoolerState.Equals("Stopped", StringComparison.OrdinalIgnoreCase))
            return Row(id, topic, "WARN", "Служба печати остановлена при наличии принтеров", evidence, resolution);
        var primary = d.Printers.FirstOrDefault(x => x.IsDefault);
        if (primary is null)
            return Row(id, topic, "INFO", "Проверьте принтер, выбранный в приложении", evidence, resolution);
        if (primary.WorkOffline == true || primary.PrinterStatus is 6 or 7 || primary.ErrorState is >= 3)
            return Row(id, topic, "WARN", "Принтер по умолчанию сообщает ограничение печати", evidence, resolution);
        if (primary.PrinterStatus is null or 1 or 2 && primary.ErrorState is null or 0 or 1)
            return Row(id, topic, "UNKNOWN", "Драйвер не сообщил достоверное состояние принтера", evidence, resolution);
        return Row(id, topic, "INFO", "Состояние принтера получено от драйвера", evidence, resolution);
    }

    private static CommonProblemFinding Devices(CommonProblemSnapshot d)
    {
        const string id = "DEVICE.CONFIG";
        const string topic = "Devices";
        if (d.DeviceState != ProbeState.Complete) return Missing(id, topic, "Устройства: данных недостаточно", d.DeviceError);
        const string resolution = "Откройте Диспетчер устройств (Win+X), найдите устройство и сверяйте код ошибки с симптомом. Для кода 14 согласуйте перезагрузку, для 28 проверьте установку драйвера. Драйверы — только из корпоративного/OEM-источника. Отключённые устройства не включать автоматически; коды 22/45 могут отражать штатную конфигурацию.";
        var evidence = d.Devices.Count == 0 ? "Запрос кодов ошибок PnP выполнен; записей с ненулевым кодом нет." :
            string.Join("\n", d.Devices.Select(x => $"{x.Name}; Present={x.Present?.ToString() ?? "Unknown"}; Code={x.ErrorCode?.ToString() ?? "Unknown"}"));
        if (d.Devices.Any(x => x.Present == true && x.ErrorCode is > 0 and not 22 and not 45))
            return Row(id, topic, "WARN", "Обнаружены коды ошибок присутствующих устройств", evidence, resolution);
        if (d.Devices.Any(x => x.Present is null || x.ErrorCode is null))
            return Row(id, topic, "UNKNOWN", "Недостаточно сведений о присутствии/коде устройства", evidence, resolution);
        if (d.Devices.Count > 0) return Row(id, topic, "INFO", "Есть отключённые или отсутствующие устройства", evidence, resolution);
        return Row(id, topic, "OK", "В запросе PnP коды ошибок не обнаружены", evidence, resolution);
    }

    private static bool UsableAddress(string text)
    {
        if (!IPAddress.TryParse(text, out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6) return !ip.IsIPv6LinkLocal && !ip.IsIPv6Multicast;
        var b = ip.GetAddressBytes();
        return b[0] != 0 && b[0] < 224 && !(b[0] == 169 && b[1] == 254);
    }

    private static bool ResolverAddress(string text)
    {
        if (!IPAddress.TryParse(text, out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;
        return ip.AddressFamily == AddressFamily.InterNetworkV6 ? !ip.IsIPv6Multicast : ip.GetAddressBytes()[0] is > 0 and < 224;
    }
    private static string Values(List<string> values) => values.Count == 0 ? "не указаны" : string.Join(", ", values);
    private static CommonProblemFinding Missing(string id, string topic, string title, string detail)
        => Row(id, topic, "UNKNOWN", title, string.IsNullOrWhiteSpace(detail) ? "Проверка не выполнена." : detail,
            "Повторите сбор. Проверьте доступность WMI/поставщика данных и права, не отключая политики защиты. Отсутствие данных не является признаком исправности.");
    private static CommonProblemFinding Row(string id, string topic, string status, string title, string evidence, string resolution)
        => new() { Id = id, Topic = topic, Status = status, Title = title, Evidence = evidence, Resolution = resolution };
}
