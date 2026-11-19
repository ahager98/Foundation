namespace Foundation.DataAccess;

/// <summary>
/// Describes a parent entity that must be resolved and wired onto a new child entity during
/// creation. <see cref="GenericCrud{TEntity,TContext}.CreateEntityAsync"/> uses this to attach
/// the parent to the EF context and set the navigation property via reflection.
/// </summary>
public record ParentReference(int Id, string NavigationProperty, Type ParentType);
