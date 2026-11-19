using System.Diagnostics.CodeAnalysis;

namespace Foundation.Compliance;

/// <summary>One violation. <see cref="Location"/> is a code or file location, never a data value.</summary>
[ExcludeFromCodeCoverage]
public sealed record HipaaFinding(string Rule, string Location, string Message);
