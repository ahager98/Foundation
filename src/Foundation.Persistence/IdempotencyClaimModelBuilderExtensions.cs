using Microsoft.EntityFrameworkCore;

namespace Foundation.Persistence;

/// <summary>Maps the idempotency claim table into a service's EF model.</summary>
public static class IdempotencyClaimModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="IdempotencyClaim"/> with a unique index on the claim identity digest. The unique index is what makes a claim
    /// atomic across service instances. The service generates the migration (migrations live in each service).
    /// </summary>
    public static ModelBuilder ApplyFoundationIdempotencyClaims(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyClaim>(e =>
        {
            e.ToTable("IdempotencyClaims");
            e.Property(c => c.Scope).HasMaxLength(200).IsRequired();
            e.Property(c => c.Key).HasMaxLength(300).IsRequired();
            e.Property(c => c.Identity).HasMaxLength(64).IsRequired().IsFixedLength();
            e.HasIndex(c => c.Identity).IsUnique();
            e.HasIndex(c => c.ExpiresUtc);
        });
        return modelBuilder;
    }
}
