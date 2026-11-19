using System.Diagnostics.CodeAnalysis;

namespace Foundation.Exceptions;

[ExcludeFromCodeCoverage]
public class ResourceExistsException : DomainException
{
    public ResourceExistsException()
    {
    }

    /// <summary>Structured, client-safe form: entity type, optional id/index, reason code.</summary>
    public ResourceExistsException(string entityType, string? entityId, string reasonCode)
        : base(entityType, entityId, reasonCode)
    {
    }

    public ResourceExistsException(string message)
        : base(message)
    {
    }

    public ResourceExistsException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
