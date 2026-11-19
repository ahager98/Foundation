namespace Foundation.Caching;

/// <summary>
/// Process-wide state for one aggregate: which keys are cached per scope, for the per-scope size bound. Holds
/// keys only, never values. Register as a singleton.
/// </summary>
public sealed class WriteThroughCacheState<TAggregate>(TimeProvider? clock = null)
{
    private sealed class ScopeKeys
    {
        public readonly LinkedList<string> Order = new();
        public readonly HashSet<string> Members = [];
        public DateTimeOffset LastTouched;
    }

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ScopeKeys> _keysByScope = [];

    /// <summary>
    /// Records a key; returns the keys that must now be evicted to stay within <paramref name="maxKeys"/> for the
    /// scope, plus the scope tags that were forgotten while still live (their entries must be evicted by tag in one
    /// call). Scopes idle longer than <paramref name="ttl"/> (all their entries have expired) are forgotten, and the
    /// number of tracked scopes never exceeds <paramref name="maxScopes"/>, so the bookkeeping cannot grow with the
    /// number of tenants and communities ever seen.
    /// </summary>
    internal (List<string> Keys, List<string> Scopes) Track(
        string scopeTag, string fullKey, int maxKeys, TimeSpan ttl, int maxScopes)
    {
        lock (_gate)
        {
            DateTimeOffset now = _clock.GetUtcNow();
            List<string> evict = [];
            List<string> forgotten = [];
            if (!_keysByScope.TryGetValue(scopeTag, out ScopeKeys? scope))
            {
                ForgetIdleScopes(now, ttl, maxScopes - 1, forgotten);
                _keysByScope[scopeTag] = scope = new ScopeKeys();
            }

            scope.LastTouched = now;
            if (!scope.Members.Add(fullKey))
                return (evict, forgotten);

            scope.Order.AddLast(fullKey);
            while (scope.Order.Count > maxKeys)
            {
                string oldest = scope.Order.First!.Value;
                scope.Order.RemoveFirst();
                scope.Members.Remove(oldest);
                evict.Add(oldest);
            }

            return (evict, forgotten);
        }
    }

    // Drops expired scopes first (their entries have already expired), then the least recently touched until at most
    // keep scopes remain. A dropped scope that is still live has its tag added to forgotten, so the caller can evict
    // all of its entries with one tag call and forgetting the bookkeeping never leaves entries outside the bound.
    private void ForgetIdleScopes(DateTimeOffset now, TimeSpan ttl, int keep, List<string> forgotten)
    {
        if (_keysByScope.Count <= keep)
            return;

        foreach (string idle in _keysByScope.Where(kv => kv.Value.LastTouched + ttl < now).Select(static kv => kv.Key).ToList())
            _keysByScope.Remove(idle);

        while (_keysByScope.Count > Math.Max(keep, 0))
        {
            KeyValuePair<string, ScopeKeys> oldest = _keysByScope.MinBy(static kv => kv.Value.LastTouched);
            forgotten.Add(oldest.Key);
            _keysByScope.Remove(oldest.Key);
        }
    }

    internal void Clear(string scopeTag)
    {
        lock (_gate)
            _keysByScope.Remove(scopeTag);
    }

    /// <summary>Forgets every scope of one tenant (their tags all start with <paramref name="tenantPrefix"/>).</summary>
    internal void ClearTenant(string tenantPrefix)
    {
        lock (_gate)
        {
            foreach (string scope in _keysByScope.Keys.Where(k => k.StartsWith(tenantPrefix, StringComparison.Ordinal)).ToList())
                _keysByScope.Remove(scope);
        }
    }

    internal int Count(string scopeTag)
    {
        lock (_gate)
            return _keysByScope.TryGetValue(scopeTag, out ScopeKeys? scope) ? scope.Order.Count : 0;
    }

    internal int ScopeCount
    {
        get
        {
            lock (_gate)
                return _keysByScope.Count;
        }
    }
}
