using System.Text;

namespace Foundation.Ccda.Tests;

[ExcludeFromCodeCoverage]
[TestClass]
public class CcdaParserTests
{
    [TestMethod]
    public void Parse_ReadsHeader_WhenDocumentIsCcd()
    {
        // Act
        CcdaDocument document = CcdaParser.Parse(CcdaSamples.CcdText);

        // Assert
        Assert.AreEqual(CcdaDocumentKind.ContinuityOfCareDocument, document.Kind);
        Assert.AreEqual("2.16.840.1.113883.19.5.99999.1", document.Identifier!.Root);
        Assert.AreEqual("DOC-0001", document.Identifier.Extension);
        Assert.AreEqual("SET-0001", document.SetId!.Extension);
        Assert.AreEqual(2, document.VersionNumber);
        Assert.AreEqual(2, document.TemplateIds.Count);
        Assert.AreEqual("34133-9", document.Code!.Code);
        Assert.AreEqual("LOINC", document.Code.CodeSystemName);
        Assert.AreEqual("Synthetic Continuity of Care Document", document.Title);
        Assert.AreEqual("N", document.ConfidentialityCode!.Code);
        Assert.AreEqual("en-US", document.LanguageCode);
        Assert.IsTrue(document.HasStructuredBody);
    }

    [TestMethod]
    public void Parse_ReadsEffectiveTimeWithOffset_WhenHeaderGivesOne()
    {
        // Act
        CcdaDocument document = CcdaParser.Parse(CcdaSamples.CcdText);

        // Assert
        CcdaTimestamp time = document.EffectiveTime!;
        Assert.AreEqual(new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.FromHours(-5)), time.Value);
        Assert.AreEqual(CcdaTimePrecision.Second, time.Precision);
        Assert.IsTrue(time.HasOffset);
    }

    [TestMethod]
    public void Parse_ReadsPatient_WhenRecordTargetIsPresent()
    {
        // Act
        CcdaPatient patient = CcdaParser.Parse(CcdaSamples.CcdText).Patient!;

        // Assert
        Assert.AreEqual(2, patient.Identifiers.Count);
        Assert.AreEqual("MRN-998877", patient.Identifiers[0].Extension);
        Assert.AreEqual("UNK", patient.Identifiers[1].NullFlavor);
        CcdaPersonName name = patient.Names.Single();
        Assert.AreEqual("L", name.Use);
        CollectionAssert.AreEqual(new[] { "Alex", "J" }, name.Given.ToArray());
        Assert.AreEqual("Sample", name.Family);
        Assert.AreEqual("Ms. Alex J Sample", name.Text);
        Assert.AreEqual("F", patient.Gender!.Code);
        Assert.AreEqual(new DateTimeOffset(1980, 2, 12, 0, 0, 0, TimeSpan.Zero), patient.BirthTime!.Value);
        Assert.AreEqual(CcdaTimePrecision.Day, patient.BirthTime.Precision);
        CollectionAssert.AreEqual(new[] { "2106-3", "2028-9" }, patient.Races.Select(r => r.Code).ToArray());
        Assert.AreEqual("2186-5", patient.Ethnicities.Single().Code);
        Assert.AreEqual("en", patient.Languages.Single().Code);
    }

    [TestMethod]
    public void Parse_ReadsPatientContactDetails_AndSkipsNullFlavoredTelecoms()
    {
        // Act
        CcdaPatient patient = CcdaParser.Parse(CcdaSamples.CcdText).Patient!;

        // Assert
        CcdaAddress address = patient.Addresses.Single();
        Assert.AreEqual("HP", address.Use);
        CollectionAssert.AreEqual(new[] { "100 Example Street", "Unit 2" }, address.StreetLines.ToArray());
        Assert.AreEqual("Testville", address.City);
        Assert.AreEqual("ZZ", address.State);
        Assert.AreEqual("00000", address.PostalCode);
        Assert.AreEqual("US", address.Country);
        CcdaTelecom telecom = patient.Telecoms.Single();
        Assert.AreEqual("tel:+1(555)555-0100", telecom.Value);
        Assert.AreEqual("HP", telecom.Use);
    }

    [TestMethod]
    public void Parse_ReadsPersonAndDeviceAuthorsAndCustodian_WhenPresent()
    {
        // Act
        CcdaDocument document = CcdaParser.Parse(CcdaSamples.CcdText);

        // Assert
        Assert.AreEqual(2, document.Authors.Count);
        CcdaAuthor person = document.Authors[0];
        Assert.AreEqual("Pat Clinician MD", person.Names.Single().Text);
        Assert.AreEqual("0000000000", person.Identifiers.Single().Extension);
        Assert.AreEqual("Example Clinic", person.Organization!.Name);
        Assert.AreEqual(CcdaTimePrecision.Day, person.Time!.Precision);
        CcdaAuthor device = document.Authors[1];
        Assert.AreEqual("Example EHR", device.DeviceName);
        Assert.AreEqual(0, device.Names.Count);
        Assert.AreEqual("Example Health System", document.Custodian!.Name);
        Assert.AreEqual(1, document.Custodian.Telecoms.Count);
        Assert.AreEqual("Testville", document.Custodian.Addresses.Single().City);
    }

    [TestMethod]
    public void Parse_ClassifiesSectionsInDocumentOrder_IncludingNestedSections()
    {
        // Act
        CcdaDocument document = CcdaParser.Parse(CcdaSamples.CcdText);

        // Assert
        CcdaSectionKind[] expected =
        {
            CcdaSectionKind.Allergies, CcdaSectionKind.Medications, CcdaSectionKind.Problems,
            CcdaSectionKind.Results, CcdaSectionKind.VitalSigns, CcdaSectionKind.Immunizations,
            CcdaSectionKind.Procedures, CcdaSectionKind.SocialHistory, CcdaSectionKind.Unknown,
        };
        CollectionAssert.AreEqual(expected, document.Sections.Select(s => s.Kind).ToArray());
        Assert.AreEqual("Local Notes", document.Sections[8].Title);
        Assert.AreEqual(2, document.GetSection(CcdaSectionKind.Problems)!.EntryCount);
    }

    [TestMethod]
    public void Parse_ClassifiesByLoincCode_WhenSectionHasNoKnownTemplate()
    {
        // Arrange
        string xml = CcdaSamples.WithSections(
            CcdaSamples.Section("<templateId root=\"9.9.9\"/><code code=\"10160-0\"/>") +
            CcdaSamples.Section("<code code=\"11450-4\" codeSystem=\"2.16.840.1.113883.6.1\"/>") +
            CcdaSamples.Section("<code code=\"11450-4\" codeSystem=\"2.16.840.1.113883.19.5.99999.5\"/>"));

        // Act
        CcdaDocument document = CcdaParser.Parse(xml);

        // Assert
        CollectionAssert.AreEqual(
            new[] { CcdaSectionKind.Medications, CcdaSectionKind.Problems, CcdaSectionKind.Unknown },
            document.Sections.Select(s => s.Kind).ToArray());
    }

    [TestMethod]
    public void Parse_RendersNarrativeAsPlainText_WhenSectionHasTablesListsAndBreaks()
    {
        // Act
        CcdaDocument document = CcdaParser.Parse(CcdaSamples.CcdText);

        // Assert
        Assert.AreEqual("Substance | Reaction\nPenicillin G | Hives", document.GetSection(CcdaSectionKind.Allergies)!.Text);
        Assert.AreEqual("Amlodipine 5 MG Oral Tablet, twice daily", document.GetSection(CcdaSectionKind.Medications)!.Text);
        Assert.AreEqual("Never smoker.\nOccasional alcohol.", document.GetSection(CcdaSectionKind.SocialHistory)!.Text);
        Assert.AreEqual("Free text note.", document.Sections[8].Text);
        Assert.IsNull(document.GetSection(CcdaSectionKind.VitalSigns)!.Text);
    }

    [TestMethod]
    public void Parse_ReportsNoStructuredBody_WhenBodyIsNonXml()
    {
        // Arrange
        string xml =
            $"<ClinicalDocument {CcdaSamples.Namespaces}>" +
            "<templateId root=\"2.16.840.1.113883.10.20.22.1.10\"/>" +
            "<component><nonXMLBody><text mediaType=\"text/plain\">x</text></nonXMLBody></component>" +
            "</ClinicalDocument>";

        // Act
        CcdaDocument document = CcdaParser.Parse(xml);

        // Assert
        Assert.IsFalse(document.HasStructuredBody);
        Assert.AreEqual(CcdaDocumentKind.UnstructuredDocument, document.Kind);
        Assert.AreEqual(0, document.Sections.Count);
        Assert.IsNull(document.Patient);
        Assert.IsNull(document.Identifier);
    }

    [TestMethod]
    public void Parse_ThrowsEmpty_WhenTextIsNullOrWhitespace()
    {
        // Act & Assert
        Assert.AreEqual(CcdaParseFailure.Empty, Assert.ThrowsExactly<CcdaParseException>(() => CcdaParser.Parse(null)).Reason);
        Assert.AreEqual(CcdaParseFailure.Empty, Assert.ThrowsExactly<CcdaParseException>(() => CcdaParser.Parse("")).Reason);
        Assert.AreEqual(CcdaParseFailure.Empty, Assert.ThrowsExactly<CcdaParseException>(() => CcdaParser.Parse(" \r\n ")).Reason);
    }

    [TestMethod]
    public void Parse_ThrowsTooLarge_WhenDocumentExceedsByteCap()
    {
        // Arrange
        CcdaParserOptions options = new() { MaxDocumentBytes = 100 };

        // Act
        CcdaParseException ex = Assert.ThrowsExactly<CcdaParseException>(() => CcdaParser.Parse(CcdaSamples.CcdText, options));

        // Assert
        Assert.AreEqual(CcdaParseFailure.TooLarge, ex.Reason);
    }

    [TestMethod]
    public void Parse_ThrowsMalformedWithLocation_WhenXmlIsNotWellFormed()
    {
        // Act
        CcdaParseException ex = Assert.ThrowsExactly<CcdaParseException>(
            () => CcdaParser.Parse("<ClinicalDocument xmlns=\"urn:hl7-org:v3\">\n<title>x</titl>"));

        // Assert
        Assert.AreEqual(CcdaParseFailure.MalformedXml, ex.Reason);
        Assert.AreEqual(2, ex.LineNumber);
        Assert.IsTrue(ex.LinePosition > 0);
    }

    [TestMethod]
    public void Parse_NeverEchoesDocumentContent_WhenRejectingMalformedXml()
    {
        // Arrange
        string xml = "<ClinicalDocument xmlns=\"urn:hl7-org:v3\"><SecretPatientName>Jane</SecretPatientNam></ClinicalDocument>";

        // Act
        CcdaParseException ex = Assert.ThrowsExactly<CcdaParseException>(() => CcdaParser.Parse(xml));

        // Assert
        Assert.IsFalse(ex.Message.Contains("Secret", StringComparison.Ordinal));
        Assert.IsFalse(ex.Message.Contains("Jane", StringComparison.Ordinal));
        Assert.IsNull(ex.InnerException);
    }

    [TestMethod]
    public void Parse_RejectsDtd_WhenDocumentDeclaresExternalEntity()
    {
        // Arrange
        string xml =
            "<?xml version=\"1.0\"?><!DOCTYPE ClinicalDocument [<!ENTITY xxe SYSTEM \"file:///c:/windows/win.ini\">]>" +
            "<ClinicalDocument xmlns=\"urn:hl7-org:v3\"><title>&xxe;</title></ClinicalDocument>";

        // Act
        CcdaParseException ex = Assert.ThrowsExactly<CcdaParseException>(() => CcdaParser.Parse(xml));

        // Assert
        Assert.AreEqual(CcdaParseFailure.MalformedXml, ex.Reason);
    }

    [TestMethod]
    public void Parse_RejectsDtd_WhenDocumentDeclaresEntityExpansion()
    {
        // Arrange
        string xml =
            "<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY lol \"lol\"><!ENTITY lol2 \"&lol;&lol;&lol;&lol;\">]>" +
            "<ClinicalDocument xmlns=\"urn:hl7-org:v3\"><title>&lol2;</title></ClinicalDocument>";

        // Act
        CcdaParseException ex = Assert.ThrowsExactly<CcdaParseException>(() => CcdaParser.Parse(xml));

        // Assert
        Assert.AreEqual(CcdaParseFailure.MalformedXml, ex.Reason);
    }

    [TestMethod]
    public void Parse_ThrowsNotClinicalDocument_WhenRootIsWrongNameOrNamespace()
    {
        // Act
        CcdaParseException wrongName = Assert.ThrowsExactly<CcdaParseException>(
            () => CcdaParser.Parse("<Bundle xmlns=\"urn:hl7-org:v3\"/>"));
        CcdaParseException noNamespace = Assert.ThrowsExactly<CcdaParseException>(
            () => CcdaParser.Parse("<ClinicalDocument/>"));

        // Assert
        Assert.AreEqual(CcdaParseFailure.NotClinicalDocument, wrongName.Reason);
        Assert.AreEqual(CcdaParseFailure.NotClinicalDocument, noNamespace.Reason);
    }

    [TestMethod]
    public void Parse_ThrowsTooDeep_WhenNestingIsPathological()
    {
        // Arrange: deep enough that unbounded recursion would overflow the stack.
        const int depth = 50_000;
        StringBuilder xml = new();
        xml.Append("<ClinicalDocument xmlns=\"urn:hl7-org:v3\"><component><structuredBody><component><section><text>");
        xml.Insert(xml.Length, "<content>", depth);
        xml.Insert(xml.Length, "</content>", depth);
        xml.Append("</text></section></component></structuredBody></component></ClinicalDocument>");

        // Act
        CcdaParseException ex = Assert.ThrowsExactly<CcdaParseException>(() => CcdaParser.Parse(xml.ToString()));

        // Assert
        Assert.AreEqual(CcdaParseFailure.TooDeep, ex.Reason);
    }

    [TestMethod]
    public void Parse_ThrowsTooManySections_WhenSectionCapIsExceeded()
    {
        // Arrange
        CcdaParserOptions options = new() { MaxSections = 2 };

        // Act
        CcdaParseException ex = Assert.ThrowsExactly<CcdaParseException>(() => CcdaParser.Parse(CcdaSamples.CcdText, options));

        // Assert
        Assert.AreEqual(CcdaParseFailure.TooManySections, ex.Reason);
    }

    [TestMethod]
    public void Parse_ThrowsTooManyEntries_WhenEntryCapIsExceeded()
    {
        // Arrange
        CcdaParserOptions options = new() { MaxEntriesPerSection = 1 };

        // Act
        CcdaParseException ex = Assert.ThrowsExactly<CcdaParseException>(() => CcdaParser.Parse(CcdaSamples.CcdText, options));

        // Assert
        Assert.AreEqual(CcdaParseFailure.TooManyEntries, ex.Reason);
    }

    [TestMethod]
    public void Parse_ThrowsOutOfRange_WhenAnOptionIsNotPositive()
    {
        // Act & Assert
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CcdaParser.Parse("x", new CcdaParserOptions { MaxDocumentBytes = 0 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CcdaParser.Parse("x", new CcdaParserOptions { MaxElementDepth = 0 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CcdaParser.Parse("x", new CcdaParserOptions { MaxSections = 0 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CcdaParser.Parse("x", new CcdaParserOptions { MaxEntriesPerSection = 0 }));
    }

    [TestMethod]
    public void TryParse_ReturnsFalse_WhenDocumentIsRejected()
    {
        // Act
        bool isParsed = CcdaParser.TryParse("<not-xml", out CcdaDocument? document);

        // Assert
        Assert.IsFalse(isParsed);
        Assert.IsNull(document);
    }

    [TestMethod]
    public void TryParse_ReturnsDocument_WhenDocumentIsValid()
    {
        // Act
        bool isParsed = CcdaParser.TryParse(CcdaSamples.CcdText, out CcdaDocument? document);

        // Assert
        Assert.IsTrue(isParsed);
        Assert.AreEqual("Synthetic Continuity of Care Document", document!.Title);
    }

    [TestMethod]
    public async Task ParseAsync_ParsesAndLeavesStreamOpen_WhenStreamHoldsDocument()
    {
        // Arrange
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(CcdaSamples.CcdText));

        // Act
        CcdaDocument document = await CcdaParser.ParseAsync(stream);

        // Assert
        Assert.AreEqual("Synthetic Continuity of Care Document", document.Title);
        Assert.AreEqual(2, document.Problems.Count);
        Assert.IsTrue(stream.CanRead);
    }

    [TestMethod]
    public async Task ParseAsync_HonoursDeclaredEncoding_WhenStreamIsUtf16()
    {
        // Arrange
        string xml = $"<?xml version=\"1.0\" encoding=\"utf-16\"?><ClinicalDocument {CcdaSamples.Namespaces}><title>Caf\u00e9</title></ClinicalDocument>";
        using MemoryStream stream = new(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(xml)).ToArray());

        // Act
        CcdaDocument document = await CcdaParser.ParseAsync(stream);

        // Assert
        Assert.AreEqual("Caf\u00e9", document.Title);
    }

    [TestMethod]
    public async Task ParseAsync_ThrowsTooLarge_WhenStreamExceedsByteCap()
    {
        // Arrange
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(CcdaSamples.CcdText));
        CcdaParserOptions options = new() { MaxDocumentBytes = 1024 };

        // Act
        CcdaParseException ex = await Assert.ThrowsExactlyAsync<CcdaParseException>(() => CcdaParser.ParseAsync(stream, options));

        // Assert
        Assert.AreEqual(CcdaParseFailure.TooLarge, ex.Reason);
        Assert.AreEqual(1025, stream.Position);
    }

    [TestMethod]
    public async Task ParseAsync_ThrowsEmpty_WhenStreamIsEmpty()
    {
        // Arrange
        using MemoryStream stream = new();

        // Act
        CcdaParseException ex = await Assert.ThrowsExactlyAsync<CcdaParseException>(() => CcdaParser.ParseAsync(stream));

        // Assert
        Assert.AreEqual(CcdaParseFailure.Empty, ex.Reason);
    }

    [TestMethod]
    public async Task ParseAsync_ThrowsMalformed_WhenStreamIsNotXml()
    {
        // Arrange
        using MemoryStream stream = new(Encoding.UTF8.GetBytes("not xml"));

        // Act
        CcdaParseException ex = await Assert.ThrowsExactlyAsync<CcdaParseException>(() => CcdaParser.ParseAsync(stream));

        // Assert
        Assert.AreEqual(CcdaParseFailure.MalformedXml, ex.Reason);
    }

    [TestMethod]
    public async Task ParseAsync_ThrowsArgumentNull_WhenStreamIsNull()
    {
        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => CcdaParser.ParseAsync(null!));
    }
}
