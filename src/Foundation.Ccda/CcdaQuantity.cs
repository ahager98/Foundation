using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>A physical quantity (<c>PQ</c>, <c>INT</c>, <c>REAL</c>): a value and a UCUM unit.</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaQuantity
{
    /// <summary>The value, or null when the raw text is not a number.</summary>
    public decimal? Value { get; init; }

    /// <summary>The UCUM unit, e.g. <c>mg</c> or <c>mm[Hg]</c>.</summary>
    public string? Unit { get; init; }

    /// <summary>The value exactly as the document gave it.</summary>
    public string? RawValue { get; init; }
}
