using System.Buffers.Binary;
using System.Globalization;
using System.Net;

namespace G.PcHealthCheck;

internal static class EndpointReviewCore
{
    public static IReadOnlyList<string> TableNames { get; } = Array.AsReadOnly(new[] { "TCP4", "TCP6", "UDP4", "UDP6" });
    public static EndpointTable Decode(byte[] bytes, string table, int limit = 5000)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (limit is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(limit));
        // OWNER_PID layouts contain only DWORDs/byte arrays: 4-byte alignment,
        // 4-byte count header, then 24/56/12/28-byte rows (not OWNER_MODULE layouts).
        var stride = table switch { "TCP4" => 24, "TCP6" => 56, "UDP4" => 12, "UDP6" => 28, _ => throw new ArgumentException("Unknown endpoint table.", nameof(table)) };
        if (bytes.Length < 4) throw new InvalidDataException("Endpoint table header is incomplete.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if ((ulong)count * (uint)stride + 4 > (ulong)bytes.Length) throw new InvalidDataException("Endpoint row count exceeds the returned buffer.");
        var tcp = table.StartsWith("TCP", StringComparison.Ordinal); var v6 = table.EndsWith('6');
        var output = new EndpointTable { Name = table, State = count > limit ? "Partial" : "Complete", ReportedCount = count };
        if (count > limit) output.Message = $"Windows вернула {count} записей; сохранены первые {limit}. Таблица неполная.";
        for (var i = 0; i < Math.Min(count, (uint)limit); i++)
        {
            var r = bytes.AsSpan(4 + i * stride, stride);
            uint? state = tcp ? BinaryPrimitives.ReadUInt32LittleEndian(r[(v6 ? 48 : 0)..]) : null;
            var a = tcp && !v6 ? 4 : 0;
            var port = v6 ? 20 : a + 4;
            var hasRemote = tcp && state != 2;
            output.Rows.Add(new EndpointRow
            {
                Table = table, Protocol = tcp ? "TCP" : "UDP", Family = v6 ? "IPv6" : "IPv4",
                LocalAddress = Address(r, a, v6), LocalPort = BinaryPrimitives.ReadUInt16BigEndian(r[port..]),
                RemoteAddress = hasRemote ? Address(r, v6 ? 24 : 12, v6) : null,
                RemotePort = hasRemote ? BinaryPrimitives.ReadUInt16BigEndian(r[(v6 ? 44 : 16)..]) : null,
                Pid = BinaryPrimitives.ReadUInt32LittleEndian(r[(stride - 4)..]), StateCode = state,
                State = state is null ? "BOUND" : TcpState(state.Value)
            });
        }
        return output;
    }
    private static string Address(ReadOnlySpan<byte> row, int offset, bool v6)
        => v6 ? new IPAddress(row.Slice(offset, 16), BinaryPrimitives.ReadUInt32BigEndian(row.Slice(offset + 16, 4))).ToString()
            : new IPAddress(row.Slice(offset, 4)).ToString();
    private static string TcpState(uint state) => state switch
    {
        1 => "CLOSED", 2 => "LISTEN", 3 => "SYN_SENT", 4 => "SYN_RECEIVED", 5 => "ESTABLISHED",
        6 => "FIN_WAIT_1", 7 => "FIN_WAIT_2", 8 => "CLOSE_WAIT", 9 => "CLOSING", 10 => "LAST_ACK",
        11 => "TIME_WAIT", 12 => "DELETE_TCB", _ => $"UNKNOWN({state})"
    };
    public static List<EndpointRow> Associate(IEnumerable<EndpointRow> rows, IEnumerable<EndpointProcess> before, IEnumerable<EndpointProcess> after)
    {
        ArgumentNullException.ThrowIfNull(rows); ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after);
        var a = before.GroupBy(x => x.Pid).ToDictionary(x => x.Key, x => x.ToArray());
        var b = after.GroupBy(x => x.Pid).ToDictionary(x => x.Key, x => x.ToArray());
        return rows.Select(row =>
        {
            var clean = row with { ProcessName = "", ProcessStartedAt = null, ProcessEvidence = "Unresolved" };
            if (row.Pid == 0 || (row.Protocol == "TCP" && row.StateCode is 1 or 11 or 12)) return clean with { ProcessEvidence = "Unavailable" };
            if (!a.TryGetValue(row.Pid, out var first) || !b.TryGetValue(row.Pid, out var last)) return clean;
            if (first.Length != 1 || last.Length != 1) return clean with { ProcessEvidence = "Ambiguous" };
            if (first[0].StartedAt != last[0].StartedAt || first[0].Name != last[0].Name) return clean with { ProcessEvidence = "Changed" };
            if (string.IsNullOrWhiteSpace(first[0].Name)) return clean;
            return clean with { ProcessName = first[0].Name, ProcessStartedAt = first[0].StartedAt, ProcessEvidence = "Stable" };
        }).ToList();
    }
    private sealed record EndpointKey(string Table, string LocalAddress, int LocalPort, string? RemoteAddress, int? RemotePort, uint Pid);
    private static EndpointKey Key(EndpointRow row) => new(row.Table, row.LocalAddress, row.LocalPort, row.RemoteAddress, row.RemotePort, row.Pid);
    public static bool Comparable(EndpointSnapshot current, EndpointSnapshot previous)
    {
        var a = current.ExecutionContext; var b = previous.ExecutionContext;
        return !string.IsNullOrWhiteSpace(current.ComputerName) && current.ComputerName.Equals(previous.ComputerName, StringComparison.OrdinalIgnoreCase)
            && current.StartedAt >= previous.FinishedAt && previous.FinishedAt >= previous.StartedAt
            && a is not null && b is not null && !string.IsNullOrWhiteSpace(a.ProcessSid) && a.ProcessSid == b.ProcessSid
            && a.SessionId >= 0 && a.SessionId == b.SessionId && a.HasAdministratorToken is not null && a.HasAdministratorToken == b.HasAdministratorToken
            && a.IsElevated is not null && a.IsElevated == b.IsElevated;
    }
    public static List<EndpointObservation> Compare(EndpointSnapshot current, EndpointSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        var result = new List<EndpointObservation>();
        var compare = previous is not null && Comparable(current, previous);
        foreach (var table in current.Tables)
        {
            var oldTables = previous?.Tables.Where(x => x.Name == table.Name).ToArray();
            var prior = oldTables is { Length: 1 } ? oldTables[0] : null;
            if (!compare || table.State != "Complete" || prior?.State != "Complete")
            {
                result.AddRange(table.Rows.Select(x => new EndpointObservation(x, previous is null ? "Baseline" : "NotCompared")));
                continue;
            }
            var old = prior.Rows.GroupBy(Key).ToDictionary(x => x.Key, x => x.ToArray());
            var now = table.Rows.GroupBy(Key).ToDictionary(x => x.Key, x => x.ToArray());
            foreach (var row in table.Rows)
            {
                var key = Key(row); old.TryGetValue(key, out var priorRows);
                if (now[key].Length > 1 || priorRows is { Length: > 1 }) result.Add(new(row, "Ambiguous"));
                else if (priorRows is null) result.Add(new(row, "Appeared"));
                else
                {
                    var previousRow = priorRows[0];
                    var change = row.ProcessEvidence == "Stable" && previousRow.ProcessEvidence == "Stable" && row.ProcessStartedAt != previousRow.ProcessStartedAt
                        ? "OwnerChanged" : row.StateCode != previousRow.StateCode ? "Changed" : "ObservedAgain";
                    result.Add(new(row, change, previousRow.State));
                }
            }
            foreach (var row in prior.Rows)
                if (!now.ContainsKey(Key(row))) result.Add(new(row, "NotObserved"));
        }
        return result;
    }
    public static bool Matches(EndpointObservation item, string query, string filter)
    {
        var row = item.Row;
        var match = filter switch
        {
            "All" => true, "TCP" => row.Protocol == "TCP", "UDP" => row.Protocol == "UDP", "LISTEN" => row.State == "LISTEN",
            "ESTABLISHED" => row.State == "ESTABLISHED", "Changes" => item.Change is "Appeared" or "NotObserved" or "Changed" or "OwnerChanged", _ => false
        };
        if (!match) return false;
        var q = query.Trim(); if (q.Length == 0) return true;
        return new[] { row.ProcessName, row.Pid.ToString(CultureInfo.InvariantCulture), row.LocalAddress, row.LocalPort.ToString(CultureInfo.InvariantCulture), row.RemoteAddress ?? "",
            row.RemotePort?.ToString(CultureInfo.InvariantCulture) ?? "", row.Table, row.State, row.ProcessEvidence, ChangeText(item.Change) }
            .Any(x => x.Contains(q, StringComparison.OrdinalIgnoreCase));
    }
    public static string ChangeText(string value) => value switch
    {
        "Baseline" => "Первый снимок", "NotCompared" => "Нет сравнения", "Appeared" => "Появилось в снимке",
        "NotObserved" => "Больше не наблюдается", "Changed" => "Изменилось состояние", "ObservedAgain" => "Наблюдается снова",
        "OwnerChanged" => "Иной процесс с тем же PID", "Ambiguous" => "Неоднозначное совпадение", _ => value
    };
    public static string ProcessText(string value) => value switch
    {
        "Stable" => "PID и время запуска совпали до/после", "Changed" => "Процесс сменился во время сбора",
        "Unavailable" => "Привязка к процессу недоступна", "Ambiguous" => "Неоднозначная идентичность процесса",
        "NotChecked" => "Сопоставление не завершено", _ => "Имя/время запуска не подтверждены"
    };
    public static string CollectionText(string value) => value switch
    { "Complete" => "Собрано", "Partial" => "Частично", "Unavailable" => "Недоступно", "Cancelled" => "Остановлено", _ => "Не собиралось" };
}
