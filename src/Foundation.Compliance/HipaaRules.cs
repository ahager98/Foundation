using System.Diagnostics.CodeAnalysis;

namespace Foundation.Compliance;

/// <summary>Stable ids of the technical-safeguard rules.</summary>
[ExcludeFromCodeCoverage]
public static class HipaaRules
{
    public const string UnclassifiedField = "HIPAA-CLASSIFY";
    public const string MissingRetention = "HIPAA-RETENTION";
    public const string EndpointAuthorization = "HIPAA-AUTHZ";
    public const string LogLeakage = "HIPAA-LOG-LEAK";
    public const string UnlistedEgress = "HIPAA-EGRESS";
    public const string InsecureConfiguration = "HIPAA-CONFIG";
    public const string ExpiredWaiver = "HIPAA-WAIVER";
    public const string UnloadableType = "HIPAA-LOAD";
}
