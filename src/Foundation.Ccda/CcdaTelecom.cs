using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>A telecom address (<c>TEL</c>) such as <c>tel:+1-555-0100</c> or <c>mailto:</c> URI.</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaTelecom
{
    /// <summary>The URI.</summary>
    public string? Value { get; init; }

    /// <summary>The use code, e.g. <c>HP</c> (home) or <c>WP</c> (work).</summary>
    public string? Use { get; init; }
}
