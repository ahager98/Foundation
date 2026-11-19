using System.Diagnostics.CodeAnalysis;

namespace Foundation.Ccda;

/// <summary>Input caps for <see cref="CcdaParser"/>; input is untrusted so every dimension is bounded.</summary>
[ExcludeFromCodeCoverage]
public sealed class CcdaParserOptions
{
    /// <summary>
    /// Maximum document size in bytes (UTF-8 bytes for string input, raw bytes for stream input).
    /// Defaults to 16 MiB; raise it for documents that embed large attachments.
    /// </summary>
    public long MaxDocumentBytes { get; init; } = 16L * 1024 * 1024;

    /// <summary>Maximum element nesting depth. Defaults to 128.</summary>
    public int MaxElementDepth { get; init; } = 128;

    /// <summary>Maximum number of sections in the structured body, nested sections included. Defaults to 500.</summary>
    public int MaxSections { get; init; } = 500;

    /// <summary>Maximum number of <c>entry</c> elements in one section. Defaults to 10,000.</summary>
    public int MaxEntriesPerSection { get; init; } = 10_000;
}
