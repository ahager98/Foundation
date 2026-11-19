namespace Foundation.Ccda;

/// <summary>
/// An HL7 interval of time (<c>IVL_TS</c>): <c>low</c>/<c>high</c> bounds, or a single <c>value</c>
/// when the document recorded a point in time.
/// </summary>
public sealed class CcdaInterval
{
    /// <summary>The <c>low</c> bound (start, onset), or null when absent or unknown.</summary>
    public CcdaTimestamp? Low { get; init; }

    /// <summary>The <c>high</c> bound (end, resolution), or null when absent or unknown.</summary>
    public CcdaTimestamp? High { get; init; }

    /// <summary>The <c>value</c> attribute, when the document recorded a point in time.</summary>
    public CcdaTimestamp? Value { get; init; }

    /// <summary>The best start time: <see cref="Low"/>, else the point <see cref="Value"/>.</summary>
    public CcdaTimestamp? Start => Low ?? Value;
}
