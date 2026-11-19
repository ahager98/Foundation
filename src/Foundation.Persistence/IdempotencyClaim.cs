using System.Diagnostics.CodeAnalysis;

namespace Foundation.Persistence;

/// <summary>One claim row. Owned by the service's model through <see cref="IdempotencyClaimModelBuilderExtensions.ApplyFoundationIdempotencyClaims"/>.</summary>
[ExcludeFromCodeCoverage]
public class IdempotencyClaim
{
    public long Id { get; set; }

    /// <summary>Tenant and handler, e.g. "5:sms".</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Event id and item key, e.g. "evt-1:a". Plain identifiers only.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Lowercase hex SHA-256 of the exact (Scope, Key) text. The unique index is on this, not on Scope/Key, so a
    /// case-insensitive database collation can never treat "evt-A" and "evt-a" as the same claim.
    /// </summary>
    public string Identity { get; set; } = string.Empty;

    /// <summary>Proves ownership; a stale owner cannot complete or release a claim that was taken over.</summary>
    public Guid Token { get; set; }

    public bool Completed { get; set; }

    /// <summary>End of the lease (unfinished) or of the retention window (completed), UTC.</summary>
    public DateTime ExpiresUtc { get; set; }
}
