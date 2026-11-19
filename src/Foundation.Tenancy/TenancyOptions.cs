using Microsoft.Extensions.Configuration;

namespace Foundation.Tenancy;

/// <summary>
/// Host-selected tenancy configuration. Bound from the <c>Tenancy</c> configuration section.
/// </summary>
public sealed class TenancyOptions
{
    /// <summary>The active tenancy strategy. Defaults to <see cref="TenancyStrategy.DatabasePerTenant"/>.</summary>
    public TenancyStrategy Strategy { get; init; } = TenancyStrategy.DatabasePerTenant;

    /// <summary>
    /// Builds <see cref="TenancyOptions"/> from <c>Tenancy:Strategy</c>. Defaults to
    /// <see cref="TenancyStrategy.DatabasePerTenant"/> when the key is absent, and throws on an
    /// unrecognized value (fail-fast at startup rather than silently defaulting).
    /// </summary>
    public static TenancyOptions FromConfiguration(IConfiguration configuration)
    {
        string? raw = configuration["Tenancy:Strategy"];
        if (string.IsNullOrWhiteSpace(raw))
            return new TenancyOptions();

        if (!Enum.TryParse(raw, ignoreCase: true, out TenancyStrategy strategy)
            || !Enum.IsDefined(strategy))
        {
            throw new InvalidOperationException(
                $"Unsupported Tenancy:Strategy '{raw}'. Supported values: "
                + $"{TenancyStrategy.DatabasePerTenant}, {TenancyStrategy.SharedDatabase}.");
        }

        return new TenancyOptions { Strategy = strategy };
    }
}
