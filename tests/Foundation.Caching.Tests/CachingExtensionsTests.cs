using Foundation.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Hybrid;

namespace Foundation.Caching.Tests;

[TestClass]
public class CachingExtensionsTests
{
    [TestMethod]
    public void AddFoundationCaching_RegistersHybridCache_WithDefaultTtl()
    {
        IConfiguration config = new ConfigurationBuilder().Build();
        ServiceCollection services = new();
        services.AddFoundationCaching(config);
        ServiceProvider sp = services.BuildServiceProvider();
        HybridCache cache = sp.GetRequiredService<HybridCache>();
        Assert.IsNotNull(cache);
    }

    [TestMethod]
    public void AddFoundationCaching_RegistersHybridCache_WithCustomTtl()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Caching:LookupTtlMinutes"] = "60" })
            .Build();
        ServiceCollection services = new();
        services.AddFoundationCaching(config);
        ServiceProvider sp = services.BuildServiceProvider();
        HybridCache cache = sp.GetRequiredService<HybridCache>();
        Assert.IsNotNull(cache);
    }
}
