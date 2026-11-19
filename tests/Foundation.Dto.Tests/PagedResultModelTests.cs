using Foundation.Dto;

namespace Foundation.Dto.Tests;

[TestClass]
public class PagedResultModelTests
{
    [TestMethod]
    public void TotalPages_IsZeroWhenPageSizeIsZero()
    {
        PagedResultModel<int> model = new() { PageSize = 0, TotalCount = 10 };
        Assert.AreEqual(0, model.TotalPages);
    }

    [TestMethod]
    public void TotalPages_RoundsUp()
    {
        PagedResultModel<int> model = new() { PageSize = 3, TotalCount = 10 };
        Assert.AreEqual(4, model.TotalPages);
    }

    [TestMethod]
    public void HasNextPage_TrueWhenNotLastPage()
    {
        PagedResultModel<int> model = new() { PageNumber = 1, PageSize = 5, TotalCount = 10 };
        Assert.IsTrue(model.HasNextPage);
    }

    [TestMethod]
    public void HasNextPage_FalseOnLastPage()
    {
        PagedResultModel<int> model = new() { PageNumber = 2, PageSize = 5, TotalCount = 10 };
        Assert.IsFalse(model.HasNextPage);
    }

    [TestMethod]
    public void HasPreviousPage_TrueOnSecondPage()
    {
        PagedResultModel<int> model = new() { PageNumber = 2, PageSize = 5, TotalCount = 10 };
        Assert.IsTrue(model.HasPreviousPage);
    }

    [TestMethod]
    public void HasPreviousPage_FalseOnFirstPage()
    {
        PagedResultModel<int> model = new() { PageNumber = 1, PageSize = 5, TotalCount = 10 };
        Assert.IsFalse(model.HasPreviousPage);
    }

    [TestMethod]
    public void LookupModel_SetsProperties()
    {
        LookupModel model = new() { Id = 42, Description = "Test" };
        Assert.AreEqual(42, model.Id);
        Assert.AreEqual("Test", model.Description);
    }
}
