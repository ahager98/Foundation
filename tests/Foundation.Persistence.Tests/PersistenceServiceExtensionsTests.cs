using Foundation.Persistence;
using Foundation.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Foundation.Persistence.Tests;

public class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options);

[TestClass]
public class PersistenceServiceExtensionsTests
{
    private static ServiceProvider BuildProvider(string? provider)
    {
        Dictionary<string, string?> values = new()
        {
            ["ConnectionStrings:Tenant"] = "Host=db;Database=LIVE{tenantId}CRM;Username=app;Password=x"
        };
        if (provider is not null)
            values["Database:Provider"] = provider;

        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        ServiceCollection services = new();
        services.AddSingleton(config);
        services.AddFoundationPersistence<SampleDbContext>(config);
        return services.BuildServiceProvider();
    }

    [TestMethod]
    public void Postgres_RegistersNpgsqlProviderAndBuilder()
    {
        using ServiceProvider sp = BuildProvider("Postgres");
        using IServiceScope scope = sp.CreateScope();

        ITenantConnectionStringBuilder builder =
            scope.ServiceProvider.GetRequiredService<ITenantConnectionStringBuilder>();
        Assert.AreEqual("Postgres", builder.ProviderName);

        SampleDbContext ctx = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
        Assert.AreEqual("Npgsql.EntityFrameworkCore.PostgreSQL", ctx.Database.ProviderName);
    }

    [TestMethod]
    public void SqlServer_RegistersSqlServerProviderAndBuilder()
    {
        using ServiceProvider sp = BuildProvider("SqlServer");
        using IServiceScope scope = sp.CreateScope();

        ITenantConnectionStringBuilder builder =
            scope.ServiceProvider.GetRequiredService<ITenantConnectionStringBuilder>();
        Assert.AreEqual("SqlServer", builder.ProviderName);

        SampleDbContext ctx = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
        Assert.AreEqual("Microsoft.EntityFrameworkCore.SqlServer", ctx.Database.ProviderName);
    }

    [TestMethod]
    public void DefaultsToSqlServer_WhenProviderUnset()
    {
        using ServiceProvider sp = BuildProvider(null);
        using IServiceScope scope = sp.CreateScope();

        SampleDbContext ctx = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
        Assert.AreEqual("Microsoft.EntityFrameworkCore.SqlServer", ctx.Database.ProviderName);
    }

    [TestMethod]
    public void UnknownProvider_Throws()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "Oracle" })
            .Build();
        ServiceCollection services = new();
        services.AddSingleton(config);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => services.AddFoundationPersistence<SampleDbContext>(config));
    }
}
