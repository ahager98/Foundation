namespace Foundation.DataAccess;

public interface IGenericCrud<TEntity> where TEntity : class
{
    Task<TEntity> CreateEntityAsync(TEntity entity, IEnumerable<ParentReference>? parents = null, CancellationToken ct = default);
    Task<TEntity> GetEntityByIdDetachedAsync(int id, CancellationToken ct = default);
    Task<TEntity> GetEntityByIdAsync(int id, CancellationToken ct = default);
    Task UpdateEntityAsync(int id, TEntity entity, CancellationToken ct = default);
    Task DeleteEntityAsync(int id, TEntity? entity, CancellationToken ct = default);
    Task SoftDeleteEntityAsync(int id, TEntity entity, CancellationToken ct = default);
}
