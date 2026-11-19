namespace Foundation.Idempotency;

/// <summary>
/// Process-local claim store. Atomic within one process only, so it does NOT give exactly-once effects across
/// service instances. Use it in tests and single-instance local runs; production needs a durable store. Expired and completed entries are
/// only overwritten when the same claim is made again, never removed, so memory grows with the number of distinct claims.
/// </summary>
public sealed class InMemoryIdempotencyClaimStore(TimeProvider? clock = null) : IIdempotencyClaimStore
{
    private sealed record Entry(Guid Token, bool Completed, DateTimeOffset ExpiresAt);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Scope, string Key), Entry> _entries = [];

    public ValueTask<ClaimResult> TryClaimAsync(string scope, string key, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lease, TimeSpan.Zero);
        DateTimeOffset now = _clock.GetUtcNow();
        lock (_gate)
        {
            if (_entries.TryGetValue((scope, key), out Entry? existing) && existing.ExpiresAt > now)
            {
                return ValueTask.FromResult(new ClaimResult(
                    existing.Completed ? ClaimOutcome.AlreadyCompleted : ClaimOutcome.InProgress, Guid.Empty));
            }

            Guid token = Guid.NewGuid();
            _entries[(scope, key)] = new Entry(token, Completed: false, now + lease);
            return ValueTask.FromResult(new ClaimResult(ClaimOutcome.Acquired, token));
        }
    }

    public ValueTask<bool> CompleteAsync(string scope, string key, Guid token, TimeSpan retention, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue((scope, key), out Entry? existing) || existing.Token != token)
                return ValueTask.FromResult(false);

            _entries[(scope, key)] = existing with { Completed = true, ExpiresAt = _clock.GetUtcNow() + retention };
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> ReleaseAsync(string scope, string key, Guid token, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue((scope, key), out Entry? existing) || existing.Token != token || existing.Completed)
                return ValueTask.FromResult(false);

            _entries.Remove((scope, key));
            return ValueTask.FromResult(true);
        }
    }
}
