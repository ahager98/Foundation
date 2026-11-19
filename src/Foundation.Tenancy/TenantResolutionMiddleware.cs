using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Foundation.Tenancy;

/// <summary>
/// Copies the request's tenant id off the route and onto the scoped <see cref="ITenantService"/> via
/// <see cref="ITenantService.SetTenantId"/>, so the tenant stays resolvable even where the
/// <c>HttpContext</c> is not.
/// <para>
/// WHY THIS EXISTS — <see cref="TenantService"/> reads the tenant from the active request's route
/// values, which requires <c>IHttpContextAccessor.HttpContext</c>. That accessor is backed by an
/// <c>AsyncLocal</c>, and some callers run the work in a DETACHED execution context where it is null.
/// The one that bites in practice is <c>HybridCache.GetOrCreateAsync</c>: its factory delegate runs
/// detached, so a cached read resolved no tenant and
/// <c>TenantDbContext.CurrentTenantId</c> fell back to 0. In
/// <see cref="TenancyStrategy.SharedDatabase"/> mode the global query filter is
/// <c>TenantId == CurrentTenantId</c>, so a 0 there matches NO rows: every cached read of an
/// <c>ITenantScoped</c> entity silently returned empty (list) or 404 (point read), while the uncached
/// write path on the same id worked. The filter fails closed, so this leaked nothing — it hid data.
/// </para>
/// <para>
/// The middleware runs while the HttpContext IS available and stamps the id onto the request-scoped
/// service, which the detached delegate still holds a reference to (DI scope is not AsyncLocal). The
/// fallback in <c>TenantService.ResolveTenantId</c> then returns the right tenant instead of empty.
/// Register it AFTER <c>UseRouting()</c> — route values do not exist before that.
/// </para>
/// <para>
/// Requests with no tenant route value (health probes, webhooks, gateway-level routes) are left
/// alone: nothing is stamped, and the service keeps whatever a caller set explicitly.
/// </para>
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    /// <summary>
    /// The route-parameter (and query-parameter) name for the tenant id — the only spelling accepted,
    /// matching <c>TenantService.ResolveTenantId</c>. Keep the two in step.
    /// </summary>
    private const string RouteKey = "tenantId";

    public async Task InvokeAsync(HttpContext context, ITenantService tenantService)
    {
        if (TryReadTenantId(context, out string? tenantId))
            tenantService.SetTenantId(tenantId!);

        await next(context);
    }

    private static bool TryReadTenantId(HttpContext context, out string? tenantId)
    {
        if (TryRoute(context, RouteKey, out tenantId)) return true;

        // QUERY-STRING FALLBACK. Not every tenant-scoped operation hangs off a tenant-rooted route:
        // endpoints keyed on another aggregate take the tenant as a query parameter instead (Platform's
        // POST /User/{userId}/Lock?tenantId=… is the canonical case). Those reads still touch
        // ITenantScoped tables — Lock loads the actor's roles, and Role IS tenant-scoped — so without
        // this the filter resolved 0 and the actor looked role-less, answering 403 to a legitimate admin.
        // A query value is exactly as client-supplied as a route value, so this widens nothing: the
        // filter is defense-in-depth layered over repository predicates that already constrain the
        // tenant explicitly, and authorization is enforced separately.
        if (TryQuery(context, RouteKey, out tenantId)) return true;

        tenantId = null;
        return false;
    }

    private static bool TryRoute(HttpContext context, string key, out string? value)
    {
        if (context.Request.RouteValues.TryGetValue(key, out object? raw) && raw is not null)
        {
            value = raw.ToString();
            return !string.IsNullOrEmpty(value);
        }
        value = null;
        return false;
    }

    private static bool TryQuery(HttpContext context, string key, out string? value)
    {
        if (context.Request.Query.TryGetValue(key, out Microsoft.Extensions.Primitives.StringValues raw))
        {
            // A repeated parameter (?tenantId=1&tenantId=2) is ambiguous; take the first, matching how
            // model binding resolves a scalar. Blank is treated as absent.
            value = raw.Count > 0 ? raw[0] : null;
            return !string.IsNullOrEmpty(value);
        }
        value = null;
        return false;
    }
}

/// <summary>Pipeline registration for <see cref="TenantResolutionMiddleware"/>.</summary>
public static class TenantResolutionMiddlewareExtensions
{
    /// <summary>
    /// Adds <see cref="TenantResolutionMiddleware"/>. Call AFTER <c>UseRouting()</c> and BEFORE any
    /// endpoint that reads tenant-scoped data. Required by every
    /// <see cref="TenancyStrategy.SharedDatabase"/> service — without it, work that runs detached from
    /// the HttpContext (notably <c>HybridCache</c> factory delegates) resolves tenant 0 and the global
    /// query filter hides every row.
    /// </summary>
    public static IApplicationBuilder UseFoundationTenantResolution(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolutionMiddleware>();
}
