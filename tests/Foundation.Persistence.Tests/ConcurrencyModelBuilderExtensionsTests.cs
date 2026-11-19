using Foundation.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundation.Persistence.Tests;

public class ConcurrencyEntity : BaseEntity
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>Applies the provider-aware concurrency mapping; provider is chosen by the options.</summary>
public class ConcurrencyTestDbContext(DbContextOptions<ConcurrencyTestDbContext> options) : DbContext(options)
{
    public DbSet<ConcurrencyEntity> Entities => Set<ConcurrencyEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyFoundationConcurrencyTokens(Database);
    }
}

[TestClass]
public class ConcurrencyModelBuilderExtensionsTests
{
    // Model building runs OnModelCreating offline — no live database connection is opened — so a
    // throwaway connection string is enough to exercise each provider's mapping.
    private static IEntityType BuildModel(Action<DbContextOptionsBuilder<ConcurrencyTestDbContext>> configure)
    {
        DbContextOptionsBuilder<ConcurrencyTestDbContext> builder = new();
        configure(builder);
        using ConcurrencyTestDbContext ctx = new(builder.Options);
        return ctx.Model.FindEntityType(typeof(ConcurrencyEntity))!;
    }

    private static ConcurrencyTestDbContext NewPostgresContext()
    {
        DbContextOptionsBuilder<ConcurrencyTestDbContext> builder = new();
        builder.UseNpgsql("Host=x;Database=y;Username=u;Password=p");
        return new ConcurrencyTestDbContext(builder.Options);
    }

    [TestMethod]
    public void SqlServer_MapsRowVersionAsConcurrencyToken()
    {
        IEntityType et = BuildModel(b => b.UseSqlServer("Server=x;Database=y;User Id=sa;Password=p;"));

        IProperty rowVersion = et.FindProperty(nameof(BaseEntity.RowVersion))!;
        Assert.IsTrue(rowVersion.IsConcurrencyToken);
        Assert.AreEqual(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
        Assert.IsNull(et.FindProperty("xmin"), "SQL Server must not introduce an xmin token.");
    }

    [TestMethod]
    public void Postgres_MapsRowVersionOntoXminSystemColumn()
    {
        IEntityType et = BuildModel(b => b.UseNpgsql("Host=x;Database=y;Username=u;Password=p"));

        // The CLR property must survive: callers (ETag/If-Match, GenericCrud's reflection-based
        // caller-supplied-token check) read and write BaseEntity.RowVersion directly, so a shadow
        // property would silently disable optimistic concurrency on PostgreSQL.
        IProperty rowVersion = et.FindProperty(nameof(BaseEntity.RowVersion))!;
        Assert.IsNotNull(rowVersion.PropertyInfo,
            "RowVersion must stay a real CLR property, not an EF shadow property.");
        Assert.IsTrue(rowVersion.IsConcurrencyToken);
        Assert.AreEqual(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
        Assert.AreEqual("xmin", rowVersion.GetColumnName());
        Assert.AreEqual("xid", rowVersion.GetColumnType());
        Assert.IsNull(et.FindProperty("xmin"),
            "The token is the mapped RowVersion property; no separate xmin property should exist.");
    }

    [TestMethod]
    public void Postgres_RowVersionConverter_RoundTripsTransactionId()
    {
        IEntityType et = BuildModel(b => b.UseNpgsql("Host=x;Database=y;Username=u;Password=p"));
        ValueConverter converter = et.FindProperty(nameof(BaseEntity.RowVersion))!.GetValueConverter()!;

        Assert.AreEqual(typeof(uint), converter.ProviderClrType);

        // A transaction id read from xmin becomes a byte[] token, and that token converts back to
        // the same id — otherwise an If-Match round-trip would compare against the wrong version.
        object? token = converter.ConvertToProvider(converter.ConvertFromProvider(123456789u));
        Assert.AreEqual(123456789u, token);
        Assert.AreEqual(uint.MaxValue,
            converter.ConvertToProvider(converter.ConvertFromProvider(uint.MaxValue)));
    }

    [TestMethod]
    public void Postgres_RowVersionConverter_ToleratesMalformedClientToken()
    {
        IEntityType et = BuildModel(b => b.UseNpgsql("Host=x;Database=y;Username=u;Password=p"));
        ValueConverter converter = et.FindProperty(nameof(BaseEntity.RowVersion))!.GetValueConverter()!;

        // Tokens arrive from an HTTP If-Match header, so a truncated or over-long value must convert
        // to *some* transaction id (which then fails the concurrency check → 409) rather than throw.
        Assert.AreEqual(0u, converter.ConvertToProvider(Array.Empty<byte>()));
        Assert.AreEqual(1u, converter.ConvertToProvider(new byte[] { 1 }));
        Assert.AreEqual(1u, converter.ConvertToProvider(new byte[] { 1, 0, 0, 0, 9, 9, 9, 9 }));
    }

    [TestMethod]
    public void Postgres_DoesNotCreateXminColumn()
    {
        // xmin is a PostgreSQL system column: emitting it in CREATE TABLE would fail with
        // "column name xmin conflicts with a system column name". Npgsql skips system columns, and
        // this asserts the mapped-property form still gets that treatment.
        using ConcurrencyTestDbContext ctx = NewPostgresContext();

        string script = ctx.Database.GenerateCreateScript();

        StringAssert.Contains(script, "CREATE TABLE", "Sanity: the script must actually create the table.");
        StringAssert.Contains(script, "Name", "Sanity: ordinary columns must still be created.");
        Assert.IsFalse(script.Contains("xmin", StringComparison.OrdinalIgnoreCase),
            $"CREATE TABLE must not include the xmin system column. Script:\n{script}");
        Assert.IsFalse(script.Contains("RowVersion", StringComparison.OrdinalIgnoreCase),
            $"RowVersion must not be created as its own column. Script:\n{script}");
    }

    [TestMethod]
    public void InMemory_MapsNoConcurrencyToken()
    {
        IEntityType et = BuildModel(b => b.UseInMemoryDatabase(nameof(InMemory_MapsNoConcurrencyToken)));

        // Non-relational: the helper is a no-op, so RowVersion stays an ordinary (non-token) property.
        IProperty? rowVersion = et.FindProperty(nameof(BaseEntity.RowVersion));
        Assert.IsNotNull(rowVersion);
        Assert.IsFalse(rowVersion.IsConcurrencyToken);
        Assert.IsNull(et.FindProperty("xmin"));
    }
}
