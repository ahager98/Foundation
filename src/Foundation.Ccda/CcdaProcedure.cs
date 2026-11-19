using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>A procedure: a Procedure Activity <c>procedure</c>, <c>observation</c> or <c>act</c>.</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaProcedure
{
    /// <summary>The activity's identifiers.</summary>
    public IReadOnlyList<CcdaIdentifier> Identifiers { get; init; } = [];

    /// <summary>The procedure, e.g. SNOMED CT or CPT.</summary>
    public CcdaCode? Code { get; init; }

    /// <summary>The <c>statusCode</c>, e.g. <c>completed</c>.</summary>
    public string? Status { get; init; }

    /// <summary>When the procedure was performed.</summary>
    public CcdaInterval? Period { get; init; }

    /// <summary>The body site.</summary>
    public CcdaCode? TargetSite { get; init; }

    /// <summary>True when <c>negationInd="true"</c>: the procedure was not performed.</summary>
    public bool IsNegated { get; init; }
}
