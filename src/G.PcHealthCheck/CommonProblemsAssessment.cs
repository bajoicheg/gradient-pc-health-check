namespace G.PcHealthCheck;

internal static class CommonProblemsAssessment
{
    // Test-first contract. This draft is not releasable until the behavioral tests pass.
    public static List<CommonProblemFinding> Assess(CommonProblemSnapshot data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return [];
    }
    public static bool CanOfferDnsFlush(CommonProblemSnapshot data) => false;
}
