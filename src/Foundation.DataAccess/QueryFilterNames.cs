namespace Foundation.DataAccess;

/// <summary>
/// Canonical names for the global query filters services register, so code that must reason about
/// them can tell one from another.
/// <para>
/// They live in this assembly rather than beside <c>TenantDbContext</c> because
/// <see cref="GenericCrud{TEntity, TContext}"/> needs them and the dependency runs
/// Persistence → DataAccess, not the other way round. <c>TenantDbContext</c> re-exports them.
/// </para>
/// <para>
/// The distinction is load-bearing, not cosmetic. A key lookup that must reach a soft-deleted row has to
/// drop the soft-delete filter — but dropping the TENANT filter at the same time would let an update
/// locate another tenant's row by id under
/// <c>TenancyStrategy.SharedDatabase</c>. That is why the argument-less
/// <c>IgnoreQueryFilters()</c> is never used for this: it is all-or-nothing.
/// </para>
/// </summary>
public static class QueryFilterNames
{
    /// <summary>
    /// The per-tenant predicate (<c>e.TenantId == CurrentTenantId</c>). MUST NOT be ignored by anything
    /// that locates rows for writing — it is the tenant-isolation boundary under
    /// <c>TenancyStrategy.SharedDatabase</c>.
    /// </summary>
    public const string Tenant = "Tenant";

    /// <summary>
    /// The soft-delete predicate (typically <c>e.IsActive</c>). Safe to ignore for a by-id lookup: the
    /// caller already named the exact row, and reactivate/undelete flows cannot work otherwise.
    /// </summary>
    public const string SoftDelete = "SoftDelete";
}
