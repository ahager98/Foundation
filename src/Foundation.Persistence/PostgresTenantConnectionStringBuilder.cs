using Foundation.Tenancy;
using Npgsql;

namespace Foundation.Persistence;

/// <summary>
/// PostgreSQL implementation of <see cref="ITenantConnectionStringBuilder"/>. Substitutes the tenant
/// id into <see cref="NpgsqlConnectionStringBuilder.Database"/> so the replacement is structurally
/// scoped to the database name — not spliced into the raw connection string text — and applies
/// pool sizing using Npgsql's connection-string keywords.
/// </summary>
public sealed class PostgresTenantConnectionStringBuilder : ITenantConnectionStringBuilder
{
    public string ProviderName => "Postgres";

    public string Build(string template, string tenantId, int maxPoolSize, int minPoolSize)
    {
        // Modify only the Database property so the substitution is structurally scoped to the
        // database name — not spliced into the raw connection string text.
        NpgsqlConnectionStringBuilder csb = new(template);
        // {tenantId} is the only placeholder token recognised in the database name.
        if (csb.Database is not null)
            csb.Database = csb.Database.Replace("{tenantId}", tenantId);

        // Npgsql pools connections per distinct connection string, so each tenant gets its own pool.
        csb.MaxPoolSize = maxPoolSize;
        csb.MinPoolSize = minPoolSize;
        return csb.ConnectionString;
    }

    public string BuildShared(string template, int maxPoolSize, int minPoolSize)
    {
        // Shared database: a single fixed database serves every tenant, so leave Database untouched
        // and apply pool sizing only.
        NpgsqlConnectionStringBuilder csb = new(template)
        {
            MaxPoolSize = maxPoolSize,
            MinPoolSize = minPoolSize,
        };
        return csb.ConnectionString;
    }
}
