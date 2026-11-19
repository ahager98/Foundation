using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Hybrid;

namespace Foundation.Caching;

/// <summary>
/// Write-through cache for one aggregate. Reads go through <see cref="GetOrCreateAsync{T}"/>; every write goes
/// through <see cref="WriteAsync{TResult}"/>, which evicts the aggregate's entries for the whole tenant (every
/// sub-scope) before and after the write, so there is no write path that skips eviction.
/// <list type="bullet">
/// <item>Keys and tags always carry tenant and sub-scope, so an entry can never be served across them.</item>
/// <item>Without a resolved tenant (id 0 or less) nothing is cached or served from cache (fail closed).</item>
/// <item>Keys must be plain identifiers (no names or free text); values are never logged by this type.</item>
/// <item>The cached value is shared by every caller in the same tenant and sub-scope. Authorize the caller before
/// <c>GetOrCreateAsync</c>, and put any per-user or per-role variation in the key.</item>
/// <item>Entries holding PHI/PII live at most <see cref="WriteThroughCacheOptions.MaxClassifiedTtl"/>.</item>
/// </list>
/// </summary>
public sealed partial class WriteThroughCache<TAggregate>(
    HybridCache cache,
    ICacheScopeProvider scopeProvider,
    WriteThroughCacheState<TAggregate> state,
    WriteThroughCacheOptions options)
{
    private readonly HybridCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly ICacheScopeProvider _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));
    private readonly WriteThroughCacheState<TAggregate> _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly WriteThroughCacheOptions _options = ValidOptions(options);

    private static readonly string AggregateName = typeof(TAggregate).FullName ?? typeof(TAggregate).Name;

    [GeneratedRegex(@"^[A-Za-z0-9_.:-]{1,128}\z")]
    private static partial Regex SafeKey();

    /// <summary>Returns the cached value for <paramref name="key"/> in the current scope, creating it with <paramref name="factory"/> on a miss.</summary>
    public async ValueTask<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!SafeKey().IsMatch(key ?? string.Empty))
            throw new ArgumentException("Cache key must be 1-128 characters of letters, digits, '_', '.', ':' or '-' (no names or free text).", nameof(key));

        CacheScope scope = _scopeProvider.Current;
        if (scope.TenantId <= 0)
            return await factory(cancellationToken);

        string scopeTag = ScopeTag(scope);
        string fullKey = $"{scopeTag}:{key}";

        T value = await _cache.GetOrCreateAsync(
            fullKey,
            factory,
            new HybridCacheEntryOptions { Expiration = _options.EffectiveTtl, LocalCacheExpiration = _options.EffectiveLocalTtl },
            [scopeTag, TenantTag(scope)],
            cancellationToken);

        (List<string> overflow, List<string> forgotten) =
            _state.Track(scopeTag, fullKey, _options.MaxEntriesPerScope, _options.EffectiveTtl, _options.MaxTrackedScopes);

        // Already untracked, so a cancelled caller must not skip these. The value was read successfully, so an eviction
        // failure is reported through the hook rather than failing the read. A forgotten scope is evicted with one tag
        // call, so a read never pays one round trip per entry.
        foreach (string evicted in overflow)
            await EvictAsync(() => _cache.RemoveAsync(evicted, CancellationToken.None));

        foreach (string tag in forgotten)
            await EvictAsync(() => _cache.RemoveByTagAsync(tag, CancellationToken.None));

        return value;
    }

    /// <summary>
    /// Runs a write and evicts this aggregate's entries for the whole tenant (every sub-scope, because one record can be
    /// cached under several). Eviction happens before the write, to drop entries that are already stale, and again after
    /// it, also when the write throws, because a failed write may still have been applied. The delegate must finish its
    /// commit before it returns: a transaction that commits later leaves a window in which a read can re-cache old data.
    /// A read that began before the commit can still store its old value after the second eviction; the entry lifetime
    /// bounds that window. An eviction failure never replaces the write's result or exception; it is reported through
    /// <see cref="WriteThroughCacheOptions.OnEvictionFailed"/>.
    /// </summary>
    public async Task<TResult> WriteAsync<TResult>(Func<CancellationToken, Task<TResult>> write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        await EvictScopeAsync();
        try
        {
            return await write(cancellationToken);
        }
        finally
        {
            await EvictScopeAsync();
        }
    }

    /// <inheritdoc cref="WriteAsync{TResult}"/>
    public async Task WriteAsync(Func<CancellationToken, Task> write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        await WriteAsync<bool>(async ct =>
        {
            await write(ct);
            return true;
        }, cancellationToken);
    }

    // Eviction must not be cancellable by the request token: a cancelled caller must still leave no stale entry.
    private async Task EvictScopeAsync()
    {
        CacheScope scope = _scopeProvider.Current;
        if (scope.TenantId <= 0)
            return;

        string tenantTag = TenantTag(scope);
        try
        {
            await _cache.RemoveByTagAsync(tenantTag, CancellationToken.None);
            _state.ClearTenant(tenantTag + ":");
        }
        catch (Exception ex)
        {
            // The tracker is left as it was: the entries are still cached, so the per-scope bound must still see them.
            ReportEvictionFailure(ex);
        }
    }

    private static WriteThroughCacheOptions ValidOptions(WriteThroughCacheOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return options;
    }

    private async ValueTask EvictAsync(Func<ValueTask> evict)
    {
        try
        {
            await evict();
        }
        catch (Exception ex)
        {
            ReportEvictionFailure(ex);
        }
    }

    private void ReportEvictionFailure(Exception ex)
    {
        try
        {
            _options.OnEvictionFailed?.Invoke(ex);
        }
        catch (Exception)
        {
            // A failing diagnostics hook must not turn into a read or write failure either.
        }
    }

    private static string TenantTag(CacheScope scope) => $"{AggregateName}:t{scope.TenantId}";

    private static string ScopeTag(CacheScope scope) =>
        $"{AggregateName}:t{scope.TenantId}:c{(scope.SubScopeId?.ToString() ?? "-")}";
}
