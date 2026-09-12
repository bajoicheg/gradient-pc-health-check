using System.Management;
using System.Reflection;
using System.Text.Json;

namespace G.PcHealthCheck;

internal static class StorageCompletionSelfTest
{
    public static int Run()
    {
        var count = 0; var failures = new List<string>();
        void Test(string name, Action action)
        {
            count++;
            try { action(); } catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); }
        }
        static System.Management.EnumerationOptions Options()
            => (System.Management.EnumerationOptions)typeof(WindowsDiskDetailsSource)
                .GetMethod("Options", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        Test("projected disk query explicitly requests locatable object identity", () =>
        {
            Require(Options().EnsureLocatable, "Query does not require the instance path needed by GetRelated.");
        });
        Test("actual projected WMI query retains a usable object path", () =>
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope("root\\CIMV2"),
                new ObjectQuery("SELECT Caption FROM Win32_OperatingSystem"), Options());
            using var rows = searcher.Get();
            var countRows = 0;
            foreach (ManagementObject row in rows)
            {
                using (row)
                {
                    countRows++;
                    Require(!string.IsNullOrWhiteSpace(row.Path.RelativePath), "Projected instance lost its relative WMI path.");
                }
            }
            Require(countRows > 0, "Operating system provider returned no instance.");
        });
        Test("search reaches folders beyond display cap and export retains them", () =>
        {
            using var form = new StorageReviewForm(true);
            var data = new FolderUsageSnapshot { Root = "C:\\Synthetic", Outcome = "Partial" };
            for (var i = 0; i < 2002; i++) data.Folders.Add(new() { Path = $"C:\\Synthetic\\row{i:D4}", Bytes = 2002 - i });
            form.DisplaySnapshot(data);
            var grid = (DataGridView)form.Controls.Find("StorageEvidence", true).Single();
            Require(grid.Rows.Count == 2000, "Display cap not enforced.");
            var search = (TextBox)form.Controls.Find("StorageSearch", true).Single();
            search.Text = "row2001";
            Require(grid.Rows.Count == 1 && grid.Rows[0].Cells["Path"].Value?.ToString()?.EndsWith("row2001", StringComparison.Ordinal) == true,
                "Search only used displayed rows.");
            using var json = JsonDocument.Parse(StorageReviewReport.Json(data));
            Require(json.RootElement.GetProperty("Snapshot").GetProperty("Folders").GetArrayLength() == 2002, "Search truncated export.");
        });
        Test("disk window prioritizes fault and preserves absent counters", () =>
        {
            using var form = new StorageReviewForm(false);
            form.DisplaySnapshot(new DiskDetailsSnapshot { Disks =
            [
                new() { DeviceId = "1", Name = "Unknown synthetic disk", Health = null },
                new() { DeviceId = "2", Name = "Faulted synthetic disk", Health = 2 }
            ] });
            var grid = (DataGridView)form.Controls.Find("StorageEvidence", true).Single();
            Require(grid.Rows[0].Cells["Attention"].Value?.ToString() == "CRIT", "Known fault not first.");
            Require(grid.Rows[0].Cells["Wear"].Value is null && grid.Rows[0].Cells["Hours"].Value is null, "Missing counters became zeros.");
        });
        Test("stopped empty folder remains explicitly incomplete in reports", () =>
        {
            var data = new FolderUsageSnapshot { Root = "C:\\Synthetic", Outcome = "Stopped", Folders = [new() { Path = "C:\\Synthetic", Incomplete = true }] };
            var summary = StorageReviewReport.Summary(data);
            Require(summary.Contains("Stopped", StringComparison.Ordinal) && summary.Contains("неполные", StringComparison.OrdinalIgnoreCase), "Stop became a complete empty-folder verdict.");
            using var json = JsonDocument.Parse(StorageReviewReport.Json(data));
            Require(json.RootElement.GetProperty("Snapshot").GetProperty("Outcome").GetString() == "Stopped", "JSON lost stop outcome.");
        });
        Console.WriteLine($"Storage completion review: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 232;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
