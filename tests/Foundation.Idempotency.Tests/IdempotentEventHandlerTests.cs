using Foundation.Idempotency;

namespace Foundation.Idempotency.Tests;

public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;
    public void Advance(TimeSpan by) => _now += by;
    public override DateTimeOffset GetUtcNow() => _now;
}

[TestClass]
public class IdempotentEventHandlerTests
{
    private sealed record Recipient(string Id, bool Fail = false);

    private sealed class SmsHandler(IIdempotencyClaimStore store, IdempotentHandlerOptions? options = null)
        : IdempotentEventHandler<Recipient>(store, options)
    {
        public List<string> Sent { get; } = [];
        public Func<Recipient, CancellationToken, Task>? Before { get; set; }

        protected override string HandlerName => "sms";
        protected override string ItemKey(Recipient item) => item.Id;

        protected override async Task ProcessOnceAsync(Recipient item, CancellationToken cancellationToken)
        {
            if (Before is not null)
                await Before(item, cancellationToken);
            if (item.Fail)
                throw new InvalidOperationException("Jane Doe 555-0100 could not be reached");
            lock (Sent)
                Sent.Add(item.Id);
        }
    }

    [TestMethod]
    public void Options_ThatWouldDisableTheGuarantee_AreRejectedUpFront()
    {
        InMemoryIdempotencyClaimStore store = new();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SmsHandler(store, new() { Lease = TimeSpan.Zero }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SmsHandler(store, new() { Retention = TimeSpan.Zero }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SmsHandler(store, new() { CompleteAttempts = 0 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SmsHandler(store, new() { CompleteTimeout = TimeSpan.Zero }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SmsHandler(store, new() { CompleteTimeout = TimeSpan.FromSeconds(-1) }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SmsHandler(store, new() { CompleteRetryDelay = TimeSpan.FromSeconds(-1) }));
        Assert.ThrowsExactly<ArgumentNullException>(() => new SmsHandler(null!));
    }

    [TestMethod]
    public async Task InMemoryStore_RejectsANonPositiveLease()
    {
        InMemoryIdempotencyClaimStore store = new();
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            async () => await store.TryClaimAsync("s", "k", TimeSpan.Zero));
    }

    private static Recipient[] Batch(params string[] ids) => ids.Select(i => new Recipient(i)).ToArray();

    [TestMethod]
    public async Task EachItemIsProcessedOnce()
    {
        SmsHandler handler = new(new InMemoryIdempotencyClaimStore());

        IdempotentBatchResult result = await handler.HandleAsync("evt-1", 5, Batch("a", "b"));

        CollectionAssert.AreEqual(new[] { "a", "b" }, handler.Sent);
        Assert.IsTrue(result.Items.All(i => i.Outcome == ItemOutcome.Processed));
    }

    [TestMethod]
    public async Task SecondDeliveryOfSameEvent_CausesNoSecondWrite()
    {
        SmsHandler handler = new(new InMemoryIdempotencyClaimStore());
        await handler.HandleAsync("evt-1", 5, Batch("a", "b"));

        IdempotentBatchResult second = await handler.HandleAsync("evt-1", 5, Batch("a", "b"));

        Assert.AreEqual(2, handler.Sent.Count);
        Assert.IsTrue(second.Items.All(i => i.Outcome == ItemOutcome.Duplicate));
    }

    [TestMethod]
    public async Task DifferentEventOrTenant_IsNotADuplicate()
    {
        SmsHandler handler = new(new InMemoryIdempotencyClaimStore());
        await handler.HandleAsync("evt-1", 5, Batch("a"));

        await handler.HandleAsync("evt-2", 5, Batch("a"));
        await handler.HandleAsync("evt-1", 6, Batch("a"));

        Assert.AreEqual(3, handler.Sent.Count);
    }

    [TestMethod]
    public async Task FailingItem_DoesNotBlockOthers_AndRetryTouchesOnlyIt()
    {
        SmsHandler handler = new(new InMemoryIdempotencyClaimStore());
        Recipient[] items = [new("a"), new("bad", Fail: true), new("c")];

        IdempotentBatchResult first = await handler.HandleAsync("evt-1", 5, items);

        CollectionAssert.AreEqual(new[] { "a", "c" }, handler.Sent);
        Assert.IsTrue(first.HasFailures);
        ItemResult failed = first.Items.Single(i => i.Outcome == ItemOutcome.Failed);
        Assert.AreEqual("bad", failed.ItemKey);
        Assert.AreEqual(nameof(InvalidOperationException), failed.FailureType);

        // Redelivery after the cause is fixed: a and c are skipped, only the failed item runs.
        IdempotentBatchResult second = await handler.HandleAsync("evt-1", 5, [new("a"), new("bad"), new("c")]);

        CollectionAssert.AreEqual(new[] { "a", "c", "bad" }, handler.Sent);
        CollectionAssert.AreEqual(
            new[] { ItemOutcome.Duplicate, ItemOutcome.Processed, ItemOutcome.Duplicate },
            second.Items.Select(i => i.Outcome).ToArray());
    }

    [TestMethod]
    public async Task FailureResult_NeverCarriesTheExceptionMessage()
    {
        SmsHandler handler = new(new InMemoryIdempotencyClaimStore());

        IdempotentBatchResult result = await handler.HandleAsync("evt-1", 5, [new Recipient("a", Fail: true)]);

        ItemResult failed = result.Items.Single();
        Assert.IsFalse((failed.FailureType ?? "").Contains("Jane"));
        Assert.IsFalse(failed.ToString().Contains("555-0100"));
    }

    [TestMethod]
    public async Task ClaimIsHeldBeforeAnySideEffect()
    {
        InMemoryIdempotencyClaimStore store = new();
        SmsHandler handler = new(store);
        ClaimOutcome? seenDuringProcessing = null;
        handler.Before = async (_, ct) =>
            seenDuringProcessing = (await store.TryClaimAsync("5:sms", "evt-1|a", TimeSpan.FromMinutes(1), ct)).Outcome;

        await handler.HandleAsync("evt-1", 5, Batch("a"));

        Assert.AreEqual(ClaimOutcome.InProgress, seenDuringProcessing);
    }

    [TestMethod]
    public async Task ConcurrentDeliveries_ProcessEachItemOnce()
    {
        InMemoryIdempotencyClaimStore store = new();
        TaskCompletionSource gate = new();
        SmsHandler first = new(store) { Before = (_, _) => gate.Task };
        SmsHandler second = new(store);

        Task<IdempotentBatchResult> running = first.HandleAsync("evt-1", 5, Batch("a"));
        IdempotentBatchResult overlapping = await second.HandleAsync("evt-1", 5, Batch("a"));
        gate.SetResult();
        await running;

        Assert.AreEqual(ItemOutcome.InProgress, overlapping.Items.Single().Outcome);
        Assert.IsTrue(overlapping.HasInProgress);
        Assert.AreEqual(0, second.Sent.Count);
        Assert.AreEqual(1, first.Sent.Count);
    }

    [TestMethod]
    public async Task Cancellation_ReleasesTheClaimAndPropagates()
    {
        InMemoryIdempotencyClaimStore store = new();
        using CancellationTokenSource cts = new();
        SmsHandler handler = new(store) { Before = (_, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; } };

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => handler.HandleAsync("evt-1", 5, Batch("a"), cts.Token));

        handler.Before = null;
        IdempotentBatchResult retry = await handler.HandleAsync("evt-1", 5, Batch("a"));
        Assert.AreEqual(ItemOutcome.Processed, retry.Items.Single().Outcome);
    }

    [TestMethod]
    public async Task CrashedWorker_ClaimLapsesAfterLease_AndCanBeTakenOver()
    {
        TestClock clock = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        InMemoryIdempotencyClaimStore store = new(clock);
        // A worker claimed the item and died: claim taken, never completed or released.
        await store.TryClaimAsync("5:sms", "evt-1|a", TimeSpan.FromMinutes(5), CancellationToken.None);
        SmsHandler handler = new(store, new IdempotentHandlerOptions { Lease = TimeSpan.FromMinutes(5) });

        IdempotentBatchResult beforeLapse = await handler.HandleAsync("evt-1", 5, Batch("a"));
        clock.Advance(TimeSpan.FromMinutes(6));
        IdempotentBatchResult afterLapse = await handler.HandleAsync("evt-1", 5, Batch("a"));

        Assert.AreEqual(ItemOutcome.InProgress, beforeLapse.Items.Single().Outcome);
        Assert.AreEqual(ItemOutcome.Processed, afterLapse.Items.Single().Outcome);
    }

    [TestMethod]
    public async Task CompletedClaim_ExpiresAfterRetention()
    {
        TestClock clock = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        SmsHandler handler = new(new InMemoryIdempotencyClaimStore(clock), new IdempotentHandlerOptions { Retention = TimeSpan.FromHours(24) });
        await handler.HandleAsync("evt-1", 5, Batch("a"));

        clock.Advance(TimeSpan.FromHours(25));
        await handler.HandleAsync("evt-1", 5, Batch("a"));

        Assert.AreEqual(2, handler.Sent.Count);
    }

    private sealed class FailingCompleteStoreMock(IIdempotencyClaimStore inner) : IIdempotencyClaimStore
    {
        public int Releases;
        public ValueTask<ClaimResult> TryClaimAsync(string s, string k, TimeSpan l, CancellationToken c) => inner.TryClaimAsync(s, k, l, c);
        public ValueTask<bool> CompleteAsync(string s, string k, Guid t, TimeSpan r, CancellationToken c) => throw new TimeoutException("store down");
        public ValueTask<bool> ReleaseAsync(string s, string k, Guid t, CancellationToken c) { Releases++; return inner.ReleaseAsync(s, k, t, c); }
    }

    [TestMethod]
    public async Task CompleteFailing_AfterSideEffect_ReportsFailureWithoutReleasing()
    {
        FailingCompleteStoreMock store = new(new InMemoryIdempotencyClaimStore());
        SmsHandler handler = new(store, new IdempotentHandlerOptions { CompleteRetryDelay = TimeSpan.Zero });

        IdempotentBatchResult result = await handler.HandleAsync("evt-1", 5, Batch("a"));

        Assert.AreEqual(ItemOutcome.Failed, result.Items.Single().Outcome);
        Assert.AreEqual(0, store.Releases);
        Assert.AreEqual(1, handler.Sent.Count);
    }

    private sealed class FailingReleaseStoreMock(IIdempotencyClaimStore inner) : IIdempotencyClaimStore
    {
        public ValueTask<ClaimResult> TryClaimAsync(string s, string k, TimeSpan l, CancellationToken c) => inner.TryClaimAsync(s, k, l, c);
        public ValueTask<bool> CompleteAsync(string s, string k, Guid t, TimeSpan r, CancellationToken c) => inner.CompleteAsync(s, k, t, r, c);
        public ValueTask<bool> ReleaseAsync(string s, string k, Guid t, CancellationToken c) => throw new TimeoutException("store down");
    }

    [TestMethod]
    public async Task ReleaseFailing_DoesNotMaskTheOriginalFailure()
    {
        SmsHandler handler = new(new FailingReleaseStoreMock(new InMemoryIdempotencyClaimStore()));

        IdempotentBatchResult result = await handler.HandleAsync("evt-1", 5, [new Recipient("a", Fail: true)]);

        Assert.AreEqual(nameof(InvalidOperationException), result.Items.Single().FailureType);
    }

    private sealed class FlakyCompleteStoreMock(IIdempotencyClaimStore inner, int failures) : IIdempotencyClaimStore
    {
        public int Attempts;
        public ValueTask<ClaimResult> TryClaimAsync(string s, string k, TimeSpan l, CancellationToken c) => inner.TryClaimAsync(s, k, l, c);
        public ValueTask<bool> ReleaseAsync(string s, string k, Guid t, CancellationToken c) => inner.ReleaseAsync(s, k, t, c);
        public ValueTask<bool> CompleteAsync(string s, string k, Guid t, TimeSpan r, CancellationToken c) =>
            ++Attempts <= failures ? throw new TimeoutException("blip") : inner.CompleteAsync(s, k, t, r, c);
    }

    [TestMethod]
    public async Task TransientCompleteFailure_IsRetried_SoTheItemIsNotRepeatedLater()
    {
        FlakyCompleteStoreMock store = new(new InMemoryIdempotencyClaimStore(), failures: 2);
        SmsHandler handler = new(store, new IdempotentHandlerOptions { CompleteRetryDelay = TimeSpan.Zero });

        IdempotentBatchResult first = await handler.HandleAsync("evt-1", 5, Batch("a"));
        IdempotentBatchResult redelivery = await handler.HandleAsync("evt-1", 5, Batch("a"));

        Assert.AreEqual(ItemOutcome.Processed, first.Items.Single().Outcome);
        Assert.AreEqual(3, store.Attempts); // two failures, then the third attempt succeeded
        Assert.AreEqual(ItemOutcome.Duplicate, redelivery.Items.Single().Outcome);
        Assert.AreEqual(1, handler.Sent.Count);
    }

    private sealed class LostClaimStoreMock(IIdempotencyClaimStore inner) : IIdempotencyClaimStore
    {
        public ValueTask<ClaimResult> TryClaimAsync(string s, string k, TimeSpan l, CancellationToken c) => inner.TryClaimAsync(s, k, l, c);
        public ValueTask<bool> ReleaseAsync(string s, string k, Guid t, CancellationToken c) => inner.ReleaseAsync(s, k, t, c);
        public ValueTask<bool> CompleteAsync(string s, string k, Guid t, TimeSpan r, CancellationToken c) => ValueTask.FromResult(false);
    }

    [TestMethod]
    public async Task ClaimTakenOverBeforeCompletion_IsReportedAsClaimLost()
    {
        SmsHandler handler = new(new LostClaimStoreMock(new InMemoryIdempotencyClaimStore()));

        IdempotentBatchResult result = await handler.HandleAsync("evt-1", 5, Batch("a"));

        Assert.AreEqual(ItemOutcome.Failed, result.Items.Single().Outcome);
        Assert.AreEqual("ClaimLost", result.Items.Single().FailureType);
    }

    private sealed class ReportingHandler(IIdempotencyClaimStore store, bool hookThrows = false) : IdempotentEventHandler<Recipient>(store)
    {
        public List<(string EventId, string ItemKey, Exception Error)> Failures { get; } = [];
        protected override string HandlerName => "report";
        protected override string ItemKey(Recipient item) => item.Id;
        protected override Task ProcessOnceAsync(Recipient item, CancellationToken cancellationToken) =>
            item.Fail ? throw new InvalidOperationException("Jane Doe 555-0100 could not be reached") : Task.CompletedTask;

        protected override void OnItemFailed(string eventId, string itemKey, Exception exception)
        {
            Failures.Add((eventId, itemKey, exception));
            if (hookThrows)
                throw new InvalidOperationException("hook failed");
        }
    }

    [TestMethod]
    public async Task FailedItem_IsReportedToTheHookWithIdentifiersAndTheException_NotInTheResult()
    {
        ReportingHandler handler = new(new InMemoryIdempotencyClaimStore());

        IdempotentBatchResult result = await handler.HandleAsync("evt-1", 5, [new Recipient("bad", Fail: true), new Recipient("ok")]);

        (string eventId, string itemKey, Exception error) = handler.Failures.Single();
        Assert.AreEqual("evt-1", eventId);
        Assert.AreEqual("bad", itemKey);
        Assert.IsInstanceOfType<InvalidOperationException>(error);
        Assert.IsFalse(result.Items.Any(i => (i.FailureType ?? "").Contains("Jane")));
    }

    [TestMethod]
    public async Task ThrowingHook_DoesNotBreakTheLoop()
    {
        ReportingHandler handler = new(new InMemoryIdempotencyClaimStore(), hookThrows: true);

        IdempotentBatchResult result = await handler.HandleAsync("evt-1", 5, [new Recipient("bad", Fail: true), new Recipient("ok")]);

        CollectionAssert.AreEqual(new[] { ItemOutcome.Failed, ItemOutcome.Processed }, result.Items.Select(i => i.Outcome).ToArray());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Jane Doe")]
    [DataRow("a/b")]
    public async Task EventIdThatIsNotAPlainIdentifier_IsRejected(string eventId)
    {
        SmsHandler handler = new(new InMemoryIdempotencyClaimStore());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => handler.HandleAsync(eventId, 5, Batch("a")));
    }

    [TestMethod]
    public async Task ItemKeyThatIsNotAPlainIdentifier_IsRejectedBeforeAnyClaim()
    {
        SmsHandler handler = new(new InMemoryIdempotencyClaimStore());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => handler.HandleAsync("evt-1", 5, [new Recipient("Jane Doe")]));
        Assert.AreEqual(0, handler.Sent.Count);
    }

    [TestMethod]
    public async Task UnresolvedTenant_IsRejected()
    {
        SmsHandler handler = new(new InMemoryIdempotencyClaimStore());
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => handler.HandleAsync("evt-1", 0, Batch("a")));
    }

    [TestMethod]
    public async Task NullItems_Throw()
    {
        SmsHandler handler = new(new InMemoryIdempotencyClaimStore());
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => handler.HandleAsync("evt-1", 5, null!));
    }

