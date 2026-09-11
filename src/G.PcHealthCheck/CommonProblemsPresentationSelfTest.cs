using System.Text.Json;

namespace G.PcHealthCheck;

internal static class CommonProblemsPresentationSelfTest
{
    public static int Run()
    {
        try
        {
            if (TestCompletionSemantics() != 0) return 140;
            foreach (var topic in new[] { "Network", "Printing", "Devices", "Storage", "Startup", "Updates", "Apps" })
                if (CommonProblemTools.UriForTopic(topic)?.StartsWith("ms-settings:", StringComparison.Ordinal) != true) return 121;
            foreach (var topic in new[] { "", "Network;calc.exe", "https://example.invalid", "ms-settings:reset", "../Network" })
                if (CommonProblemTools.UriForTopic(topic) is not null) return 122;
            var previous = new CommonProblemSnapshot { CollectedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z") };
            var current = new CommonProblemSnapshot
            {
                NetworkState = ProbeState.Complete,
                Adapters = [new NetworkProbe { Name = "<script>alert('x')</script>&", IsUp = true, Addresses = ["192.0.2.10"], DnsServers = ["192.0.2.53"] }]
            };
            var html = CommonProblemsReport.Html(current, previous, null);
            if (html.Contains("<script>", StringComparison.Ordinal) || !html.Contains("&lt;script&gt;", StringComparison.Ordinal)) return 123;
            if (!html.Contains("Предыдущий снимок", StringComparison.Ordinal) || !html.Contains("Текущий снимок", StringComparison.Ordinal)) return 124;
            using var json = JsonDocument.Parse(CommonProblemsReport.Json(current, previous, null));
            if (json.RootElement.GetProperty("SchemaVersion").GetInt32() != 1) return 125;
            if (json.RootElement.GetProperty("CurrentSnapshot").GetProperty("NetworkState").GetString() != "Complete") return 126;
            if (json.RootElement.GetProperty("PreviousSnapshot").ValueKind != JsonValueKind.Object) return 127;
            using var main = new Form { ClientSize = new Size(1100, 700) };
            using var content = new Panel { Dock = DockStyle.Fill };
            main.Controls.Add(content);
            CommonProblemsMenu.Attach(main);
            CommonProblemsMenu.Attach(main);
            main.PerformLayout();
            if (main.MainMenuStrip is not { Items.Count: 2 } || main.Controls.Count != 2) return 128;
            if (content.Top < main.MainMenuStrip.Bottom) return 129;
            using var extra = new CommonProblemsForm();
            if (extra.Controls.Count == 0 || extra.MinimumSize.Width < 800) return 130;
            Console.WriteLine("Common problems presentation: URI allow-list, HTML encoding, JSON envelope and menu layout passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Common problems presentation failure: " + ex);
            return 139;
        }
    }

    private static int TestCompletionSemantics()
    {
        var failures = new List<string>();
        var count = 0;
        void Test(string name, Action test)
        {
            count++;
            try { test(); } catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
        }
        static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
        static CommonProblemSnapshot Mixed() => new()
        {
            NetworkState = ProbeState.Failed, NetworkError = "Synthetic missing network signal",
            PrinterState = ProbeState.Complete, SpoolerState = "Running",
            DeviceState = ProbeState.Complete,
            Devices = [new DeviceProbe { Name = "Synthetic device", Present = true, ErrorCode = 43 }]
        };
        Test("actionable device before unknown network and informational printing", () =>
        {
            var rows = CommonProblemsAssessment.Assess(Mixed());
            Require(rows.Select(x => x.Status).SequenceEqual(new[] { "WARN", "UNKNOWN", "INFO" }), "Findings are not ordered by required attention.");
            Require(rows[0].Id == "DEVICE.CONFIG", "Device problem is not first.");
        });
        Test("equal attention preserves deterministic category order", () =>
        {
            var rows = CommonProblemsAssessment.Assess(new CommonProblemSnapshot());
            Require(rows.Select(x => x.Id).SequenceEqual(new[] { "NET.CONFIG", "PRINT.CONFIG", "DEVICE.CONFIG" }), "Equal-priority findings were reordered.");
        });
        Test("clipboard summary uses actionable-first order", () =>
        {
            var text = CommonProblemsReport.Summary(Mixed(), null, null);
            var warn = text.IndexOf("[WARN]", StringComparison.Ordinal);
            var unknown = text.IndexOf("[UNKNOWN]", StringComparison.Ordinal);
            var info = text.IndexOf("[INFO]", StringComparison.Ordinal);
            Require(warn >= 0 && unknown > warn && info > unknown, "Summary does not put actionable findings first.");
        });
        Test("HTML and JSON share actionable-first order", () =>
        {
            var data = Mixed();
            var html = CommonProblemsReport.Html(data, null, null);
            Require(html.IndexOf("<td>WARN</td>", StringComparison.Ordinal) < html.IndexOf("<td>UNKNOWN</td>", StringComparison.Ordinal), "HTML order differs from required attention.");
            using var json = JsonDocument.Parse(CommonProblemsReport.Json(data, null, null));
            Require(json.RootElement.GetProperty("CurrentFindings")[0].GetProperty("Id").GetString() == "DEVICE.CONFIG", "JSON first finding is not the actionable device issue.");
        });
        Test("unavailable spooler state is explicit unknown", () =>
        {
            var data = Mixed(); data.SpoolerState = "Unknown";
            data.Printers = [new PrinterProbe { Name = "Synthetic printer", IsDefault = true, PrinterStatus = 3, ErrorState = 2 }];
            var row = CommonProblemsAssessment.Assess(data).Single(x => x.Id == "PRINT.CONFIG");
            Require(row.Status == "UNKNOWN", "Unavailable spooler state was hidden by a printer reading.");
            Require(row.Evidence.Contains("Unknown", StringComparison.Ordinal), "Unknown spooler is absent from evidence.");
        });
        Test("known offline flag remains actionable despite unknown spooler", () =>
        {
            var data = Mixed(); data.SpoolerState = "Unknown";
            data.Printers = [new PrinterProbe { IsDefault = true, WorkOffline = true }];
            Require(CommonProblemsAssessment.Assess(data).Single(x => x.Id == "PRINT.CONFIG").Status == "WARN", "Missing spooler hid a positive offline observation.");
        });
        Test("no installed printers remains contextual when spooler is unknown", () =>
        {
            var data = Mixed(); data.SpoolerState = "Unknown";
            Require(CommonProblemsAssessment.Assess(data).Single(x => x.Id == "PRINT.CONFIG").Status == "INFO", "Absence of printers became a fault.");
        });
        Test("known printing fault does not change caller data", () =>
        {
            var data = Mixed(); var device = data.Devices[0];
            CommonProblemsAssessment.Assess(data);
            Require(data.Devices.Count == 1 && ReferenceEquals(device, data.Devices[0]) && device.ErrorCode == 43, "Assessment mutated input.");
        });
        Console.WriteLine($"Common problems completion regression: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 1;
    }
}
