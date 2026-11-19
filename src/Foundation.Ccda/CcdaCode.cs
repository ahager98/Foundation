using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>A coded value (<c>CD</c>/<c>CE</c>/<c>CV</c>/<c>CS</c>), with its translations and original text.</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaCode
{
    /// <summary>The code, e.g. <c>38341003</c>.</summary>
    public string? Code { get; init; }

    /// <summary>The code-system OID, e.g. <c>2.16.840.1.113883.6.96</c> (SNOMED CT).</summary>
    public string? CodeSystem { get; init; }

    /// <summary>The code-system name as the document gave it, if any.</summary>
    public string? CodeSystemName { get; init; }

    /// <summary>The display name as the document gave it, if any.</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// The original text, with a <c>reference</c> to the section narrative resolved to that
    /// narrative's plain text.
    /// </summary>
    public string? OriginalText { get; init; }

    /// <summary>The null flavor when the value is not coded, e.g. <c>UNK</c> or <c>OTH</c>.</summary>
    public string? NullFlavor { get; init; }

    /// <summary>Equivalent codes in other code systems.</summary>
    public IReadOnlyList<CcdaCode> Translations { get; init; } = [];
}
