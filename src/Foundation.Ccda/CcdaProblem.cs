using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>A problem: one Problem Observation, with the status of the Problem Concern Act around it.</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaProblem
{
    /// <summary>The observation's identifiers.</summary>
    public IReadOnlyList<CcdaIdentifier> Identifiers { get; init; } = [];

    /// <summary>The problem itself (the observation <c>value</c>), normally SNOMED CT.</summary>
    public CcdaCode? Problem { get; init; }

    /// <summary>The problem type (the observation <c>code</c>), e.g. diagnosis or symptom.</summary>
    public CcdaCode? ProblemType { get; init; }

    /// <summary>Onset (<see cref="CcdaInterval.Low"/>) and resolution (<see cref="CcdaInterval.High"/>).</summary>
    public CcdaInterval? Period { get; init; }

    /// <summary>The concern act's <c>statusCode</c>, e.g. <c>active</c> or <c>completed</c>.</summary>
    public string? ConcernStatus { get; init; }

    /// <summary>The clinical status from a Problem Status observation, when present.</summary>
    public CcdaCode? Status { get; init; }

    /// <summary>True when <c>negationInd="true"</c>: the problem was asserted absent.</summary>
    public bool IsNegated { get; init; }
}
