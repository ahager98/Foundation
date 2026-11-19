using System.Diagnostics.CodeAnalysis;

namespace Foundation.Compliance;

/// <summary>The member holds personally identifiable information.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
[ExcludeFromCodeCoverage]
public sealed class PiiAttribute() : ClassifiedAttribute(DataClassification.Pii);
