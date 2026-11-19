namespace Foundation.Ccda.Tests;

/// <summary>Entry extraction through <see cref="CcdaParser"/>, one test per clinical statement type.</summary>
[ExcludeFromCodeCoverage]
[TestClass]
public class CcdaParserEntryTests
{
    private CcdaDocument _document = null!;

    [TestInitialize]
    public void Setup()
    {
        _document = CcdaParser.Parse(CcdaSamples.CcdText);
    }

    [TestMethod]
    public void Parse_ExtractsProblemWithConcernAndClinicalStatus_WhenProblemIsActive()
    {
        // Act
        CcdaProblem problem = _document.Problems[0];

        // Assert
        Assert.AreEqual("59621000", problem.Problem!.Code);
        Assert.AreEqual("Essential hypertension", problem.Problem.DisplayName);
        Assert.AreEqual("55607006", problem.ProblemType!.Code);
        Assert.AreEqual("active", problem.ConcernStatus);
        Assert.AreEqual("55561003", problem.Status!.Code);
        Assert.AreEqual("PROB-1", problem.Identifiers.Single().Extension);
        Assert.AreEqual(new DateTimeOffset(2020, 3, 1, 0, 0, 0, TimeSpan.Zero), problem.Period!.Low!.Value);
        Assert.IsNull(problem.Period.High);
        Assert.IsFalse(problem.IsNegated);
    }

    [TestMethod]
    public void Parse_ExtractsNegatedResolvedProblem_WithPartialPrecisionDates()
    {
        // Act
        CcdaProblem problem = _document.Problems[1];

        // Assert
        Assert.IsTrue(problem.IsNegated);
        Assert.AreEqual("completed", problem.ConcernStatus);
        Assert.IsNull(problem.Status);
        Assert.AreEqual(CcdaTimePrecision.Year, problem.Period!.Low!.Precision);
        Assert.AreEqual(CcdaTimePrecision.Month, problem.Period.High!.Precision);
        Assert.AreEqual(new DateTimeOffset(2016, 7, 1, 0, 0, 0, TimeSpan.Zero), problem.Period.High.Value);
    }

    [TestMethod]
    public void Parse_ExtractsAllergyWithReactionSeverityAndStatus_WhenAllergyIsComplete()
    {
        // Act
        CcdaAllergy allergy = _document.Allergies.Single();

        // Assert
        Assert.AreEqual("7980", allergy.Allergen!.Code);
        Assert.AreEqual("Penicillin G", allergy.Allergen.OriginalText);
        Assert.AreEqual("419511003", allergy.AllergyType!.Code);
        CcdaCode reaction = allergy.Reactions.Single();
        Assert.AreEqual("247472004", reaction.Code);
        Assert.AreEqual("Hives", reaction.OriginalText);
        Assert.AreEqual("6736007", allergy.Severity!.Code);
        Assert.AreEqual("55561003", allergy.Status!.Code);
        Assert.AreEqual("active", allergy.ConcernStatus);
        Assert.AreEqual(CcdaTimePrecision.Day, allergy.Period!.Low!.Precision);
        Assert.IsFalse(allergy.IsNegated);
    }

