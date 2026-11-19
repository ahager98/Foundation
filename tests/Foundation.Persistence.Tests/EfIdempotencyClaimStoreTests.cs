using Foundation.Idempotency;
using Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Foundation.Persistence.Tests;

public sealed class ClaimDb(DbContextOptions<ClaimDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyFoundationIdempotencyClaims();
}

public sealed class ClaimDbFactory(string path) : IDbContextFactory<ClaimDb>
{
    public ClaimDb CreateDbContext() =>
        new(new DbContextOptionsBuilder<ClaimDb>().UseSqlite($"Data Source={path}").Options);
}

public sealed class StepClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;
    public void Advance(TimeSpan by) => _now += by;
    public override DateTimeOffset GetUtcNow() => _now;
}

// A real SQLite file is used because the InMemory provider does not enforce the unique index that makes a claim atomic.
[TestClass]
public class EfIdempotencyClaimStoreTests
{
    private string _path = string.Empty;
    private StepClock _clock = null!;
    private EfIdempotencyClaimStore<ClaimDb> _store = null!;
    private ClaimDbFactory _factory = null!;

    [TestInitialize]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), $"claims-{Guid.NewGuid():N}.db");
        _factory = new ClaimDbFactory(_path);
        using (ClaimDb db = _factory.CreateDbContext())
            db.Database.EnsureCreated();
        _clock = new StepClock(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        _store = new EfIdempotencyClaimStore<ClaimDb>(_factory, _clock);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + "*"))
            File.Delete(file);
    }

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    [TestMethod]
    public async Task FirstClaimIsAcquired_SecondIsInProgress()
    {
        ClaimResult first = await _store.TryClaimAsync("5:sms", "evt-1:a", Lease);
        ClaimResult second = await _store.TryClaimAsync("5:sms", "evt-1:a", Lease);

        Assert.AreEqual(ClaimOutcome.Acquired, first.Outcome);
        Assert.AreNotEqual(Guid.Empty, first.Token);
        Assert.AreEqual(ClaimOutcome.InProgress, second.Outcome);
    }

    [TestMethod]
    public async Task ConcurrentClaims_ExactlyOneInstanceWins()
    {
        // Separate store instances model separate service instances sharing one database.
        EfIdempotencyClaimStore<ClaimDb>[] instances =
            Enumerable.Range(0, 8).Select(_ => new EfIdempotencyClaimStore<ClaimDb>(_factory, _clock)).ToArray();

        ClaimResult[] results = await Task.WhenAll(
            instances.Select(i => i.TryClaimAsync("5:sms", "evt-1:a", Lease).AsTask()));

        Assert.AreEqual(1, results.Count(r => r.Outcome == ClaimOutcome.Acquired));
        Assert.AreEqual(7, results.Count(r => r.Outcome == ClaimOutcome.InProgress));
    }

    [TestMethod]
    public async Task Complete_MakesLaterClaimsADuplicate_UntilRetentionLapses()
    {
        ClaimResult claim = await _store.TryClaimAsync("5:sms", "evt-1:a", Lease);
        await _store.CompleteAsync("5:sms", "evt-1:a", claim.Token, TimeSpan.FromHours(24));

        Assert.AreEqual(ClaimOutcome.AlreadyCompleted, (await _store.TryClaimAsync("5:sms", "evt-1:a", Lease)).Outcome);

        _clock.Advance(TimeSpan.FromHours(25));
        Assert.AreEqual(ClaimOutcome.Acquired, (await _store.TryClaimAsync("5:sms", "evt-1:a", Lease)).Outcome);
    }

    [TestMethod]
    public async Task Release_LetsARedeliveryClaimAgain()
    {
        ClaimResult claim = await _store.TryClaimAsync("5:sms", "evt-1:a", Lease);
        await _store.ReleaseAsync("5:sms", "evt-1:a", claim.Token);

        Assert.AreEqual(ClaimOutcome.Acquired, (await _store.TryClaimAsync("5:sms", "evt-1:a", Lease)).Outcome);
    }

    [TestMethod]
    public async Task LapsedClaim_IsTakenOver_AndStaleOwnerCannotTouchIt()
    {
        ClaimResult crashed = await _store.TryClaimAsync("5:sms", "evt-1:a", Lease);
        _clock.Advance(TimeSpan.FromMinutes(6));

        ClaimResult takeover = await _store.TryClaimAsync("5:sms", "evt-1:a", Lease);
        Assert.AreEqual(ClaimOutcome.Acquired, takeover.Outcome);

        await _store.CompleteAsync("5:sms", "evt-1:a", crashed.Token, TimeSpan.FromHours(1));
        await _store.ReleaseAsync("5:sms", "evt-1:a", crashed.Token);

        Assert.AreEqual(ClaimOutcome.InProgress, (await _store.TryClaimAsync("5:sms", "evt-1:a", Lease)).Outcome);
    }

    [TestMethod]
    public async Task ConcurrentTakeoverOfALapsedClaim_HasOneWinner()
    {
        await _store.TryClaimAsync("5:sms", "evt-1:a", Lease);
        _clock.Advance(TimeSpan.FromMinutes(6));
        EfIdempotencyClaimStore<ClaimDb>[] instances =
            Enumerable.Range(0, 6).Select(_ => new EfIdempotencyClaimStore<ClaimDb>(_factory, _clock)).ToArray();

        ClaimResult[] results = await Task.WhenAll(
            instances.Select(i => i.TryClaimAsync("5:sms", "evt-1:a", Lease).AsTask()));

        Assert.AreEqual(1, results.Count(r => r.Outcome == ClaimOutcome.Acquired));
    }

    [TestMethod]
    public async Task ReleasingACompletedClaim_IsANoOp()
    {
        ClaimResult claim = await _store.TryClaimAsync("5:sms", "evt-1:a", Lease);
        await _store.CompleteAsync("5:sms", "evt-1:a", claim.Token, TimeSpan.FromHours(1));

        await _store.ReleaseAsync("5:sms", "evt-1:a", claim.Token);

        Assert.AreEqual(ClaimOutcome.AlreadyCompleted, (await _store.TryClaimAsync("5:sms", "evt-1:a", Lease)).Outcome);
    }

    [TestMethod]
    public async Task ScopesAndKeys_AreIndependent()
    {
        await _store.TryClaimAsync("5:sms", "evt-1:a", Lease);

        Assert.AreEqual(ClaimOutcome.Acquired, (await _store.TryClaimAsync("6:sms", "evt-1:a", Lease)).Outcome);
        Assert.AreEqual(ClaimOutcome.Acquired, (await _store.TryClaimAsync("5:email", "evt-1:a", Lease)).Outcome);
        Assert.AreEqual(ClaimOutcome.Acquired, (await _store.TryClaimAsync("5:sms", "evt-1:b", Lease)).Outcome);
    }

    [TestMethod]
    public async Task PurgeExpired_RemovesOnlyExpiredClaims()
    {
        await _store.TryClaimAsync("5:sms", "old", TimeSpan.FromMinutes(1));
        await _store.TryClaimAsync("5:sms", "fresh", TimeSpan.FromHours(1));
        _clock.Advance(TimeSpan.FromMinutes(10));

        int removed = await _store.PurgeExpiredAsync();

        Assert.AreEqual(1, removed);
        Assert.AreEqual(ClaimOutcome.InProgress, (await _store.TryClaimAsync("5:sms", "fresh", Lease)).Outcome);
    }

    private sealed class FailingInsertInterceptor : SaveChangesInterceptor
    {
        public int Attempts;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Attempts);
            throw new DbUpdateException("insert refused");
        }
    }

    private sealed class InterceptedFactory(string path, SaveChangesInterceptor interceptor) : IDbContextFactory<ClaimDb>
    {
        public ClaimDb CreateDbContext() =>
            new(new DbContextOptionsBuilder<ClaimDb>().UseSqlite($"Data Source={path}").AddInterceptors(interceptor).Options);
    }

    // The table exists and is empty, so only the insert fails and no row explains the failure.
    [TestMethod]
    public async Task InsertFailureThatIsNotAClaimConflict_IsRetriedOnce_ThenRethrownUnchanged()
    {
        FailingInsertInterceptor interceptor = new();
        EfIdempotencyClaimStore<ClaimDb> broken = new(new InterceptedFactory(_path, interceptor), _clock);

        DbUpdateException ex = await Assert.ThrowsExactlyAsync<DbUpdateException>(
            () => broken.TryClaimAsync("5:sms", "evt-1|a", Lease).AsTask());

        Assert.AreEqual("insert refused", ex.Message);
        Assert.AreEqual(2, interceptor.Attempts);
    }

    [TestMethod]
    public async Task IdsDifferingOnlyByCase_AreDistinctClaims()
    {
        Assert.AreEqual(ClaimOutcome.Acquired, (await _store.TryClaimAsync("5:sms", "evt-A|a", Lease)).Outcome);
        Assert.AreEqual(ClaimOutcome.Acquired, (await _store.TryClaimAsync("5:sms", "evt-a|a", Lease)).Outcome);

        await using ClaimDb db = _factory.CreateDbContext();
        List<string> identities = await db.Set<IdempotencyClaim>().Select(c => c.Identity).ToListAsync();
        Assert.AreEqual(2, identities.Distinct().Count());
        Assert.IsTrue(identities.All(i => i == i.ToLowerInvariant() && i.Length == 64));
    }

    [TestMethod]
    public async Task Complete_And_Release_ReportOwnership()
    {
        ClaimResult claim = await _store.TryClaimAsync("5:sms", "evt-1|a", Lease);

        Assert.IsFalse(await _store.CompleteAsync("5:sms", "evt-1|a", Guid.NewGuid(), TimeSpan.FromHours(1)));
        Assert.IsFalse(await _store.ReleaseAsync("5:sms", "evt-1|a", Guid.NewGuid()));
        Assert.IsTrue(await _store.CompleteAsync("5:sms", "evt-1|a", claim.Token, TimeSpan.FromHours(1)));
    }

    [TestMethod]
    public async Task PurgeExpired_RunsInBatches()
    {
        for (int i = 0; i < 5; i++)
            await _store.TryClaimAsync("5:sms", $"old-{i}", TimeSpan.FromMinutes(1));
        await _store.TryClaimAsync("5:sms", "fresh", TimeSpan.FromHours(1));
        _clock.Advance(TimeSpan.FromMinutes(10));

        int removed = await _store.PurgeExpiredAsync(batchSize: 2);

        Assert.AreEqual(5, removed);
        Assert.AreEqual(ClaimOutcome.InProgress, (await _store.TryClaimAsync("5:sms", "fresh", Lease)).Outcome);
    }

    [TestMethod]
    public async Task PurgeExpired_RejectsANonPositiveBatchSize()
    {
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => _store.PurgeExpiredAsync(batchSize: 0));
    }

    [TestMethod]
    public async Task WorksUnderTheHandlerBase_EndToEnd()
    {
        List<string> sent = [];
        EndToEndHandler handler = new(_store, sent);

        await handler.HandleAsync("evt-9", 5, ["a", "b"]);
        IdempotentBatchResult redelivery = await handler.HandleAsync("evt-9", 5, ["a", "b"]);

        CollectionAssert.AreEqual(new[] { "a", "b" }, sent);
        Assert.IsTrue(redelivery.Items.All(i => i.Outcome == ItemOutcome.Duplicate));
    }

    private sealed class EndToEndHandler(IIdempotencyClaimStore store, List<string> sent) : IdempotentEventHandler<string>(store)
    {
        protected override string HandlerName => "e2e";
        protected override string ItemKey(string item) => item;
        protected override Task ProcessOnceAsync(string item, CancellationToken cancellationToken)
        {
            sent.Add(item);
            return Task.CompletedTask;
        }
    }
}
