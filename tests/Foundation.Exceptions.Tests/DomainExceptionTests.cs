using Foundation.Exceptions;

namespace Foundation.Exceptions.Tests;

[TestClass]
public class DomainExceptionTests
{
    [TestMethod]
    public void StructuredCtor_SetsFieldsAndRendersClientDetail()
    {
        ResourceNotFoundException ex = new("Person", "42", "not_found");

        Assert.AreEqual("Person", ex.EntityType);
        Assert.AreEqual("42", ex.EntityId);
        Assert.AreEqual("not_found", ex.ReasonCode);
        Assert.AreEqual("Person 42: not_found", ex.ClientDetail);
    }

    [TestMethod]
    public void StructuredCtor_WithoutId_OmitsId()
    {
        ConcurrencyConflictException ex = new("Case", null, "stale_etag");
        Assert.AreEqual("Case: stale_etag", ex.ClientDetail);
    }

    [TestMethod]
    public void LegacyMessageCtor_HasNoClientDetail()
    {
        ResourceExistsException ex = new("Jane Doe already exists");
        Assert.IsNull(ex.ClientDetail);
        Assert.AreEqual("Jane Doe already exists", ex.Message);
    }

    [TestMethod]
    public void FreeTextInStructuredField_IsDroppedNotRendered()
    {
        ResourceInactiveException ex = new("Person", "Jane Doe, DOB 1940-01-01", "inactive");
        Assert.AreEqual("Person: inactive", ex.ClientDetail);

        ResourceInactiveException badReason = new("Person", "1", "Jane Doe is inactive");
        Assert.IsNull(badReason.ClientDetail);
    }

    [TestMethod]
    public void OverlongField_IsDropped()
    {
        ResourceNotFoundException ex = new("Person", new string('1', 65), "not_found");
        Assert.AreEqual("Person: not_found", ex.ClientDetail);
    }

    [TestMethod]
    public void TrailingNewlineInAField_IsNotAPlainIdentifier()
    {
        ResourceNotFoundException ex = new("Person", "42\n", "not_found");
        Assert.AreEqual("Person: not_found", ex.ClientDetail);
    }

    [TestMethod]
    public void ClientReasonCode_IsExposedOnlyWhenItIsAPlainIdentifier()
    {
        Assert.AreEqual("not_found", new ResourceNotFoundException("Person", "1", "not_found").ClientReasonCode);
        Assert.IsNull(new ResourceNotFoundException("Person", "1", "Jane Doe is missing").ClientReasonCode);
        Assert.IsNull(new ResourceNotFoundException("free text only").ClientReasonCode);
    }

    [TestMethod]
    [DataRow("123-45-6789")]
    [DataRow("1940-01-01")]
    [DataRow("jane.doe")]
    [DataRow("MRN:0042")]
    public void IdsThatAreNotDatabaseKeys_AreNotRendered(string id)
    {
        ResourceNotFoundException ex = new("Person", id, "not_found");
        Assert.AreEqual("Person: not_found", ex.ClientDetail);
    }

    [TestMethod]
    public void NumericAndGuidIds_AreRendered()
    {
        Assert.AreEqual("Person 42: not_found", new ResourceNotFoundException("Person", "42", "not_found").ClientDetail);
        string guid = "0f8fad5b-d9cb-469f-a165-70867728950e";
        Assert.AreEqual($"Person {guid}: not_found", new ResourceNotFoundException("Person", guid, "not_found").ClientDetail);
    }

    [TestMethod]
    [DataRow("Not_Found")]
    [DataRow("not-found")]
    [DataRow("jane.doe")]
    [DataRow("1st_reason")]
    public void ReasonCodesThatAreNotLowerSnakeCase_AreDropped(string reason)
    {
        ResourceNotFoundException ex = new("Person", "42", reason);
        Assert.IsNull(ex.ClientDetail);
        Assert.IsNull(ex.ClientReasonCode);
    }
}
