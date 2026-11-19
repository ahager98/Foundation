using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>A result or vital-sign observation.</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaObservation
{
    /// <summary>The observation's identifiers.</summary>
    public IReadOnlyList<CcdaIdentifier> Identifiers { get; init; } = [];

    /// <summary>What was observed, normally LOINC.</summary>
    public CcdaCode? Code { get; init; }

    /// <summary>The <c>statusCode</c>, e.g. <c>completed</c>.</summary>
    public string? Status { get; init; }

    /// <summary>When the observation applies.</summary>
    public CcdaInterval? EffectiveTime { get; init; }

    /// <summary>The result value.</summary>
    public CcdaValue? Value { get; init; }

    /// <summary>Interpretations, e.g. <c>N</c> (normal) or <c>H</c> (high).</summary>
    public IReadOnlyList<CcdaCode> Interpretations { get; init; } = [];

    /// <summary>The reference range as text: its narrative, else a rendered <c>IVL_PQ</c> such as <c>12-16 g/dL</c>.</summary>
    public string? ReferenceRange { get; init; }
}
