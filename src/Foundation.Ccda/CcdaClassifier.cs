using System.Xml.Linq;

namespace Foundation.Ccda;

/// <summary>Recognises document and section kinds from their template ids (sections fall back to LOINC).</summary>
internal static class CcdaClassifier
{
    private const string DocumentRoot = "2.16.840.1.113883.10.20.22.1.";
    private const string SectionRoot = "2.16.840.1.113883.10.20.22.2.";

    private static readonly IReadOnlyDictionary<string, CcdaDocumentKind> DocumentTemplates =
        new Dictionary<string, CcdaDocumentKind>(StringComparer.Ordinal)
        {
            [DocumentRoot + "2"] = CcdaDocumentKind.ContinuityOfCareDocument,
            [DocumentRoot + "3"] = CcdaDocumentKind.HistoryAndPhysical,
            [DocumentRoot + "4"] = CcdaDocumentKind.ConsultationNote,
            [DocumentRoot + "5"] = CcdaDocumentKind.DiagnosticImagingReport,
            [DocumentRoot + "6"] = CcdaDocumentKind.ProcedureNote,
            [DocumentRoot + "7"] = CcdaDocumentKind.OperativeNote,
            [DocumentRoot + "8"] = CcdaDocumentKind.DischargeSummary,
            [DocumentRoot + "9"] = CcdaDocumentKind.ProgressNote,
            [DocumentRoot + "10"] = CcdaDocumentKind.UnstructuredDocument,
            [DocumentRoot + "13"] = CcdaDocumentKind.TransferSummary,
            [DocumentRoot + "14"] = CcdaDocumentKind.ReferralNote,
            [DocumentRoot + "15"] = CcdaDocumentKind.CarePlan,
        };

    private static readonly IReadOnlyDictionary<string, CcdaSectionKind> SectionTemplates =
        new Dictionary<string, CcdaSectionKind>(StringComparer.Ordinal)
        {
            [SectionRoot + "6"] = CcdaSectionKind.Allergies,
            [SectionRoot + "6.1"] = CcdaSectionKind.Allergies,
            [SectionRoot + "1"] = CcdaSectionKind.Medications,
            [SectionRoot + "1.1"] = CcdaSectionKind.Medications,
            [SectionRoot + "5"] = CcdaSectionKind.Problems,
            [SectionRoot + "5.1"] = CcdaSectionKind.Problems,
            [SectionRoot + "3"] = CcdaSectionKind.Results,
            [SectionRoot + "3.1"] = CcdaSectionKind.Results,
            [SectionRoot + "4"] = CcdaSectionKind.VitalSigns,
            [SectionRoot + "4.1"] = CcdaSectionKind.VitalSigns,
            [SectionRoot + "2"] = CcdaSectionKind.Immunizations,
            [SectionRoot + "2.1"] = CcdaSectionKind.Immunizations,
            [SectionRoot + "7"] = CcdaSectionKind.Procedures,
            [SectionRoot + "7.1"] = CcdaSectionKind.Procedures,
            [SectionRoot + "22"] = CcdaSectionKind.Encounters,
            [SectionRoot + "22.1"] = CcdaSectionKind.Encounters,
            [SectionRoot + "10"] = CcdaSectionKind.PlanOfTreatment,
            [SectionRoot + "17"] = CcdaSectionKind.SocialHistory,
            [SectionRoot + "15"] = CcdaSectionKind.FamilyHistory,
            [SectionRoot + "14"] = CcdaSectionKind.FunctionalStatus,
            [SectionRoot + "8"] = CcdaSectionKind.Assessment,
            [SectionRoot + "9"] = CcdaSectionKind.AssessmentAndPlan,
            [SectionRoot + "12"] = CcdaSectionKind.ReasonForVisit,
            [SectionRoot + "13"] = CcdaSectionKind.ChiefComplaint,
            ["1.3.6.1.4.1.19376.1.5.3.1.1.13.2.1"] = CcdaSectionKind.ChiefComplaint,
            ["1.3.6.1.4.1.19376.1.5.3.1.3.4"] = CcdaSectionKind.HistoryOfPresentIllness,
            [SectionRoot + "20"] = CcdaSectionKind.PastMedicalHistory,
            [SectionRoot + "18"] = CcdaSectionKind.Payers,
            [SectionRoot + "23"] = CcdaSectionKind.MedicalEquipment,
            [SectionRoot + "21"] = CcdaSectionKind.AdvanceDirectives,
            [SectionRoot + "21.1"] = CcdaSectionKind.AdvanceDirectives,
            [SectionRoot + "45"] = CcdaSectionKind.Instructions,
            [SectionRoot + "56"] = CcdaSectionKind.MentalStatus,
            [SectionRoot + "57"] = CcdaSectionKind.Nutrition,
            [SectionRoot + "60"] = CcdaSectionKind.Goals,
            [SectionRoot + "58"] = CcdaSectionKind.HealthConcerns,
        };

