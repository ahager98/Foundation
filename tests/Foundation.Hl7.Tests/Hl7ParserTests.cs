namespace Foundation.Hl7.Tests;

[TestClass]
public class Hl7ParserTests
{
    private const string Sample =
        "MSH|^~\\&|SND|FAC|RCV|FAC2|20240101120000||ADT^A01^ADT_A01|MSG0001|P|2.5.1\r" +
        "EVN|A01|20240101120000\r" +
        "PID|1||12345^^^HOSP^MR||DOE^JOHN^Q||19800101|M\r" +
        "OBX|1|ST|CODE1||first\r" +
        "OBX|2|ST|CODE2||second\r";

    [TestMethod]
    public void Parse_SplitsSegmentsInOrder_WhenMessageIsWellFormed()
    {
        Hl7Message message = Hl7Parser.Parse(Sample);

        CollectionAssert.AreEqual(
            new[] { "MSH", "EVN", "PID", "OBX", "OBX" },
            message.Segments.Select(s => s.Id).ToArray());
        Assert.AreEqual(Hl7Delimiters.Standard, message.Delimiters);
    }

    [TestMethod]
    public void Parse_NumbersMshFieldsFromTheSeparator_WhenSegmentIsMsh()
    {
        Hl7Message message = Hl7Parser.Parse(Sample);

        Assert.AreEqual("|", message.GetValue("MSH", 1));
        Assert.AreEqual("^~\\&", message.GetValue("MSH", 2));
        Assert.AreEqual("SND", message.GetValue("MSH", 3));
        Assert.AreEqual("ADT", message.GetValue("MSH", 9, component: 1));
        Assert.AreEqual("A01", message.GetValue("MSH", 9, component: 2));
        Assert.AreEqual("2.5.1", message.GetValue("MSH", 12));
    }

    [TestMethod]
    public void GetValue_ReadsComponents_WhenFieldHasSeveral()
    {
        Hl7Message message = Hl7Parser.Parse(Sample);

        Assert.AreEqual("12345", message.GetValue("PID", 3, component: 1));
        Assert.AreEqual("HOSP", message.GetValue("PID", 3, component: 4));
        Assert.AreEqual("JOHN", message.GetValue("PID", 5, component: 2));
    }

    [TestMethod]
    public void GetValue_ReturnsEmpty_WhenSegmentFieldOrComponentIsAbsent()
    {
        Hl7Message message = Hl7Parser.Parse(Sample);

        Assert.AreEqual(string.Empty, message.GetValue("ZZZ", 1));
        Assert.AreEqual(string.Empty, message.GetValue("PID", 40));
        Assert.AreEqual(string.Empty, message.GetValue("PID", 5, component: 9));
        Assert.AreEqual(string.Empty, message.GetValue("PID", 5, subComponent: 3));
        Assert.AreEqual(string.Empty, message.GetValue("PID", 5, repetition: 4));
    }

