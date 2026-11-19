using Foundation.DataAccess;
using Foundation.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundation.DataAccess.Tests;

// Minimal EF entity for testing
public class TestEntity : BaseEntity
{
    public string Name { get; set; } = string.Empty;
}

public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<TestEntity> TestEntities => Set<TestEntity>();
}

public class TestGenericCrud(TestDbContext context)
    : GenericCrud<TestEntity, TestDbContext>(context, NullLogger.Instance);

[TestClass]
public class GenericCrudTests
{
    private static TestDbContext CreateContext()
    {
        DbContextOptions<TestDbContext> opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(opts);
    }

    [TestMethod]
    public async Task CreateEntityAsync_AddsEntity()
    {
        using TestDbContext ctx = CreateContext();
        TestGenericCrud crud = new(ctx);
        TestEntity entity = new() { Name = "Alice" };
        TestEntity result = await crud.CreateEntityAsync(entity);
        Assert.IsTrue(result.Id > 0);
    }

    [TestMethod]
    public async Task GetEntityByIdAsync_ReturnsEntity()
    {
        using TestDbContext ctx = CreateContext();
        TestGenericCrud crud = new(ctx);
        TestEntity entity = new() { Name = "Bob" };
        await crud.CreateEntityAsync(entity);

        TestEntity found = await crud.GetEntityByIdAsync(entity.Id);
        Assert.AreEqual("Bob", found.Name);
    }

    [TestMethod]
    public async Task GetEntityByIdAsync_Throws_WhenNotFound()
    {
        using TestDbContext ctx = CreateContext();
        TestGenericCrud crud = new(ctx);
        await Assert.ThrowsExactlyAsync<ResourceNotFoundException>(
            () => crud.GetEntityByIdAsync(999));
    }

    [TestMethod]
    public async Task GetEntityByIdDetachedAsync_Throws_WhenNotFound()
    {
        using TestDbContext ctx = CreateContext();
        TestGenericCrud crud = new(ctx);
        await Assert.ThrowsExactlyAsync<ResourceNotFoundException>(
            () => crud.GetEntityByIdDetachedAsync(999));
    }

    [TestMethod]
    public async Task UpdateEntityAsync_Throws_WhenNotFound()
    {
        using TestDbContext ctx = CreateContext();
        TestGenericCrud crud = new(ctx);
        await Assert.ThrowsExactlyAsync<ResourceNotFoundException>(
            () => crud.UpdateEntityAsync(999, new TestEntity()));
    }

    [TestMethod]
    public async Task DeleteEntityAsync_RemovesEntity()
    {
        using TestDbContext ctx = CreateContext();
        TestGenericCrud crud = new(ctx);
        TestEntity entity = new() { Name = "Carol" };
        await crud.CreateEntityAsync(entity);

        await crud.DeleteEntityAsync(entity.Id, null);
        await Assert.ThrowsExactlyAsync<ResourceNotFoundException>(
            () => crud.GetEntityByIdAsync(entity.Id));
    }

    [TestMethod]
    public async Task DeleteEntityAsync_Throws_WhenNotFound()
    {
        using TestDbContext ctx = CreateContext();
        TestGenericCrud crud = new(ctx);
        await Assert.ThrowsExactlyAsync<ResourceNotFoundException>(
            () => crud.DeleteEntityAsync(999, null));
    }
}
