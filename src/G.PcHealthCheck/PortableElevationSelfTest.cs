using System.Diagnostics;
using System.Reflection;

namespace G.PcHealthCheck;

internal static class PortableElevationSelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        var count = 0;
        void Test(string name, Action action)
        {
            count++;
            try { action(); }
            catch (Exception ex) { failures.Add(name + ": " + (ex.InnerException?.Message ?? ex.Message)); }
        }
        var session = "00000000-1111-2222-3333-444444444444";
        var pipe = "GPcHealthCheck-" + session;
        var nonce = new string('A', 64);
        string[] Args(string actions, string? pipeOverride = null, string? nonceOverride = null) =>
            ["--worker", "--session", session, "--actions", actions, "--pipe", pipeOverride ?? pipe, "--nonce", nonceOverride ?? nonce];
        Test("portable worker validates session instead of rejecting its location", () =>
            Require(RemediationWorker.Run(["--worker"]) == 20, "Expected missing-session code 20, not a location rejection."));
        Test("portable worker rejects invalid pipe", () =>
            Require(RemediationWorker.Run(Args("Dism", "bad-pipe")) == 24, "Pipe validation changed."));
        Test("portable worker rejects invalid nonce", () =>
            Require(RemediationWorker.Run(Args("Sfc", nonceOverride: "bad-nonce")) == 25, "Nonce validation changed."));
        foreach (var action in new[] { "", "NotAllowed", "CleanTemp", "Dism,NotAllowed", "Sfc,CleanTemp" })
            Test("portable worker rejects action list [" + action + "]", () =>
                Require(RemediationWorker.Run(Args(action)) == 21, "Forbidden action list was not rejected with code 21."));
        Test("obsolete bootstrap remains disabled without copying or elevation", () =>
            Require(RemediationWorker.RunBootstrap(["--bootstrap-worker"]) == 48, "Legacy bootstrap was re-enabled."));

        // Resolve the planned factory dynamically so this test-only commit compiles
        // against 0.5.1 and fails on behavior, not a missing compile-time symbol.
        foreach (var path in new[]
        {
            @"C:\Users\Synthetic\Downloads\G-PC-Health-Check.exe",
            @"C:\Users\Synthetic\Downloads\Проверка ПК (1).exe",
            @"D:\Portable tools\My renamed utility.exe",
            @"C:\Program Files\Other folder\check.exe",
            @"E:\health.exe",
            @"\\synthetic-server\tools\Проверка ПК.exe",
            @"D:\A & B\tool [test].exe"
        })
            Test("UAC launch preserves exact executable path: " + path, () =>
            {
                var info = StartInfo(path, "--worker --session synthetic");
                Require(info.FileName == path, "Executable name/location was rewritten.");
                Require(info.UseShellExecute && info.Verb == "runas", "UAC elevation was bypassed.");
                Require(info.Arguments == "--worker --session synthetic", "Arguments were changed or a command shell was inserted.");
                Require(info.WorkingDirectory == Environment.SystemDirectory, "Elevated worker inherits a writable working directory.");
            });
        foreach (var path in new[] { "", " ", "relative.exe", @"C:relative.exe" })
            Test("unresolved executable identity is rejected: " + path, () =>
            {
                try { StartInfo(path, "--worker"); }
                catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { return; }
                throw new InvalidOperationException("An empty/relative executable path was accepted.");
            });
        Console.WriteLine($"Portable elevation regression: {count - failures.Count}/{count} passed.");
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        return failures.Count == 0 ? 0 : 180;
    }

    private static ProcessStartInfo StartInfo(string path, string arguments)
    {
        var method = typeof(RemediationWorker).GetMethod("CreateElevationStartInfo", BindingFlags.Static | BindingFlags.NonPublic);
        Require(method is not null, "Portable elevation launch factory is not implemented.");
        return (ProcessStartInfo)method!.Invoke(null, [path, arguments])!;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
