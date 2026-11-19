using System.Text.RegularExpressions;

namespace Foundation.Ccda;

/// <summary>Source-generated regular expressions used by the parser.</summary>
internal static partial class CcdaPatterns
{
    /// <summary>HL7 TS: <c>YYYY[MM[DD[HH[MM[SS[.FFFF]]]]]][+/-ZZZZ]</c>.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(
        @"^(\d{4})(\d{2})?(\d{2})?(\d{2})?(\d{2})?(\d{2})?(?:\.(\d{1,4}))?([+-]\d{4})?$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    internal static partial Regex Timestamp();

    /// <summary>A run of whitespace.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    internal static partial Regex Whitespace();
}
