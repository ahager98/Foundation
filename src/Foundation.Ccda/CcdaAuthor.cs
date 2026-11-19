using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>A document <c>author</c>: a person or device and the organization it represents.</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaAuthor
{
    /// <summary>When the author contributed.</summary>
    public CcdaTimestamp? Time { get; init; }

    /// <summary>Identifiers of the assigned author, such as an NPI.</summary>
    public IReadOnlyList<CcdaIdentifier> Identifiers { get; init; } = [];

    /// <summary>Names of the assigned person; empty for a device author.</summary>
    public IReadOnlyList<CcdaPersonName> Names { get; init; } = [];

    /// <summary>The device or software name, for a device author.</summary>
    public string? DeviceName { get; init; }

    /// <summary>The organization the author represents.</summary>
    public CcdaOrganization? Organization { get; init; }
}
