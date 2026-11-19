using Foundation.Persistence;
using Foundation.Tenancy;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Foundation.Persistence.Tests;

[TestClass]
public class PostgresTenantConnectionStringBuilderTests
{
    private const string Template = "Host=pg;Database=LIVE{tenantId}CRM;Username=app;Password=x";

    [TestMethod]
    public void ProviderName_IsPostgres()
    {
        PostgresTenantConnectionStringBuilder builder = new();
        Assert.AreEqual("Postgres", builder.ProviderName);
    }

    [TestMethod]
    public void Build_SubstitutesTenantIdIntoDatabase()
    {
        PostgresTenantConnectionStringBuilder builder = new();
        string conn = builder.Build(Template, "75", 50, 0);

        NpgsqlConnectionStringBuilder parsed = new(conn);
        Assert.AreEqual("LIVE75CRM", parsed.Database);
    }

    [TestMethod]
    public void Build_LeavesTheRetiredCompanyIdTokenUnsubstituted()
    {
        // {tenantId} is the only recognised placeholder. A connection-string template still spelling
        // the retired {companyId} is a configuration defect and must NOT be silently substituted —
        // it fails loudly at connect time against a literal database name instead.
        PostgresTenantConnectionStringBuilder builder = new();
        string conn = builder.Build("Host=pg;Database=LIVE{companyId}CRM;Username=app;Password=x", "75", 50, 0);

        NpgsqlConnectionStringBuilder parsed = new(conn);
        Assert.AreEqual("LIVE{companyId}CRM", parsed.Database);
    }

    [TestMethod]
    public void Build_AppliesPoolSizes()
    {
        PostgresTenantConnectionStringBuilder builder = new();
        string conn = builder.Build(Template, "1", 20, 2);

        NpgsqlConnectionStringBuilder parsed = new(conn);
        Assert.AreEqual(20, parsed.MaxPoolSize);
        Assert.AreEqual(2, parsed.MinPoolSize);
    }

    [TestMethod]
    public void Build_IsUsableViaTenantService()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Tenant"] = Template
            })
            .Build();

        TenantService svc = new(config, new PostgresTenantConnectionStringBuilder());
        svc.SetTenantId("42");

        string conn = svc.GetTenantConnectionStringSync();
        NpgsqlConnectionStringBuilder parsed = new(conn);
        Assert.AreEqual("LIVE42CRM", parsed.Database);
    }

    [TestMethod]
    public void BuildShared_LeavesDatabaseUntouched_AndAppliesPoolSizes()
    {
        const string shared = "Host=pg;Database=Shared;Username=app;Password=x";
        PostgresTenantConnectionStringBuilder builder = new();

        string conn = builder.BuildShared(shared, 30, 3);

        NpgsqlConnectionStringBuilder parsed = new(conn);
        Assert.AreEqual("Shared", parsed.Database);
        Assert.AreEqual(30, parsed.MaxPoolSize);
        Assert.AreEqual(3, parsed.MinPoolSize);
    }

    [TestMethod]
    public void SharedDatabase_ViaTenantService_UsesTemplateVerbatim()
    {
        // The full matrix corner: SharedDatabase strategy + Postgres provider. No {tenantId}
        // substitution and no positive-int guard — the fixed shared database is used as-is.
        const string shared = "Host=pg;Database=Shared;Username=app;Password=x";
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Tenant"] = shared })
            .Build();

        TenantService svc = new(
            config,
            new PostgresTenantConnectionStringBuilder(),
            new TenancyOptions { Strategy = TenancyStrategy.SharedDatabase });
        svc.SetTenantId("not-an-int"); // tolerated in shared mode

        NpgsqlConnectionStringBuilder parsed = new(svc.GetTenantConnectionStringSync());
        Assert.AreEqual("Shared", parsed.Database);
    }
}