    [TestMethod]
    public async Task ProviderTimeout_IsAnItemFailure_NotACancellation_AndLaterItemsStillRun()
    {
        SmsHandler handler = new(new InMemoryIdempotencyClaimStore())
        {
            Before = (item, _) => item.Id == "slow" ? throw new TaskCanceledException("provider timed out") : Task.CompletedTask
        };

        IdempotentBatchResult result = await handler.HandleAsync("evt-1", 5, Batch("slow", "b"));

        CollectionAssert.AreEqual(new[] { ItemOutcome.Failed, ItemOutcome.Processed }, result.Items.Select(i => i.Outcome).ToArray());
        CollectionAssert.AreEqual(new[] { "b" }, handler.Sent);
        // The timed-out item's claim was released, so a redelivery retries it.
        handler.Before = null;
        Assert.AreEqual(ItemOutcome.Processed, (await handler.HandleAsync("evt-1", 5, Batch("slow"))).Items.Single().Outcome);
    }

    [TestMethod]
    public async Task MalformedItemKey_FailsTheBatchBeforeAnyItemRuns()
    {
        SmsHandler handler = new(new InMemoryIdempotencyClaimStore());

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => handler.HandleAsync("evt-1", 5, [new Recipient("ok"), new Recipient("Jane Doe"), new Recipient("later")]));

