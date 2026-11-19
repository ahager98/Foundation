namespace Foundation.Idempotency;

/// <summary>Outcome of trying to claim one unit of work.</summary>
public enum ClaimOutcome
{
    /// <summary>The caller now owns the work and must complete or release the claim.</summary>
    Acquired,

    /// <summary>The work already finished within the retention window; skip it.</summary>
    AlreadyCompleted,

    /// <summary>Another worker holds an unexpired claim; skip it, the owner will finish or its lease will lapse.</summary>
    InProgress
}
