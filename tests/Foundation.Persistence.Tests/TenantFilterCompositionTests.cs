using Foundation.DataAccess;
using Foundation.Tenancy;
using Foundation.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundation.Persistence.Tests;

/// <summary>A tenant-scoped, soft-deletable entity (the common shape).</summary>
public class SoftDeleteTenantEntity : BaseEntity, ITenantScoped
{
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Mirrors a real service: installs its own (anonymous) soft-delete filter, then lets the base add
/// the named tenant filter. The two must AND-compose, not overwrite each other.
/// </summary>
public class ComposedFilterDbContext(
    DbContextOptions<ComposedFilterDbContext> options,
    ITenantService tenantService,
    TenancyOptions tenancy)
    : TenantDbContext(options, tenantService, tenancy)
{
    public DbSet<SoftDeleteTenantEntity> Entities => Set<SoftDeleteTenantEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The service's own soft-delete filter (anonymous), applied first...
        modelBuilder.Entity<SoftDeleteTenantEntity>().HasQueryFilter(e => e.IsActive);
        // ...then the base adds the NAMED tenant filter — which must compose with the above.
        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>A tenant-scoped entity that pre-declares its own named TenantId index.</summary>
public class PreIndexedTenantEntity : BaseEntity, ITenantScoped
{
    public int TenantId { get; set; }
}

public class PreIndexedDbContext(
    DbContextOptions<PreIndexedDbContext> options,
    ITenantService tenantService,
    TenancyOptions tenancy)
    : TenantDbContext(options, tenantService, tenancy)
{
    public const string IndexName = "IX_Pre_TenantId";

    public DbSet<PreIndexedTenantEntity> Entities => Set<PreIndexedTenantEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PreIndexedTenantEntity>().HasIndex(e => e.TenantId).HasDatabaseName(IndexName);
        base.OnModelCreating(modelBuilder);
    }
}

[TestClass]
public class TenantFilterCompositionTests
{
    private static ITenantService Tenant(string tenantId)
    {
        TenantService svc = new(new ConfigurationBuilder().AddInMemoryCollection([]).Build());
        svc.SetTenantId(tenantId);
        return svc;
    }

    private static ComposedFilterDbContext Composed(string db, string tenantId) =>
        new(new DbContextOptionsBuilder<ComposedFilterDbContext>().UseInMemoryDatabase(db).Options,
            Tenant(tenantId), new TenancyOptions { Strategy = TenancyStrategy.SharedDatabase });

    [TestMethod]
    public void SharedDatabase_LiftsSoftDeleteToNamed_AndAddsNamedTenantFilter()
    {
        using ComposedFilterDbContext ctx = Composed(nameof(SharedDatabase_LiftsSoftDeleteToNamed_AndAddsNamedTenantFilter), "5");

        IEntityType et = ctx.Model.FindEntityType(typeof(SoftDeleteTenantEntity))!;

        // The derived context's anonymous soft-delete filter was LIFTED to a named filter, and the base
        // added a separate named tenant filter — two coexisting NAMED filters, no anonymous one (EF
        // forbids mixing anonymous + named). Each predicate lives in its own filter, not AND-composed.
        List<IQueryFilter> filters = et.GetDeclaredQueryFilters().ToList();
        Assert.AreEqual(2, filters.Count);
        Assert.IsFalse(filters.Any(f => f.IsAnonymous), "All filters must be named.");

        IQueryFilter softDelete = filters.Single(f => f.Key == TenantDbContext.SoftDeleteQueryFilterName);
        IQueryFilter tenant = filters.Single(f => f.Key == TenantDbContext.TenantQueryFilterName);
        StringAssert.Contains(softDelete.Expression!.Body.ToString(), "IsActive");
        StringAssert.Contains(tenant.Expression!.Body.ToString(), "TenantId");
    }

