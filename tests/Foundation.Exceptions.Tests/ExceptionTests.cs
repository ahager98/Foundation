using Foundation.Exceptions;

namespace Foundation.Exceptions.Tests;

[TestClass]
public class ExceptionTests
{
    [TestMethod]
    public void ResourceNotFoundException_DefaultCtor_HasNoMessage()
    {
        ResourceNotFoundException ex = new();
        Assert.IsNotNull(ex);
    }

    [TestMethod]
    public void ResourceNotFoundException_MessageCtor_SetsMessage()
    {
        ResourceNotFoundException ex = new("not found");
        Assert.AreEqual("not found", ex.Message);
    }

    [TestMethod]
    public void ResourceNotFoundException_InnerCtor_SetsInner()
    {
        Exception inner = new("inner");
        ResourceNotFoundException ex = new("msg", inner);
        Assert.AreEqual(inner, ex.InnerException);
    }

    [TestMethod]
    public void ResourceExistsException_DefaultCtor_HasNoMessage()
    {
        ResourceExistsException ex = new();
        Assert.IsNotNull(ex);
    }

    [TestMethod]
    public void ResourceExistsException_MessageCtor_SetsMessage()
    {
        ResourceExistsException ex = new("exists");
        Assert.AreEqual("exists", ex.Message);
    }

    [TestMethod]
    public void ResourceExistsException_InnerCtor_SetsInner()
    {
        Exception inner = new("inner");
        ResourceExistsException ex = new("msg", inner);
        Assert.AreEqual(inner, ex.InnerException);
    }

    [TestMethod]
    public void ConcurrencyConflictException_DefaultCtor_HasNoMessage()
    {
        ConcurrencyConflictException ex = new();
        Assert.IsNotNull(ex);
    }

    [TestMethod]
    public void ConcurrencyConflictException_MessageCtor_SetsMessage()
    {
        ConcurrencyConflictException ex = new("conflict");
        Assert.AreEqual("conflict", ex.Message);
    }

    [TestMethod]
    public void ConcurrencyConflictException_InnerCtor_SetsInner()
    {
        Exception inner = new("inner");
        ConcurrencyConflictException ex = new("msg", inner);
        Assert.AreEqual(inner, ex.InnerException);
    }

    [TestMethod]
    public void ResourceNotFoundException_IsException() =>
        Assert.IsInstanceOfType<Exception>(new ResourceNotFoundException());

    [TestMethod]
    public void ResourceExistsException_IsException() =>
        Assert.IsInstanceOfType<Exception>(new ResourceExistsException());

    [TestMethod]
    public void ConcurrencyConflictException_IsException() =>
        Assert.IsInstanceOfType<Exception>(new ConcurrencyConflictException());
}
