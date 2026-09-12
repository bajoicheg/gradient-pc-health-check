namespace G.PcHealthCheck;

internal enum ReviewCollectionState { Complete, Partial, Unavailable, Missing }

internal sealed class TempCandidate
{
    public string Path { get; set; } = "";
    public long Bytes { get; set; }
    public DateTime LastWriteTime { get; set; }
}

internal sealed class TempPreviewSnapshot
{
    public string Root { get; set; } = "";
    public int OlderThanDays { get; set; }
    public DateTime Cutoff { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public ReviewCollectionState State { get; set; } = ReviewCollectionState.Unavailable;
    public long VisitedEntries { get; set; }
    public long CandidateFiles { get; set; }
    public long CandidateBytes { get; set; }
    public long SkippedLinks { get; set; }
    public long Errors { get; set; }
    public int MaxEntries { get; set; }
    public int MaxRows { get; set; }
    public double TimeLimitSeconds { get; set; }
    public List<TempCandidate> LargestFiles { get; set; } = [];
    public List<string> Issues { get; set; } = [];
    public long OmittedIssues { get; set; }
}

internal sealed class ReviewSource
{
    public string Name { get; set; } = "";
    public string Scope { get; set; } = "";
    public ReviewCollectionState State { get; set; }
    public string Detail { get; set; } = "";
    public int Items { get; set; }
}

internal sealed class StartupReviewEntry
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Source { get; set; } = "";
    public string State { get; set; } = "Не определено";
}

internal sealed class StartupReviewSnapshot
{
    public DateTime CollectedAt { get; set; }
    public string Account { get; set; } = "";
    public ReviewCollectionState State { get; set; } = ReviewCollectionState.Unavailable;
    public List<StartupReviewEntry> Entries { get; set; } = [];
    public List<ReviewSource> Sources { get; set; } = [];
    public List<string> Issues { get; set; } = [];
}
