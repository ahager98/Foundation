namespace Foundation.Hl7.Tests;

[TestClass]
public class Hl7DelimitersTests
{
    [TestMethod]
    public void Decode_ResolvesStandardEscapes_WhenTextContainsThem()
    {
        Hl7Delimiters d = Hl7Delimiters.Standard;

        Assert.AreEqual("a|b^c&d~e\\f", d.Decode("a\\F\\b\\S\\c\\T\\d\\R\\e\\E\\f"));
    }

    [TestMethod]
    public void Decode_ReturnsInputUnchanged_WhenThereIsNoEscape()
    {
        Assert.AreEqual("plain", Hl7Delimiters.Standard.Decode("plain"));
    }

    [TestMethod]
    public void Decode_PreservesUnknownSequences_WhenEscapeIsNotStandard()
    {
        Assert.AreEqual("a\\.br\\b\\X0D\\c", Hl7Delimiters.Standard.Decode("a\\.br\\b\\X0D\\c"));
    }

    [TestMethod]
    public void Decode_PreservesTail_WhenEscapeIsUnterminated()
    {
        Assert.AreEqual("a|b\\oops", Hl7Delimiters.Standard.Decode("a\\F\\b\\oops"));
    }

    [TestMethod]
    public void Decode_Throws_WhenInputIsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Hl7Delimiters.Standard.Decode(null!));
    }

    [TestMethod]
    public void GetValue_DecodesEscapes_WhenFieldContainsThem()
    {
        Hl7Message message = Hl7Parser.Parse("MSH|^~\\&\rNTE|1||a\\F\\b\\S\\c\r");

        Assert.AreEqual("a|b^c", message.GetValue("NTE", 3));
        Assert.AreEqual("a\\F\\b\\S\\c", message.GetSegment("NTE")!.Field(3).Raw);
    }

    [TestMethod]
    public void IsValid_ReturnsTrue_WhenDelimitersAreDistinctPunctuation()
    {
        Assert.IsTrue(Hl7Delimiters.Standard.IsValid);
        Assert.IsTrue(new Hl7Delimiters('|', '^', '~', '\\', '&', '#').IsValid);
    }

    [TestMethod]
    public void IsValid_ReturnsFalse_WhenDelimitersCollideOrAreUnsafe()
    {
        Assert.IsFalse(new Hl7Delimiters('|', '|', '~', '\\', '&').IsValid);
        Assert.IsFalse(new Hl7Delimiters('|', '^', '~', '\\', '&', '&').IsValid);
        Assert.IsFalse(new Hl7Delimiters('A', '^', '~', '\\', '&').IsValid);
        Assert.IsFalse(new Hl7Delimiters('|', '^', '~', '\\', '7').IsValid);
        Assert.IsFalse(new Hl7Delimiters('|', '^', '~', '\\', '\r').IsValid);
        Assert.IsFalse(new Hl7Delimiters('|', '^', '~', '\\', '\n').IsValid);
    }

    [TestMethod]
    public void Constructors_ThrowArgumentNull_WhenGivenNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new Hl7Component(null!, new[] { "a" }));
        Assert.ThrowsExactly<ArgumentNullException>(() => new Hl7Component("a", null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new Hl7Repetition(null!, Array.Empty<Hl7Component>()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new Hl7Repetition("a", null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new Hl7Field(null!, Array.Empty<Hl7Repetition>()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new Hl7Field("a", null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new Hl7Segment(null!, Array.Empty<Hl7Field>()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new Hl7Segment("PID", null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new Hl7Message(null!, Array.Empty<Hl7Segment>()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new Hl7Message(Hl7Delimiters.Standard, null!));
    }

    [TestMethod]
    public void Exception_CarriesReasonAndSegment_WhenConstructed()
    {
        Hl7ParseException general = new(Hl7ParseFailure.Empty);
        Hl7ParseException specific = new(Hl7ParseFailure.InvalidSegmentId, 3);

        Assert.AreEqual(-1, general.SegmentIndex);
        Assert.AreEqual(Hl7ParseFailure.InvalidSegmentId, specific.Reason);
        Assert.AreEqual(3, specific.SegmentIndex);
    }
}
