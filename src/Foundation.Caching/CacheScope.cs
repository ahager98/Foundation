using System.Diagnostics.CodeAnalysis;

namespace Foundation.Caching;

/// <summary>
/// The tenant, and optionally a sub-scope within it, that a cache operation runs for. Supplied per request by the service.
/// The sub-scope is whatever the service partitions a tenant by (a site, region, department, workspace); null means
/// tenant-wide.
/// </summary>
[ExcludeFromCodeCoverage]
public readonly record struct CacheScope(int TenantId, int? SubScopeId);
