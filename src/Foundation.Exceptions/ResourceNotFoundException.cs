using System.Diagnostics.CodeAnalysis;

namespace Foundation.Exceptions;

[ExcludeFromCodeCoverage]
public class ResourceNotFoundException : DomainException
{
    public ResourceNotFoundException()
    {
    }

    /// <summary>Structured, client-safe form: entity type, optional id/index, reason code.</summary>
    public ResourceNotFoundException(string entityType, string? entityId, string reasonCode)
        : base(entityType, entityId, reasonCode)
    {
    }

    public ResourceNotFoundException(string message)
        : base(message)
    {
    }

    public ResourceNotFoundException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
