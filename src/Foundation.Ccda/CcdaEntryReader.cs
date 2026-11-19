using System.Xml.Linq;

namespace Foundation.Ccda;

/// <summary>
/// Extracts C-CDA R2.1 clinical statements from a section's direct <c>entry</c> children. Readers are
/// tolerant: a statement of an unexpected shape is skipped rather than failing the document.
/// </summary>
internal sealed class CcdaEntryReader
{
    private readonly CcdaDataTypeReader _types;

    internal CcdaEntryReader(CcdaDataTypeReader types)
    {
        _types = types;
    }

    internal void ReadProblems(XElement section, List<CcdaProblem> problems)
    {
        foreach (XElement statement in Statements(section))
        {
            string? concernStatus = statement.Is("act") ? Status(statement) : null;
            foreach (XElement observation in SubjectObservations(statement, CcdaTemplateIds.ProblemObservation))
            {
                problems.Add(new CcdaProblem
                {
                    Identifiers = CcdaDataTypeReader.ReadIdentifiers(observation),
                    Problem = _types.ReadCode(observation.Child("value")),
                    ProblemType = _types.ReadCode(observation.Child("code")),
                    Period = CcdaDataTypeReader.ReadInterval(observation.Child("effectiveTime")),
                    ConcernStatus = concernStatus,
                    Status = _types.ReadCode(Related(observation, CcdaTemplateIds.ProblemStatus).FirstOrDefault().Child("value")),
                    IsNegated = observation.IsNegated(),
                });
            }
        }
    }

    internal void ReadAllergies(XElement section, List<CcdaAllergy> allergies)
    {
        foreach (XElement statement in Statements(section))
        {
            string? concernStatus = statement.Is("act") ? Status(statement) : null;
            foreach (XElement observation in SubjectObservations(statement, CcdaTemplateIds.AllergyObservation))
            {
                XElement? allergen = observation.Children("participant")
                    .Where(p => p.Attr("typeCode") is null or "CSM")
                    .Select(p => p.Child("participantRole").Child("playingEntity").Child("code"))
                    .FirstOrDefault(c => c is not null);
                XElement? severity = observation.Descendants(CcdaXml.V3 + "observation")
                    .FirstOrDefault(o => o.HasTemplate(CcdaTemplateIds.Severity));

                allergies.Add(new CcdaAllergy
                {
                    Identifiers = CcdaDataTypeReader.ReadIdentifiers(observation),
                    Allergen = _types.ReadCode(allergen),
                    AllergyType = _types.ReadCode(observation.Child("value")),
                    Reactions = _types.ReadCodes(
                        Related(observation, CcdaTemplateIds.Reaction).Select(r => r.Child("value"))),
                    Severity = _types.ReadCode(severity.Child("value")),
                    Period = CcdaDataTypeReader.ReadInterval(observation.Child("effectiveTime")),
                    ConcernStatus = concernStatus,
                    Status = _types.ReadCode(Related(observation, CcdaTemplateIds.AllergyStatus).FirstOrDefault().Child("value")),
                    IsNegated = observation.IsNegated(),
                });
            }
        }
    }

    internal void ReadMedications(XElement section, List<CcdaMedication> medications)
    {
        foreach (XElement statement in Statements(section).Where(s => s.Is("substanceAdministration")))
        {
            List<XElement> times = statement.Children("effectiveTime").ToList();
            XElement? period = times.FirstOrDefault(t => t.XsiType() is null or "IVL_TS");
            XElement? frequency = times.FirstOrDefault(t => t.XsiType() is "PIVL_TS");

            medications.Add(new CcdaMedication
            {
                Identifiers = CcdaDataTypeReader.ReadIdentifiers(statement),
                Medication = _types.ReadCode(Material(statement).Child("code")),
                Status = Status(statement),
                MoodCode = statement.Attr("moodCode"),
                Period = CcdaDataTypeReader.ReadInterval(period),
                DosingPeriod = CcdaDataTypeReader.ReadQuantity(frequency.Child("period")),
                Dose = CcdaDataTypeReader.ReadQuantity(statement.Child("doseQuantity")),
                Route = _types.ReadCode(statement.Child("routeCode")),
                IsNegated = statement.IsNegated(),
            });
        }
    }

