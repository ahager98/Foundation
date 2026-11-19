using Foundation.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Foundation.Tenancy.Tests;

/// <summary>
/// Covers the middleware that copies the route's tenant id onto the scoped <see cref="ITenantService"/>.
/// The regression it guards: a HybridCache factory delegate runs detached from the HttpContext, so
/// without this priming step <c>TenantService</c> resolved empty, <c>CurrentTenantId</c> fell back to 0,
/// and the SharedDatabase query filter hid every tenant-scoped row.
/// </summary>
[TestClass]
public class TenantResolutionMiddlewareTests
{
    private static DefaultHttpContext ContextWithRoute(params (string Key, object Value)[] routeValues)
    {
        DefaultHttpContext http = new();
        foreach ((string key, object value) in routeValues)
            http.Request.RouteValues[key] = value;
        return http;
    }

    private static async Task<TenantService> RunAsync(HttpContext http)
    {
        // The service under test is the SAME instance the detached delegate would later hold.
        TenantService svc = new(
            new HttpContextAccessor(),                     // deliberately NOT pointed at http
            new ConfigurationBuilder().Build(),
            new SqlServerTenantConnectionStringBuilder());

        TenantResolutionMiddleware middleware = new(_ => Task.CompletedTask);
        await middleware.InvokeAsync(http, svc);
        return svc;
    }

    [TestMethod]
    public async Task InvokeAsync_StampsTenantId_FromTenantIdRouteValue()
    {
        TenantService svc = await RunAsync(ContextWithRoute(("tenantId", "75")));

        // Accessor has no HttpContext here, mirroring a detached HybridCache factory: resolution must
        // still return 75 rather than falling through to empty.
        Assert.AreEqual("75", svc.GetTenantId());
    }

    [TestMethod]
    public async Task InvokeAsync_IgnoresTheRetiredCompanyIdRouteValue()
    {
        // `tenantId` is the only spelling every service emits. A route still supplying the retired
        // `companyId` must resolve NOTHING rather than quietly scoping the request — a silent fallback
        // would let a stale route template keep working and hide the regression.
        TenantService svc = await RunAsync(ContextWithRoute(("companyId", "82")));
        Assert.AreEqual(string.Empty, svc.GetTenantId());
    }

    [TestMethod]
    public async Task InvokeAsync_UsesTenantId_AndIgnoresAnUnrelatedRouteValue()
    {
        TenantService svc = await RunAsync(ContextWithRoute(("companyId", "82"), ("tenantId", "75")));
        Assert.AreEqual("75", svc.GetTenantId());
    }

    [TestMethod]
    public async Task InvokeAsync_StampsNothing_WhenNoTenantRouteValue()
    {
        // Health probes and webhooks carry no tenant segment; the service must be left untouched.
        TenantService svc = await RunAsync(ContextWithRoute());
        Assert.AreEqual(string.Empty, svc.GetTenantId());
    }

    [TestMethod]
    public async Task InvokeAsync_StampsNothing_WhenRouteValueIsEmpty()
    {
        // An unset Postman-style variable can produce an empty segment; stamping "" would be worse than
        // leaving the explicit value alone, because it silently pins the scope to tenant 0.
        TenantService svc = await RunAsync(ContextWithRoute(("tenantId", string.Empty)));
        Assert.AreEqual(string.Empty, svc.GetTenantId());
    }

    [TestMethod]
    public async Task InvokeAsync_CoercesNonStringRouteValue()
    {
        // TenantService.ResolveTenantId ignores a non-string route value; the middleware does not, so
        // priming still works if a caller populates RouteValues with a boxed int.
        TenantService svc = await RunAsync(ContextWithRoute(("tenantId", 75)));
        Assert.AreEqual("75", svc.GetTenantId());
    }

    [TestMethod]
    public async Task InvokeAsync_FallsBackToQueryString_WhenRouteHasNoTenant()
    {
        // Endpoints keyed on another aggregate carry the tenant as a query parameter, e.g. a
        // POST /User/{userId}/Lock?tenantId=75. Those reads still touch tenant-scoped tables.
        DefaultHttpContext http = ContextWithRoute(("userId", "9"));
        http.Request.QueryString = new QueryString("?tenantId=75&actorUserId=9");

        TenantService svc = await RunAsync(http);

        Assert.AreEqual("75", svc.GetTenantId());
    }

    [TestMethod]
    public async Task InvokeAsync_IgnoresTheRetiredCompanyIdQueryString()
    {
        // Same rule on the query-string path: only `tenantId` is read.
        DefaultHttpContext http = ContextWithRoute(("userId", "9"));
        http.Request.QueryString = new QueryString("?companyId=82");

        TenantService svc = await RunAsync(http);

        Assert.AreEqual(string.Empty, svc.GetTenantId());
    }

    [TestMethod]
    public async Task InvokeAsync_PrefersRouteOverQueryString()
    {
        // The route segment is the canonical source; a query value must never override it.
        DefaultHttpContext http = ContextWithRoute(("tenantId", "75"));
        http.Request.QueryString = new QueryString("?tenantId=999");

        TenantService svc = await RunAsync(http);

        Assert.AreEqual("75", svc.GetTenantId());
    }

    [TestMethod]
    public async Task InvokeAsync_IgnoresBlankQueryStringValue()
    {
        DefaultHttpContext http = ContextWithRoute(("userId", "9"));
        http.Request.QueryString = new QueryString("?tenantId=");

        TenantService svc = await RunAsync(http);

        Assert.AreEqual(string.Empty, svc.GetTenantId());
    }

    [TestMethod]
    public async Task InvokeAsync_TakesFirstValue_WhenQueryParameterRepeated()
    {
        DefaultHttpContext http = ContextWithRoute(("userId", "9"));
        http.Request.QueryString = new QueryString("?tenantId=75&tenantId=999");

        TenantService svc = await RunAsync(http);

        Assert.AreEqual("75", svc.GetTenantId());
    }

    [TestMethod]
    public async Task InvokeAsync_CallsNext()
    {
        bool called = false;
        TenantService svc = new(
            new HttpContextAccessor(),
            new ConfigurationBuilder().Build(),
            new SqlServerTenantConnectionStringBuilder());
        TenantResolutionMiddleware middleware = new(_ => { called = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(ContextWithRoute(("tenantId", "75")), svc);

        Assert.IsTrue(called);
    }
}
