using Foundation.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Foundation.Tenancy.Tests;

[TestClass]
public class TenantServiceTests
{
    private static IConfiguration BuildConfig(string? template = null, string? key = null)
    {
        Dictionary<string, string?> values = [];
        if (template is not null)
            values[$"ConnectionStrings:{key ?? "Tenant"}"] = template;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [TestMethod]
    public async Task SetTenantId_ReturnsExplicitId()
    {
        TenantService svc = new(BuildConfig());
        svc.SetTenantId("42");
        Assert.AreEqual("42", await svc.GetTenantIdAsync());
    }

    [TestMethod]
    public async Task GetTenantIdAsync_ReturnsEmpty_WhenNoContextAndNoManualId()
    {
        TenantService svc = new(BuildConfig());
        Assert.AreEqual(string.Empty, await svc.GetTenantIdAsync());
    }

    [TestMethod]
    public void GetTenantId_ReturnsSyncTenantId()
    {
        TenantService svc = new(BuildConfig());
        svc.SetTenantId("99");
        Assert.AreEqual("99", svc.GetTenantId());
    }

    [TestMethod]
    public void GetTenantConnectionStringSync_ReturnsEmpty_WhenNoTemplate()
    {
        TenantService svc = new(BuildConfig());
        svc.SetTenantId("1");
        Assert.AreEqual(string.Empty, svc.GetTenantConnectionStringSync());
    }

    [TestMethod]
    public void GetTenantConnectionStringSync_SubstitutesTenantId()
    {
        string template = "Server=sql;Database=App{tenantId}Orders;User Id=sa;Password=x;";
        TenantService svc = new(BuildConfig(template));
        svc.SetTenantId("75");
        string conn = svc.GetTenantConnectionStringSync();
        StringAssert.Contains(conn, "App75Orders");
    }

    [TestMethod]
    public void GetTenantConnectionStringSync_RejectsNonIntTenantId()
    {
        string template = "Server=sql;Database=App{tenantId}Orders;User Id=sa;Password=x;";
        TenantService svc = new(BuildConfig(template));
        svc.SetTenantId("evil;Drop=1");
        Assert.AreEqual(string.Empty, svc.GetTenantConnectionStringSync());
    }

    [TestMethod]
    public void GetTenantConnectionStringSync_RejectsZeroTenantId()
    {
        string template = "Server=sql;Database=App{tenantId}Orders;User Id=sa;Password=x;";
        TenantService svc = new(BuildConfig(template));
        svc.SetTenantId("0");
        Assert.AreEqual(string.Empty, svc.GetTenantConnectionStringSync());
    }

    [TestMethod]
    public void GetTenantConnectionStringSync_RejectsNegativeTenantId()
    {
        string template = "Server=sql;Database=App{tenantId}Orders;User Id=sa;Password=x;";
        TenantService svc = new(BuildConfig(template));
        svc.SetTenantId("-1");
        Assert.AreEqual(string.Empty, svc.GetTenantConnectionStringSync());
    }

    [TestMethod]
    public void GetTenantConnectionStringSync_UsesCustomConnectionStringKey()
    {
        string template = "Server=sql;Database=App{tenantId}Billing;User Id=sa;Password=x;";
        Dictionary<string, string?> values = new()
        {
            ["ConnectionStrings:PersonTenant"] = template,
            ["Tenant:ConnectionStringKey"] = "PersonTenant"
        };
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        TenantService svc = new(config);
        svc.SetTenantId("10");
        string conn = svc.GetTenantConnectionStringSync();
        StringAssert.Contains(conn, "App10Billing");
    }

    [TestMethod]
    public void GetTenantConnectionStringSync_SetsPoolSizes()
    {
        string template = "Server=sql;Database=App{tenantId}Orders;User Id=sa;Password=x;";
        Dictionary<string, string?> values = new()
        {
            ["ConnectionStrings:Tenant"] = template,
            ["Tenant:MaxPoolSize"] = "20",
            ["Tenant:MinPoolSize"] = "2"
        };
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        TenantService svc = new(config);
        svc.SetTenantId("1");
        string conn = svc.GetTenantConnectionStringSync();
        StringAssert.Contains(conn, "Max Pool Size=20");
        StringAssert.Contains(conn, "Min Pool Size=2");
    }

    [TestMethod]
    public void SharedDatabase_ReturnsTemplate_WithoutSubstitution()
    {
        string template = "Server=sql;Database=Shared;User Id=sa;Password=x;";
        TenantService svc = new(BuildConfig(template), tenancy: new TenancyOptions { Strategy = TenancyStrategy.SharedDatabase });
        svc.SetTenantId("75");
        string conn = svc.GetTenantConnectionStringSync();
        StringAssert.Contains(conn, "Shared");
        Assert.IsFalse(conn.Contains("{tenantId}", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SharedDatabase_DoesNotRequirePositiveIntTenantId()
    {
        // No int guard in shared mode: a missing/non-int tenant id still yields the fixed connection.
        string template = "Server=sql;Database=Shared;User Id=sa;Password=x;";
        TenantService svc = new(BuildConfig(template), tenancy: new TenancyOptions { Strategy = TenancyStrategy.SharedDatabase });
        svc.SetTenantId("not-an-int");
        string conn = svc.GetTenantConnectionStringSync();
        StringAssert.Contains(conn, "Shared");
    }

    [TestMethod]
    public void SharedDatabase_AppliesPoolSizes()
    {
        Dictionary<string, string?> values = new()
        {
            ["ConnectionStrings:Tenant"] = "Server=sql;Database=Shared;User Id=sa;Password=x;",
            ["Tenant:MaxPoolSize"] = "30",
            ["Tenant:MinPoolSize"] = "3"
        };
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        TenantService svc = new(config, tenancy: new TenancyOptions { Strategy = TenancyStrategy.SharedDatabase });
        string conn = svc.GetTenantConnectionStringSync();
        StringAssert.Contains(conn, "Max Pool Size=30");
        StringAssert.Contains(conn, "Min Pool Size=3");
    }

    /// <summary>
    /// Builds an accessor whose request carries the given route values, so the route-value branch of
    /// <c>ResolveTenantId</c> can be exercised.
    /// </summary>
    private static IHttpContextAccessor AccessorWithRouteValues(params (string Key, object Value)[] routeValues)
    {
        DefaultHttpContext http = new();
        foreach ((string key, object value) in routeValues)
            http.Request.RouteValues[key] = value;
        return new HttpContextAccessor { HttpContext = http };
    }

    private static TenantService RouteScopedService(IHttpContextAccessor accessor)
        => new(accessor, BuildConfig(), new SqlServerTenantConnectionStringBuilder());

    [TestMethod]
    public void ResolveTenantId_PrefersTenantIdRouteValue()
    {
        TenantService svc = RouteScopedService(AccessorWithRouteValues(("tenantId", "75")));
        Assert.AreEqual("75", svc.GetTenantId());
    }

    [TestMethod]
    public void ResolveTenantId_IgnoresTheRetiredCompanyIdRouteValue()
    {
        // `tenantId` is the only route-parameter name read. A route still spelling the retired
        // `companyId` resolves nothing, so a database-per-tenant service falls through to an empty
        // connection string and fails loudly rather than silently serving the wrong scope.
        TenantService svc = RouteScopedService(AccessorWithRouteValues(("companyId", "82")));
        Assert.AreEqual(string.Empty, svc.GetTenantId());
    }

    [TestMethod]
    public void ResolveTenantId_UsesTenantId_AndIgnoresAnUnrelatedRouteValue()
    {
        TenantService svc = RouteScopedService(AccessorWithRouteValues(("companyId", "82"), ("tenantId", "75")));
        Assert.AreEqual("75", svc.GetTenantId());
    }

    [TestMethod]
    public void ResolveTenantId_FallsBackToExplicitId_WhenNoRouteValuePresent()
    {
        TenantService svc = RouteScopedService(AccessorWithRouteValues());
        svc.SetTenantId("42");
        Assert.AreEqual("42", svc.GetTenantId());
    }

    [TestMethod]
    public void ResolveTenantId_FallsBackToExplicitId_WhenRouteValueIsNotAString()
    {
        // Route values arrive as strings from the router; a non-string (e.g. a directly-populated int)
        // is ignored rather than coerced, matching the pre-rename behaviour.
        TenantService svc = RouteScopedService(AccessorWithRouteValues(("tenantId", 75)));
        svc.SetTenantId("42");
        Assert.AreEqual("42", svc.GetTenantId());
    }
}

[TestClass]
public class TenancyOptionsTests
{
    private static IConfiguration Config(string? strategy)
    {
        Dictionary<string, string?> values = [];
        if (strategy is not null)
            values["Tenancy:Strategy"] = strategy;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [TestMethod]
    public void FromConfiguration_DefaultsToDatabasePerTenant_WhenAbsent()
    {
        Assert.AreEqual(TenancyStrategy.DatabasePerTenant, TenancyOptions.FromConfiguration(Config(null)).Strategy);
    }

    [TestMethod]
    public void FromConfiguration_ParsesSharedDatabase_CaseInsensitive()
    {
        Assert.AreEqual(TenancyStrategy.SharedDatabase, TenancyOptions.FromConfiguration(Config("shareddatabase")).Strategy);
    }

    [TestMethod]
    public void FromConfiguration_Throws_OnUnrecognizedValue()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => TenancyOptions.FromConfiguration(Config("nonsense")));
        }
}
