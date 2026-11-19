namespace Foundation.Ccda;

/// <summary>
/// A parsed C-CDA document: header, sections and the extracted clinical entries. Entries are listed
/// at the document level, in document order, whichever section held them. A plain class, so nothing
/// in it is printed by an accidental <c>ToString()</c>; the patient and entries are PHI.
/// </summary>
public sealed class CcdaDocument
{
    /// <summary>The document id.</summary>
    public CcdaIdentifier? Identifier { get; init; }

    /// <summary>The set id shared by every version of the document.</summary>
    public CcdaIdentifier? SetId { get; init; }

    /// <summary>The version number within the set.</summary>
    public int? VersionNumber { get; init; }

    /// <summary>The C-CDA document type, from the document template ids.</summary>
    public CcdaDocumentKind Kind { get; init; }

    /// <summary>The document-level template ids.</summary>
    public IReadOnlyList<CcdaIdentifier> TemplateIds { get; init; } = [];

    /// <summary>The document type code, normally LOINC (e.g. <c>34133-9</c> for a summary of episode note).</summary>
    public CcdaCode? Code { get; init; }

    /// <summary>The title.</summary>
    public string? Title { get; init; }

    /// <summary>When the document was created.</summary>
    public CcdaTimestamp? EffectiveTime { get; init; }

    /// <summary>The confidentiality code, e.g. <c>N</c> (normal) or <c>R</c> (restricted).</summary>
    public CcdaCode? ConfidentialityCode { get; init; }

    /// <summary>The document language, e.g. <c>en-US</c>.</summary>
    public string? LanguageCode { get; init; }

    /// <summary>The patient, or null when the document has no <c>recordTarget/patientRole</c>.</summary>
    public CcdaPatient? Patient { get; init; }

    /// <summary>The authors.</summary>
    public IReadOnlyList<CcdaAuthor> Authors { get; init; } = [];

    /// <summary>The custodian organization.</summary>
    public CcdaOrganization? Custodian { get; init; }

    /// <summary>True when the body is a <c>structuredBody</c>; false for a <c>nonXMLBody</c> or no body.</summary>
    public bool HasStructuredBody { get; init; }

    /// <summary>The body sections, nested sections included, in document order.</summary>
    public IReadOnlyList<CcdaSection> Sections { get; init; } = [];

    /// <summary>Problems from Problems sections.</summary>
    public IReadOnlyList<CcdaProblem> Problems { get; init; } = [];

    /// <summary>Allergies from Allergies sections.</summary>
    public IReadOnlyList<CcdaAllergy> Allergies { get; init; } = [];

    /// <summary>Medications from Medications sections.</summary>
    public IReadOnlyList<CcdaMedication> Medications { get; init; } = [];

    /// <summary>Immunizations from Immunizations sections.</summary>
    public IReadOnlyList<CcdaImmunization> Immunizations { get; init; } = [];

    /// <summary>Result panels from Results sections.</summary>
    public IReadOnlyList<CcdaResultOrganizer> Results { get; init; } = [];

    /// <summary>Vital-sign sets from Vital Signs sections.</summary>
    public IReadOnlyList<CcdaResultOrganizer> VitalSigns { get; init; } = [];

    /// <summary>Procedures from Procedures sections.</summary>
    public IReadOnlyList<CcdaProcedure> Procedures { get; init; } = [];

    /// <summary>Gets the first section of a kind.</summary>
    /// <param name="kind">The section kind.</param>
    /// <returns>The section, or null when the document has none of that kind.</returns>
    public CcdaSection? GetSection(CcdaSectionKind kind)
    {
        return Sections.FirstOrDefault(s => s.Kind == kind);
    }
}
