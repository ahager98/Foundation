using System.Diagnostics.CodeAnalysis;

namespace Foundation.Compliance;

/// <summary>Declares how long a classified entity is retained and what happens when that period ends.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
[ExcludeFromCodeCoverage]
public sealed class RetentionAttribute(int years, string deletion) : Attribute
{
    /// <summary>Retention period in years. Must be positive; the check reports anything else.</summary>
    public int Years { get; } = years;

    /// <summary>Deletion behavior at end of retention, e.g. "hard-delete", "anonymize". Must not be blank.</summary>
    public string Deletion { get; } = deletion;
}
