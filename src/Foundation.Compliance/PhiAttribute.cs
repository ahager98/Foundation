using System.Diagnostics.CodeAnalysis;

namespace Foundation.Compliance;

/// <summary>The member holds protected health information.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
[ExcludeFromCodeCoverage]
public sealed class PhiAttribute() : ClassifiedAttribute(DataClassification.Phi);
