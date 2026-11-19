using System.Globalization;
using System.Xml.Linq;

namespace Foundation.Ccda;

/// <summary>Builds a <see cref="CcdaDocument"/> from a loaded, depth-checked <c>ClinicalDocument</c> element.</summary>
internal sealed class CcdaDocumentBuilder
{
    private readonly XElement _root;
    private readonly CcdaParserOptions _limits;
    private readonly CcdaDataTypeReader _types;
    private readonly CcdaEntryReader _entries;

    internal CcdaDocumentBuilder(XElement root, CcdaParserOptions limits)
    {
        _root = root;
        _limits = limits;
        _types = new CcdaDataTypeReader(new CcdaNarrativeIndex(root));
        _entries = new CcdaEntryReader(_types);
    }

    internal CcdaDocument Build()
    {
        XElement? structuredBody = _root.Child("component").Child("structuredBody");
        List<XElement> sectionElements = new();
        if (structuredBody is not null)
        {
            sectionElements.AddRange(structuredBody.Descendants(CcdaXml.V3 + "section"));
        }

        if (sectionElements.Count > _limits.MaxSections)
        {
            throw new CcdaParseException(CcdaParseFailure.TooManySections);
        }

        List<CcdaSection> sections = new(sectionElements.Count);
        List<CcdaProblem> problems = new();
        List<CcdaAllergy> allergies = new();
        List<CcdaMedication> medications = new();
        List<CcdaImmunization> immunizations = new();
        List<CcdaResultOrganizer> results = new();
        List<CcdaResultOrganizer> vitalSigns = new();
        List<CcdaProcedure> procedures = new();

        foreach (XElement element in sectionElements)
        {
            int entryCount = element.Children("entry").Count();
            if (entryCount > _limits.MaxEntriesPerSection)
            {
                throw new CcdaParseException(CcdaParseFailure.TooManyEntries);
            }

            CcdaSectionKind kind = CcdaClassifier.ClassifySection(element);
            XElement? narrative = element.Child("text");
            string? text = narrative is null ? null : CcdaNarrative.ToPlainText(narrative);
            sections.Add(new CcdaSection
            {
                Kind = kind,
                TemplateIds = CcdaDataTypeReader.ReadIdentifiers(element, "templateId"),
                Code = _types.ReadCode(element.Child("code")),
                Title = element.Child("title").TextOf(),
                Text = string.IsNullOrEmpty(text) ? null : text,
                EntryCount = entryCount,
            });

            switch (kind)
            {
                case CcdaSectionKind.Problems:
                    _entries.ReadProblems(element, problems);
                    break;
                case CcdaSectionKind.Allergies:
                    _entries.ReadAllergies(element, allergies);
                    break;
                case CcdaSectionKind.Medications:
                    _entries.ReadMedications(element, medications);
                    break;
                case CcdaSectionKind.Immunizations:
                    _entries.ReadImmunizations(element, immunizations);
                    break;
                case CcdaSectionKind.Results:
                    _entries.ReadOrganizers(element, results);
                    break;
                case CcdaSectionKind.VitalSigns:
                    _entries.ReadOrganizers(element, vitalSigns);
                    break;
                case CcdaSectionKind.Procedures:
                    _entries.ReadProcedures(element, procedures);
                    break;
            }
        }

        string? version = _root.Child("versionNumber").Attr("value");
        return new CcdaDocument
        {
            Identifier = CcdaDataTypeReader.ReadIdentifiers(_root).FirstOrDefault(),
            SetId = CcdaDataTypeReader.ReadIdentifiers(_root, "setId").FirstOrDefault(),
            VersionNumber = int.TryParse(version, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                ? number
                : null,
            Kind = CcdaClassifier.ClassifyDocument(_root),
            TemplateIds = CcdaDataTypeReader.ReadIdentifiers(_root, "templateId"),
            Code = _types.ReadCode(_root.Child("code")),
            Title = _root.Child("title").TextOf(),
            EffectiveTime = CcdaDataTypeReader.ReadTimestamp(_root.Child("effectiveTime")),
            ConfidentialityCode = _types.ReadCode(_root.Child("confidentialityCode")),
            LanguageCode = _root.Child("languageCode").Attr("code"),
            Patient = ReadPatient(),
            Authors = _root.Children("author").Select(ReadAuthor).ToList(),
            Custodian = ReadOrganization(
                _root.Child("custodian").Child("assignedCustodian").Child("representedCustodianOrganization")),
            HasStructuredBody = structuredBody is not null,
            Sections = sections,
            Problems = problems,
            Allergies = allergies,
            Medications = medications,
            Immunizations = immunizations,
            Results = results,
            VitalSigns = vitalSigns,
            Procedures = procedures,
        };
    }

    private CcdaPatient? ReadPatient()
    {
        XElement? role = _root.Child("recordTarget").Child("patientRole");
        if (role is null)
        {
            return null;
        }

        XElement? patient = role.Child("patient");
        List<XElement?> races = new() { patient.Child("raceCode") };
        List<XElement?> ethnicities = new() { patient.Child("ethnicGroupCode") };
        if (patient is not null)
        {
            races.AddRange(patient.Elements(CcdaXml.Sdtc + "raceCode"));
            ethnicities.AddRange(patient.Elements(CcdaXml.Sdtc + "ethnicGroupCode"));
        }

        return new CcdaPatient
        {
            Identifiers = CcdaDataTypeReader.ReadIdentifiers(role),
            Names = CcdaDataTypeReader.ReadNames(patient),
            Gender = _types.ReadCode(patient.Child("administrativeGenderCode")),
            BirthTime = CcdaDataTypeReader.ReadTimestamp(patient.Child("birthTime")),
            Addresses = CcdaDataTypeReader.ReadAddresses(role),
            Telecoms = CcdaDataTypeReader.ReadTelecoms(role),
            Races = _types.ReadCodes(races),
            Ethnicities = _types.ReadCodes(ethnicities),
            Languages = _types.ReadCodes(
                patient.Children("languageCommunication").Select(l => l.Child("languageCode"))),
        };
    }

    private CcdaAuthor ReadAuthor(XElement author)
    {
        XElement? assigned = author.Child("assignedAuthor");
        return new CcdaAuthor
        {
            Time = CcdaDataTypeReader.ReadTimestamp(author.Child("time")),
            Identifiers = CcdaDataTypeReader.ReadIdentifiers(assigned),
            Names = CcdaDataTypeReader.ReadNames(assigned.Child("assignedPerson")),
            DeviceName = assigned.Child("assignedAuthoringDevice").Child("softwareName").TextOf()
                ?? assigned.Child("assignedAuthoringDevice").Child("manufacturerModelName").TextOf(),
            Organization = ReadOrganization(assigned.Child("representedOrganization")),
        };
    }

    private static CcdaOrganization? ReadOrganization(XElement? organization)
    {
        if (organization is null)
        {
            return null;
        }

        return new CcdaOrganization
        {
            Identifiers = CcdaDataTypeReader.ReadIdentifiers(organization),
            Name = organization.Children("name").Select(n => n.TextOf()).FirstOrDefault(n => n is not null),
            Telecoms = CcdaDataTypeReader.ReadTelecoms(organization),
            Addresses = CcdaDataTypeReader.ReadAddresses(organization),
        };
    }
}
