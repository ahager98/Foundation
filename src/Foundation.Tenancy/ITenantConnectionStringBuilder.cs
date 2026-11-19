namespace Foundation.Tenancy;

/// <summary>
/// Provider-specific finalization of a per-tenant connection string. <see cref="TenantService"/>
/// owns the provider-agnostic concerns (reading the template, validating the tenant id to prevent
/// injection); implementations own only the provider-specific assembly — substituting the tenant id
/// into the database-name portion and applying connection-pool settings using the right driver's
/// connection-string builder.
/// <para>
/// Implementations are selected by the <c>Database:Provider</c> configuration setting via the
/// persistence registration extension. The default registration (<c>AddFoundationTenancy</c>) is
/// <see cref="SqlServerTenantConnectionStringBuilder"/>.
/// </para>
/// </summary>
public interface ITenantConnectionStringBuilder
{
    /// <summary>Provider key this builder handles (e.g. "SqlServer", "Postgres").</summary>
    string ProviderName { get; }

    /// <summary>
    /// Substitutes <paramref name="tenantId"/> into the database-name portion of
    /// <paramref name="template"/> and applies the pool sizes, returning a fully-qualified
    /// connection string. The caller has already validated that <paramref name="tenantId"/> is a
    /// safe positive integer and that <paramref name="template"/> is non-null. Used in
    /// <see cref="TenancyStrategy.DatabasePerTenant"/> mode.
    /// </summary>
    string Build(string template, string tenantId, int maxPoolSize, int minPoolSize);

    /// <summary>
    /// Applies the pool sizes to <paramref name="template"/> and returns it without any tenant-id
    /// substitution — for <see cref="TenancyStrategy.SharedDatabase"/> mode, where a single fixed
    /// database serves every tenant. The caller has already validated that <paramref name="template"/>
    /// is non-null.
    /// </summary>
    string BuildShared(string template, int maxPoolSize, int minPoolSize);
}
