using System.Diagnostics.CodeAnalysis;

namespace Foundation.Compliance;

/// <summary>A third party that may receive PHI, the hosts it is reached on, and the Business Associate Agreement on file.</summary>
[ExcludeFromCodeCoverage]
public sealed record BaaVendor(string Name, IReadOnlyList<string> Hosts, string BaaReference);
