using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>
/// An observation <c>value</c>, whose shape depends on its <c>xsi:type</c>: a quantity (<c>PQ</c>,
/// <c>INT</c>, <c>REAL</c>), a code (<c>CD</c>, <c>CE</c>, <c>CO</c>, ...) or text (<c>ST</c>, <c>ED</c>,
/// and a rendered range for <c>IVL_PQ</c>). Exactly one of the three is normally set.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaValue
{
    /// <summary>The <c>xsi:type</c> without its prefix, e.g. <c>PQ</c>; null when the document omitted it.</summary>
    public string? XsiType { get; init; }

    /// <summary>The quantity, for numeric types.</summary>
    public CcdaQuantity? Quantity { get; init; }

    /// <summary>The code, for coded types.</summary>
    public CcdaCode? Code { get; init; }

    /// <summary>The text, for string, boolean, timestamp and range types.</summary>
    public string? Text { get; init; }

    /// <summary>The null flavor when the value is not known.</summary>
    public string? NullFlavor { get; init; }
}
