using System.Diagnostics.CodeAnalysis;

namespace Foundation.Compliance;

/// <summary>Marks an entity or DTO member as holding classified data. Use <see cref="PhiAttribute"/> or <see cref="PiiAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
[ExcludeFromCodeCoverage]
public abstract class ClassifiedAttribute(DataClassification classification) : Attribute
{
    public DataClassification Classification { get; } = classification;
}