    internal void ReadImmunizations(XElement section, List<CcdaImmunization> immunizations)
    {
        foreach (XElement statement in Statements(section).Where(s => s.Is("substanceAdministration")))
        {
            XElement? material = Material(statement);
            immunizations.Add(new CcdaImmunization
            {
                Identifiers = CcdaDataTypeReader.ReadIdentifiers(statement),
                Vaccine = _types.ReadCode(material.Child("code")),
                Status = Status(statement),
                Date = CcdaDataTypeReader.ReadInterval(statement.Child("effectiveTime"))?.Start,
                LotNumber = material.Child("lotNumberText").TextOf(),
                Route = _types.ReadCode(statement.Child("routeCode")),
                Dose = CcdaDataTypeReader.ReadQuantity(statement.Child("doseQuantity")),
                IsRefused = statement.IsNegated(),
            });
        }
    }

    internal void ReadOrganizers(XElement section, List<CcdaResultOrganizer> organizers)
    {
        foreach (XElement statement in Statements(section))
        {
            if (statement.Is("organizer"))
            {
                List<CcdaObservation> observations = statement.Children("component")
                    .Select(c => c.Child("observation"))
                    .OfType<XElement>()
                    .Select(ReadObservation)
                    .ToList();

                organizers.Add(new CcdaResultOrganizer
                {
                    Identifiers = CcdaDataTypeReader.ReadIdentifiers(statement),
                    Code = _types.ReadCode(statement.Child("code")),
                    Status = Status(statement),
                    EffectiveTime = CcdaDataTypeReader.ReadInterval(statement.Child("effectiveTime")),
                    Observations = observations,
                });
            }
            else if (statement.Is("observation"))
            {
                organizers.Add(new CcdaResultOrganizer { Observations = [ReadObservation(statement)] });
            }
        }
    }

    internal void ReadProcedures(XElement section, List<CcdaProcedure> procedures)
    {
        foreach (XElement statement in Statements(section))
        {
            if (!statement.Is("procedure") && !statement.Is("observation") && !statement.Is("act"))
            {
                continue;
            }

            procedures.Add(new CcdaProcedure
            {
                Identifiers = CcdaDataTypeReader.ReadIdentifiers(statement),
                Code = _types.ReadCode(statement.Child("code")),
                Status = Status(statement),
                Period = CcdaDataTypeReader.ReadInterval(statement.Child("effectiveTime")),
                TargetSite = _types.ReadCode(statement.Child("targetSiteCode")),
                IsNegated = statement.IsNegated(),
            });
        }
    }

    private static IEnumerable<XElement> Statements(XElement section)
    {
        return section.Children("entry").SelectMany(e => e.Elements()).Where(e => e.Name.Namespace == CcdaXml.V3);
    }

    /// <summary>
    /// The observations a concern act is about (its <c>SUBJ</c> relationships, or those carrying the
    /// template), or the statement itself when the entry is a bare observation.
    /// </summary>
    private static IEnumerable<XElement> SubjectObservations(XElement statement, string templateRoot)
    {
        if (statement.Is("observation"))
        {
            yield return statement;
            yield break;
        }

        if (!statement.Is("act"))
        {
            yield break;
        }

        foreach (XElement relationship in statement.Children("entryRelationship"))
        {
            XElement? observation = relationship.Child("observation");
            if (observation is not null
                && (observation.HasTemplate(templateRoot) || relationship.Attr("typeCode") == "SUBJ"))
            {
                yield return observation;
            }
        }
    }

    private static IEnumerable<XElement> Related(XElement statement, string templateRoot)
    {
        return statement.Children("entryRelationship")
            .Select(r => r.Child("observation"))
            .OfType<XElement>()
            .Where(o => o.HasTemplate(templateRoot));
    }

    private static XElement? Material(XElement substanceAdministration)
    {
        return substanceAdministration.Child("consumable").Child("manufacturedProduct").Child("manufacturedMaterial");
    }

    private static string? Status(XElement statement)
    {
        return statement.Child("statusCode").Attr("code");
    }

    private CcdaObservation ReadObservation(XElement observation)
    {
        XElement? range = observation.Child("referenceRange").Child("observationRange");
        return new CcdaObservation
        {
            Identifiers = CcdaDataTypeReader.ReadIdentifiers(observation),
            Code = _types.ReadCode(observation.Child("code")),
            Status = Status(observation),
            EffectiveTime = CcdaDataTypeReader.ReadInterval(observation.Child("effectiveTime")),
            Value = _types.ReadValue(observation.Child("value")),
            Interpretations = _types.ReadCodes(observation.Children("interpretationCode")),
            ReferenceRange = _types.ResolveText(range.Child("text")) ?? _types.ReadValue(range.Child("value"))?.Text,
        };
    }
}
