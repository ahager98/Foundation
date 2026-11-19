using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>An immunization: one Immunization Activity (<c>substanceAdministration</c>).</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaImmunization
{
    /// <summary>The activity's identifiers.</summary>
    public IReadOnlyList<CcdaIdentifier> Identifiers { get; init; } = [];

    /// <summary>The vaccine (<c>manufacturedMaterial/code</c>), normally CVX.</summary>
    public CcdaCode? Vaccine { get; init; }

    /// <summary>The <c>statusCode</c>, e.g. <c>completed</c>.</summary>
    public string? Status { get; init; }

    /// <summary>When the vaccine was given (the effective time's value, else its low bound).</summary>
    public CcdaTimestamp? Date { get; init; }

    /// <summary>The lot number.</summary>
    public string? LotNumber { get; init; }

    /// <summary>The route.</summary>
    public CcdaCode? Route { get; init; }

    /// <summary>The dose.</summary>
    public CcdaQuantity? Dose { get; init; }

    /// <summary>True when <c>negationInd="true"</c>: the immunization was not given (refused, contraindicated).</summary>
    public bool IsRefused { get; init; }
}
