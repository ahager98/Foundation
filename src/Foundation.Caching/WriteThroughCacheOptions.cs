namespace Foundation.Caching;

/// <summary>Options for one aggregate's cache.</summary>
public sealed class WriteThroughCacheOptions
{
    /// <summary>Hard ceiling on the lifetime of an entry that holds PHI or PII.</summary>
    public static readonly TimeSpan MaxClassifiedTtl = TimeSpan.FromMinutes(5);

    /// <summary>Entry lifetime. Defaults to 30 minutes, the package default.</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// True (the default) caps the lifetime at <see cref="MaxClassifiedTtl"/>, because nothing ties a cache registration
    /// to the [Phi]/[Pii] classification of what it holds. Set it to false only for an aggregate that is explicitly
    /// known to hold neither PHI nor PII.
    /// </summary>
    public bool ContainsClassifiedData { get; set; } = true;

    /// <summary>
    /// Ceiling on how long an entry may sit in the per-process (L1) cache. HybridCache has no backplane, so another
    /// instance's L1 may keep serving a value this instance just evicted until it expires; this bounds that window.
    /// </summary>
    public TimeSpan MaxLocalCacheTtl { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Called when evicting the cache fails (for example a Redis outage). An eviction failure never replaces the write's
    /// own result or exception: a committed write must not be reported as failed (that invites a duplicate retry), so the
    /// failure is reported here instead and stale entries live at most <see cref="Ttl"/> (the local copy at most
    /// <see cref="MaxLocalCacheTtl"/>). Log the exception by type; never log cached values.
    /// </summary>
    public Action<Exception>? OnEvictionFailed { get; set; }

    /// <summary>
    /// Most entries kept per tenant-and-sub-scope scope; the oldest is evicted past this. The bound is per scope, not per
    /// tenant: with <see cref="MaxTrackedScopes"/> a process holds at most MaxEntriesPerScope x MaxTrackedScopes keys per
    /// aggregate (10 million by default), and a tenant with many sub-scopes can occupy many scopes. Lower either value
    /// for tenants with many sub-scopes. The tracker is per process, so each instance bounds only its own view of a shared L2.
    /// </summary>
    public int MaxEntriesPerScope { get; set; } = 1000;

    /// <summary>
    /// Most tenant/sub-scope scopes tracked at once; idle scopes are forgotten first, then the least recently used (a live
    /// forgotten scope is evicted by tag). A busy tenant with many sub-scopes can crowd other tenants' scopes out through this.
    /// </summary>
    public int MaxTrackedScopes { get; set; } = 10_000;

    /// <summary>
    /// Fails fast on values that would break the cache bookkeeping (a bound below 1 would empty the tracked key list
    /// and crash the eviction loop). Called by the registration and by the <see cref="WriteThroughCache{TAggregate}"/> constructor.
    /// </summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxEntriesPerScope, 1, nameof(MaxEntriesPerScope));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxTrackedScopes, 1, nameof(MaxTrackedScopes));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Ttl, TimeSpan.Zero, nameof(Ttl));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaxLocalCacheTtl, TimeSpan.Zero, nameof(MaxLocalCacheTtl));
    }

    /// <summary>The lifetime actually applied: <see cref="Ttl"/>, capped for classified data.</summary>
    public TimeSpan EffectiveTtl => ContainsClassifiedData && Ttl > MaxClassifiedTtl ? MaxClassifiedTtl : Ttl;

    /// <summary>The L1 lifetime actually applied: the entry lifetime, capped by <see cref="MaxLocalCacheTtl"/>.</summary>
    public TimeSpan EffectiveLocalTtl => EffectiveTtl < MaxLocalCacheTtl ? EffectiveTtl : MaxLocalCacheTtl;
}
