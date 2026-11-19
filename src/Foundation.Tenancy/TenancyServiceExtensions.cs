using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Foundation.Tenancy;

public static class TenancyServiceExtensions
{
    /// <summary>
    /// Registers <see cref="ITenantService"/> / <see cref="TenantService"/> as scoped, plus (via
    /// <c>TryAdd</c>, so callers can override) a default <see cref="TenancyOptions"/>
    /// (<see cref="TenancyStrategy.DatabasePerTenant"/>) and a default SQL Server
    /// <see cref="ITenantConnectionStringBuilder"/>. Also registers <c>IHttpContextAccessor</c>
    /// (idempotent — safe to call from multiple services).
    /// <para>Use the <see cref="AddFoundationTenancy(IServiceCollection, IConfiguration)"/> overload to
    /// honor the host's <c>Tenancy:Strategy</c> setting; the provider is chosen by
    /// <c>AddFoundationPersistence</c> via <c>Database:Provider</c>.</para>
    /// </summary>
    [ExcludeFromCodeCoverage]
    public static IServiceCollection AddFoundationTenancy(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton(new TenancyOptions());
        services.TryAddScoped<ITenantConnectionStringBuilder, SqlServerTenantConnectionStringBuilder>();
        services.AddScoped<ITenantService, TenantService>();
        return services;
    }

    /// <summary>
    /// Registers tenancy services and binds <see cref="TenancyOptions"/> from <c>Tenancy:Strategy</c>.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public static IServiceCollection AddFoundationTenancy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TenancyOptions.FromConfiguration(configuration));
        return services.AddFoundationTenancy();
    }
}
