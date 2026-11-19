using System.Diagnostics.CodeAnalysis;

namespace Foundation.Exceptions;

[ExcludeFromCodeCoverage]
public class ResourceInactiveException : DomainException
{
    public ResourceInactiveException()
    {
    }

    /// <summary>Structured, client-safe form: entity type, optional id/index, reason code.</summary>
    public ResourceInactiveException(string entityType, string? entityId, string reasonCode)
        : base(entityType, entityId, reasonCode)
    {
    }

    public ResourceInactiveException(string message)
        : base(message)
    {
    }

    public ResourceInactiveException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
