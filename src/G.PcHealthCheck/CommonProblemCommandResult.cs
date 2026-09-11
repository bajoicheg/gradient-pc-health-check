namespace G.PcHealthCheck;

internal static class CommonProblemCommandResult
{
    // An exception cannot prove whether an external command started or completed.
    // Record this attempt, never retain a previous success or invent an exit code.
    public static RemediationBatchResult UnconfirmedDnsFlush(DateTime startedAt, bool elevated, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var finishedAt = DateTime.Now;
        return new RemediationBatchResult
        {
            SessionId = Guid.NewGuid().ToString("D"), StartedAt = startedAt,
            FinishedAt = finishedAt, Elevated = elevated,
            Actions =
            [
                new RemediationActionResult
                {
                    Id = "FlushDns", Success = false, ExitCode = null,
                    StartedAt = startedAt, FinishedAt = finishedAt,
                    Message = $"Запуск или завершение команды не подтверждены: {error.GetType().Name}, 0x{error.HResult:X8}. Проверьте результат; это не подтверждение отмены уже начавшегося действия."
                }
            ]
        };
    }
}
