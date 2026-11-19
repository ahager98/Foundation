namespace Foundation.Idempotency;

/// <summary>Per-item results of one delivery.</summary>
public sealed class IdempotentBatchResult(IReadOnlyList<ItemResult> items)
{
    public IReadOnlyList<ItemResult> Items { get; } = items;

    public bool HasFailures => Items.Any(static i => i.Outcome == ItemOutcome.Failed);

    public bool HasInProgress => Items.Any(static i => i.Outcome == ItemOutcome.InProgress);
}
