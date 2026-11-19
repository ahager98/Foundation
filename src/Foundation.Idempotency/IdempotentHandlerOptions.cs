namespace Foundation.Idempotency;

/// <summary>Tunables for an idempotent handler.</summary>
public sealed class IdempotentHandlerOptions
{
    /// <summary>How long an unfinished claim blocks others before it can be taken over (worker crash). Must exceed the slowest item.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a completed claim keeps suppressing duplicates. Defaults to 24 hours.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Attempts to record completion after the side effect ran, before the item is reported failed.</summary>
    public int CompleteAttempts { get; set; } = 3;

    /// <summary>Pause between completion attempts.</summary>
    public TimeSpan CompleteRetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Time limit for each completion attempt.</summary>
    public TimeSpan CompleteTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Rejects values that would silently disable the guarantee (a claim that never holds, completion that always times out).</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Lease, TimeSpan.Zero, nameof(Lease));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Retention, TimeSpan.Zero, nameof(Retention));
        ArgumentOutOfRangeException.ThrowIfLessThan(CompleteAttempts, 1, nameof(CompleteAttempts));
        ArgumentOutOfRangeException.ThrowIfLessThan(CompleteRetryDelay, TimeSpan.Zero, nameof(CompleteRetryDelay));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(CompleteTimeout, TimeSpan.Zero, nameof(CompleteTimeout));
    }
}
