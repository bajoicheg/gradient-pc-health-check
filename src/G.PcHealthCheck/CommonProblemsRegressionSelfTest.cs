namespace G.PcHealthCheck;

internal static class CommonProblemsRegressionSelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        var count = 0;
        void Test(string name, Action action)
        {
            count++;
            try { action(); } catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
        }
        void Network(string name, string[] addresses, string[] dns, string expected)
            => Test(name, () =>
            {
                var d = Complete(); d.Adapters = [Adapter(addresses, dns)];
                Require(Row(d, "NET.CONFIG").Status == expected, "Incorrect network classification.");
            });

        Test("uncollected telemetry cannot be healthy", () =>
        {
            var rows = CommonProblemsAssessment.Assess(new());
            Require(rows.Count == 3 && rows.All(x => x.Status == "UNKNOWN"), "Missing sections must remain UNKNOWN.");
        });
        Test("collection failures cannot reuse stale rows", () =>
        {
            var d = Complete(); d.NetworkState = ProbeState.Failed; d.NetworkError = "synthetic failure";
            d.Adapters = [Adapter(["192.0.2.10"], ["192.0.2.1"])];
            Require(Row(d, "NET.CONFIG").Status == "UNKNOWN", "Failed collector reused data.");
            Require(!CommonProblemsAssessment.CanOfferDnsFlush(d), "Failed collection offered an action.");
        });
        Test("no active NIC is an observation not internet RCA", () =>
        {
            var d = Complete(); d.Adapters = [new NetworkProbe { Name = "disabled", IsUp = false }];
            Require(Row(d, "NET.CONFIG").Status == "WARN", "No active interface was missed.");
        });
        Network("APIPA-only", ["169.254.10.20"], ["192.0.2.1"], "WARN");
        Network("link-local IPv6 only", ["fe80::1234"], ["fe80::1"], "WARN");
        Network("IPv6-only is supported", ["2001:db8::10"], ["2001:db8::53"], "INFO");
        Network("private IPv6 supported", ["fd00::10"], ["fd00::53"], "INFO");
        Network("APIPA with usable IPv6", ["169.254.1.2", "2001:db8::10"], ["2001:db8::53"], "INFO");
        Network("IPv4 with DNS is not proof of internet access", ["192.0.2.10"], ["192.0.2.53"], "INFO");
        Network("missing DNS", ["192.0.2.10"], [], "WARN");
        Network("invalid address is not usable", ["garbage", "0.0.0.0", "127.0.0.1", "224.0.0.1"], ["192.0.2.1"], "WARN");
        Network("loopback DNS may be a legitimate local resolver", ["192.0.2.10"], ["127.0.0.1"], "INFO");
        Test("secondary APIPA does not hide working NIC configuration", () =>
        {
            var d = Complete(); d.Adapters = [Adapter(["169.254.1.2"], []), Adapter(["192.0.2.10"], ["192.0.2.53"])];
            Require(Row(d, "NET.CONFIG").Status == "INFO", "Secondary NIC created global network failure.");
            Require(CommonProblemsAssessment.CanOfferDnsFlush(d), "Valid network lost optional DNS action.");
        });
        Test("APIPA cannot suggest DNS cleanup", () =>
        {
            var d = Complete(); d.Adapters = [Adapter(["169.254.1.2"], ["192.0.2.53"])];
            Require(!CommonProblemsAssessment.CanOfferDnsFlush(d), "DNS cleanup was offered for missing usable address.");
        });
        Test("missing resolver cannot suggest DNS cleanup", () =>
        {
            var d = Complete(); d.Adapters = [Adapter(["192.0.2.10"], [])];
            Require(!CommonProblemsAssessment.CanOfferDnsFlush(d), "DNS cleanup was offered without a configured resolver.");
        });
        Test("no installed printers is not a fault", () => Require(Row(Complete(), "PRINT.CONFIG").Status == "INFO", "No printers reported as failure."));
        Test("default printer offline flag is visible", () =>
        {
            var d = Complete(); d.Printers = [new PrinterProbe { Name = "Synthetic printer", IsDefault = true, WorkOffline = true }];
            Require(Row(d, "PRINT.CONFIG").Status == "WARN", "Default offline mode was missed.");
        });
        Test("nondefault disconnected printer is informational", () =>
        {
            var d = Complete(); d.Printers = [new PrinterProbe { Name = "Unused", WorkOffline = true, PrinterStatus = 7 }];
            Require(Row(d, "PRINT.CONFIG").Status == "INFO", "Unused offline printer became a global failure.");
        });
        Test("printer status unknown is not ready", () =>
        {
            var d = Complete(); d.Printers = [new PrinterProbe { IsDefault = true, PrinterStatus = 2, ErrorState = 0 }];
            Require(Row(d, "PRINT.CONFIG").Status == "UNKNOWN", "Unknown printer status was shown as ready.");
        });
        Test("idle status is not physical reachability proof", () =>
        {
            var d = Complete(); d.Printers = [new PrinterProbe { IsDefault = true, PrinterStatus = 3, WorkOffline = false, ErrorState = 2 }];
            Require(Row(d, "PRINT.CONFIG").Status == "INFO", "Driver-reported state overclaimed successful printing.");
        });
        Test("stopped spooler with installed printers requires attention", () =>
        {
            var d = Complete(); d.SpoolerState = "Stopped"; d.Printers = [new PrinterProbe { Name = "Synthetic" }];
            Require(Row(d, "PRINT.CONFIG").Status == "WARN", "Stopped spooler was missed.");
        });
        Test("failed printer query remains unknown", () =>
        {
            var d = Complete(); d.PrinterState = ProbeState.Failed;
            Require(Row(d, "PRINT.CONFIG").Status == "UNKNOWN", "Failed printer query looked healthy.");
        });
        Test("empty PnP error list is a collected result", () => Require(Row(Complete(), "DEVICE.CONFIG").Status == "OK", "Successful empty error query was lost."));
        foreach (var code in new[] { 10, 14, 28, 31, 43 })
        {
            var captured = code;
            Test("present PnP error " + code, () =>
            {
                var d = Complete(); d.Devices = [new DeviceProbe { Name = "Synthetic", Present = true, ErrorCode = captured }];
                Require(Row(d, "DEVICE.CONFIG").Status == "WARN", "Actionable PnP error was missed.");
                Require(Row(d, "DEVICE.CONFIG").Evidence.Contains(captured.ToString(), StringComparison.Ordinal), "Error code absent from evidence.");
            });
        }
        foreach (var code in new[] { 22, 24, 45 })
        {
            var captured = code;
            Test("disabled/disconnected device " + code, () =>
            {
                var d = Complete(); d.Devices = [new DeviceProbe { Present = captured == 22, ErrorCode = captured }];
                Require(Row(d, "DEVICE.CONFIG").Status == "INFO", "Disabled/disconnected device treated as confirmed failure.");
            });
        }
        Test("unknown device presence is not confirmed hardware failure", () =>
        {
            var d = Complete(); d.Devices = [new DeviceProbe { Present = null, ErrorCode = 43 }];
            Require(Row(d, "DEVICE.CONFIG").Status == "UNKNOWN", "Unknown presence became confirmed device failure.");
        });
        Test("failed PnP query is not healthy", () =>
        {
            var d = Complete(); d.DeviceState = ProbeState.Failed;
            Require(Row(d, "DEVICE.CONFIG").Status == "UNKNOWN", "Failed PnP query was hidden.");
        });
        Test("guidance and evidence exist on every result", () =>
        {
            var rows = CommonProblemsAssessment.Assess(Complete());
            Require(rows.Count == 3, "Expected three independent categories.");
            Require(rows.All(x => x.Id.Length > 0 && x.Evidence.Length > 0 && x.Resolution.Length > 0), "Unexplained result.");
        });
        Test("evaluation does not mutate telemetry", () =>
        {
            var d = Complete(); var a = Adapter(["169.254.1.2"], []); d.Adapters = [a];
            CommonProblemsAssessment.Assess(d);
            Require(d.Adapters.Count == 1 && ReferenceEquals(d.Adapters[0], a) && a.Addresses[0] == "169.254.1.2", "Input mutated.");
        });
        Test("null data rejected", () =>
        {
            try { CommonProblemsAssessment.Assess(null!); throw new InvalidOperationException("Null accepted."); }
            catch (ArgumentNullException) { }
        });
        Console.WriteLine($"Common problems regression: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 111;
    }

    private static CommonProblemSnapshot Complete() => new()
    {
        NetworkState = ProbeState.Complete, PrinterState = ProbeState.Complete, DeviceState = ProbeState.Complete,
        SpoolerState = "Running", Adapters = [Adapter(["192.0.2.10"], ["192.0.2.53"])]
    };
    private static NetworkProbe Adapter(string[] addresses, string[] dns) => new()
        { Name = "Synthetic NIC", IsUp = true, Addresses = addresses.ToList(), DnsServers = dns.ToList() };
    private static CommonProblemFinding Row(CommonProblemSnapshot d, string id)
        => CommonProblemsAssessment.Assess(d).Single(x => x.Id == id);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
