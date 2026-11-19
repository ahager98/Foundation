using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>An instance identifier (<c>II</c>): an OID or UUID <c>root</c> plus an optional <c>extension</c>.</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaIdentifier
{
    /// <summary>The assigning-authority OID or UUID.</summary>
    public string? Root { get; init; }

    /// <summary>The identifier within the root, e.g. a medical record number.</summary>
    public string? Extension { get; init; }

    /// <summary>The null flavor when the identifier is not known, e.g. <c>UNK</c>.</summary>
    public string? NullFlavor { get; init; }
}