    [TestMethod]
    public void Parse_ExtractsMedicationWithDoseRouteAndFrequency_WhenMedicationIsActive()
    {
        // Act
        CcdaMedication medication = _document.Medications.Single();

        // Assert
        Assert.AreEqual("197361", medication.Medication!.Code);
        Assert.AreEqual("00000-0000-00", medication.Medication.Translations.Single().Code);
        Assert.AreEqual("active", medication.Status);
        Assert.AreEqual("EVN", medication.MoodCode);
        Assert.AreEqual(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero), medication.Period!.Low!.Value);
        Assert.IsNull(medication.Period.High);
        Assert.AreEqual(12m, medication.DosingPeriod!.Value);
        Assert.AreEqual("h", medication.DosingPeriod.Unit);
        Assert.AreEqual(1m, medication.Dose!.Value);
        Assert.IsNull(medication.Dose.Unit);
        Assert.AreEqual("C38288", medication.Route!.Code);
        Assert.IsFalse(medication.IsNegated);
    }

    [TestMethod]
    public void Parse_ExtractsGivenAndRefusedImmunizations_WhenSectionHasBoth()
    {
        // Act
        IReadOnlyList<CcdaImmunization> immunizations = _document.Immunizations;

        // Assert
        Assert.AreEqual(2, immunizations.Count);
        CcdaImmunization given = immunizations[0];
        Assert.AreEqual("140", given.Vaccine!.Code);
        Assert.AreEqual(new DateTimeOffset(2023, 10, 15, 0, 0, 0, TimeSpan.Zero), given.Date!.Value);
        Assert.AreEqual("LOT-123", given.LotNumber);
        Assert.AreEqual(0.5m, given.Dose!.Value);
        Assert.AreEqual("mL", given.Dose.Unit);
        Assert.AreEqual("C28161", given.Route!.Code);
        Assert.IsFalse(given.IsRefused);
        Assert.AreEqual("115", immunizations[1].Vaccine!.Code);
        Assert.IsTrue(immunizations[1].IsRefused);
    }

    [TestMethod]
    public void Parse_ExtractsResultPanelWithValuesAndReferenceRanges_WhenOrganizerIsPresent()
    {
        // Act
        CcdaResultOrganizer panel = _document.Results[0];

        // Assert
        Assert.AreEqual("58410-2", panel.Code!.Code);
        Assert.AreEqual("completed", panel.Status);
        Assert.AreEqual(CcdaTimePrecision.Minute, panel.EffectiveTime!.Value!.Precision);
        Assert.AreEqual(2, panel.Observations.Count);
        CcdaObservation hemoglobin = panel.Observations[0];
        Assert.AreEqual("718-7", hemoglobin.Code!.Code);
        Assert.AreEqual("PQ", hemoglobin.Value!.XsiType);
        Assert.AreEqual(13.2m, hemoglobin.Value.Quantity!.Value);
        Assert.AreEqual("g/dL", hemoglobin.Value.Quantity.Unit);
        Assert.AreEqual("N", hemoglobin.Interpretations.Single().Code);
        Assert.AreEqual("12.0-16.0 g/dL", hemoglobin.ReferenceRange);
        CcdaObservation whiteCells = panel.Observations[1];
        Assert.AreEqual("H", whiteCells.Interpretations.Single().Code);
        Assert.AreEqual("4.0-10.5 10*3/uL", whiteCells.ReferenceRange);
    }

    [TestMethod]
    public void Parse_WrapsBareObservationInUncodedOrganizer_WhenEntryHasNoOrganizer()
    {
        // Act
        CcdaResultOrganizer bare = _document.Results[1];

        // Assert
        Assert.IsNull(bare.Code);
        CcdaObservation observation = bare.Observations.Single();
        Assert.AreEqual("5778-6", observation.Code!.Code);
        Assert.AreEqual("ST", observation.Value!.XsiType);
        Assert.AreEqual("Yellow", observation.Value.Text);
    }

    [TestMethod]
    public void Parse_ExtractsVitalSigns_WhenVitalSignsOrganizerIsPresent()
    {
        // Act
        CcdaResultOrganizer vitals = _document.VitalSigns.Single();

        // Assert
        CollectionAssert.AreEqual(new[] { "8480-6", "8867-4" }, vitals.Observations.Select(o => o.Code!.Code).ToArray());
        Assert.AreEqual(120m, vitals.Observations[0].Value!.Quantity!.Value);
        Assert.AreEqual("mm[Hg]", vitals.Observations[0].Value!.Quantity!.Unit);
    }

    [TestMethod]
    public void Parse_ExtractsProcedureWithTargetSite_WhenProcedureIsPresent()
    {
        // Act
        CcdaProcedure procedure = _document.Procedures.Single();

        // Assert
        Assert.AreEqual("80146002", procedure.Code!.Code);
        Assert.AreEqual("completed", procedure.Status);
        Assert.AreEqual(new DateTimeOffset(2015, 6, 10, 0, 0, 0, TimeSpan.Zero), procedure.Period!.Start!.Value);
        Assert.AreEqual("66754008", procedure.TargetSite!.Code);
        Assert.IsFalse(procedure.IsNegated);
    }

    [TestMethod]
    public void Parse_ReadsValuesByType_WhenXsiTypeIsMissingBooleanNullOrUnresolved()
    {
        // Arrange
        string observations =
            Observation("<value code=\"X1\" codeSystem=\"9.9\"/>") +
            Observation("<value xsi:type=\"BL\" value=\"true\"/>") +
            Observation("<value xsi:type=\"PQ\" nullFlavor=\"NI\"/>") +
            Observation("<value xsi:type=\"PQ\" value=\"abc\" unit=\"mg\"/>") +
            Observation("<value xsi:type=\"ST\"><reference value=\"#missing\"/>fallback</value>") +
            Observation("<value xsi:type=\"IVL_PQ\"><low value=\"5\" unit=\"mg\"/></value>") +
            Observation("<value/>");
        string xml = CcdaSamples.WithSections(CcdaSamples.Section(
            "<templateId root=\"2.16.840.1.113883.10.20.22.2.3.1\"/>" + observations));

        // Act
        CcdaValue?[] values = CcdaParser.Parse(xml).Results.Select(r => r.Observations.Single().Value).ToArray();

        // Assert
        Assert.AreEqual("X1", values[0]!.Code!.Code);
        Assert.IsNull(values[0]!.XsiType);
        Assert.AreEqual("true", values[1]!.Text);
        Assert.AreEqual("NI", values[2]!.NullFlavor);
        Assert.IsNull(values[2]!.Quantity);
        Assert.IsNull(values[3]!.Quantity!.Value);
        Assert.AreEqual("abc", values[3]!.Quantity!.RawValue);
        Assert.AreEqual("fallback", values[4]!.Text);
        Assert.AreEqual(">=5 mg", values[5]!.Text);
        Assert.IsNull(values[6]);
    }

    [TestMethod]
    public void Parse_IgnoresEntries_WhenSectionKindHasNoExtractor()
    {
        // Arrange
        string xml = CcdaSamples.WithSections(CcdaSamples.Section(
            "<code code=\"29762-2\"/><entry><observation><code code=\"72166-2\"/></observation></entry>"));

        // Act
        CcdaDocument document = CcdaParser.Parse(xml);

        // Assert
        Assert.AreEqual(1, document.Sections.Single().EntryCount);
        Assert.AreEqual(0, document.Results.Count);
        Assert.AreEqual(0, document.Problems.Count);
    }

    private static string Observation(string valueXml)
    {
        return $"<entry><observation><code code=\"1-1\"/>{valueXml}</observation></entry>";
    }
}
