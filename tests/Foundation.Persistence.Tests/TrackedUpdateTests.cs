using System.Linq.Expressions;
using Foundation.DataAccess;
using Foundation.Persistence;
using Foundation.Tenancy;
using Foundation.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Foundation.Persistence.Tests;

public class GlobalEntity : BaseEntity
{
    public string Label { get; set; } = string.Empty;
}

public sealed class GlobalDbContext(DbContextOptions<GlobalDbContext> options, ITenantService tenantService, TenancyOptions tenancy)
    : TenantDbContext(options, tenantService, tenancy)
{
    public DbSet<GlobalEntity> Items => Set<GlobalEntity>();
}

public class ParentEntity : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public List<ChildEntity> Children { get; set; } = [];
}

public class ChildEntity : BaseEntity
{
    public string Label { get; set; } = string.Empty;
    public int ParentEntityId { get; set; }
}

public sealed class ParentDbContext(DbContextOptions<ParentDbContext> options, ITenantService tenantService, TenancyOptions tenancy)
    : TenantDbContext(options, tenantService, tenancy)
{
    public DbSet<ParentEntity> Parents => Set<ParentEntity>();
    public DbSet<ChildEntity> Children => Set<ChildEntity>();
}

// Test convenience: the real API requires an explicit If-Match decision and an acting user on every call.
public static class UpdateTestExtensions
{
    public static Task<T> Update<T>(
        this TenantDbContext db,
        int id,
        Action<T> apply,
        byte[]? expectedRowVersion = null,
        int? updatedById = null,
        Func<IQueryable<T>, IQueryable<T>>? include = null,
        Expression<Func<T, bool>>? scope = null,
        Func<T, CancellationToken, Task>? audit = null)
        where T : BaseEntity =>
        db.UpdateTrackedAsync(id, apply, expectedRowVersion, updatedById ?? 1, include, scope, audit);
}

public class RvEntity : BaseEntity
{
    public string Name { get; set; } = string.Empty;
}

// Two context types, because EF caches the model per context type and these differ only in the token mapping.
public sealed class RvTokenDb(DbContextOptions<RvTokenDb> options, ITenantService tenantService, TenancyOptions tenancy)
    : TenantDbContext(options, tenantService, tenancy)
{
    public DbSet<RvEntity> Items => Set<RvEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RvEntity>().Property(e => e.RowVersion).IsConcurrencyToken();
        base.OnModelCreating(modelBuilder);
    }
}

public sealed class RvPlainDb(DbContextOptions<RvPlainDb> options, ITenantService tenantService, TenancyOptions tenancy)
    : TenantDbContext(options, tenantService, tenancy)
{
    public DbSet<RvEntity> Items => Set<RvEntity>();
}

[TestClass]
public class TrackedUpdateTests
{
    private static TestTenantDbContext Context(string db, TenancyStrategy strategy, string tenantId, TimeProvider? clock = null)
    {
        TenantService svc = new(new ConfigurationBuilder().AddInMemoryCollection([]).Build());
        svc.SetTenantId(tenantId);
        DbContextOptions<TestTenantDbContext> opts = new DbContextOptionsBuilder<TestTenantDbContext>()
            .UseInMemoryDatabase(db).Options;
        return new TestTenantDbContext(opts, svc, new TenancyOptions { Strategy = strategy }, clock);
    }

