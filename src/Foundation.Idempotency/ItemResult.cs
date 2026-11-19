using System.Diagnostics.CodeAnalysis;

namespace Foundation.Idempotency;

/// <summary>Result for one item. Carries the exception type name only, never its message (which may hold PHI/PII).</summary>
[ExcludeFromCodeCoverage]
public sealed record ItemResult(string ItemKey, ItemOutcome Outcome, string? FailureType = null);