    private static readonly IReadOnlyDictionary<string, CcdaSectionKind> SectionLoincCodes =
        new Dictionary<string, CcdaSectionKind>(StringComparer.Ordinal)
        {
            ["48765-2"] = CcdaSectionKind.Allergies,
            ["10160-0"] = CcdaSectionKind.Medications,
            ["11450-4"] = CcdaSectionKind.Problems,
            ["30954-2"] = CcdaSectionKind.Results,
            ["8716-3"] = CcdaSectionKind.VitalSigns,
            ["11369-6"] = CcdaSectionKind.Immunizations,
            ["47519-4"] = CcdaSectionKind.Procedures,
            ["46240-8"] = CcdaSectionKind.Encounters,
            ["18776-5"] = CcdaSectionKind.PlanOfTreatment,
            ["29762-2"] = CcdaSectionKind.SocialHistory,
            ["10157-6"] = CcdaSectionKind.FamilyHistory,
            ["47420-5"] = CcdaSectionKind.FunctionalStatus,
            ["51848-0"] = CcdaSectionKind.Assessment,
            ["51847-2"] = CcdaSectionKind.AssessmentAndPlan,
            ["29299-5"] = CcdaSectionKind.ReasonForVisit,
            ["10154-3"] = CcdaSectionKind.ChiefComplaint,
            ["46239-0"] = CcdaSectionKind.ChiefComplaint,
            ["10164-2"] = CcdaSectionKind.HistoryOfPresentIllness,
            ["11348-0"] = CcdaSectionKind.PastMedicalHistory,
            ["48768-6"] = CcdaSectionKind.Payers,
            ["46264-8"] = CcdaSectionKind.MedicalEquipment,
            ["42348-3"] = CcdaSectionKind.AdvanceDirectives,
            ["69730-0"] = CcdaSectionKind.Instructions,
            ["10190-7"] = CcdaSectionKind.MentalStatus,
            ["61144-2"] = CcdaSectionKind.Nutrition,
            ["61146-7"] = CcdaSectionKind.Goals,
            ["75310-3"] = CcdaSectionKind.HealthConcerns,
        };

    internal static CcdaDocumentKind ClassifyDocument(XElement clinicalDocument)
    {
        foreach (XElement templateId in clinicalDocument.Children("templateId"))
        {
            string? root = templateId.Attr("root");
            if (root is not null && DocumentTemplates.TryGetValue(root, out CcdaDocumentKind kind))
            {
                return kind;
            }
        }

        return CcdaDocumentKind.Unknown;
    }

    internal static CcdaSectionKind ClassifySection(XElement section)
    {
        foreach (XElement templateId in section.Children("templateId"))
        {
            string? root = templateId.Attr("root");
            if (root is not null && SectionTemplates.TryGetValue(root, out CcdaSectionKind kind))
            {
                return kind;
            }
        }

        XElement? code = section.Child("code");
        string? loinc = code.Attr("code");
        bool isLoinc = code.Attr("codeSystem") is null or CcdaTemplateIds.Loinc;
        if (loinc is not null && isLoinc && SectionLoincCodes.TryGetValue(loinc, out CcdaSectionKind byCode))
        {
            return byCode;
        }

        return CcdaSectionKind.Unknown;
    }
}