    private static async Task<int> SeedAsync(string db, string tenantId, byte[]? rowVersion = null)
    {
        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, tenantId);
        TenantEntity e = new() { Name = "before", CreatedById = 3, RowVersion = rowVersion };
        ctx.Entities.Add(e);
        await ctx.SaveChangesAsync();
        return e.Id;
    }

    [TestMethod]
    public async Task Update_AppliesDelegate_StampsAuditAndKeepsCreationFields()
    {
        string db = nameof(Update_AppliesDelegate_StampsAuditAndKeepsCreationFields);
        int id = await SeedAsync(db, "5");
        FixedClock clock = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));

        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5", clock);
        TenantEntity updated = await ctx.Update<TenantEntity>(id, e => e.Name = "after", updatedById: 9);

        Assert.AreEqual(id, updated.Id);
        Assert.AreEqual("after", updated.Name);
        Assert.AreEqual(9, updated.UpdatedById);
        Assert.AreEqual(3, updated.CreatedById);
        Assert.AreEqual(clock.GetUtcNow().UtcDateTime, updated.UpdatedDate);
        await using TestTenantDbContext read = Context(db, TenancyStrategy.SharedDatabase, "5");
        Assert.AreEqual(1, await read.Entities.CountAsync());
    }

    [TestMethod]
    public async Task Update_OtherTenantsRow_IsNotFoundAndTouchesNothing()
    {
        string db = nameof(Update_OtherTenantsRow_IsNotFoundAndTouchesNothing);
        int id = await SeedAsync(db, "5");

        await using (TestTenantDbContext ctx7 = Context(db, TenancyStrategy.SharedDatabase, "7"))
        {
            ResourceNotFoundException ex = await Assert.ThrowsExactlyAsync<ResourceNotFoundException>(
                () => ctx7.Update<TenantEntity>(id, e => e.Name = "hijacked"));
            Assert.AreEqual("not_found", ex.ReasonCode);
        }

        await using TestTenantDbContext ctx5 = Context(db, TenancyStrategy.SharedDatabase, "5");
        Assert.AreEqual("before", (await ctx5.Entities.SingleAsync()).Name);
    }

    [TestMethod]
    public async Task Update_MissingId_IsNotFound()
    {
        await using TestTenantDbContext ctx = Context(nameof(Update_MissingId_IsNotFound), TenancyStrategy.SharedDatabase, "5");
        await Assert.ThrowsExactlyAsync<ResourceNotFoundException>(
            () => ctx.Update<TenantEntity>(404, e => e.Name = "x"));
    }

    [TestMethod]
    public async Task Update_StaleRowVersion_IsConflictAndWritesNothing()
    {
        string db = nameof(Update_StaleRowVersion_IsConflictAndWritesNothing);
        int id = await SeedAsync(db, "5", rowVersion: [1, 2, 3]);

        await using (TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5"))
        {
            ConcurrencyConflictException ex = await Assert.ThrowsExactlyAsync<ConcurrencyConflictException>(
                () => ctx.Update<TenantEntity>(id, e => e.Name = "late", expectedRowVersion: [9, 9, 9]));
            Assert.AreEqual("stale_etag", ex.ReasonCode);
        }

        await using TestTenantDbContext read = Context(db, TenancyStrategy.SharedDatabase, "5");
        Assert.AreEqual("before", (await read.Entities.SingleAsync()).Name);
    }

    [TestMethod]
    public async Task Update_MatchingRowVersion_Succeeds()
    {
        string db = nameof(Update_MatchingRowVersion_Succeeds);
        int id = await SeedAsync(db, "5", rowVersion: [1, 2, 3]);

        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5");
        TenantEntity updated = await ctx.Update<TenantEntity>(id, e => e.Name = "ok", expectedRowVersion: [1, 2, 3]);

        Assert.AreEqual("ok", updated.Name);
    }

    [TestMethod]
    public async Task Update_NullStoredRowVersionWithExpected_IsConflict()
    {
        string db = nameof(Update_NullStoredRowVersionWithExpected_IsConflict);
        int id = await SeedAsync(db, "5");

        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5");
        await Assert.ThrowsExactlyAsync<ConcurrencyConflictException>(
            () => ctx.Update<TenantEntity>(id, e => e.Name = "x", expectedRowVersion: [1]));
    }

    [TestMethod]
    public async Task Update_DelegateChangingProtectedField_Throws()
    {
        string db = nameof(Update_DelegateChangingProtectedField_Throws);
        int id = await SeedAsync(db, "5");

        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ctx.Update<TenantEntity>(id, e => e.Id = 0));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ctx.Update<TenantEntity>(id, e => e.TenantId = 99));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ctx.Update<TenantEntity>(id, e => e.CreatedById = 1));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ctx.Update<TenantEntity>(id, e => e.CreatedDate = DateTime.UnixEpoch));
    }

    [TestMethod]
    public async Task Update_WithUnresolvedTenant_IsNotFoundEvenForAnUnstampedRow()
    {
        string db = nameof(Update_WithUnresolvedTenant_IsNotFoundEvenForAnUnstampedRow);
        int id;
        await using (TestTenantDbContext seed = Context(db, TenancyStrategy.DatabasePerTenant, "5"))
        {
            TenantEntity e = new() { Name = "before" }; // TenantId left at 0, as an unresolved tenant would leave it
            seed.Entities.Add(e);
            await seed.SaveChangesAsync();
            id = e.Id;
        }

        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "0");
        await Assert.ThrowsExactlyAsync<ResourceNotFoundException>(
            () => ctx.Update<TenantEntity>(id, e => e.Name = "x"));
    }

    [TestMethod]
    public async Task Update_DelegateThrowsAfterChangingProtectedField_LeavesNothingTrackedToSave()
    {
        string db = nameof(Update_DelegateThrowsAfterChangingProtectedField_LeavesNothingTrackedToSave);
        int id = await SeedAsync(db, "5");

        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5");
        await Assert.ThrowsExactlyAsync<FormatException>(() => ctx.Update<TenantEntity>(id, e =>
        {
            e.TenantId = 99;
            throw new FormatException("validation failed");
        }));

        await ctx.Update<TenantEntity>(id, e => e.Name = "second");

        await using TestTenantDbContext read = Context(db, TenancyStrategy.SharedDatabase, "5");
        TenantEntity stored = await read.Entities.SingleAsync();
        Assert.AreEqual(5, stored.TenantId);
        Assert.AreEqual("second", stored.Name);
    }

    [TestMethod]
    public async Task Update_OnANoTrackingContext_StillPersists()
    {
        string db = nameof(Update_OnANoTrackingContext_StillPersists);
        int id = await SeedAsync(db, "5");
        TenantService svc = new(new ConfigurationBuilder().AddInMemoryCollection([]).Build());
        svc.SetTenantId("5");
        DbContextOptions<TestTenantDbContext> opts = new DbContextOptionsBuilder<TestTenantDbContext>()
            .UseInMemoryDatabase(db).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options;

        await using (TestTenantDbContext ctx = new(opts, svc, new TenancyOptions { Strategy = TenancyStrategy.SharedDatabase }))
            await ctx.Update<TenantEntity>(id, e => e.Name = "persisted");

        await using TestTenantDbContext read = Context(db, TenancyStrategy.SharedDatabase, "5");
        Assert.AreEqual("persisted", (await read.Entities.SingleAsync()).Name);
    }

    [TestMethod]
    public async Task Update_WhereDelegateEditsNothingOnTheRoot_StillStampsTheAggregate()
    {
        string db = nameof(Update_WhereDelegateEditsNothingOnTheRoot_StillStampsTheAggregate);
        int id = await SeedAsync(db, "5");
        FixedClock clock = new(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero));

        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5", clock);
        TenantEntity updated = await ctx.Update<TenantEntity>(id, _ => { /* children-only edit */ });

        Assert.AreEqual(clock.GetUtcNow().UtcDateTime, updated.UpdatedDate);
    }

    [TestMethod]
    public async Task Update_DelegateEditsAnotherTrackedEntityThenThrows_ThatEditIsRolledBackToo()
    {
        string db = nameof(Update_DelegateEditsAnotherTrackedEntityThenThrows_ThatEditIsRolledBackToo);
        int id = await SeedAsync(db, "5");
        int otherId = await SeedAsync(db, "5");

        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5");
        TenantEntity other = await ctx.Entities.SingleAsync(e => e.Id == otherId); // a loaded child of the aggregate
        await Assert.ThrowsExactlyAsync<FormatException>(() => ctx.Update<TenantEntity>(id, e =>
        {
            e.Name = "root-edit";
            other.Name = "child-edit";
            ctx.Entities.Add(new TenantEntity { Name = "new-child" });
            throw new FormatException("rejected");
        }));

        await ctx.SaveChangesAsync(); // a later save on the same context must persist nothing of the rejected update

        await using TestTenantDbContext read = Context(db, TenancyStrategy.SharedDatabase, "5");
        List<TenantEntity> rows = await read.Entities.OrderBy(e => e.Id).ToListAsync();
        CollectionAssert.AreEqual(new[] { "before", "before" }, rows.Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public async Task Update_OfAGlobalEntity_WorksWithoutAResolvedTenant()
    {
        string db = nameof(Update_OfAGlobalEntity_WorksWithoutAResolvedTenant);
        TenantService svc = new(new ConfigurationBuilder().AddInMemoryCollection([]).Build());
        svc.SetTenantId("0");
        DbContextOptions<GlobalDbContext> opts = new DbContextOptionsBuilder<GlobalDbContext>().UseInMemoryDatabase(db).Options;
        int id;
        await using (GlobalDbContext seed = new(opts, svc, new TenancyOptions { Strategy = TenancyStrategy.SharedDatabase }))
        {
            GlobalEntity g = new() { Label = "before" };
            seed.Items.Add(g);
            await seed.SaveChangesAsync();
            id = g.Id;
        }

        await using GlobalDbContext ctx = new(opts, svc, new TenancyOptions { Strategy = TenancyStrategy.SharedDatabase });
        GlobalEntity updated = await ctx.Update<GlobalEntity>(id, g => g.Label = "after");

        Assert.AreEqual("after", updated.Label);
    }

    [TestMethod]
    public async Task Update_WithIncludeAndSync_KeepsExistingChildKeys_AndAddsOnlyNewOnes()
    {
        string db = nameof(Update_WithIncludeAndSync_KeepsExistingChildKeys_AndAddsOnlyNewOnes);
        TenantService svc = new(new ConfigurationBuilder().AddInMemoryCollection([]).Build());
        svc.SetTenantId("5");
        DbContextOptions<ParentDbContext> opts = new DbContextOptionsBuilder<ParentDbContext>().UseInMemoryDatabase(db).Options;
        TenancyOptions tenancy = new() { Strategy = TenancyStrategy.DatabasePerTenant };
        int parentId;
        List<int> originalChildIds;
        await using (ParentDbContext seed = new(opts, svc, tenancy))
        {
            ParentEntity p = new() { Name = "p", Children = [new ChildEntity { Label = "a" }, new ChildEntity { Label = "b" }] };
            seed.Parents.Add(p);
            await seed.SaveChangesAsync();
            parentId = p.Id;
            originalChildIds = p.Children.Select(c => c.Id).Order().ToList();
        }

        await using (ParentDbContext ctx = new(opts, svc, tenancy))
        {
            await ctx.Update<ParentEntity>(parentId, p => TrackedChildren.Sync(
                    p.Children,
                    [(Id: originalChildIds[0], Label: "a-renamed"), (Id: 0, Label: "c")],
                    i => i.Id,
                    i => new ChildEntity { Label = i.Label },
                    (child, i) => child.Label = i.Label),
                include: q => q.Include(p => p.Children));
        }

        await using ParentDbContext read = new(opts, svc, tenancy);
        List<ChildEntity> children = await read.Children.Where(c => c.ParentEntityId == parentId).OrderBy(c => c.Id).ToListAsync();
        Assert.AreEqual(2, children.Count);
        Assert.AreEqual(originalChildIds[0], children[0].Id); // the kept child kept its key
        Assert.AreEqual("a-renamed", children[0].Label);
        Assert.AreEqual("c", children[1].Label);
        Assert.IsFalse(children.Any(c => c.Id == originalChildIds[1])); // the missing one was removed
    }

    [TestMethod]
    public async Task Update_DatabasePerTenant_DoesNotRequireTenantColumn()
    {
        string db = nameof(Update_DatabasePerTenant_DoesNotRequireTenantColumn);
        int id;
        await using (TestTenantDbContext seed = Context(db, TenancyStrategy.DatabasePerTenant, "5"))
        {
            TenantEntity e = new() { Name = "before" };
            seed.Entities.Add(e);
            await seed.SaveChangesAsync();
            id = e.Id;
        }

        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.DatabasePerTenant, "5");
        TenantEntity updated = await ctx.Update<TenantEntity>(id, e => e.Name = "after");

        Assert.AreEqual("after", updated.Name);
    }

    [TestMethod]
    public async Task Update_NullArguments_Throw()
    {
        await using TestTenantDbContext ctx = Context(nameof(Update_NullArguments_Throw), TenancyStrategy.SharedDatabase, "5");
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ctx.Update<TenantEntity>(1, null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => TrackedUpdateExtensions.UpdateTrackedAsync<TenantEntity>(null!, 1, _ => { }, null, 1));
    }

    [TestMethod]
    public async Task Update_ScopePredicate_ExcludesRowsOutsideTheCallersScope()
    {
        string db = nameof(Update_ScopePredicate_ExcludesRowsOutsideTheCallersScope);
        int id = await SeedAsync(db, "5");

        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5");
        await Assert.ThrowsExactlyAsync<ResourceNotFoundException>(
            () => ctx.Update<TenantEntity>(id, e => e.Name = "x", scope: e => e.Name == "another-community"));
        TenantEntity updated = await ctx.Update<TenantEntity>(id, e => e.Name = "y", scope: e => e.Name == "before");
        Assert.AreEqual("y", updated.Name);
    }

    [TestMethod]
    public async Task Update_AuditCallback_RunsBeforeTheSave_AndNotOnFailure()
    {
        string db = nameof(Update_AuditCallback_RunsBeforeTheSave_AndNotOnFailure);
        int id = await SeedAsync(db, "5");
        List<string> audited = [];

        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5");
        await ctx.Update<TenantEntity>(id, e => e.Name = "ok", updatedById: 7, audit: (e, _) => { audited.Add($"{e.Id}:{e.UpdatedById}:{e.Name}"); return Task.CompletedTask; });
        await Assert.ThrowsExactlyAsync<FormatException>(() => ctx.Update<TenantEntity>(id, _ => throw new FormatException("no"),
            audit: (e, _) => { audited.Add("should-not-run"); return Task.CompletedTask; }));

        CollectionAssert.AreEqual(new[] { $"{id}:7:ok" }, audited);
    }

    [TestMethod]
    public async Task Update_WhenTheAuditCallbackFails_NothingIsSavedAndNothingStaysTracked()
    {
        string db = nameof(Update_WhenTheAuditCallbackFails_NothingIsSavedAndNothingStaysTracked);
        int id = await SeedAsync(db, "5");

        await using (TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5"))
        {
            await Assert.ThrowsExactlyAsync<FormatException>(() => ctx.Update<TenantEntity>(id, e => e.Name = "late",
                audit: (_, _) => throw new FormatException("audit store down")));

            Assert.AreEqual(0, ctx.ChangeTracker.Entries().Count());
            await ctx.SaveChangesAsync(); // a later save on the same context must not persist the rejected edit
        }

        await using TestTenantDbContext read = Context(db, TenancyStrategy.SharedDatabase, "5");
        Assert.AreEqual("before", (await read.Entities.SingleAsync()).Name);
    }

    [TestMethod]
    public async Task Update_AlwaysRecordsTheActingUser()
    {
        string db = nameof(Update_AlwaysRecordsTheActingUser);
        int id = await SeedAsync(db, "5");

        await using TestTenantDbContext ctx = Context(db, TenancyStrategy.SharedDatabase, "5");
        TenantEntity updated = await ctx.UpdateTrackedAsync<TenantEntity>(id, e => e.Name = "x", null, updatedById: 42);

        Assert.AreEqual(42, updated.UpdatedById);
    }

    [TestMethod]
    public async Task Update_ChildProtectedFieldChange_IsRejectedAndRolledBack()
    {
        string db = nameof(Update_ChildProtectedFieldChange_IsRejectedAndRolledBack);
        TenantService svc = new(new ConfigurationBuilder().AddInMemoryCollection([]).Build());
        svc.SetTenantId("5");
        DbContextOptions<ParentDbContext> opts = new DbContextOptionsBuilder<ParentDbContext>().UseInMemoryDatabase(db).Options;
        TenancyOptions tenancy = new() { Strategy = TenancyStrategy.DatabasePerTenant };
        int parentId;
        await using (ParentDbContext seed = new(opts, svc, tenancy))
        {
            ParentEntity p = new() { Name = "p", Children = [new ChildEntity { Label = "a", CreatedById = 3 }] };
            seed.Parents.Add(p);
            await seed.SaveChangesAsync();
            parentId = p.Id;
        }

        await using (ParentDbContext ctx = new(opts, svc, tenancy))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ctx.Update<ParentEntity>(parentId, p =>
            {
                p.Children[0].Label = "renamed";
                p.Children[0].CreatedById = 99; // a child's protected field
            }, include: q => q.Include(p => p.Children)));

            await ctx.SaveChangesAsync(); // nothing of the rejected update may be saved
        }

        await using ParentDbContext read = new(opts, svc, tenancy);
        ChildEntity stored = await read.Children.SingleAsync();
        Assert.AreEqual("a", stored.Label);
        Assert.AreEqual(3, stored.CreatedById);
    }

    // A real relational provider, because the in-memory provider maps no concurrency token and never runs the write-time check.
    private static string NewSqlitePath() => Path.Combine(Path.GetTempPath(), $"rv-{Guid.NewGuid():N}.db");

    private static TenantService Tenant5()
    {
        TenantService svc = new(new ConfigurationBuilder().AddInMemoryCollection([]).Build());
        svc.SetTenantId("5");
        return svc;
    }

    [TestMethod]
    public async Task Update_WhenAnotherWriterWinsTheRaceBeforeSave_RaisesAConflict()
    {
        string path = NewSqlitePath();
        try
        {
            DbContextOptions<RvTokenDb> opts = new DbContextOptionsBuilder<RvTokenDb>().UseSqlite($"Data Source={path}").Options;
            TenancyOptions tenancy = new() { Strategy = TenancyStrategy.DatabasePerTenant };
            int id;
            await using (RvTokenDb seed = new(opts, Tenant5(), tenancy))
            {
                await seed.Database.EnsureCreatedAsync();
                RvEntity e = new() { Name = "before", RowVersion = [1] };
                seed.Items.Add(e);
                await seed.SaveChangesAsync();
                id = e.Id;
            }

            await using RvTokenDb ctx = new(opts, Tenant5(), tenancy);
            await Assert.ThrowsExactlyAsync<ConcurrencyConflictException>(() => ctx.Update<RvEntity>(id, e =>
            {
                // Between the load and the save, a second caller changes the row's version.
                using RvTokenDb other = new(opts, Tenant5(), tenancy);
                other.Items.Where(x => x.Id == id).ExecuteUpdate(s => s.SetProperty(x => x.RowVersion, new byte[] { 2 }));
                e.Name = "late";
            }, expectedRowVersion: [1]));

            // The failed save must not leave the dirty entity tracked for a later SaveChanges on this context.
            Assert.AreEqual(0, ctx.ChangeTracker.Entries().Count());

            await using RvTokenDb read = new(opts, Tenant5(), tenancy);
            Assert.AreEqual("before", (await read.Items.SingleAsync()).Name);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task Update_OnARelationalContextWhereRowVersionIsNotAConcurrencyToken_FailsLoudly()
    {
        string path = NewSqlitePath();
        try
        {
            DbContextOptions<RvPlainDb> opts = new DbContextOptionsBuilder<RvPlainDb>().UseSqlite($"Data Source={path}").Options;
            TenancyOptions tenancy = new() { Strategy = TenancyStrategy.DatabasePerTenant };
            int id;
            await using (RvPlainDb seed = new(opts, Tenant5(), tenancy))
            {
                await seed.Database.EnsureCreatedAsync();
                RvEntity e = new() { Name = "before", RowVersion = [1] };
                seed.Items.Add(e);
                await seed.SaveChangesAsync();
                id = e.Id;
            }

            await using RvPlainDb ctx = new(opts, Tenant5(), tenancy);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => ctx.Update<RvEntity>(id, e => e.Name = "x", expectedRowVersion: [1]));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}

[TestClass]
public class TrackedChildrenTests
{
    private sealed class Child : BaseEntity { public string Label { get; set; } = ""; }

    private sealed record Incoming(int Id, string Label);

    private static void Sync(List<Child> existing, params Incoming[] incoming) =>
        TrackedChildren.Sync(existing, incoming, i => i.Id,
            i => new Child { Label = i.Label }, (c, i) => c.Label = i.Label);

    [TestMethod]
    public void Sync_MatchedChild_IsUpdatedInPlaceKeepingItsKey()
    {
        Child original = new() { Id = 7, Label = "a" };
        List<Child> existing = [original];

        Sync(existing, new Incoming(7, "b"));

        Assert.AreEqual(1, existing.Count);
        Assert.AreSame(original, existing[0]);
        Assert.AreEqual(7, existing[0].Id);
        Assert.AreEqual("b", existing[0].Label);
    }

    [TestMethod]
    public void Sync_NewItem_IsAdded_AndMissingChildIsRemoved()
    {
        List<Child> existing = [new Child { Id = 1, Label = "keep" }, new Child { Id = 2, Label = "drop" }];

        Sync(existing, new Incoming(1, "keep"), new Incoming(0, "new"));

        CollectionAssert.AreEquivalent(new[] { "keep", "new" }, existing.Select(c => c.Label).ToArray());
        Assert.IsFalse(existing.Any(c => c.Id == 2));
    }

    [TestMethod]
    public void Sync_UnknownNonZeroId_IsTreatedAsNew()
    {
        List<Child> existing = [new Child { Id = 1, Label = "a" }];

        Sync(existing, new Incoming(99, "x"));

        Assert.AreEqual(1, existing.Count);
        Assert.AreEqual(0, existing[0].Id);
        Assert.AreEqual("x", existing[0].Label);
    }

    [TestMethod]
    public void Sync_NullArguments_Throw()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Sync(null!, []));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            TrackedChildren.Sync<Child, Incoming>([], null!, i => i.Id, i => new Child(), (c, i) => { }));
    }

    [TestMethod]
    public void Sync_NullDelegates_ThrowBeforeAnythingIsMutated()
    {
        Child original = new() { Id = 1, Label = "a" };
        List<Child> existing = [original];

        Assert.ThrowsExactly<ArgumentNullException>(() => TrackedChildren.Sync<Child, Incoming>(existing, [new Incoming(1, "b")], null!, i => new Child(), (c, i) => { }));
        Assert.ThrowsExactly<ArgumentNullException>(() => TrackedChildren.Sync<Child, Incoming>(existing, [new Incoming(1, "b")], i => i.Id, null!, (c, i) => { }));
        Assert.ThrowsExactly<ArgumentNullException>(() => TrackedChildren.Sync<Child, Incoming>(existing, [new Incoming(1, "b")], i => i.Id, i => new Child(), null!));
        Assert.AreEqual("a", original.Label);
    }
}
