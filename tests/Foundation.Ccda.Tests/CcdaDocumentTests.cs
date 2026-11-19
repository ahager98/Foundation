namespace Foundation.Ccda.Tests;

[ExcludeFromCodeCoverage]
[TestClass]
public class CcdaDocumentTests
{
    [TestMethod]
    public void GetSection_ReturnsFirstSectionOfKind_WhenSeveralMatch()
    {
        // Arrange
        CcdaSection first = new() { Kind = CcdaSectionKind.Results, Title = "first" };
        CcdaSection second = new() { Kind = CcdaSectionKind.Results, Title = "second" };
        CcdaDocument document = new() { Sections = [new CcdaSection { Kind = CcdaSectionKind.Problems }, first, second] };

        // Act
        CcdaSection? section = document.GetSection(CcdaSectionKind.Results);

        // Assert
        Assert.AreSame(first, section);
    }

    [TestMethod]
    public void GetSection_ReturnsNull_WhenNoSectionMatches()
    {
        // Arrange
        CcdaDocument document = new();

        // Act & Assert
        Assert.IsNull(document.GetSection(CcdaSectionKind.Allergies));
    }
}
