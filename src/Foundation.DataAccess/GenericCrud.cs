using Foundation.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Foundation.DataAccess;

/// <summary>
/// Generic EF Core CRUD base shared across all DAL projects.
/// <typeparamref name="TContext"/> is the service's own DbContext — pass it via the constructor.
/// </summary>
public class GenericCrud<TEntity, TContext>(TContext context, ILogger logger) : IGenericCrud<TEntity>
    where TEntity : class
    where TContext : DbContext
{
    protected TContext Context => context;

    /// <summary>
    /// Locates one entity by primary key, reaching rows a soft-delete global query filter would hide.
    /// <para>
    /// <c>FindAsync</c> alone is not enough. It is widely — and wrongly — assumed to bypass global query
    /// filters; it only does so while the entity is already tracked. Once it has to query the database the
    /// filters apply, so a soft-deleted row came back null and every load-mutate-save reactivate/undelete
    /// flow failed with <see cref="ResourceNotFoundException"/> on a relational provider. Nothing caught
    /// it because services register the soft-delete filter only when <c>Database.IsRelational()</c>, and
    /// their test suites run on the InMemory provider.
    /// </para>
    /// <para>
    /// <c>FindAsync</c> is still tried first: it serves a tracked entity without a round-trip and is the
    /// common case. Only on a miss does this re-query with the soft-delete filter suppressed.
    /// </para>
    /// <para>
    /// The suppression is BY NAME and deliberately never uses the argument-less
    /// <c>IgnoreQueryFilters()</c>, which would also drop
    /// <see cref="QueryFilterNames.Tenant"/> and let a by-id write reach another tenant's row under
    /// shared-database tenancy. Every named filter on the entity except the tenant one is dropped, so a
    /// service keeps working whatever it calls its soft-delete filter. A filter left ANONYMOUS cannot be
    /// dropped individually (EF Core has no name to target), so an entity whose soft-delete filter is
    /// anonymous still will not be reachable here — register it as a named filter, or use a set-based
    /// update, for those.
    /// </para>
    /// </summary>
    private async Task<TEntity?> FindByKeyIncludingFilteredAsync(int id, CancellationToken ct)
    {
        TEntity? tracked = await context.Set<TEntity>().FindAsync([id], ct);
        if (tracked is not null)
            return tracked;

        IEntityType? entityType = context.Model.FindEntityType(typeof(TEntity));
        if (entityType is null)
            return null;

        // Single int primary key only — a composite or non-int key can't be expressed by this shortcut,
        // and FindAsync above has already covered the tracked case for those.
        IReadOnlyList<IProperty>? keyProperties = entityType.FindPrimaryKey()?.Properties;
        if (keyProperties is not { Count: 1 } || keyProperties[0].ClrType != typeof(int))
            return null;

        string[] suppressible = entityType.GetDeclaredQueryFilters()
            .Where(f => !f.IsAnonymous && f.Key is not null && f.Key != QueryFilterNames.Tenant)
            .Select(f => f.Key!)
            .ToArray();

        if (suppressible.Length == 0)
            return null;

        string keyName = keyProperties[0].Name;
        return await context.Set<TEntity>()
            .IgnoreQueryFilters(suppressible)
            .FirstOrDefaultAsync(e => EF.Property<int>(e, keyName) == id, ct);
    }

    public async Task<TEntity> CreateEntityAsync(TEntity entity, IEnumerable<ParentReference>? parents = null, CancellationToken ct = default)
    {
        try
        {
            Type entityType = typeof(TEntity);

            if (parents is not null)
            {
                foreach (ParentReference parent in parents)
                {
                    object? rEntity = await context.FindAsync(parent.ParentType, [parent.Id], ct);
                    if (rEntity is null)
                        throw new ResourceNotFoundException(
                            $"The entity of {parent.NavigationProperty} with an Id of {parent.Id} was not found");

                    PropertyInfo? navProp = entityType.GetProperty(parent.NavigationProperty);
                    navProp?.SetValue(entity, rEntity);

                    if (context.Entry(rEntity).State == EntityState.Detached)
                        context.Attach(rEntity);

                    PropertyInfo? collection = parent.ParentType
                        .GetProperties()
                        .FirstOrDefault(p =>
                            p.PropertyType.IsGenericType &&
                            p.PropertyType.GetGenericTypeDefinition() == typeof(ICollection<>) &&
                            p.PropertyType.GetGenericArguments()[0] == typeof(TEntity));

                    if (collection?.GetValue(rEntity) is System.Collections.IList list)
                        list.Add(entity);
                }
            }

            await context.Set<TEntity>().AddAsync(entity, ct);
            await context.SaveChangesAsync(ct);
        }
        catch (NullReferenceException nre)
        {
            logger.LogError(nre.Message);
            throw;
        }
        catch (DbUpdateException e)
        {
            logger.LogError(e, "A database exception at {location} for object {object}",
                Assembly.GetExecutingAssembly(), typeof(TEntity));
            throw;
        }
        catch (Exception e) when (e is not ResourceNotFoundException)
        {
            logger.LogError(e, "A system error has occurred at {location} for object {object}",
                Assembly.GetExecutingAssembly(), typeof(TEntity));
            throw;
        }

        return entity;
    }

    public async Task<TEntity> GetEntityByIdDetachedAsync(int id, CancellationToken ct = default)
    {
        try
        {
            TEntity? entity = await FindByKeyIncludingFilteredAsync(id, ct);
            if (entity is not null)
            {
                context.Entry(entity).State = EntityState.Detached;
                return entity;
            }
            throw new ResourceNotFoundException($"The entity of {typeof(TEntity)} with an Id of {id} was not found");
        }
        catch (NullReferenceException nre)
        {
            logger.LogError(nre.Message);
            throw;
        }
        catch (Exception e) when (e is not ResourceNotFoundException)
        {
            logger.LogError(e, "A system error has occurred at {location} for object {object}",
                Assembly.GetExecutingAssembly(), typeof(TEntity));
            throw;
        }
    }

    public async Task<TEntity> GetEntityByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            TEntity? entity = await FindByKeyIncludingFilteredAsync(id, ct);
            if (entity is not null)
                return entity;
            throw new ResourceNotFoundException($"The entity of {typeof(TEntity)} with an Id of {id} was not found");
        }
        catch (NullReferenceException nre)
        {
            logger.LogError(nre.Message);
            throw;
        }
        catch (Exception e) when (e is not ResourceNotFoundException)
        {
            logger.LogError(e, "A system error has occurred at {location} for object {object}",
                Assembly.GetExecutingAssembly(), typeof(TEntity));
            throw;
        }
    }

    public virtual async Task UpdateEntityAsync(int id, TEntity? entity, CancellationToken ct = default)
    {
        try
        {
            TEntity? existing = await FindByKeyIncludingFilteredAsync(id, ct);
            if (existing is null)
                throw new ResourceNotFoundException($"The entity of {typeof(TEntity)} with an Id of {id} was not found");

            EntityEntry existingEntry = context.Entry(existing);

            if (entity is not null && !ReferenceEquals(existing, entity))
            {
                existingEntry.CurrentValues.SetValues(entity);

                // Optimistic concurrency: honour the caller-supplied token rather than the freshly-loaded
                // one — prevents silent overwrite of a change made between the caller's read and this write.
                foreach (IProperty token in existingEntry.Metadata.GetProperties().Where(p => p.IsConcurrencyToken))
                {
                    object? incomingToken = token.PropertyInfo?.GetValue(entity);
                    if (incomingToken is not null)
                    {
                        PropertyEntry tokenEntry = existingEntry.Property(token.Name);
                        tokenEntry.OriginalValue = incomingToken;
                        tokenEntry.IsModified = false;
                    }
                }
            }

            if (existingEntry.State == EntityState.Modified)
                await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException e)
        {
            logger.LogWarning(e, "A concurrency conflict occurred at {location} for object {object}",
                Assembly.GetExecutingAssembly(), typeof(TEntity));
            throw new ConcurrencyConflictException(
                $"The {typeof(TEntity).Name} was modified by another request. Reload it and retry.");
        }
        catch (DbUpdateException e)
        {
            logger.LogError(e, "A database exception at {location} for object {object}",
                Assembly.GetExecutingAssembly(), typeof(TEntity));
            throw;
        }
        catch (NullReferenceException e)
        {
            logger.LogError(e, e.Message);
            throw;
        }
        catch (Exception e) when (e is not ResourceNotFoundException)
        {
            logger.LogError(e, "A system error has occurred at {location} for object {object}",
                Assembly.GetExecutingAssembly(), typeof(TEntity));
            throw;
        }
    }

    public async Task DeleteEntityAsync(int id, TEntity? entity, CancellationToken ct = default)
    {
        try
        {
            entity ??= await FindByKeyIncludingFilteredAsync(id, ct);

            if (entity is null)
                throw new ResourceNotFoundException($"The entity of {typeof(TEntity)} with an Id of {id} was not found");

            if (context.Entry(entity).State == EntityState.Detached)
                context.Set<TEntity>().Attach(entity);

            context.Entry(entity).State = EntityState.Deleted;
            context.Remove(entity);
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e)
        {
            logger.LogError(e, "A database exception at {location} for object {object}",
                Assembly.GetExecutingAssembly(), typeof(TEntity));
            throw;
        }
        catch (NullReferenceException e)
        {
            logger.LogError(e, e.Message);
            throw;
        }
        catch (Exception e) when (e is not ResourceNotFoundException)
        {
            logger.LogError(e, "A system error has occurred at {location} for object {object}",
                Assembly.GetExecutingAssembly(), typeof(TEntity));
            throw;
        }
    }

    public async Task SoftDeleteEntityAsync(int id, TEntity entity, CancellationToken ct = default)
        => await UpdateEntityAsync(id, entity, ct);
}
