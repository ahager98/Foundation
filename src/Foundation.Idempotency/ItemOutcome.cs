namespace Foundation.Idempotency;

/// <summary>What happened to one item.</summary>
public enum ItemOutcome
{
    Processed,

    /// <summary>Skipped: an earlier delivery already completed it.</summary>
    Duplicate,

    /// <summary>Skipped: another worker holds an unexpired claim.</summary>
    InProgress,

    /// <summary>
    /// The item did not finish cleanly. Three cases share this outcome: the item threw and its claim was released (a
    /// redelivery retries it); the item succeeded but recording completion failed; or the claim was lost to another
    /// worker mid-processing (<c>ClaimLost</c>). In the last two the item already ran and the claim is not released, so a
    /// redelivery sees <see cref="InProgress"/> until the lease lapses and may then run the item again. The
    /// <see cref="ItemResult.FailureType"/> distinguishes them (an exception type name, or <c>ClaimLost</c>).
    /// </summary>
    Failed
}
