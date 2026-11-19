using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>The patient from <c>recordTarget/patientRole</c>. Every member is PHI.</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaPatient
{
    /// <summary>Identifiers such as medical record numbers.</summary>
    public IReadOnlyList<CcdaIdentifier> Identifiers { get; init; } = [];

    /// <summary>Names, in document order.</summary>
    public IReadOnlyList<CcdaPersonName> Names { get; init; } = [];

    /// <summary>Administrative gender.</summary>
    public CcdaCode? Gender { get; init; }

    /// <summary>Birth time.</summary>
    public CcdaTimestamp? BirthTime { get; init; }

    /// <summary>Addresses.</summary>
    public IReadOnlyList<CcdaAddress> Addresses { get; init; } = [];

    /// <summary>Phone numbers and email addresses.</summary>
    public IReadOnlyList<CcdaTelecom> Telecoms { get; init; } = [];

    /// <summary>Races: <c>raceCode</c> followed by any <c>sdtc:raceCode</c>.</summary>
    public IReadOnlyList<CcdaCode> Races { get; init; } = [];

    /// <summary>Ethnicities: <c>ethnicGroupCode</c> followed by any <c>sdtc:ethnicGroupCode</c>.</summary>
    public IReadOnlyList<CcdaCode> Ethnicities { get; init; } = [];

    /// <summary>Languages from <c>languageCommunication/languageCode</c>.</summary>
    public IReadOnlyList<CcdaCode> Languages { get; init; } = [];
}
