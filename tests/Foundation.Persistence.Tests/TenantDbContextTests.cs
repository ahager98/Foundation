using Foundation.DataAccess;
using Foundation.Persistence;
using Foundation.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Foundation.Persistence.Tests;

public class TenantEntity : BaseEntity, ITenantScoped
{
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TestTenantDbContext(
    DbContextOptions<TestTenantDbContext> options,
    ITenantService tenantService,
    TenancyOptions tenancy,
    TimeProvider? clock = null)
    : TenantDbContext(options, tenantService, tenancy)
{
    public DbSet<TenantEntity> Entities => Set<TenantEntity>();

    protected override TimeProvider Clock => clock ?? base.Clock;
}

/// <summary>Test clock whose UtcNow is fixed until <see cref="Set"/> advances it.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;
    public void Set(DateTimeOffset value) => _now = value;
    public override DateTimeOffset GetUtcNow() => _now;
}

[TestClass]
public class TenantDbContextTests
{
    private static ITenantService Tenant(string tenantId)
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        TenantService svc = new(config);
        svc.SetTenantId(tenantId);
        return svc;
    }

    private static TestTenantDbContext Context(string databaseName, TenancyStrategy strategy, string tenantId, TimeProvider? clock = null)
    {
        DbContextOptions<TestTenantDbContext> opts = new DbContextOptionsBuilder<TestTenantDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new TestTenantDbContext(opts, Tenant(tenantId), new TenancyOptions { Strategy = strategy }, clock);
    }

    [TestMethod]
    public async Task SharedDatabase_QueryFilter_HidesOtherTenantsRows()
    {
        string db = nameof(SharedDatabase_QueryFilter_HidesOtherTenantsRows);

        await using (TestTenantDbContext ctx5 = Context(db, TenancyStrategy.SharedDatabase, "5"))
        {
            ctx5.Entities.Add(new TenantEntity { Name = "tenant-5" });
            await ctx5.SaveChangesAsync();
        }
        await using (TestTenantDbContext ctx7 = Context(db, TenancyStrategy.SharedDatabase, "7"))
        {
            ctx7.Entities.Add(new TenantEntity { Name = "tenant-7" });
            await ctx7.SaveChangesAsync();
        }

        await using TestTenantDbContext read5 = Context(db, TenancyStrategy.SharedDatabase, "5");
        List<TenantEntity> rows = await read5.Entities.ToListAsync();

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("tenant-5", rows[0].Name);
    }

    [TestMethod]
    public async Task SharedDatabase_StampsTenantId_OnInsert()
    {
        string db = nameof(SharedDatabase_StampsTenantId_OnInsert);
        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "9");

        TenantEntity entity = new() { Name = "x" }; // TenantId left at default 0
        ctx.Entities.Add(entity);
        await ctx.SaveChangesAsync();

        Assert.AreEqual(9, entity.TenantId);
    }

    [TestMethod]
    public async Task SharedDatabase_RejectsCrossTenantWrite()
    {
        string db = nameof(SharedDatabase_RejectsCrossTenantWrite);
        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5");

        ctx.Entities.Add(new TenantEntity { Name = "evil", TenantId = 8 }); // different tenant

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
    }

    [TestMethod]
    public async Task SharedDatabase_GetByQuery_OtherTenantRow_NotVisible()
    {
        string db = nameof(SharedDatabase_GetByQuery_OtherTenantRow_NotVisible);
        int id;
        await using (TestTenantDbContext ctx5 = Context(db, TenancyStrategy.SharedDatabase, "5"))
        {
            TenantEntity e = new() { Name = "owned-by-5" };
            ctx5.Entities.Add(e);
            await ctx5.SaveChangesAsync();
            id = e.Id;
        }

        await using TestTenantDbContext ctx7 = Context(db, TenancyStrategy.SharedDatabase, "7");
        TenantEntity? found = await ctx7.Entities.FirstOrDefaultAsync(e => e.Id == id);

        Assert.IsNull(found);
    }

    [TestMethod]
    public async Task DatabasePerTenant_NoFilter_NoStamping()
    {
        string db = nameof(DatabasePerTenant_NoFilter_NoStamping);
        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.DatabasePerTenant, "5");

        TenantEntity unstamped = new() { Name = "a" };          // stays TenantId 0
        TenantEntity other = new() { Name = "b", TenantId = 99 };
        ctx.Entities.Add(unstamped);
        ctx.Entities.Add(other);
        await ctx.SaveChangesAsync();

        Assert.AreEqual(0, unstamped.TenantId, "DatabasePerTenant mode must not stamp TenantId");

        // No global query filter — rows are visible regardless of TenantId.
        await using TestTenantDbContext read = Context(db, TenancyStrategy.DatabasePerTenant, "5");
        Assert.AreEqual(2, await read.Entities.CountAsync());
    }

    [TestMethod]
    public async Task StampsTimestamps_OnInsert()
    {
        string db = nameof(StampsTimestamps_OnInsert);
        DateTimeOffset created = new(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);
        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5", new FixedClock(created));

        TenantEntity entity = new() { Name = "x" }; // CreatedDate/UpdatedDate left at default
        ctx.Entities.Add(entity);
        await ctx.SaveChangesAsync();

        Assert.AreEqual(created.UtcDateTime, entity.CreatedDate);
        Assert.AreEqual(created.UtcDateTime, entity.UpdatedDate);
    }

    [TestMethod]
    public async Task StampsTimestamps_OnUpdate_BumpsUpdatedDate_PreservesCreatedDate()
    {
        string db = nameof(StampsTimestamps_OnUpdate_BumpsUpdatedDate_PreservesCreatedDate);
        DateTimeOffset created = new(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset updated = created.AddHours(3);
        FixedClock clock = new(created);

        int id;
        await using (TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5", clock))
        {
            TenantEntity e = new() { Name = "before" };
            ctx.Entities.Add(e);
            await ctx.SaveChangesAsync();
            id = e.Id;
        }

        clock.Set(updated);
        await using TestTenantDbContext editCtx = Context(db, TenancyStrategy.SharedDatabase, "5", clock);
        TenantEntity loaded = await editCtx.Entities.FirstAsync(e => e.Id == id);
        loaded.Name = "after";
        loaded.CreatedDate = updated.UtcDateTime; // caller tries to overwrite creation time — must be ignored
        await editCtx.SaveChangesAsync();

        Assert.AreEqual(created.UtcDateTime, loaded.CreatedDate, "CreatedDate must survive an update");
        Assert.AreEqual(updated.UtcDateTime, loaded.UpdatedDate, "UpdatedDate must advance on update");
    }

    [TestMethod]
    public async Task StampsTimestamps_InDatabasePerTenantMode()
    {
        string db = nameof(StampsTimestamps_InDatabasePerTenantMode);
        DateTimeOffset created = new(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);
        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.DatabasePerTenant, "5", new FixedClock(created));

        TenantEntity entity = new() { Name = "x" };
        ctx.Entities.Add(entity);
        await ctx.SaveChangesAsync();

        Assert.AreEqual(created.UtcDateTime, entity.CreatedDate, "timestamps are stamped regardless of tenancy strategy");
        Assert.AreEqual(created.UtcDateTime, entity.UpdatedDate);
    }

    [TestMethod]
    public void AddFoundationPersistence_RegistersDbContext()
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenancy:Strategy"] = "SharedDatabase",
            ["ConnectionStrings:Tenant"] = "Server=sql;Database=Shared;User Id=sa;Password=x;"
        }).Build();

        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(config); // hosts register this; tests must too
        services.AddFoundationPersistence<TestTenantDbContext>(config);

        using ServiceProvider sp = services.BuildServiceProvider();
        using IServiceScope scope = sp.CreateScope();

        Assert.IsNotNull(scope.ServiceProvider.GetService<TestTenantDbContext>());
        Assert.AreEqual(TenancyStrategy.SharedDatabase, sp.GetRequiredService<TenancyOptions>().Strategy);
    }
}
