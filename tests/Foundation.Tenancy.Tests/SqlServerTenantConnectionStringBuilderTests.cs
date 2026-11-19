using Foundation.Tenancy;
using Microsoft.Data.SqlClient;

namespace Foundation.Tenancy.Tests;

[TestClass]
public class SqlServerTenantConnectionStringBuilderTests
{
    private const string Template = "Server=sql;Database=LIVE{tenantId}CRM;User Id=sa;Password=x;";

    [TestMethod]
    public void ProviderName_IsSqlServer()
    {
        SqlServerTenantConnectionStringBuilder builder = new();
        Assert.AreEqual("SqlServer", builder.ProviderName);
    }

    [TestMethod]
    public void Build_SubstitutesTenantIdIntoInitialCatalog()
    {
        SqlServerTenantConnectionStringBuilder builder = new();
        string conn = builder.Build(Template, "75", 50, 0);

        SqlConnectionStringBuilder parsed = new(conn);
        Assert.AreEqual("LIVE75CRM", parsed.InitialCatalog);
    }

    [TestMethod]
    public void Build_LeavesTheRetiredCompanyIdTokenUnsubstituted()
    {
        // {tenantId} is the only recognised placeholder. A connection-string template still spelling
        // the retired {companyId} is a configuration defect and must NOT be silently substituted —
        // it fails loudly at connect time against a literal database name instead.
        SqlServerTenantConnectionStringBuilder builder = new();
        string conn = builder.Build("Server=sql;Database=LIVE{companyId}CRM;User Id=sa;Password=x;", "75", 50, 0);

        SqlConnectionStringBuilder parsed = new(conn);
        Assert.AreEqual("LIVE{companyId}CRM", parsed.InitialCatalog);
    }

    [TestMethod]
    public void Build_AppliesPoolSizes()
    {
        SqlServerTenantConnectionStringBuilder builder = new();
        string conn = builder.Build(Template, "1", 20, 2);

        SqlConnectionStringBuilder parsed = new(conn);
        Assert.AreEqual(20, parsed.MaxPoolSize);
        Assert.AreEqual(2, parsed.MinPoolSize);
    }

    [TestMethod]
    public void BuildShared_LeavesCatalogUntouched_AndAppliesPoolSizes()
    {
        const string shared = "Server=sql;Database=Shared;User Id=sa;Password=x;";
        SqlServerTenantConnectionStringBuilder builder = new();

        string conn = builder.BuildShared(shared, 30, 3);

        SqlConnectionStringBuilder parsed = new(conn);
        Assert.AreEqual("Shared", parsed.InitialCatalog);
        Assert.AreEqual(30, parsed.MaxPoolSize);
        Assert.AreEqual(3, parsed.MinPoolSize);
    }
}
