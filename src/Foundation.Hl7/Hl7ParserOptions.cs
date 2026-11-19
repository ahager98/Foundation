using System.Diagnostics.CodeAnalysis;

namespace Foundation.Hl7;

/// <summary>Input caps for <see cref="Hl7Parser"/>; input is untrusted so every dimension is bounded.</summary>
[ExcludeFromCodeCoverage]
public sealed class Hl7ParserOptions
{
    /// <summary>Maximum message length in characters. Defaults to 1,048,576.</summary>
    public int MaxMessageLength { get; init; } = 1_048_576;

    /// <summary>Maximum number of segments. Defaults to 10,000.</summary>
    public int MaxSegments { get; init; } = 10_000;
}