        Assert.AreEqual(0, handler.Sent.Count);
    }

    [TestMethod]
    public async Task EventAndItemKeys_CannotCollideAcrossTheSeparator()
    {
        SmsHandler handler = new(new InMemoryIdempotencyClaimStore());

        await handler.HandleAsync("a:b", 5, Batch("c"));
        IdempotentBatchResult other = await handler.HandleAsync("a", 5, Batch("b:c"));

        Assert.AreEqual(ItemOutcome.Processed, other.Items.Single().Outcome);
        Assert.AreEqual(2, handler.Sent.Count);
    }

    [TestMethod]
    public async Task KeyWithTrailingNewline_IsRejected()
    {
        SmsHandler handler = new(new InMemoryIdempotencyClaimStore());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => handler.HandleAsync("evt-1\n", 5, Batch("a")));
    }
}

[TestClass]
public class InMemoryIdempotencyClaimStoreTests
{
    [TestMethod]
    public async Task StaleToken_CannotCompleteOrReleaseATakenOverClaim()
    {
        TestClock clock = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        InMemoryIdempotencyClaimStore store = new(clock);
        ClaimResult old = await store.TryClaimAsync("s", "k", TimeSpan.FromMinutes(1), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(2));
        ClaimResult current = await store.TryClaimAsync("s", "k", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.AreEqual(ClaimOutcome.Acquired, current.Outcome);

        await store.ReleaseAsync("s", "k", old.Token);
        await store.CompleteAsync("s", "k", old.Token, TimeSpan.FromHours(1));

        Assert.AreEqual(ClaimOutcome.InProgress, (await store.TryClaimAsync("s", "k", TimeSpan.FromMinutes(1))).Outcome);
    }

    [TestMethod]
    public async Task ReleasingACompletedClaim_IsANoOp()
    {
        InMemoryIdempotencyClaimStore store = new();
        ClaimResult claim = await store.TryClaimAsync("s", "k", TimeSpan.FromMinutes(1));
        await store.CompleteAsync("s", "k", claim.Token, TimeSpan.FromHours(1));

        await store.ReleaseAsync("s", "k", claim.Token);

        Assert.AreEqual(ClaimOutcome.AlreadyCompleted, (await store.TryClaimAsync("s", "k", TimeSpan.FromMinutes(1))).Outcome);
    }

    [TestMethod]
    public async Task Complete_And_Release_ReportWhetherTheCallerStillOwnedTheClaim()
    {
        InMemoryIdempotencyClaimStore store = new();
        ClaimResult claim = await store.TryClaimAsync("s", "k", TimeSpan.FromMinutes(1));

        Assert.IsFalse(await store.CompleteAsync("s", "k", Guid.NewGuid(), TimeSpan.FromHours(1)));
        Assert.IsFalse(await store.ReleaseAsync("s", "k", Guid.NewGuid()));
        Assert.IsTrue(await store.CompleteAsync("s", "k", claim.Token, TimeSpan.FromHours(1)));
        Assert.IsFalse(await store.ReleaseAsync("s", "k", claim.Token)); // completed claims are not released
    }
}
