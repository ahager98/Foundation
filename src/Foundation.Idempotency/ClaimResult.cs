using System.Diagnostics.CodeAnalysis;

namespace Foundation.Idempotency;

/// <summary>The outcome plus the token that proves ownership of an acquired claim.</summary>
[ExcludeFromCodeCoverage]
public readonly record struct ClaimResult(ClaimOutcome Outcome, Guid Token);
