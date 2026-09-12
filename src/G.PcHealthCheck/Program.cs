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
            if (result == 0) result = CommonProblemsRegressionSelfTest.Run();
            if (result == 0) result = CommonProblemsPresentationSelfTest.Run();
            if (result == 0) result = CommonProblemCommandSelfTest.Run();
            if (result == 0) result = SystemDiskSelectionSelfTest.Run();
            if (result == 0) result = PortableElevationSelfTest.Run();
            if (result == 0) result = ReadOnlyReviewSelfTest.Run();
            if (result == 0) result = ReadOnlyReviewUiSelfTest.Run();
            Environment.Exit(result);
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
        using var main = new MainForm();
        CommonProblemsMenu.Attach(main);
        ReadOnlyReviewMenu.Attach(main);
        Application.Run(main);
    }
}
