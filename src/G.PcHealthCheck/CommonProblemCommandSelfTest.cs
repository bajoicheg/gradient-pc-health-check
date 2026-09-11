using System.Text.Json;

namespace G.PcHealthCheck;

internal static class CommonProblemCommandSelfTest
{
    public static int Run()
    {
        try
        {
            var started = DateTime.Now.AddSeconds(-1);
            var result = CommonProblemCommandResult.UnconfirmedDnsFlush(started, false, new InvalidOperationException("<private-error-text>"));
            var failed = result.Actions.Single();
            if (failed.Success || failed.ExitCode is not null || failed.Id != "FlushDns" || failed.StartedAt != started) return 151;
            if (failed.Message.Contains("<private-error-text>", StringComparison.Ordinal) || failed.FinishedAt < started) return 152;
            using var json = JsonDocument.Parse(CommonProblemsReport.Json(new CommonProblemSnapshot(), null, result));
            var recorded = json.RootElement.GetProperty("LastCommand").GetProperty("Actions")[0];
            if (recorded.GetProperty("Success").GetBoolean() || recorded.GetProperty("ExitCode").ValueKind != JsonValueKind.Null) return 153;
            var summary = CommonProblemsReport.Summary(new CommonProblemSnapshot(), null, result);
            if (!summary.Contains("не подтверждены", StringComparison.Ordinal)) return 154;
            if (!Guid.TryParse(result.SessionId, out _) || result.Elevated) return 155;
            Console.WriteLine("Common problems command evidence: unconfirmed attempt, nullable exit code, redacted error and export passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Common problems command evidence failed: " + ex);
            return 159;
        }
    }
}
