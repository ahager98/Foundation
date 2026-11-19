using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Foundation.DataAccess;

[ExcludeFromCodeCoverage]
public class BaseEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; }
    public DateTime UpdatedDate { get; set; }
    public int CreatedById { get; set; }
    public int UpdatedById { get; set; }

    /// <summary>
    /// Optimistic-concurrency token. Configured as a SQL <c>rowversion</c> (store-generated,
    /// concurrency token) for relational providers in each service's DbContext.OnModelCreating;
    /// left as an ordinary property under the InMemory test provider. Surfaced to clients as an
    /// ETag so updates can be guarded with <c>If-Match</c>.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// Set to <c>true</c> when this record was deactivated as a side-effect of its parent being
    /// deactivated (cascade). When the parent is later reactivated, only children with this flag
    /// set are restored — children that were independently deactivated remain inactive.
    /// </summary>
    public bool InactivatedByParent { get; set; }
}
