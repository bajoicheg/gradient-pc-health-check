using System.Text.Json;

namespace G.PcHealthCheck;

internal static class CommonProblemsPresentationSelfTest
{
    public static int Run()
    {
        try
        {
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
}
