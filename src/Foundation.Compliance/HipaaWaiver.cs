using System.Diagnostics.CodeAnalysis;

namespace Foundation.Compliance;

/// <summary>
/// A compliance-owner-approved exception for one rule at one location. Waivers always expire; an expired
/// waiver stops suppressing its finding and is itself reported.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record HipaaWaiver(string Rule, string Location, string Reason, string ApprovedBy, DateOnly Expires);
