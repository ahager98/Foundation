using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>
/// A Result Organizer or Vital Signs Organizer (a panel such as a CBC, or one set of vitals) and its
/// observations. An observation placed directly in a section entry becomes an organizer with no code.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaResultOrganizer
{
    /// <summary>The organizer's identifiers.</summary>
    public IReadOnlyList<CcdaIdentifier> Identifiers { get; init; } = [];

    /// <summary>The panel code, or null for a bare observation.</summary>
    public CcdaCode? Code { get; init; }

    /// <summary>The <c>statusCode</c>.</summary>
    public string? Status { get; init; }

    /// <summary>When the panel applies.</summary>
    public CcdaInterval? EffectiveTime { get; init; }

    /// <summary>The observations, in document order.</summary>
    public IReadOnlyList<CcdaObservation> Observations { get; init; } = [];
}
