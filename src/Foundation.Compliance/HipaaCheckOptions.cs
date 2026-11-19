using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;

namespace Foundation.Compliance;

/// <summary>Inputs to <see cref="HipaaCheck.Run"/>.</summary>
[ExcludeFromCodeCoverage]
public sealed class HipaaCheckOptions
{
    /// <summary>Assemblies holding entities, DTOs and controllers.</summary>
    public IReadOnlyList<System.Reflection.Assembly> Assemblies { get; init; } = [];

    /// <summary>Individual types to check in addition to <see cref="Assemblies"/> (useful for targeted checks and tests).</summary>
    public IReadOnlyList<Type> Types { get; init; } = [];

    /// <summary>C# source files scanned for classified members reaching log statements.</summary>
    public IReadOnlyList<string> SourceFiles { get; init; } = [];

    /// <summary>appsettings-style JSON files scanned for egress targets and insecure transport, storage or secret settings.</summary>
    public IReadOnlyList<string> ConfigurationFiles { get; init; } = [];

    /// <summary>Extra outbound hosts or URLs not discoverable from configuration (vendor SDK endpoints, model providers).</summary>
    public IReadOnlyList<string> EgressTargets { get; init; } = [];

    public BaaInventory BaaInventory { get; init; } = new([]);

    public IReadOnlyList<HipaaWaiver> Waivers { get; init; } = [];

    /// <summary>Longest a waiver may run from <see cref="Today"/>; a waiver expiring later is reported and not honored.</summary>
    public int MaxWaiverDays { get; init; } = 365;

    /// <summary>
    /// Folder that log-statement and configuration-file locations are reported relative to, so findings and waivers key on
    /// a stable path. Without it log statements report the full path and configuration files only their file name.
    /// </summary>
    public string? SourceRoot { get; init; }

    /// <summary>Minimal-API endpoints (from the app's EndpointDataSource) to check alongside controllers.</summary>
    public IReadOnlyList<Microsoft.AspNetCore.Http.Endpoint> Endpoints { get; init; } = [];

    /// <summary>
    /// Case-sensitive regex over type names that hold person, case, note, form or other personal data and so must be
    /// fully classified (fail closed). It matches whole PascalCase words (optionally plural), so "PersonRecord" and
    /// "IndividualNotes" match while "PlatformOptions", "Information" and "Personalization" do not. The default is deliberately
    /// generic; a service widens it with its own domain terms.
    /// </summary>
    public Regex SensitiveTypePattern { get; init; } = new(
        @"(Person|People|Case|Note|Form|Individual|Contact)s?(?![a-z])",
        RegexOptions.Compiled);

    /// <summary>
    /// Member names that carry no personal data of their own (keys, scoping and audit columns) and so need no
    /// classification on a sensitive type. Add the service's own scoping column names (a site, region or workspace id).
    /// </summary>
    public IReadOnlySet<string> ExemptMemberNames { get; init; } =
        new HashSet<string>(StringComparer.Ordinal) { "Id", "TenantId", "RowVersion", "IsActive" };

    /// <summary>Overridable clock for deterministic waiver-expiry tests.</summary>
    public DateOnly Today { get; init; } = DateOnly.FromDateTime(DateTime.UtcNow);
}