    [TestMethod]
    public async Task SharedDatabase_IgnoreSoftDeleteFilterByName_KeepsTenantIsolation()
    {
        string db = nameof(SharedDatabase_IgnoreSoftDeleteFilterByName_KeepsTenantIsolation);

        await using (ComposedFilterDbContext seed5 = Composed(db, "5"))
        {
            seed5.Entities.Add(new SoftDeleteTenantEntity { Name = "active-5" });
            seed5.Entities.Add(new SoftDeleteTenantEntity { Name = "inactive-5", IsActive = false });
            await seed5.SaveChangesAsync();
        }
        await using (ComposedFilterDbContext seed7 = Composed(db, "7"))
        {
            seed7.Entities.Add(new SoftDeleteTenantEntity { Name = "inactive-7", IsActive = false });
            await seed7.SaveChangesAsync();
        }

        await using ComposedFilterDbContext read5 = Composed(db, "5");

        // Disabling ONLY the soft-delete filter reveals tenant 5's inactive row but STILL hides tenant
        // 7's rows — the named tenant filter remains in force. This is the win over the old all-or-nothing
        // IgnoreQueryFilters(), which would have leaked tenant 7's row.
        List<SoftDeleteTenantEntity> rows = await read5.Entities
            .IgnoreQueryFilters([TenantDbContext.SoftDeleteQueryFilterName])
            .ToListAsync();

        CollectionAssert.AreEquivalent(
            new[] { "active-5", "inactive-5" },
            rows.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public async Task SharedDatabase_SoftDeleteAndTenant_BothApply()
    {
        string db = nameof(SharedDatabase_SoftDeleteAndTenant_BothApply);

        await using (ComposedFilterDbContext seed5 = Composed(db, "5"))
        {
            seed5.Entities.Add(new SoftDeleteTenantEntity { Name = "active-5" });
            seed5.Entities.Add(new SoftDeleteTenantEntity { Name = "inactive-5", IsActive = false });
            await seed5.SaveChangesAsync();
        }
        await using (ComposedFilterDbContext seed7 = Composed(db, "7"))
        {
            seed7.Entities.Add(new SoftDeleteTenantEntity { Name = "active-7" });
            await seed7.SaveChangesAsync();
        }

        await using ComposedFilterDbContext read5 = Composed(db, "5");
        List<SoftDeleteTenantEntity> rows = await read5.Entities.ToListAsync();

        // Only the active row owned by tenant 5 survives: soft-delete hides inactive-5, the tenant
        // filter hides active-7.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("active-5", rows[0].Name);

        // Ignoring filters reveals every row — proving the rows exist and were merely filtered.
        Assert.AreEqual(3, await read5.Entities.IgnoreQueryFilters().CountAsync());
    }

    [TestMethod]
    public void SharedDatabase_DoesNotDuplicate_PreDeclaredTenantIdIndex()
    {
        using PreIndexedDbContext ctx = new(
            new DbContextOptionsBuilder<PreIndexedDbContext>()
                .UseInMemoryDatabase(nameof(SharedDatabase_DoesNotDuplicate_PreDeclaredTenantIdIndex)).Options,
            Tenant("5"), new TenancyOptions { Strategy = TenancyStrategy.SharedDatabase });

        IEntityType et = ctx.Model.FindEntityType(typeof(PreIndexedTenantEntity))!;
        List<IIndex> tenantIdIndexes = et.GetIndexes()
            .Where(ix => ix.Properties[0].Name == nameof(ITenantScoped.TenantId))
            .ToList();

        // The base deferred to the service's pre-declared index instead of adding a second one.
        Assert.AreEqual(1, tenantIdIndexes.Count);
        Assert.AreEqual(PreIndexedDbContext.IndexName, tenantIdIndexes[0].GetDatabaseName());
    }

    [TestMethod]
    public void SharedDatabase_AddsTenantIdIndex_WhenNoneDeclared()
    {
        using ComposedFilterDbContext ctx = Composed(nameof(SharedDatabase_AddsTenantIdIndex_WhenNoneDeclared), "5");

        IEntityType et = ctx.Model.FindEntityType(typeof(SoftDeleteTenantEntity))!;
        int tenantIdIndexes = et.GetIndexes().Count(ix => ix.Properties[0].Name == nameof(ITenantScoped.TenantId));

        Assert.AreEqual(1, tenantIdIndexes, "Base should add exactly one supporting TenantId index.");
    }

    #region GenericCrud key lookups vs. the composed filters

    private sealed class Crud(ComposedFilterDbContext ctx)
        : GenericCrud<SoftDeleteTenantEntity, ComposedFilterDbContext>(ctx, NullLogger.Instance);

    private static SoftDeleteTenantEntity Seed(ComposedFilterDbContext ctx, int id, int tenantId, bool isActive)
    {
        SoftDeleteTenantEntity entity = new()
        {
            Id = id, TenantId = tenantId, Name = $"e{id}", IsActive = isActive,
            CreatedById = 1, UpdatedById = 1
        };
        ctx.Entities.Add(entity);
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear(); // force a real query rather than a change-tracker hit
        return entity;
    }

    /// <summary>
    /// A by-id lookup must reach a SOFT-DELETED row. Before this, GenericCrud located rows with
    /// FindAsync, which does not bypass a global query filter once it queries the database — so every
    /// load-mutate-save reactivate/undelete flow threw ResourceNotFoundException on a relational
    /// provider, and services could not un-delete anything.
    /// </summary>
    [TestMethod]
    public async Task GetEntityByIdAsync_FindsSoftDeletedRow_WhenSoftDeleteFilterWouldHideIt()
    {
        using ComposedFilterDbContext ctx = Composed(nameof(GetEntityByIdAsync_FindsSoftDeletedRow_WhenSoftDeleteFilterWouldHideIt), "5");
        Seed(ctx, 1, tenantId: 5, isActive: false);

        // Hidden from an ordinary read...
        Assert.IsNull(await ctx.Entities.FirstOrDefaultAsync(e => e.Id == 1),
            "Sanity: the soft-delete filter must be in force.");

        // ...but reachable by key.
        SoftDeleteTenantEntity found = await new Crud(ctx).GetEntityByIdAsync(1);
        Assert.AreEqual(1, found.Id);
        Assert.IsFalse(found.IsActive);
    }

    /// <summary>
    /// The security half of the same change: suppressing soft-delete must NOT suppress tenant isolation.
    /// The argument-less IgnoreQueryFilters() would drop both, letting a by-id write reach another
    /// tenant's row under shared-database tenancy. The lookup names the filters it drops and never
    /// includes <see cref="QueryFilterNames.Tenant"/>.
    /// </summary>
    [TestMethod]
    public async Task GetEntityByIdAsync_StillHidesAnotherTenantsRow_WhenSoftDeleteIsSuppressed()
    {
        const string db = nameof(GetEntityByIdAsync_StillHidesAnotherTenantsRow_WhenSoftDeleteIsSuppressed);

        // Row belongs to tenant 99 and is soft-deleted, so the lookup must suppress soft-delete to see
        // it at all — the exact path that could leak if the tenant filter went with it.
        using (ComposedFilterDbContext seeding = Composed(db, "99"))
            Seed(seeding, 7, tenantId: 99, isActive: false);

        using ComposedFilterDbContext ctx = Composed(db, "5");

        await Assert.ThrowsExactlyAsync<ResourceNotFoundException>(
            () => new Crud(ctx).GetEntityByIdAsync(7),
            "Tenant 5 must not reach tenant 99's row, even with soft-delete suppressed.");
    }

    /// <summary>An active row of the caller's own tenant keeps resolving — no regression.</summary>
    [TestMethod]
    public async Task GetEntityByIdAsync_FindsOwnActiveRow()
    {
        using ComposedFilterDbContext ctx = Composed(nameof(GetEntityByIdAsync_FindsOwnActiveRow), "5");
        Seed(ctx, 3, tenantId: 5, isActive: true);

        SoftDeleteTenantEntity found = await new Crud(ctx).GetEntityByIdAsync(3);

        Assert.AreEqual(3, found.Id);
        Assert.IsTrue(found.IsActive);
    }

    /// <summary>
    /// The whole point: an update can now write to a soft-deleted row, which is what reactivate needs.
    /// </summary>
    [TestMethod]
    public async Task UpdateEntityAsync_ReactivatesSoftDeletedRow()
    {
        using ComposedFilterDbContext ctx = Composed(nameof(UpdateEntityAsync_ReactivatesSoftDeletedRow), "5");
        Seed(ctx, 11, tenantId: 5, isActive: false);

        SoftDeleteTenantEntity update = new()
        {
            Id = 11, TenantId = 5, Name = "e11", IsActive = true, CreatedById = 1, UpdatedById = 1
        };
        await new Crud(ctx).UpdateEntityAsync(11, update);

        ctx.ChangeTracker.Clear();
        Assert.IsNotNull(await ctx.Entities.FirstOrDefaultAsync(e => e.Id == 11),
            "After reactivation the row must be visible through the filtered set again.");
    }

    #endregion
}
