namespace Foundation.Tenancy;

/// <summary>
/// Selects how a service isolates tenant data. Chosen by the host via <c>Tenancy:Strategy</c>
/// so the same binaries can be deployed either way without code changes.
/// </summary>
public enum TenancyStrategy
{
    /// <summary>
    /// One physical database per tenant (the default). The tenant id is substituted into the
    /// connection-string template (<c>{tenantId}</c>) and isolation is enforced at the
    /// connection level. No <c>TenantId</c> column or query filter is required.
    /// </summary>
    DatabasePerTenant = 0,

    /// <summary>
    /// A single shared database for all tenants. Rows are distinguished by a <c>TenantId</c>
    /// discriminator and isolated at query time via an EF Core global query filter on
    /// <c>ITenantScoped</c> entities.
    /// </summary>
    SharedDatabase = 1
}
