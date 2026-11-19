using System.Text.RegularExpressions;

namespace Foundation.Idempotency;

/// <summary>
/// Base for redeliverable event handlers. At-least-once delivery then produces exactly-once effects:
/// <list type="number">
/// <item>Each item takes a durable, cross-instance claim keyed by event id and item key BEFORE any side effect.</item>
/// <item>Everything after the claim is wrapped: success completes the claim and a failure of the item releases it, so
/// a redelivery retries only what did not finish. A failure to record completion (after the side effect ran) keeps the
/// claim, so it blocks others until the lease lapses; a crash does the same.</item>
/// <item>Items run in a loop with isolated try/catch: one failing item never blocks or hides the others.</item>
/// <item>The "claimed/processing" state lives only in the claim store. It is never a value of the handler's own
/// status column, so the status used to select pending rows cannot double as the success marker.</item>
/// </list>
/// The concrete handler implements only <see cref="ProcessOnceAsync"/>. Cost: items run one after another, each needing a claim and a completion round-trip to the store (there is no batch claim),
/// so size batches accordingly. Delivery remains at-least-once: a crash
/// after a side effect but before completion can repeat that effect once the lease lapses, so side effects that
/// cannot tolerate that should carry their own provider-side idempotency key (use the event id and item key).
/// </summary>
public abstract partial class IdempotentEventHandler<TItem>(
    IIdempotencyClaimStore store,
    IdempotentHandlerOptions? options = null)
{
    private readonly IIdempotencyClaimStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IdempotentHandlerOptions _options = ValidOptions(options);

    [GeneratedRegex(@"^[A-Za-z0-9_.:-]{1,128}\z")]
    private static partial Regex SafeId();

    /// <summary>Stable name of this handler; part of the claim scope so two handlers never share claims.</summary>
    protected abstract string HandlerName { get; }

    /// <summary>Identifier of the item within its event (a recipient id, row id). Plain identifier, never a name or free text.</summary>
    protected abstract string ItemKey(TItem item);

    /// <summary>Performs the item's side effects. Called at most once per claim, only after the claim is held.</summary>
    protected abstract Task ProcessOnceAsync(TItem item, CancellationToken cancellationToken);

    /// <summary>
    /// Called with identifiers only when an item fails, or when a store call around it fails (release, complete, lost claim).
    /// The exception is passed here and never placed in <see cref="ItemResult"/>, because its message can carry PHI/PII:
    /// log it by type and stack, with the event id and item key, never by message. The default does nothing.
    /// </summary>
    protected virtual void OnItemFailed(string eventId, string itemKey, Exception exception)
    {
    }

    /// <summary>Handles one delivery of <paramref name="eventId"/> for <paramref name="tenantId"/>.</summary>
    /// <exception cref="OperationCanceledException">Cancellation releases the in-flight claim and stops the loop.</exception>
    public async Task<IdempotentBatchResult> HandleAsync(
        string eventId, int tenantId, IEnumerable<TItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        RequireSafe(eventId, nameof(eventId));
        if (tenantId <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenantId), "A resolved tenant is required to claim work.");

        string scope = $"{tenantId}:{HandlerName}";
        if (scope.Length > 200)
            throw new InvalidOperationException("HandlerName is too long; the claim scope must stay within 200 characters.");

        // Validate every key before claiming anything, so a malformed item fails the batch atomically instead of
        // after earlier items already ran (a poison item would otherwise block the rest on every redelivery).
        List<(TItem Item, string Key)> keyed = [];
        foreach (TItem item in items)
        {
            string itemKey = ItemKey(item);
            RequireSafe(itemKey, nameof(items));
            keyed.Add((item, itemKey));
        }

        List<ItemResult> results = [];
        foreach ((TItem item, string itemKey) in keyed)
        {
            // '|' is outside the identifier charset, so (eventId, itemKey) pairs can never collide.
            results.Add(await HandleItemAsync(scope, eventId, itemKey, item, cancellationToken));
        }

        return new IdempotentBatchResult(results);
    }

    private async Task<ItemResult> HandleItemAsync(
        string scope, string eventId, string itemKey, TItem item, CancellationToken cancellationToken)
    {
        // '|' is outside the identifier charset, so (eventId, itemKey) pairs can never collide.
        string claimKey = $"{eventId}|{itemKey}";
        ClaimResult claim = await _store.TryClaimAsync(scope, claimKey, _options.Lease, cancellationToken);
        if (claim.Outcome == ClaimOutcome.AlreadyCompleted)
            return new ItemResult(itemKey, ItemOutcome.Duplicate);
        if (claim.Outcome == ClaimOutcome.InProgress)
            return new ItemResult(itemKey, ItemOutcome.InProgress);

        try
        {
            await ProcessOnceAsync(item, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled: release and stop the loop. A provider timeout (also an
            // OperationCanceledException) is an ordinary item failure and is handled below.
            await ReleaseQuietlyAsync(scope, claimKey, claim.Token, eventId, itemKey);
            throw;
        }
        catch (Exception ex)
        {
            await ReleaseQuietlyAsync(scope, claimKey, claim.Token, eventId, itemKey);
            Report(eventId, itemKey, ex);
            return new ItemResult(itemKey, ItemOutcome.Failed, ex.GetType().Name);
        }

        // The side effect already happened. Recording completion is retried a bounded number of times so one transient
        // store error does not turn into a repeat once the lease lapses; the claim is never released here (that would
        // invite an immediate repeat).
        Exception? lastFailure = null;
        for (int attempt = 1; attempt <= Math.Max(_options.CompleteAttempts, 1); attempt++)
        {
            using CancellationTokenSource timeout = new(_options.CompleteTimeout);
            try
            {
                if (await _store.CompleteAsync(scope, claimKey, claim.Token, _options.Retention, timeout.Token))
                    return new ItemResult(itemKey, ItemOutcome.Processed);

                // Another worker took the claim over mid-processing, so the item may run again there.
                Report(eventId, itemKey, new InvalidOperationException("The claim was taken over before completion; the item may run again."));
                return new ItemResult(itemKey, ItemOutcome.Failed, "ClaimLost");
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                if (attempt < _options.CompleteAttempts && _options.CompleteRetryDelay > TimeSpan.Zero)
                    await Task.Delay(_options.CompleteRetryDelay, CancellationToken.None);
            }
        }

        Report(eventId, itemKey, lastFailure!);
        return new ItemResult(itemKey, ItemOutcome.Failed, lastFailure!.GetType().Name);
    }

    // Release must survive a cancelled request token, and a store failure here must not mask the real error.
    private async Task ReleaseQuietlyAsync(string scope, string claimKey, Guid token, string eventId, string itemKey)
    {
        try
        {
            await _store.ReleaseAsync(scope, claimKey, token, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The claim lapses after the lease; surface it through the hook and carry on.
            Report(eventId, itemKey, ex);
        }
    }

    // A faulty hook must never break the loop.
    private void Report(string eventId, string itemKey, Exception exception)
    {
        try
        {
            OnItemFailed(eventId, itemKey, exception);
        }
        catch (Exception)
        {
            // Nothing more can be done with a failing diagnostics hook.
        }
    }

    private static IdempotentHandlerOptions ValidOptions(IdempotentHandlerOptions? options)
    {
        IdempotentHandlerOptions valid = options ?? new();
        valid.Validate();
        return valid;
    }

    private static void RequireSafe(string value, string name)
    {
        if (!SafeId().IsMatch(value ?? string.Empty))
            throw new ArgumentException("Must be 1-128 characters of letters, digits, '_', '.', ':' or '-' (no names or free text).", name);
    }
}
