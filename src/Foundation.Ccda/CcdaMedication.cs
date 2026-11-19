using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>A medication: one Medication Activity (<c>substanceAdministration</c>).</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaMedication
{
    /// <summary>The activity's identifiers.</summary>
    public IReadOnlyList<CcdaIdentifier> Identifiers { get; init; } = [];

    /// <summary>The drug (<c>manufacturedMaterial/code</c>), normally RxNorm.</summary>
    public CcdaCode? Medication { get; init; }

    /// <summary>The <c>statusCode</c>, e.g. <c>active</c> or <c>completed</c>.</summary>
    public string? Status { get; init; }

    /// <summary>The <c>moodCode</c>: <c>EVN</c> (taken) or <c>INT</c> (intended).</summary>
    public string? MoodCode { get; init; }

    /// <summary>When the medication was or is to be taken (the first <c>IVL_TS</c> effective time).</summary>
    public CcdaInterval? Period { get; init; }

    /// <summary>The dosing period from the <c>PIVL_TS</c> effective time, e.g. 12 h for twice daily.</summary>
    public CcdaQuantity? DosingPeriod { get; init; }

    /// <summary>The dose per administration.</summary>
    public CcdaQuantity? Dose { get; init; }

    /// <summary>The route, e.g. oral.</summary>
    public CcdaCode? Route { get; init; }

    /// <summary>True when <c>negationInd="true"</c>: the medication was not taken.</summary>
    public bool IsNegated { get; init; }
}
