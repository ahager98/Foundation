namespace Foundation.Idempotency;

/// <summary>
/// Durable, cross-instance claim store. <see cref="TryClaimAsync"/> MUST be atomic across every instance of the
/// service (a unique database key, Redis SET NX, ...). An <c>IDistributedCache</c> get-then-set is not atomic and
/// is not a valid implementation; an in-process store is valid only for tests and single-instance local runs.
/// </summary>
public interface IIdempotencyClaimStore
{
    /// <summary>
    /// Claims <paramref name="key"/> within <paramref name="scope"/> (tenant plus handler). A claim that was never
    /// completed and whose <paramref name="lease"/> has lapsed (worker crashed) can be taken over.
    /// </summary>
    ValueTask<ClaimResult> TryClaimAsync(string scope, string key, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the claim done for <paramref name="retention"/>. Returns false, changing nothing, when
    /// <paramref name="token"/> no longer owns the claim (the lease lapsed and another worker took it over).
    /// </summary>
    ValueTask<bool> CompleteAsync(string scope, string key, Guid token, TimeSpan retention, CancellationToken cancellationToken = default);

    /// <summary>Drops an unfinished claim so a redelivery can retry. Returns false, changing nothing, when <paramref name="token"/> no longer owns it.</summary>
    ValueTask<bool> ReleaseAsync(string scope, string key, Guid token, CancellationToken cancellationToken = default);
}
