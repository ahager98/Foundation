using System.Linq.Expressions;
using Foundation.DataAccess;
using Foundation.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundation.Persistence;

/// <summary>
/// Base <see cref="DbContext"/> for services that supports both tenancy strategies.
/// <para>
/// In <see cref="TenancyStrategy.SharedDatabase"/> mode it applies an EF Core global query filter
/// scoping every <see cref="ITenantScoped"/> entity to the current tenant, and stamps
/// <see cref="ITenantScoped.TenantId"/> on insert. In <see cref="TenancyStrategy.DatabasePerTenant"/>
/// mode neither applies — isolation is enforced at the connection level — so this base behaves like a
/// plain <see cref="DbContext"/>.
/// </para>
/// <para>
/// The tenant predicate is applied as a <b>named</b> query filter (<see cref="TenantQueryFilterName"/>),
/// so it coexists with any soft-delete filter a derived context declared rather than replacing it
/// (EF Core 10 named query filters). Derived contexts keep declaring their soft-delete filter the usual
/// way — a single anonymous <c>e =&gt; e.IsActive</c> — and the base <b>lifts</b> it to the named
/// <see cref="SoftDeleteQueryFilterName"/> filter (EF Core allows only one anonymous filter and forbids
/// mixing anonymous + named filters). Both filters can then be disabled independently, e.g.
/// <c>IgnoreQueryFilters([TenantDbContext.SoftDeleteQueryFilterName])</c> drops soft-delete while
/// keeping tenant isolation. Derived contexts MUST therefore apply their own query filters <b>first</b>
/// and call <c>base.OnModelCreating(modelBuilder)</c> <b>last</b>, so the base can read the existing
/// filter (and any indexes already declared) and lift it.
/// </para>
/// </summary>
public abstract class TenantDbContext : DbContext
{
    /// <summary>Name of the per-tenant global query filter applied by the base in shared-database mode.
    /// Re-exported from <see cref="QueryFilterNames.Tenant"/>, which is where
    /// <c>GenericCrud</c> reads it from (it cannot reference this assembly).</summary>
    public const string TenantQueryFilterName = QueryFilterNames.Tenant;

    /// <summary>
    /// Name the base assigns to a derived context's soft-delete filter when it lifts that filter from
    /// anonymous to named. Pass this to <c>IgnoreQueryFilters([...])</c> to disable soft-delete while
    /// keeping tenant isolation. Re-exported from <see cref="QueryFilterNames.SoftDelete"/>.
    /// </summary>
    public const string SoftDeleteQueryFilterName = QueryFilterNames.SoftDelete;

    private readonly ITenantService _tenantService;
    private readonly TenancyStrategy _strategy;

    protected TenantDbContext(DbContextOptions options, ITenantService tenantService, TenancyOptions tenancy)
        : base(options)
    {
        _tenantService = tenantService;
        _strategy = tenancy.Strategy;
    }

    /// <summary>
    /// The current tenant's id, resolved from <see cref="ITenantService"/>. Returns 0 when the
    /// tenant cannot be resolved — a 0 discriminator matches no rows, so the filter fails closed.
    /// </summary>
    public int CurrentTenantId =>
        int.TryParse(_tenantService.GetTenantId(), out int id) ? id : 0;

    /// <summary>True when rows are isolated by a TenantId column (SharedDatabase) rather than by connection.</summary>
    internal bool IsSharedTenancy => _strategy == TenancyStrategy.SharedDatabase;

