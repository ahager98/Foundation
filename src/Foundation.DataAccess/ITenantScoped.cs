namespace Foundation.DataAccess;

/// <summary>
/// Marks an entity as tenant-scoped. In <c>SharedDatabase</c> tenancy mode the EF Core global
/// query filter restricts reads of this entity to the current tenant and <see cref="TenantId"/>
/// is stamped on insert. In <c>DatabasePerTenant</c> mode the interface has no effect (tenant
/// isolation is enforced at the connection level), so the column may be left unmapped.
/// <para>Entities opt in explicitly, e.g. <c>class Foo : BaseEntity, ITenantScoped</c>.</para>
/// </summary>
public interface ITenantScoped
{
    /// <summary>The owning tenant's id. Auto-stamped on insert in <c>SharedDatabase</c> mode.</summary>
    int TenantId { get; set; }
}
