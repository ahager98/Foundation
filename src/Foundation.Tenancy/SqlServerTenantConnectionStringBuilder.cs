using Microsoft.Data.SqlClient;

namespace Foundation.Tenancy;

/// <summary>
/// Azure SQL / SQL Server implementation of <see cref="ITenantConnectionStringBuilder"/>.
/// Substitutes the tenant id into <see cref="SqlConnectionStringBuilder.InitialCatalog"/> so the
/// replacement is structurally scoped to the database name — not spliced into the raw connection
/// string text — and applies pool sizing.
/// </summary>
public sealed class SqlServerTenantConnectionStringBuilder : ITenantConnectionStringBuilder
{
    public string ProviderName => "SqlServer";

    public string Build(string template, string tenantId, int maxPoolSize, int minPoolSize)
    {
        // Modify only the InitialCatalog property so the substitution is structurally scoped
        // to the database name — not spliced into the raw connection string text.
        SqlConnectionStringBuilder csb = new(template);
        // {tenantId} is the only placeholder token recognised in the database name.
        csb.InitialCatalog = csb.InitialCatalog.Replace("{tenantId}", tenantId);

        // ADO.NET pools connections per distinct connection string, so each tenant gets its own
        // pool. Tune the pool to stay within Azure SQL per-database/per-pool connection limits.
        csb.MaxPoolSize = maxPoolSize;
        csb.MinPoolSize = minPoolSize;
        return csb.ConnectionString;
    }

    public string BuildShared(string template, int maxPoolSize, int minPoolSize)
    {
        // Shared database: a single fixed catalog serves every tenant, so leave InitialCatalog
        // untouched and apply pool sizing only.
        SqlConnectionStringBuilder csb = new(template)
        {
            MaxPoolSize = maxPoolSize,
            MinPoolSize = minPoolSize,
        };
        return csb.ConnectionString;
    }
}
