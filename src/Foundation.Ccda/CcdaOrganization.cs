using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>An organization, e.g. the custodian or an author's represented organization.</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaOrganization
{
    /// <summary>Identifiers such as an NPI.</summary>
    public IReadOnlyList<CcdaIdentifier> Identifiers { get; init; } = [];

    /// <summary>The first name the organization is given.</summary>
    public string? Name { get; init; }

    /// <summary>Phone numbers and email addresses.</summary>
    public IReadOnlyList<CcdaTelecom> Telecoms { get; init; } = [];

    /// <summary>Addresses.</summary>
    public IReadOnlyList<CcdaAddress> Addresses { get; init; } = [];
}
