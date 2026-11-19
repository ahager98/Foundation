namespace Foundation.Ccda.Tests;

[ExcludeFromCodeCoverage]
[TestClass]
public class CcdaTimestampTests
{
    [TestMethod]
    [DataRow("2024", CcdaTimePrecision.Year, 2024, 1, 1, 0, 0, 0)]
    [DataRow("202402", CcdaTimePrecision.Month, 2024, 2, 1, 0, 0, 0)]
    [DataRow("20240229", CcdaTimePrecision.Day, 2024, 2, 29, 0, 0, 0)]
    [DataRow("2024022913", CcdaTimePrecision.Hour, 2024, 2, 29, 13, 0, 0)]
    [DataRow("202402291305", CcdaTimePrecision.Minute, 2024, 2, 29, 13, 5, 0)]
    [DataRow("20240229130545", CcdaTimePrecision.Second, 2024, 2, 29, 13, 5, 45)]
    public void TryParse_ReadsPrecisionAndValueAsUtc_WhenNoOffsetIsGiven(
        string text, CcdaTimePrecision precision, int year, int month, int day, int hour, int minute, int second)
    {
        // Act
        bool isParsed = CcdaTimestamp.TryParse(text, out CcdaTimestamp? timestamp);

        // Assert
        Assert.IsTrue(isParsed);
        Assert.AreEqual(precision, timestamp!.Precision);
        Assert.AreEqual(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero), timestamp.Value);
        Assert.IsFalse(timestamp.HasOffset);
    }

    [TestMethod]
    public void TryParse_ReadsFractionAndOffset_WhenBothAreGiven()
    {
        // Act
        bool isParsed = CcdaTimestamp.TryParse(" 20240229130545.123-0530 ", out CcdaTimestamp? timestamp);

        // Assert
        Assert.IsTrue(isParsed);
        DateTimeOffset expected = new DateTimeOffset(2024, 2, 29, 13, 5, 45, new TimeSpan(-5, -30, 0)).AddMilliseconds(123);
        Assert.AreEqual(expected, timestamp!.Value);
        Assert.AreEqual(expected.Offset, timestamp.Value.Offset);
        Assert.AreEqual(CcdaTimePrecision.Second, timestamp.Precision);
        Assert.IsTrue(timestamp.HasOffset);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("abc")]
    [DataRow("20230229")]
    [DataRow("202413")]
    [DataRow("2024022913054")]
    [DataRow("20240101.5")]
    [DataRow("20240101120000+1500")]
    [DataRow("20240101120000+0060")]
    public void TryParse_ReturnsFalse_WhenTextIsNotAValidTimestamp(string text)
    {
        // Act
        bool isParsed = CcdaTimestamp.TryParse(text, out CcdaTimestamp? timestamp);

        // Assert
        Assert.IsFalse(isParsed);
        Assert.IsNull(timestamp);
    }

    [TestMethod]
    public void TryParse_ReturnsFalse_WhenTextIsNull()
    {
        // Act
        bool isParsed = CcdaTimestamp.TryParse(null, out CcdaTimestamp? timestamp);

        // Assert
        Assert.IsFalse(isParsed);
        Assert.IsNull(timestamp);
    }
}
