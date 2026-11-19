using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>One body section: its identification and its human-readable narrative as plain text.</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaSection
{
    /// <summary>The recognised kind, or <see cref="CcdaSectionKind.Unknown"/>.</summary>
    public CcdaSectionKind Kind { get; init; }

    /// <summary>The section's template ids (root and extension).</summary>
    public IReadOnlyList<CcdaIdentifier> TemplateIds { get; init; } = [];

    /// <summary>The section code, normally LOINC.</summary>
    public CcdaCode? Code { get; init; }

    /// <summary>The section title.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// The narrative block as plain text: paragraphs, list items, table rows and line breaks become
    /// new lines, table cells are separated by <c> | </c>, and whitespace is collapsed.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>The number of <c>entry</c> elements in the section.</summary>
    public int EntryCount { get; init; }
}
