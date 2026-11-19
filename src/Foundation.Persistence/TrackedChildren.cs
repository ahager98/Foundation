using Foundation.DataAccess;

namespace Foundation.Persistence;

/// <summary>
/// Child-collection edits for use inside <see cref="TrackedUpdateExtensions.UpdateTrackedAsync{T}"/>.
/// Children are matched in place by id so existing rows keep their primary keys; only genuinely new
/// items are added and only items missing from the incoming set are removed. Nothing is re-inserted.
/// </summary>
public static class TrackedChildren
{
    /// <summary>
    /// Reconciles <paramref name="existing"/> (the tracked collection) with <paramref name="incoming"/>.
    /// An incoming item whose id is 0 or matches no existing child is created with <paramref name="create"/>;
    /// one that matches is updated in place with <paramref name="update"/>; existing children absent from
    /// the incoming set are removed from the collection.
    /// <para>
    /// Removal only takes the child out of the collection. With a required relationship EF then deletes the orphan row;
    /// with an optional one it nulls the foreign key and the row, which may hold PHI/PII, remains. Delete such children
    /// explicitly instead of relying on this method.
    /// </para>
    /// </summary>
    public static void Sync<TChild, TIncoming>(
        ICollection<TChild> existing,
        IEnumerable<TIncoming> incoming,
        Func<TIncoming, int> incomingId,
        Func<TIncoming, TChild> create,
        Action<TChild, TIncoming> update)
        where TChild : BaseEntity
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(incomingId);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(update);

        Dictionary<int, TChild> byId = existing.Where(static c => c.Id != 0).ToDictionary(static c => c.Id);
        HashSet<int> kept = [];

        foreach (TIncoming item in incoming)
        {
            int id = incomingId(item);
            if (id != 0 && byId.TryGetValue(id, out TChild? match))
            {
                update(match, item);
                kept.Add(id);
            }
            else
            {
                existing.Add(create(item));
            }
        }

        foreach (TChild child in existing.Where(c => c.Id != 0 && !kept.Contains(c.Id)).ToList())
            existing.Remove(child);
    }
}
