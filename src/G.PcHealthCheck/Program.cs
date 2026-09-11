using System.Globalization;

namespace G.PcHealthCheck;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
        if (args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var result = SelfTest.Run();
            Environment.Exit(result != 0 ? result : CommonProblemsRegressionSelfTest.Run());
            return;
        }
        if (args.Any(a => string.Equals(a, "--bootstrap-worker", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(RemediationWorker.RunBootstrap(args));
            return;
        }
        if (args.Any(a => string.Equals(a, "--worker", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(RemediationWorker.Run(args));
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
