using Foundation.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace Foundation.Persistence;

/// <summary>
/// Durable <see cref="IIdempotencyClaimStore"/> on the service database. The claim is an insert guarded by the unique
/// claim-identity index, so exactly one instance wins; a lapsed claim is taken over with a conditional update. It uses
/// its own short-lived <typeparamref name="TContext"/> from the factory so claim writes never commit, or roll back
/// with, the handler's business changes.
/// <para>
/// Residual duplicate window: because the claim commits separately from the side effect, a worker that crashes after the
/// side effect but before <c>CompleteAsync</c>, or whose lease lapses mid-processing and is taken over, can have the item
/// run again. <c>CompleteAsync</c> reports a lost claim (returns false) so the handler can surface it; there is no lease
/// renewal and no way to enlist in the handler's own transaction. Exactly-once effects therefore also need the outbound
/// provider to honour an idempotency key (event id plus item key).
/// </para>
/// </summary>
public sealed class EfIdempotencyClaimStore<TContext>(IDbContextFactory<TContext> factory, TimeProvider? clock = null)
    : IIdempotencyClaimStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public ValueTask<ClaimResult> TryClaimAsync(string scope, string key, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lease, TimeSpan.Zero);
        return TryClaimAsync(scope, key, lease, retriesLeft: 1, cancellationToken);
    }

    // retriesLeft covers the purge race on the takeover path: the lapsed row can be deleted between the read and the
    // conditional update, in which case nobody holds the claim and the whole attempt is repeated once.
    private async ValueTask<ClaimResult> TryClaimAsync(
        string scope, string key, TimeSpan lease, int retriesLeft, CancellationToken cancellationToken)
    {
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        Guid token = Guid.NewGuid();
        string identity = IdentityOf(scope, key);

        await using TContext db = await _factory.CreateDbContextAsync(cancellationToken);

        // Read first: a redelivery (the designed, common outcome of at-least-once delivery) then costs an indexed SELECT
        // instead of a failed INSERT, which EF would log as an error and the server count as a unique-key violation. The
        // unique index stays the backstop for two instances that both pass the read.
        IdempotencyClaim? existing = await ReadAsync(db, identity, cancellationToken);
        if (existing is null)
        {
            DbUpdateException insertFailure;
            await using (TContext insertDb = await _factory.CreateDbContextAsync(cancellationToken))
            {
                insertDb.Set<IdempotencyClaim>().Add(new IdempotencyClaim
                {
                    Scope = scope, Key = key, Identity = identity, Token = token, Completed = false, ExpiresUtc = now + lease
                });
                try
                {
                    await insertDb.SaveChangesAsync(cancellationToken);
                    return new ClaimResult(ClaimOutcome.Acquired, token);
                }
                catch (DbUpdateException ex)
                {
                    insertFailure = ex;
                }
            }

            // Another instance won the race, or the row it conflicted with was purged between the failed insert and this
            // read. In the second case the whole attempt is repeated once; if the insert still fails with no row to
            // explain it, the original failure is rethrown unchanged (type and classification intact), never reported
            // as a held claim.
            existing = await ReadAsync(db, identity, cancellationToken);
            if (existing is null)
            {
                if (retriesLeft > 0)
                    return await TryClaimAsync(scope, key, lease, retriesLeft - 1, cancellationToken);

                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(insertFailure).Throw();
                throw insertFailure;
            }
        }

        if (existing.ExpiresUtc > now)
        {
            return new ClaimResult(
                existing.Completed ? ClaimOutcome.AlreadyCompleted : ClaimOutcome.InProgress, Guid.Empty);
        }

        // Lapsed: take over only if nobody changed the row since we read it.
        DateTime expected = existing.ExpiresUtc;
        Guid expectedToken = existing.Token;
        int taken = await db.Set<IdempotencyClaim>()
            .Where(c => c.Id == existing.Id && c.Token == expectedToken && c.ExpiresUtc == expected)
            .ExecuteUpdateAsync(s => s
                .SetProperty(static c => c.Token, token)
                .SetProperty(static c => c.Completed, false)
                .SetProperty(static c => c.ExpiresUtc, now + lease), cancellationToken);

        if (taken == 1)
            return new ClaimResult(ClaimOutcome.Acquired, token);

        return retriesLeft > 0
            ? await TryClaimAsync(scope, key, lease, retriesLeft - 1, cancellationToken)
            : new ClaimResult(ClaimOutcome.InProgress, Guid.Empty);
    }

    public async ValueTask<bool> CompleteAsync(string scope, string key, Guid token, TimeSpan retention, CancellationToken cancellationToken = default)
    {
        DateTime expires = _clock.GetUtcNow().UtcDateTime + retention;
        string identity = IdentityOf(scope, key);
        await using TContext db = await _factory.CreateDbContextAsync(cancellationToken);
        int updated = await db.Set<IdempotencyClaim>()
            .Where(c => c.Identity == identity && c.Token == token)
            .ExecuteUpdateAsync(s => s
                .SetProperty(static c => c.Completed, true)
                .SetProperty(static c => c.ExpiresUtc, expires), cancellationToken);
        return updated > 0;
    }

    public async ValueTask<bool> ReleaseAsync(string scope, string key, Guid token, CancellationToken cancellationToken = default)
    {
        string identity = IdentityOf(scope, key);
        await using TContext db = await _factory.CreateDbContextAsync(cancellationToken);
        int deleted = await db.Set<IdempotencyClaim>()
            .Where(c => c.Identity == identity && c.Token == token && !c.Completed)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    private static Task<IdempotencyClaim?> ReadAsync(TContext db, string identity, CancellationToken cancellationToken) =>
        db.Set<IdempotencyClaim>().AsNoTracking().FirstOrDefaultAsync(c => c.Identity == identity, cancellationToken);

    private static string IdentityOf(string scope, string key) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{scope}\0{key}")));

    /// <summary>
    /// Deletes expired claims in batches of <paramref name="batchSize"/> (so one statement never escalates to a table lock
    /// on a table every handler inserts into) and returns the number removed. The package does not schedule this: call it
    /// from the service's periodic sweep, once per tenant database under DatabasePerTenant (the factory resolves one).
    /// </summary>
    public async Task<int> PurgeExpiredAsync(int batchSize = 1000, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        int total = 0;
        while (true)
        {
            await using TContext db = await _factory.CreateDbContextAsync(cancellationToken);
            List<long> ids = await db.Set<IdempotencyClaim>()
                .Where(c => c.ExpiresUtc <= now)
                .OrderBy(static c => c.Id)
                .Select(static c => c.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0)
                return total;

            // Re-assert the expiry: a row selected above may have been taken over or completed since, and must survive.
            total += await db.Set<IdempotencyClaim>()
                .Where(c => ids.Contains(c.Id) && c.ExpiresUtc <= now)
                .ExecuteDeleteAsync(cancellationToken);
            if (ids.Count < batchSize)
                return total;
        }
    }
}
