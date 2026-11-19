using System.Linq.Expressions;
using Foundation.DataAccess;
using Foundation.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Foundation.Persistence;

/// <summary>
/// Safe update primitive. Loads the <b>tracked</b> entity, lets the caller mutate only its own fields,
/// and saves. It never constructs a new entity, never lets the caller change the key, ownership or
/// creation audit of the aggregate or of any child, and keeps tenant scoping and optimistic concurrency
/// in one place.
/// </summary>
public static class TrackedUpdateExtensions
{
    // Fields no update may change, captured per tracked entity before the delegate runs.
    private readonly record struct Protected(int Id, int TenantId, DateTime CreatedDate, int CreatedById)
    {
        public static Protected Of(BaseEntity e) =>
            new(e.Id, (e as ITenantScoped)?.TenantId ?? 0, e.CreatedDate, e.CreatedById);

        public bool Matches(BaseEntity e) => this == Of(e);

        public void Restore(BaseEntity e)
        {
            e.Id = Id;
            e.CreatedDate = CreatedDate;
            e.CreatedById = CreatedById;
            if (e is ITenantScoped scoped)
                scoped.TenantId = TenantId;
        }
    }

    /// <summary>
    /// Applies <paramref name="apply"/> to the tracked entity with the given id and saves.
    /// <list type="bullet">
    /// <item>Tenant: the lookup runs through the tenant query filter, and in shared-database mode a tenant-scoped row
    /// must also belong to the resolved current tenant. Another tenant's row is reported as not found and nothing is
    /// written. Entities that are not <see cref="ITenantScoped"/> (users, lookups) carry no tenant and are unaffected.</item>
    /// <item>Scope: pass <paramref name="scope"/> (<c>e =&gt; e.SiteId == siteId</c>) to restrict the lookup
    /// to the caller's sub-scope (a site, region, workspace). Foundation has no such concept of its own, so the predicate is the service's.</item>
    /// <item>Concurrency: <paramref name="expectedRowVersion"/> is the caller's If-Match token and is a required argument
    /// so omitting it is visible; pass <c>null</c> explicitly to skip the caller's If-Match check. The store still compares
    /// the version loaded here at write time, so a writer that slips in between the load and the save is reported as a
    /// conflict either way. A stale value raises
    /// <see cref="ConcurrencyConflictException"/>, on the pre-check and again at write time. On a relational provider the
    /// method throws if <c>RowVersion</c> is not mapped as a concurrency token (call <c>ApplyFoundationConcurrencyTokens</c>),
    /// because the write-time check would otherwise silently do nothing.</item>
    /// <item>Audit: <see cref="BaseEntity.UpdatedDate"/> is stamped by the context's clock on save and
    /// <see cref="BaseEntity.UpdatedById"/> is set from the required <paramref name="updatedById"/>, so the two always
    /// describe the same edit. <paramref name="audit"/> runs just before the save, after the edit and the protected-field check
    /// (who/what/when for PHI/PII entities). Add the audit row to the same context and it commits atomically with the
    /// update; if the callback or the save throws, the whole edit, including that row, is rolled back. The entity it sees
    /// carries the new values and <see cref="BaseEntity.UpdatedById"/>, but not yet the save-time
    /// <see cref="BaseEntity.UpdatedDate"/> or the advanced row version.</item>
    /// <item>Children: when <paramref name="apply"/> edits a child collection (for example with
    /// <see cref="TrackedChildren.Sync"/>), pass <paramref name="include"/> (<c>q =&gt; q.Include(p =&gt; p.Children)</c>) so the
    /// persisted children are loaded. Without it the collection is empty and every incoming child would be added as new.</item>
    /// <item>No tracked entity, the aggregate or any child already loaded, may have its <c>Id</c>, <c>TenantId</c>,
    /// <c>CreatedDate</c> or <c>CreatedById</c> changed by the delegate. A change throws
    /// <see cref="InvalidOperationException"/>; so does a delegate that throws. In both cases the whole edit is rolled back
    /// (protected fields restored, edited children reset, added children detached, removed children restored, root
    /// detached) so a later SaveChanges on the same context cannot persist any of it.</item>
    /// </list>
    /// </summary>
    public static async Task<T> UpdateTrackedAsync<T>(
        this TenantDbContext db,
        int id,
        Action<T> apply,
        byte[]? expectedRowVersion,
        int updatedById,
        Func<IQueryable<T>, IQueryable<T>>? include = null,
        Expression<Func<T, bool>>? scope = null,
        Func<T, CancellationToken, Task>? audit = null,
        CancellationToken cancellationToken = default)
        where T : BaseEntity
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(apply);

