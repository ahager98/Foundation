namespace Foundation.Ccda.Tests;

[ExcludeFromCodeCoverage]
[TestClass]
public class CcdaIntervalTests
{
    private static readonly CcdaTimestamp Early = new(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), CcdaTimePrecision.Day, false);
    private static readonly CcdaTimestamp Late = new(new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero), CcdaTimePrecision.Day, false);

    [TestMethod]
    public void Start_ReturnsLow_WhenLowIsPresent()
    {
        // Arrange
        CcdaInterval interval = new() { Low = Early, Value = Late };

        // Act & Assert
        Assert.AreSame(Early, interval.Start);
    }

    [TestMethod]
    public void Start_ReturnsValue_WhenLowIsAbsent()
    {
        // Arrange
        CcdaInterval interval = new() { High = Early, Value = Late };

        // Act & Assert
        Assert.AreSame(Late, interval.Start);
    }

    [TestMethod]
    public void Start_ReturnsNull_WhenOnlyHighIsPresent()
    {
        // Arrange
        CcdaInterval interval = new() { High = Late };

        // Act & Assert
        Assert.IsNull(interval.Start);
    }
}
