namespace G.PcHealthCheck;

// Synthetic acceptance cases for the four findings from the previous source review.
internal static class FileUseReviewSelfTest
{
    public static int Run()
    {
        var total = 0; var failures = new List<string>();
        void Test(string name, Action action)
        {
            total++;
            try { action(); } catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
        }
        foreach (var name in new[] { "CON", "nul.txt", "PRN.log", "AUX", "COM1.txt", "LPT9", "COM¹.bin", "LPT².log", "LPT³" })
            Test("reject reserved DOS component " + name, () => Throws<ArgumentException>(() => FileUseCore.NormalizeTarget(@"C:\Temp\" + name)));
        Test("reject reserved intermediate DOS component", () => Throws<ArgumentException>(() => FileUseCore.NormalizeTarget(@"C:\Temp\NUL\report.txt")));
        foreach (var name in new[] { "CONSOLE.txt", "COM10.txt", "NULish.txt" })
            Test("ordinary lookalike remains valid " + name, () => Require(FileUseCore.NormalizeTarget(@"C:\Temp\" + name) == @"C:\Temp\" + name, "Ordinary filename rejected or changed."));
        Test("invalid early record must not suppress later valid same-PID identity", () =>
        {
            var f = new Source { Rows = [FileUseSelfTest.Row() with { StartFileTime = 0 }, FileUseSelfTest.Row()] };
            var result = Collect(f);
            Require(result.ListCompleted && result.Processes.Count == 2, "RM list lost.");
            Require(result.Processes[0].IdentityState == "Invalid", "Invalid source record guessed.");
            Require(result.Processes[1].IdentityState == "Matched" && f.IdentityCalls == 1, "Skipped invalid identity suppressed valid metadata query.");
        });
        Test("successful result cannot exceed supplied capacity", () =>
        {
            var f = new Source { SuccessWithoutCapacity = true }; var result = Collect(f);
            Require(!result.ListCompleted && result.State == "Unavailable" && result.Processes.Count == 0, "Oversized result accepted.");
            Require(f.IdentityCalls == 0 && f.EndCalls == 1, "Invalid result enriched or cleanup missed.");
        });
        Test("native cancellation preserves Windows error code", () =>
        {
            var f = new Source { ReadCode = 1223 }; var result = Collect(f);
            Require(result.State == "Cancelled" && result.ErrorCode == 1223 && !result.ListCompleted, "Native cancellation code lost.");
            Require(f.EndCalls == 1, "Native cancellation leaked RM session.");
        });
        Test("EndSession exception preserves list and records uncertainty", () =>
        {
            var f = new Source { EndThrows = true }; var result = Collect(f);
            Require(result.ListCompleted && result.Processes.Count == 1 && result.State == "Partial", "Cleanup exception hid list or clean success claimed.");
            Require(f.EndCalls == 1 && result.EndSessionCode is null && result.Warnings.Count > 0, "Cleanup uncertainty lost.");
        });
        Test("distinct services sharing process retain both RM records", () =>
        {
            var f = new Source { Rows = [FileUseSelfTest.Row() with { ServiceName = "SyntheticA" }, FileUseSelfTest.Row() with { ServiceName = "SyntheticB" }] };
            var result = Collect(f);
            Require(result.ListCompleted && result.Processes.Select(x => x.ServiceName).SequenceEqual(new[] { "SyntheticA", "SyntheticB" }), "Shared PID collapsed records.");
            Require(result.Processes.All(x => x.IdentityState == "Matched") && f.IdentityCalls == 1, "Valid identity not reused.");
        });
        Test("cancellation after registration ends exactly one session", () =>
        {
            using var c = new CancellationTokenSource(); var f = new Source { Registered = c.Cancel };
            var result = FileUseService.Collect(f, @"C:\Temp\synthetic.txt", null, c.Token);
            Require(result.State == "Cancelled" && !result.ListCompleted && f.EndCalls == 1 && f.ReadCalls == 0, "Post-registration cancellation mishandled.");
        });
        Test("maximum retained record count accepted without truncation", () =>
        {
            var f = new Source { Rows = Enumerable.Range(0, 1024).Select(i => FileUseSelfTest.Row() with { ServiceName = "Synthetic" + i }).ToList() };
            var result = Collect(f);
            Require(result.ListCompleted && result.ReportedCount == 1024 && result.Processes.Count == 1024, "Exact limit rejected/truncated.");
            Require(f.ReadCalls == 2 && f.IdentityCalls == 1 && f.EndCalls == 1, "Bounded protocol or cleanup changed.");
        });
        Console.WriteLine($"File-use review acceptance: {total - failures.Count}/{total} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 247;
    }
    private static FileUseSnapshot Collect(Source source) => FileUseService.Collect(source, @"C:\Temp\synthetic.txt", FileUseSelfTest.Snapshot().ExecutionContext, CancellationToken.None);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Source : IFileUseSource
    {
        public List<FileUseProcess> Rows { get; init; } = [FileUseSelfTest.Row()];
        public bool SuccessWithoutCapacity { get; init; }
        public bool EndThrows { get; init; }
        public uint ReadCode { get; init; }
        public Action? Registered { get; init; }
        public int ReadCalls { get; private set; }
        public int IdentityCalls { get; private set; }
        public int EndCalls { get; private set; }
        public void CheckFile(string path, CancellationToken ct) => ct.ThrowIfCancellationRequested();
        public uint StartSession(out uint handle) { handle = 0; return 0; }
        public uint RegisterFile(uint handle, string path) { Registered?.Invoke(); return 0; }
        public FileUseBatch ReadList(uint handle, int capacity)
        {
            ReadCalls++;
            if (ReadCode != 0) return new(ReadCode, 0, 0, 0, []);
            if (!SuccessWithoutCapacity && capacity < Rows.Count) return new(234, (uint)Rows.Count, 0, 0, []);
            return new(0, (uint)Rows.Count, (uint)Rows.Count, 0, Rows);
        }
        public uint EndSession(uint handle) { EndCalls++; if (EndThrows) throw new IOException("Synthetic cleanup error"); return 0; }
        public FileUseIdentity? ReadIdentity(uint pid) { IdentityCalls++; return new(pid, FileUseSelfTest.Start, @"C:\Synthetic\example.exe"); }
    }
}