    /// <summary>
    /// Clock used to stamp <see cref="BaseEntity.CreatedDate"/>/<see cref="BaseEntity.UpdatedDate"/> on save.
    /// Defaults to <see cref="TimeProvider.System"/>; override in tests for deterministic timestamps.
    /// </summary>
    protected virtual TimeProvider Clock => TimeProvider.System;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // The built model differs by strategy (SharedDatabase adds the query filter). EF Core's
        // default model cache key is the context type only, so include the strategy to keep the
        // two models distinct. The tenant id is deliberately NOT part of the key — it is a query
        // parameter (via CurrentTenantId), so a single model serves every tenant.
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, TenancyModelCacheKeyFactory>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        if (_strategy != TenancyStrategy.SharedDatabase)
            return;

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
                ConfigureTenantFilter(modelBuilder, entityType);
        }
    }

    /// <summary>
    /// Applies the per-tenant named global query filter and a supporting index for one entity type. Any
    /// anonymous filter the derived context declared (typically soft-delete) is first lifted to the
    /// named <see cref="SoftDeleteQueryFilterName"/> filter so both can coexist and be disabled
    /// independently. The tenant filter references the instance property <see cref="CurrentTenantId"/>
    /// so EF Core parameterizes it per query (one cached model serves every tenant).
    /// </summary>
    private void ConfigureTenantFilter(ModelBuilder modelBuilder, IMutableEntityType entityType)
    {
        EntityTypeBuilder builder = modelBuilder.Entity(entityType.ClrType);

        // Lift any anonymous filter the derived context declared (e.g. soft-delete e => e.IsActive) to a
        // NAMED filter. EF Core permits only one anonymous filter per entity and forbids mixing
        // anonymous + named filters, so a named tenant filter cannot sit beside an anonymous one — we
        // clear the anonymous filter and re-register the same predicate under SoftDeleteQueryFilterName.
        LambdaExpression? softDelete = entityType.GetDeclaredQueryFilters()
            .FirstOrDefault(f => f.IsAnonymous)?.Expression;
        if (softDelete is not null)
        {
            builder.HasQueryFilter((LambdaExpression?)null);
            builder.HasQueryFilter(SoftDeleteQueryFilterName, softDelete);
        }

        // e.TenantId == this.CurrentTenantId — referencing the instance property keeps it a query
        // parameter, so one cached model serves every tenant. Registered as a NAMED filter so it
        // coexists with the soft-delete filter above instead of overwriting it.
        ParameterExpression param = Expression.Parameter(entityType.ClrType, "e");
        LambdaExpression tenantFilter = Expression.Lambda(
            Expression.Equal(
                Expression.Property(param, nameof(ITenantScoped.TenantId)),
                Expression.Property(Expression.Constant(this), nameof(CurrentTenantId))),
            param);
        builder.HasQueryFilter(TenantQueryFilterName, tenantFilter);

        // Add a supporting TenantId index only when the entity doesn't already have one that LEADS
        // with TenantId. A service that pre-declared a named IX_*_TenantId (or a composite
        // (TenantId, …) that already serves WHERE TenantId = @p) keeps its own definition, so
        // adopting shared tenancy produces no duplicate/rename migration.
        bool hasLeadingTenantIdIndex = entityType.GetIndexes()
            .Any(ix => ix.Properties.Count > 0
                && ix.Properties[0].Name == nameof(ITenantScoped.TenantId));
        if (!hasLeadingTenantIdIndex)
            builder.HasIndex(nameof(ITenantScoped.TenantId));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        StampTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        StampTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Stamps audit timestamps on <see cref="BaseEntity"/> rows being written: both
    /// <see cref="BaseEntity.CreatedDate"/> and <see cref="BaseEntity.UpdatedDate"/> on insert, and
    /// <see cref="BaseEntity.UpdatedDate"/> alone on update. An update never rewrites
    /// <see cref="BaseEntity.CreatedDate"/>, even if the caller copied a stale value over it. Runs in
    /// both tenancy strategies — audit timestamps are not tenant-specific. The <see cref="BaseEntity"/>
    /// user-id fields (<c>CreatedById</c>/<c>UpdatedById</c>) are left to the caller, which alone knows
    /// the acting user.
    /// </summary>
    private void StampTimestamps()
    {
        DateTime now = Clock.GetUtcNow().UtcDateTime;
        foreach (EntityEntry<BaseEntity> entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedDate = now;
                    entry.Entity.UpdatedDate = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedDate = now;
                    entry.Property(e => e.CreatedDate).IsModified = false;
                    break;
            }
        }
    }

    /// <summary>
    /// In <see cref="TenancyStrategy.SharedDatabase"/> mode, stamps the current tenant onto newly
    /// added <see cref="ITenantScoped"/> entities and rejects writes that target a different tenant
    /// (defense-in-depth against cross-tenant writes). A no-op in <see cref="TenancyStrategy.DatabasePerTenant"/> mode.
    /// </summary>
    private void StampTenant()
    {
        if (_strategy != TenancyStrategy.SharedDatabase)
            return;

        int tenantId = CurrentTenantId;
        foreach (EntityEntry entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added || entry.Entity is not ITenantScoped scoped)
                continue;

            if (scoped.TenantId == 0)
                scoped.TenantId = tenantId;
            else if (scoped.TenantId != tenantId)
                throw new InvalidOperationException(
                    $"Cross-tenant write rejected: entity targets tenant {scoped.TenantId} "
                    + $"but the current tenant is {tenantId}.");
        }
    }

    /// <summary>Keys the EF Core model cache by context type AND tenancy strategy.</summary>
    private sealed class TenancyModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is TenantDbContext tenant
                ? (context.GetType(), tenant._strategy, designTime)
                : (context.GetType(), designTime);
    }
}
