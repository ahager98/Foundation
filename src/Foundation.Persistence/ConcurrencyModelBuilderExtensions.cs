using Foundation.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundation.Persistence;

/// <summary>
/// Provider-aware optimistic-concurrency mapping for the <c>byte[] RowVersion</c> convention,
/// so a service can switch <c>Database:Provider</c> between SQL Server and PostgreSQL without
/// changing its model code.
/// <para>
/// The <c>byte[] RowVersion</c> column is SQL-Server-shaped (it maps to a <c>rowversion</c> column).
/// PostgreSQL has no <c>rowversion</c> type, so on PostgreSQL the same <c>RowVersion</c> property is
/// mapped onto the always-present system <c>xmin</c> column through a <c>byte[]</c>&#8596;<c>uint</c>
/// converter. Mapping it as a real CLR property (rather than an EF shadow property) is what keeps
/// <c>RowVersion</c> readable and writable by callers, so provider-agnostic features built on it —
/// HTTP <c>ETag</c> / <c>If-Match</c>, and <c>GenericCrud</c>'s caller-supplied-token check, which
/// reads the token by reflection and therefore needs a real property — behave identically on both
/// providers. On non-relational providers (the in-memory test provider) no concurrency token is
/// mapped.
/// </para>
/// </summary>
public static class ConcurrencyModelBuilderExtensions
{
    /// <summary>Conventional concurrency-token property name shared by <c>BaseEntity</c> types.</summary>
    private const string RowVersionPropertyName = nameof(BaseEntity.RowVersion);

    /// <summary>Name of PostgreSQL's always-present system row-version column.</summary>
    private const string XminColumnName = "xmin";

    /// <summary>PostgreSQL store type of the <c>xmin</c> system column (a transaction id).</summary>
    private const string XminStoreType = "xid";

    /// <summary>
    /// Translates between the <c>byte[]</c> token convention and the 4-byte unsigned
    /// transaction id PostgreSQL keeps in <c>xmin</c>. Byte order is fixed little-endian in both
    /// directions (rather than delegating to <see cref="BitConverter"/>, which follows the host) so a
    /// token round-trips to the same bytes regardless of the machine that produced it.
    /// </summary>
    private static readonly ValueConverter<byte[], uint> XminConverter = new(
        token => ToXmin(token),
        xmin => FromXmin(xmin));

    /// <summary>
    /// Maps the concurrency token for every entity that follows the <c>byte[] RowVersion</c>
    /// convention, choosing the strategy that fits the active provider. Detection is by the
    /// <c>RowVersion</c> property (name + <see cref="byte"/>[] type) rather than a specific base type,
    /// so it works regardless of which <c>BaseEntity</c> a service uses.
    /// </summary>
    public static ModelBuilder ApplyFoundationConcurrencyTokens(this ModelBuilder modelBuilder, DatabaseFacade database)
    {
        // Non-relational providers (e.g. the EF in-memory test provider) don't support concurrency tokens.
        if (!database.IsRelational())
            return modelBuilder;

        bool postgres = database.IsNpgsql();
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!HasRowVersionConvention(entityType.ClrType))
                continue;

            EntityTypeBuilder entity = modelBuilder.Entity(entityType.ClrType);
            if (postgres)
            {
                // No rowversion type on PostgreSQL: point the byte[] property at the system xmin
                // column (always present, excluded from CREATE TABLE because Npgsql skips system
                // columns) and convert the value. The store type is set explicitly because the xid
                // mapping is inferred from the *property* name "xmin", which no longer applies here.
                entity.Property(RowVersionPropertyName)
                    .HasColumnName(XminColumnName)
                    .HasColumnType(XminStoreType)
                    .HasConversion(XminConverter)
                    .IsRowVersion();
            }
            else
            {
                // SQL Server: map RowVersion to a rowversion/timestamp column.
                entity.Property(RowVersionPropertyName).IsRowVersion();
            }
        }

        return modelBuilder;
    }

    private static bool HasRowVersionConvention(Type clrType)
    {
        System.Reflection.PropertyInfo? property = clrType.GetProperty(RowVersionPropertyName);
        return property is not null && property.PropertyType == typeof(byte[]);
    }

    /// <summary>
    /// Reads a token as a PostgreSQL transaction id. A token of any other length is tolerated rather
    /// than rejected — it arrives from an HTTP <c>If-Match</c> header, so a hand-crafted or truncated
    /// value must fail the concurrency check (409) instead of throwing on the way to the database.
    /// </summary>
    private static uint ToXmin(byte[]? token)
    {
        if (token is null)
            return 0u;

        uint xmin = 0u;
        int length = Math.Min(token.Length, sizeof(uint));
        for (int i = 0; i < length; i++)
            xmin |= (uint)token[i] << (8 * i);

        return xmin;
    }

    /// <summary>Writes a PostgreSQL transaction id as a 4-byte little-endian token.</summary>
    private static byte[] FromXmin(uint xmin) =>
        [(byte)xmin, (byte)(xmin >> 8), (byte)(xmin >> 16), (byte)(xmin >> 24)];
}
