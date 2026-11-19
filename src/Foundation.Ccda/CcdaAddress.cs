using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>A postal address (<c>AD</c>).</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaAddress
{
    /// <summary>The address use, e.g. <c>HP</c> (primary home).</summary>
    public string? Use { get; init; }

    /// <summary>The street lines, in order.</summary>
    public IReadOnlyList<string> StreetLines { get; init; } = [];

    /// <summary>The city.</summary>
    public string? City { get; init; }

    /// <summary>The state or province.</summary>
    public string? State { get; init; }

    /// <summary>The postal code.</summary>
    public string? PostalCode { get; init; }

    /// <summary>The country.</summary>
    public string? Country { get; init; }

    /// <summary>The null flavor when the address is not known.</summary>
    public string? NullFlavor { get; init; }
}
