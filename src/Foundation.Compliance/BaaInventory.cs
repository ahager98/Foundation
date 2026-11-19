using System.Text.Json;

namespace Foundation.Compliance;

/// <summary>
/// Inventory of vendors covered by a Business Associate Agreement (messaging, email, mail, identity, cloud and
/// any model provider). An outbound target is allowed only when a vendor with a non-empty BAA reference lists its
/// host. Changes to the inventory belong under compliance-owner code ownership.
/// </summary>
public sealed class BaaInventory(IReadOnlyList<BaaVendor> vendors)
{
    public IReadOnlyList<BaaVendor> Vendors { get; } = vendors ?? throw new ArgumentNullException(nameof(vendors));

    /// <summary>
    /// Parses <c>{ "vendors": [ { "name": "...", "hosts": ["api.x.com", "*.y.com"], "baaReference": "..." } ] }</c>.
    /// </summary>
    public static BaaInventory FromJson(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        List<BaaVendor> vendors = [];
        if (doc.RootElement.TryGetProperty("vendors", out JsonElement array))
        {
            foreach (JsonElement v in array.EnumerateArray())
            {
                string name = v.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                string baa = v.TryGetProperty("baaReference", out JsonElement b) ? b.GetString() ?? "" : "";
                List<string> hosts = v.TryGetProperty("hosts", out JsonElement h)
                    ? h.EnumerateArray().Select(static x => x.GetString() ?? "").Where(static x => x.Length > 0).ToList()
                    : [];
                vendors.Add(new BaaVendor(name, hosts, baa));
            }
        }

        return new BaaInventory(vendors);
    }

    /// <summary>True for loopback, or when a vendor with a BAA reference lists the host (exact or <c>*.suffix</c>).</summary>
    public bool Covers(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (IsLoopback(host))
            return true;

        return Vendors.Any(v => !string.IsNullOrWhiteSpace(v.BaaReference)
            && v.Hosts.Any(pattern => Matches(pattern, host)));
    }

    internal static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host == "127.0.0.1"
        || host == "::1"
        || host == "[::1]"
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

    private static bool Matches(string pattern, string host) =>
        pattern.StartsWith("*.", StringComparison.Ordinal)
            ? host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)
            : host.Equals(pattern, StringComparison.OrdinalIgnoreCase);
}
