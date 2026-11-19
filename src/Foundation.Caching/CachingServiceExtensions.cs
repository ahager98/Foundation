using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Foundation.Caching;

public static class CachingServiceExtensions
{
    /// <summary>
    /// Registers HybridCache with an optional Redis L2 backing store.
    /// <para>
    /// When <c>Redis:ConnectionString</c> is present in config, <c>AddStackExchangeRedisCache</c>
    /// is registered first so HybridCache picks it up as the L2 distributed store. When absent,
    /// cache is in-process only (suitable for local dev and single-instance deployments).
    /// </para>
    /// <para>
    /// TTL defaults to 30 minutes; override via <c>Caching:LookupTtlMinutes</c> in config.
    /// </para>
    /// </summary>
    [ExcludeFromCodeCoverage]
    public static IServiceCollection AddFoundationCaching(this IServiceCollection services, IConfiguration configuration)
    {
        string? redisConnection = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnection))
            services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);

        int ttlMinutes = configuration.GetValue<int?>("Caching:LookupTtlMinutes") ?? 30;
        services.AddHybridCache(options =>
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(ttlMinutes),
                LocalCacheExpiration = TimeSpan.FromMinutes(ttlMinutes)
            });

        return services;
    }

    /// <summary>
    /// Registers the write-through cache for one aggregate: a singleton <see cref="WriteThroughCacheState{TAggregate}"/>
    /// and options, and a scoped <see cref="WriteThroughCache{TAggregate}"/>. The service must also register an
    /// <see cref="ICacheScopeProvider"/> that returns the current tenant and sub-scope.
    /// </summary>
    public static IServiceCollection AddFoundationWriteThroughCache<TAggregate>(
        this IServiceCollection services, Action<WriteThroughCacheOptions>? configure = null)
    {
        WriteThroughCacheOptions options = new();
        configure?.Invoke(options);

        options.Validate();

        services.AddSingleton<WriteThroughCacheState<TAggregate>>();
        services.AddScoped<WriteThroughCache<TAggregate>>(sp => new WriteThroughCache<TAggregate>(
            sp.GetRequiredService<HybridCache>(),
            sp.GetRequiredService<ICacheScopeProvider>(),
            sp.GetRequiredService<WriteThroughCacheState<TAggregate>>(),
            options));

        return services;
    }
}
