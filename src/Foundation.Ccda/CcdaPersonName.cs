using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>A person name (<c>PN</c>).</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaPersonName
{
    /// <summary>The name use, e.g. <c>L</c> (legal).</summary>
    public string? Use { get; init; }

    /// <summary>Prefixes, e.g. <c>Dr.</c>.</summary>
    public IReadOnlyList<string> Prefixes { get; init; } = [];

    /// <summary>Given names, in order.</summary>
    public IReadOnlyList<string> Given { get; init; } = [];

    /// <summary>The family name.</summary>
    public string? Family { get; init; }

    /// <summary>Suffixes, e.g. <c>Jr.</c> or <c>MD</c>.</summary>
    public IReadOnlyList<string> Suffixes { get; init; } = [];

    /// <summary>The whole name as one string: the parts in order, or the unstructured text when there are no parts.</summary>
    public string? Text { get; init; }

    /// <summary>The null flavor when the name is not known.</summary>
    public string? NullFlavor { get; init; }
}
