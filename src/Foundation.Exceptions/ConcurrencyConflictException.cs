using System.Diagnostics.CodeAnalysis;

namespace Foundation.Exceptions;

/// <summary>
/// Raised when an optimistic-concurrency token (rowversion) supplied by the caller no longer
/// matches the stored value — i.e. the row was changed by another request between the caller's
/// read and this write. Surfaced to clients as a 409 Conflict.
/// </summary>
[ExcludeFromCodeCoverage]
public class ConcurrencyConflictException : DomainException
{
    public ConcurrencyConflictException()
    {
    }

    /// <summary>Structured, client-safe form: entity type, optional id/index, reason code.</summary>
    public ConcurrencyConflictException(string entityType, string? entityId, string reasonCode)
        : base(entityType, entityId, reasonCode)
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
