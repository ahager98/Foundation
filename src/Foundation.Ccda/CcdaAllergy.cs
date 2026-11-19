using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>An allergy or intolerance: one Allergy-Intolerance Observation and its reactions.</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaAllergy
{
    /// <summary>The observation's identifiers.</summary>
    public IReadOnlyList<CcdaIdentifier> Identifiers { get; init; } = [];

    /// <summary>The substance (the consumable participant's <c>playingEntity/code</c>), e.g. RxNorm.</summary>
    public CcdaCode? Allergen { get; init; }

    /// <summary>The allergy type (the observation <c>value</c>), e.g. drug allergy.</summary>
    public CcdaCode? AllergyType { get; init; }

    /// <summary>Reactions, from Reaction observations.</summary>
    public IReadOnlyList<CcdaCode> Reactions { get; init; } = [];

    /// <summary>Severity, from the first Severity observation anywhere under the allergy.</summary>
    public CcdaCode? Severity { get; init; }

    /// <summary>Onset and resolution.</summary>
    public CcdaInterval? Period { get; init; }

    /// <summary>The concern act's <c>statusCode</c>, e.g. <c>active</c>.</summary>
    public string? ConcernStatus { get; init; }

    /// <summary>The clinical status from an Allergy Status observation, when present.</summary>
    public CcdaCode? Status { get; init; }

    /// <summary>True when <c>negationInd="true"</c>, e.g. "no known allergies".</summary>
    public bool IsNegated { get; init; }
}
