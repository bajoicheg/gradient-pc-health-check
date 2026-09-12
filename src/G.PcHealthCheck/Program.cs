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
            if (result == 0)
            {
                var behavior = ResourceProbeSelfTest.Run();
                var integration = ResourceProbeIntegrationSelfTest.Run();
                var cancellation = ResourceCancellationSelfTest.Run();
                result = behavior != 0 ? behavior : integration != 0 ? integration : cancellation;
            }
            if (result == 0)
            {
                var behavior = IncidentReviewSelfTest.Run();
                var integration = IncidentReviewIntegrationSelfTest.Run();
                result = behavior != 0 ? behavior : integration;
            }
            if (result == 0)
            {
                var behavior = PerformanceSessionSelfTest.Run();
                var integration = PerformanceSessionUiSelfTest.Run();
                var timing = PerformanceSessionTimingSelfTest.Run();
                result = behavior != 0 ? behavior : integration != 0 ? integration : timing;
            }
            if (result == 0)
            {
                var behavior = StorageReviewSelfTest.Run();
                var integration = StorageReviewIntegrationSelfTest.Run();
                var completion = StorageCompletionSelfTest.Run();
                result = behavior != 0 ? behavior : integration != 0 ? integration : completion;
            }
            if (result == 0)
            {
                var behavior = ExecutionContextSelfTest.Run();
                var integration = ExecutionContextIntegrationSelfTest.Run();
                var actions = ExecutionContextActionSelfTest.Run();
                result = behavior != 0 ? behavior : integration != 0 ? integration : actions;
            }
            if (result == 0)
            {
                var behavior = EndpointReviewSelfTest.Run();
                var integration = EndpointReviewIntegrationSelfTest.Run();
                result = behavior != 0 ? behavior : integration;
            }
            if (result == 0)
            {
                var behavior = FileUseSelfTest.Run();
                var integration = FileUseIntegrationSelfTest.Run();
                var review = FileUseReviewSelfTest.Run();
                result = behavior != 0 ? behavior : integration != 0 ? integration : review;
            }
            if (result == 0)
            {
                var behavior = ProcessObservationSelfTest.Run();
                var integration = ProcessObservationIntegrationSelfTest.Run();
                result = behavior != 0 ? behavior : integration;
            }
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
        ResourceProbeMenu.Attach(main);
        IncidentReviewMenu.Attach(main);
        PerformanceSessionMenu.Attach(main);
        StorageReviewMenu.Attach(main);
        EndpointReviewMenu.Attach(main);
        FileUseMenu.Attach(main);
        Application.Run(main);
    }
}
