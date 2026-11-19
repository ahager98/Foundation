using System.Diagnostics.CodeAnalysis;

namespace Foundation.Compliance;

/// <summary>
/// Records a reviewed decision that a member in a sensitive type holds no PHI or PII (an id, a flag, a code).
/// The reason is mandatory (a blank reason is reported as a finding) and every exemption is listed in the check report.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
[ExcludeFromCodeCoverage]
public sealed class NotClassifiedAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}
