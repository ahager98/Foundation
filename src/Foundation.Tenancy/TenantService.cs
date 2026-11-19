using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Foundation.Tenancy;

/// <summary>
/// Resolves the per-request tenant connection string by reading <c>tenantId</c> from the current HTTP
/// request's route values and, in <see cref="TenancyStrategy.DatabasePerTenant"/> mode, substituting it
/// into the configured connection-string template.
/// <para>
/// <c>SetTenantId</c> provides a fallback for scenarios without an HTTP context
/// (e.g. EF migrations, background jobs, HybridCache factory delegates).
/// </para>
/// <para>
/// Connection string template key is configurable via <c>Tenant:ConnectionStringKey</c>
/// (default <c>Tenant</c>). Services override this to their own key (e.g.
/// <c>CRMTenant</c>, <c>PersonTenant</c>).
/// </para>
/// <para>
/// The two axes are orthogonal: the <em>tenancy strategy</em> (<see cref="TenancyStrategy"/>) decides
/// <em>whether</em> the tenant id is substituted into the database name, and the <em>database provider</em>
/// (SQL Server vs PostgreSQL etc.) decides <em>how</em> the connection string is assembled — the latter is
/// delegated to an injected <see cref="ITenantConnectionStringBuilder"/>, selected from configuration by
/// the persistence registration. Defaults to <see cref="SqlServerTenantConnectionStringBuilder"/> /
/// <see cref="TenancyStrategy.DatabasePerTenant"/>.
/// </para>
/// </summary>
public class TenantService : ITenantService
{
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private readonly ITenantConnectionStringBuilder _connectionStringBuilder;
    private readonly TenancyStrategy _strategy;

    private string? _tenantId;

    /// <summary>Primary constructor — used at runtime via DI.</summary>
    public TenantService(
        IHttpContextAccessor? httpContextAccessor,
        IConfiguration configuration,
        ITenantConnectionStringBuilder connectionStringBuilder,
        TenancyOptions? tenancy = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
        _connectionStringBuilder = connectionStringBuilder;
        _strategy = (tenancy ?? new TenancyOptions()).Strategy;
    }

    /// <summary>
    /// Secondary constructor for EF Core migrations and other non-HTTP contexts. The connection-string
    /// builder defaults to SQL Server and the strategy to database-per-tenant when not supplied,
    /// preserving the historical behaviour.
    /// </summary>
    public TenantService(
        IConfiguration configuration,
        ITenantConnectionStringBuilder? connectionStringBuilder = null,
        TenancyOptions? tenancy = null)
    {
        _configuration = configuration;
        _connectionStringBuilder = connectionStringBuilder ?? new SqlServerTenantConnectionStringBuilder();
        _strategy = (tenancy ?? new TenancyOptions()).Strategy;
    }

    public void SetTenantId(string tenantId) => _tenantId = tenantId;

    public Task<string> GetTenantIdAsync() => Task.FromResult(ResolveTenantId());

    public string GetTenantId() => ResolveTenantId();

    public async Task<string> GetTenantConnectionStringAsync()
    {
        string tenantId = await GetTenantIdAsync();
        return BuildConnectionString(tenantId);
    }

    public string GetTenantConnectionStringSync()
        => BuildConnectionString(ResolveTenantId());

    /// <summary>
    /// Resolves the tenant id, preferring the active request's <c>tenantId</c> route value and
    /// falling back to the explicitly-set <see cref="_tenantId"/>. The fallback covers EF migrations,
    /// background jobs, and HybridCache factory delegates (which run in a detached execution context
    /// where <c>IHttpContextAccessor.HttpContext</c> is null).
    /// <para>
    /// <c>tenantId</c> is the only route-parameter name read: every service exposes it.
    /// <c>TenantResolutionMiddleware</c> reads the same name — keep the two in step.
    /// </para>
    /// </summary>
    private string ResolveTenantId()
    {
        if (_httpContextAccessor?.HttpContext is not null)
        {
            RouteData? routeData = _httpContextAccessor.HttpContext.GetRouteData();
            if (routeData is not null
                && routeData.Values.TryGetValue("tenantId", out object? tenantId)
                && tenantId is string tenantIdStr)
                return tenantIdStr;
        }
        return _tenantId ?? string.Empty;
    }

    /// <summary>
    /// Resolves the configured connection-string template, then assembles the final string per the
    /// active <see cref="TenancyStrategy"/> — delegating the provider-specific assembly to
    /// <see cref="_connectionStringBuilder"/>. Returns an empty string when the template is not
    /// configured, or (database-per-tenant only) when the tenant id is unsafe.
    /// </summary>
    private string BuildConnectionString(string tenantId)
    {
        string connectionStringKey = _configuration["Tenant:ConnectionStringKey"] ?? "Tenant";
        string? template = _configuration.GetConnectionString(connectionStringKey);
        if (template is null)
            return string.Empty;

        // Pool sizes are tuned per distinct connection string so each tenant gets its own pool and
        // stays within the database's per-pool connection limits. Configurable via
        // Tenant:MaxPoolSize / Tenant:MinPoolSize.
        int maxPoolSize = _configuration.GetValue<int?>("Tenant:MaxPoolSize") ?? 50;
        int minPoolSize = _configuration.GetValue<int?>("Tenant:MinPoolSize") ?? 0;

        // SharedDatabase: one fixed database serves every tenant. The tenant id is NOT part of the
        // connection string (it drives the EF query filter instead), so there is no {tenantId} to
        // substitute and no injection surface — use the template verbatim, applying pool sizing only.
        if (_strategy == TenancyStrategy.SharedDatabase)
            return _connectionStringBuilder.BuildShared(template, maxPoolSize, minPoolSize);

        // DatabasePerTenant: substitute the tenant id into the database name so each tenant resolves
        // to its own database. Reject non-positive-integer values before they can be interpolated —
        // prevents injection via a crafted route value. The guard lives here (not in the builder) so
        // it is provider-agnostic and applies to every provider.
        if (!int.TryParse(tenantId, out int tenantIdInt) || tenantIdInt <= 0)
            return string.Empty;

        return _connectionStringBuilder.Build(template, tenantId, maxPoolSize, minPoolSize);
    }
}
