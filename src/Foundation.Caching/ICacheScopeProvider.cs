namespace Foundation.Caching;

/// <summary>Resolves the current <see cref="CacheScope"/>. Implemented by the service from its tenant/sub-scope context.</summary>
public interface ICacheScopeProvider
{
    CacheScope Current { get; }
}