        // AsTracking is explicit: a context configured with NoTracking would otherwise hand back a detached object and
        // SaveChanges would silently write nothing.
        IQueryable<T> query = db.Set<T>().AsTracking();
        if (include is not null)
            query = include(query);
        if (scope is not null)
            query = query.Where(scope);

        T? entity = await query.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is null || !BelongsToCurrentTenant(db, entity))
            throw new ResourceNotFoundException(typeof(T).Name, id.ToString(), "not_found");

        EntityEntry<T> entry = db.Entry(entity);
        if (expectedRowVersion is not null)
        {
            if (db.Database.IsRelational() && !entry.Property(static e => e.RowVersion).Metadata.IsConcurrencyToken)
                throw new InvalidOperationException(
                    $"{typeof(T).Name}.RowVersion is not mapped as a concurrency token; call ApplyFoundationConcurrencyTokens in OnModelCreating.");

            if (entity.RowVersion is null || !entity.RowVersion.AsSpan().SequenceEqual(expectedRowVersion))
                throw new ConcurrencyConflictException(typeof(T).Name, id.ToString(), "stale_etag");
        }

        // Entities already tracked (the root, loaded children, and anything the caller touched earlier) are snapshotted;
        // entities already dirty belong to the caller's unit of work and are not rolled back.
        Dictionary<BaseEntity, Protected> snapshot = new(ReferenceEqualityComparer.Instance);
        HashSet<object> dirtyBefore = new(ReferenceEqualityComparer.Instance);
        foreach (EntityEntry tracked in db.ChangeTracker.Entries())
        {
            if (tracked.Entity is BaseEntity b)
                snapshot[b] = Protected.Of(b);
            if (tracked.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                dirtyBefore.Add(tracked.Entity);
        }

        // Undo a rejected or failed edit completely so nothing of it can be saved by a later SaveChanges on this
        // context; a retry reloads from the store.
        void Reject()
        {
            foreach (KeyValuePair<BaseEntity, Protected> kv in snapshot)
                kv.Value.Restore(kv.Key);

            foreach (EntityEntry other in db.ChangeTracker.Entries().ToList())
            {
                if (ReferenceEquals(other.Entity, entity) || dirtyBefore.Contains(other.Entity))
                    continue;

                switch (other.State)
                {
                    case EntityState.Added:
                        other.State = EntityState.Detached;
                        break;
                    case EntityState.Modified:
                        other.CurrentValues.SetValues(other.OriginalValues);
                        other.State = EntityState.Unchanged;
                        break;
                    case EntityState.Deleted:
                        other.State = EntityState.Unchanged;
                        break;
                }
            }

            db.Entry(entity).State = EntityState.Detached;
        }

        try
        {
            apply(entity);
        }
        catch
        {
            Reject();
            throw;
        }

        if (snapshot.Any(static kv => !kv.Value.Matches(kv.Key)))
        {
            Reject();
            throw new InvalidOperationException(
                $"{typeof(T).Name} update changed a protected field (Id, TenantId, CreatedDate or CreatedById) on the aggregate or one of its children.");
        }

        entity.UpdatedById = updatedById;

        // Touch the aggregate root even when apply only edited children (or nothing), so the UPDATE runs, the
        // store re-checks the row version and advances it, and UpdatedDate is stamped by the context.
        entry.Property(static e => e.UpdatedDate).IsModified = true;

        if (expectedRowVersion is not null)
        {
            // Make the store re-check the token at write time so a concurrent writer is detected too.
            entry.Property(static e => e.RowVersion).OriginalValue = expectedRowVersion;
        }

        try
        {
            // The audit callback runs before the save so any audit row it adds to this context commits in the same
            // SaveChanges as the update: the PHI/PII change and its audit entry are both durable or neither is.
            if (audit is not null)
                await audit(entity, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            RejectQuietly();
            throw new ConcurrencyConflictException(typeof(T).Name, id.ToString(), "stale_etag");
        }
        catch (Exception)
        {
            // Any failure (provider error, retry limit, cancellation, a cross-tenant child) must leave nothing half-edited
            // on a context that outlives this call.
            RejectQuietly();
            throw;
        }

        return entity;

        // A failure while undoing must never replace the original exception.
        void RejectQuietly()
        {
            try
            {
                Reject();
            }
            catch (Exception)
            {
                // The original exception is the one the caller needs.
            }
        }
    }

    // Only ITenantScoped rows carry a tenant. Global rows (users, lookups, registry) stay reachable without one, the
    // same as they are for queries; a tenant-scoped row needs a resolved tenant that owns it.
    private static bool BelongsToCurrentTenant(TenantDbContext db, BaseEntity entity) =>
        !db.IsSharedTenancy
        || entity is not ITenantScoped scoped
        || (db.CurrentTenantId > 0 && scoped.TenantId == db.CurrentTenantId);
}