    [TestMethod]
    public void GetValue_ThrowsOutOfRange_WhenPositionIsBelowOne()
    {
        Hl7Message message = Hl7Parser.Parse(Sample);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => message.GetValue("PID", 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => message.GetValue("PID", 1, component: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => message.GetValue("PID", 1, subComponent: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => message.GetValue("PID", 1, repetition: 0));
    }

    [TestMethod]
    public void GetSegments_ReturnsEveryMatch_WhenSegmentRepeats()
    {
        Hl7Message message = Hl7Parser.Parse(Sample);

        Hl7Segment[] observations = message.GetSegments("OBX").ToArray();

        Assert.AreEqual(2, observations.Length);
        Assert.AreEqual("first", observations[0].Field(5).Raw);
        Assert.AreEqual("second", observations[1].Field(5).Raw);
        Assert.IsNull(message.GetSegment("ZZZ"));
    }

    [TestMethod]
    public void Parse_SplitsRepetitionsAndSubComponents_WhenFieldUsesThem()
    {
        Hl7Message message = Hl7Parser.Parse("MSH|^~\\&|A\rPID|1|X&Y^Z~W\r");

        Hl7Field field = message.GetSegment("PID")!.Field(2);

        Assert.AreEqual(2, field.Repetitions.Count);
        Assert.AreEqual("X", message.GetValue("PID", 2, component: 1, subComponent: 1));
        Assert.AreEqual("Y", message.GetValue("PID", 2, component: 1, subComponent: 2));
        Assert.AreEqual("Z", message.GetValue("PID", 2, component: 2));
        Assert.AreEqual("W", message.GetValue("PID", 2, repetition: 2));
    }

    [TestMethod]
    public void Parse_AcceptsLfAndCrLfAndSkipsBlankLines_WhenSegmentsAreSeparatedDifferently()
    {
        Hl7Message lf = Hl7Parser.Parse("\n\nMSH|^~\\&|A\nPID|1\n\n");
        Hl7Message crlf = Hl7Parser.Parse("MSH|^~\\&|A\r\nPID|1\r\n");

        Assert.AreEqual(2, lf.Segments.Count);
        Assert.AreEqual(2, crlf.Segments.Count);
    }

    [TestMethod]
    public void Parse_UsesDeclaredDelimiters_WhenMessageIsNotStandard()
    {
        Hl7Message message = Hl7Parser.Parse("MSH#*@/$#SND\rPID#1#A*B$C@D/F/E\r");

        Assert.AreEqual('#', message.Delimiters.Field);
        Assert.AreEqual('*', message.Delimiters.Component);
        Assert.AreEqual('@', message.Delimiters.Repetition);
        Assert.AreEqual('/', message.Delimiters.Escape);
        Assert.AreEqual('$', message.Delimiters.SubComponent);
        Assert.AreEqual("SND", message.GetValue("MSH", 3));
        Assert.AreEqual("A", message.GetValue("PID", 2, component: 1));
        Assert.AreEqual("B", message.GetValue("PID", 2, component: 2, subComponent: 1));
        Assert.AreEqual("C", message.GetValue("PID", 2, component: 2, subComponent: 2));
        Assert.AreEqual("D#E", message.GetValue("PID", 2, repetition: 2));
    }

    [TestMethod]
    public void Parse_ReadsTruncationCharacter_WhenMsh2HasFiveCharacters()
    {
        Hl7Message message = Hl7Parser.Parse("MSH|^~\\&#|SND\r");

        Assert.AreEqual((char?)'#', message.Delimiters.Truncation);
        Assert.AreEqual("SND", message.GetValue("MSH", 3));
    }

    [TestMethod]
    public void Parse_KeepsSegmentWithoutFields_WhenLineIsJustTheId()
    {
        Hl7Message message = Hl7Parser.Parse("MSH|^~\\&\rPV1\r");

        Assert.AreEqual(0, message.GetSegment("PV1")!.Fields.Count);
        Assert.IsTrue(message.GetSegment("PV1")!.Field(1).IsEmpty);
        Assert.AreEqual(2, message.GetSegment("MSH")!.Fields.Count);
    }

    [TestMethod]
    public void Parse_TreatsTrailingSeparatorAsEmptyField_WhenSegmentEndsWithOne()
    {
        Hl7Message message = Hl7Parser.Parse("MSH|^~\\&\rPID|\r");

        Assert.AreEqual(1, message.GetSegment("PID")!.Fields.Count);
        Assert.IsTrue(message.GetSegment("PID")!.Field(1).IsEmpty);
    }

    [TestMethod]
    public void Parse_Throws_WhenTextIsNullEmptyOrWhitespace()
    {
        foreach (string? text in new string?[] { null, string.Empty, " \r\n " })
        {
            Hl7ParseException ex = Assert.ThrowsExactly<Hl7ParseException>(() => Hl7Parser.Parse(text));
            Assert.AreEqual(Hl7ParseFailure.Empty, ex.Reason);
        }
    }

    [TestMethod]
    public void Parse_Throws_WhenFirstSegmentIsNotMsh()
    {
        foreach (string text in new[] { "PID|1\r", "msh|^~\\&\r", "MSH", "\u000bMSH|^~\\&\r" })
        {
            Hl7ParseException ex = Assert.ThrowsExactly<Hl7ParseException>(() => Hl7Parser.Parse(text));
            Assert.AreEqual(Hl7ParseFailure.MissingMsh, ex.Reason);
            Assert.AreEqual(0, ex.SegmentIndex);
        }
    }

    [TestMethod]
    public void Parse_Throws_WhenDelimitersAreMalformed()
    {
        string[] cases =
        {
            "MSH|^~\\|SND\r",
            "MSH|^~\\&##|SND\r",
            "MSH|^^\\&|SND\r",
            "MSH|a~\\&|SND\r",
            "MSHx^~\\&x\r",
            "MSH|^~|SND\r",
        };

        foreach (string text in cases)
        {
            Hl7ParseException ex = Assert.ThrowsExactly<Hl7ParseException>(() => Hl7Parser.Parse(text));
            Assert.AreEqual(Hl7ParseFailure.InvalidDelimiters, ex.Reason, text);
        }
    }

    [TestMethod]
    public void Parse_Throws_WhenASegmentIdIsInvalid()
    {
        foreach (string bad in new[] { "pid|1", "PIDX|1", "P|1", "1ID|1", "PI" })
        {
            Hl7ParseException ex = Assert.ThrowsExactly<Hl7ParseException>(
                () => Hl7Parser.Parse("MSH|^~\\&\r" + bad + "\r"));
            Assert.AreEqual(Hl7ParseFailure.InvalidSegmentId, ex.Reason, bad);
            Assert.AreEqual(1, ex.SegmentIndex);
        }
    }

    [TestMethod]
    public void Parse_Throws_WhenMessageExceedsMaxLength()
    {
        Hl7ParserOptions options = new() { MaxMessageLength = 10 };

        Hl7ParseException ex = Assert.ThrowsExactly<Hl7ParseException>(
            () => Hl7Parser.Parse("MSH|^~\\&|SND\r", options));

        Assert.AreEqual(Hl7ParseFailure.TooLong, ex.Reason);
    }

    [TestMethod]
    public void Parse_Throws_WhenMessageExceedsMaxSegments()
    {
        Hl7ParserOptions options = new() { MaxSegments = 2 };

        Hl7ParseException ex = Assert.ThrowsExactly<Hl7ParseException>(
            () => Hl7Parser.Parse("MSH|^~\\&\rPID|1\rPV1|1\r", options));

        Assert.AreEqual(Hl7ParseFailure.TooManySegments, ex.Reason);
    }

    [TestMethod]
    public void Parse_Throws_WhenOptionsAreNotPositive()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Hl7Parser.Parse(Sample, new Hl7ParserOptions { MaxMessageLength = 0 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Hl7Parser.Parse(Sample, new Hl7ParserOptions { MaxSegments = 0 }));
    }

    [TestMethod]
    public void Parse_ExcludesMessageContentFromErrors_WhenParsingFails()
    {
        const string secret = "SECRET-NAME";

        Hl7ParseException ex = Assert.ThrowsExactly<Hl7ParseException>(
            () => Hl7Parser.Parse("MSH|^~\\&\rpid|" + secret + "\r"));

        Assert.IsFalse(ex.Message.Contains(secret, StringComparison.Ordinal));
        Assert.IsTrue(ex.Message.Contains("segment 1", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TryParse_ReturnsMessage_WhenInputIsValid()
    {
        bool ok = Hl7Parser.TryParse(Sample, out Hl7Message? message);

        Assert.IsTrue(ok);
        Assert.IsNotNull(message);
    }

    [TestMethod]
    public void TryParse_ReturnsFalse_WhenInputIsRejected()
    {
        bool ok = Hl7Parser.TryParse("PID|1", out Hl7Message? message);

        Assert.IsFalse(ok);
        Assert.IsNull(message);
    }
}
