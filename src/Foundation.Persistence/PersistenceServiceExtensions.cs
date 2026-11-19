using System.Diagnostics.CodeAnalysis;
using Foundation.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Foundation.Persistence;

public static class PersistenceServiceExtensions
{
    /// <summary>
    /// Registers a tenant-scoped <typeparamref name="TContext"/> across the two orthogonal axes,
    /// both read from configuration:
    /// <list type="bullet">
    /// <item><b>Tenancy strategy</b> (<c>Tenancy:Strategy</c>, default
    /// <see cref="TenancyStrategy.DatabasePerTenant"/>) — database-per-tenant resolves each tenant to
    /// its own database; <see cref="TenancyStrategy.SharedDatabase"/> shares one database and
    /// <typeparamref name="TContext"/> (when derived from <see cref="TenantDbContext"/>) applies a
    /// global tenant query filter.</item>
    /// <item><b>Database provider</b> (<c>Database:Provider</c>, case-insensitive, default
    /// <c>SqlServer</c>) — selects the EF provider (<c>UseSqlServer</c> / <c>UseNpgsql</c>) and the
    /// matching <see cref="ITenantConnectionStringBuilder"/>.</item>
    /// </list>
    /// The per-request tenant connection string is resolved from <see cref="ITenantService"/> at
    /// scope-creation time. Add a new provider by adding a case here plus its
    /// <see cref="ITenantConnectionStringBuilder"/> implementation — no change to
    /// <see cref="TenantService"/>.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public static IServiceCollection AddFoundationPersistence<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : DbContext
    {
        // Tenancy axis: binds TenancyOptions from Tenancy:Strategy (and registers ITenantService).
        services.AddFoundationTenancy(configuration);

        // Provider axis: selects the EF provider and the matching connection-string builder.
        string provider = configuration["Database:Provider"] ?? "SqlServer";
        switch (provider.ToLowerInvariant())
        {
            case "postgres":
                services.AddScoped<ITenantConnectionStringBuilder, PostgresTenantConnectionStringBuilder>();
                RegisterDbContext<TContext>(services, (opts, cs) =>
                    opts.UseNpgsql(cs, npg => npg.EnableRetryOnFailure()));
                break;
            case "sqlserver":
                services.AddScoped<ITenantConnectionStringBuilder, SqlServerTenantConnectionStringBuilder>();
                RegisterDbContext<TContext>(services, (opts, cs) =>
                    opts.UseSqlServer(cs, sql => sql.EnableRetryOnFailure()));
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported Database:Provider '{provider}'. Supported values: SqlServer, Postgres.");
        }

        return services;
    }

    // The connection string is per-tenant and per-request, so it is resolved from the scoped
    // ITenantService inside the options callback (which runs with a scoped service provider) rather
    // than captured at registration time.
    private static void RegisterDbContext<TContext>(
        IServiceCollection services,
        Action<DbContextOptionsBuilder, string> configureProvider)
        where TContext : DbContext =>
        services.AddDbContext<TContext>((sp, opts) =>
            configureProvider(opts, sp.GetRequiredService<ITenantService>().GetTenantConnectionStringSync()));
}
