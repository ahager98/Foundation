using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Foundation.Ccda;

/// <summary>
/// Parses an HL7 C-CDA (Consolidated CDA, R2.1) document. The header (ids, type, patient, authors,
/// custodian) is read, every body section is classified and its narrative rendered as plain text, and
/// the entries of Problems, Allergies, Medications, Immunizations, Results, Vital Signs and Procedures
/// sections are extracted. Other sections keep their narrative only.
/// <para>
/// Input is untrusted: DTDs are prohibited (no XXE, no entity expansion), external resources are never
/// resolved, and size, depth, section and entry counts are capped by <see cref="CcdaParserOptions"/>.
/// Parsing is fail-closed and the exception never echoes document content. The document is not
/// validated against the C-CDA schema or Schematron; a missing optional element is simply null or empty.
/// </para>
/// </summary>
public static class CcdaParser
{
    /// <summary>Parses a document held in a string, throwing <see cref="CcdaParseException"/> when it is rejected.</summary>
    /// <param name="text">The XML text.</param>
    /// <param name="options">Input caps; defaults are used when null.</param>
    /// <returns>The parsed document.</returns>
    public static CcdaDocument Parse(string? text, CcdaParserOptions? options = null)
    {
        CcdaParserOptions limits = Validate(options);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new CcdaParseException(CcdaParseFailure.Empty);
        }

        if (Encoding.UTF8.GetByteCount(text) > limits.MaxDocumentBytes)
        {
            throw new CcdaParseException(CcdaParseFailure.TooLarge);
        }

        XDocument xml;
        try
        {
            using StringReader input = new(text);
            using XmlReader reader = XmlReader.Create(input, ReaderSettings(isAsync: false));
            xml = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new CcdaParseException(CcdaParseFailure.MalformedXml, ex.LineNumber, ex.LinePosition);
        }

        return Build(xml, limits);
    }

    /// <summary>Parses a document held in a string, returning false instead of throwing when it is rejected.</summary>
    /// <param name="text">The XML text.</param>
    /// <param name="document">The parsed document when the result is true.</param>
    /// <param name="options">Input caps; defaults are used when null.</param>
    /// <returns>True when the document parsed.</returns>
    public static bool TryParse(
        string? text,
        [NotNullWhen(true)] out CcdaDocument? document,
        CcdaParserOptions? options = null)
    {
        try
        {
            document = Parse(text, options);
            return true;
        }
        catch (CcdaParseException)
        {
            document = null;
            return false;
        }
    }

    /// <summary>
    /// Reads and parses a document from a stream (a file, blob or request body), honouring the encoding
    /// the XML declares. Reading stops as soon as <see cref="CcdaParserOptions.MaxDocumentBytes"/> is
    /// passed. The stream is left open.
    /// </summary>
    /// <param name="stream">The stream to read from its current position.</param>
    /// <param name="options">Input caps; defaults are used when null.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="CcdaParseException">The document was rejected.</exception>
    public static async Task<CcdaDocument> ParseAsync(
        Stream stream,
        CcdaParserOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        CcdaParserOptions limits = Validate(options);

        using BoundedReadStream bounded = new(stream, limits.MaxDocumentBytes);
        XDocument xml;
        try
        {
            using XmlReader reader = XmlReader.Create(bounded, ReaderSettings(isAsync: true));
            xml = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        }
        catch (XmlException ex)
        {
            if (bounded.BytesRead == 0)
            {
                throw new CcdaParseException(CcdaParseFailure.Empty);
            }

            throw new CcdaParseException(CcdaParseFailure.MalformedXml, ex.LineNumber, ex.LinePosition);
        }

        return Build(xml, limits);
    }

    private static CcdaParserOptions Validate(CcdaParserOptions? options)
    {
        CcdaParserOptions limits = options ?? new CcdaParserOptions();
        if (limits.MaxDocumentBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDocumentBytes must be positive.");
        }

        if (limits.MaxElementDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxElementDepth must be positive.");
        }

        if (limits.MaxSections < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxSections must be positive.");
        }

        if (limits.MaxEntriesPerSection < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxEntriesPerSection must be positive.");
        }

        return limits;
    }

    private static XmlReaderSettings ReaderSettings(bool isAsync)
    {
        return new XmlReaderSettings
        {
            Async = isAsync,
            CloseInput = false,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };
    }

    private static CcdaDocument Build(XDocument xml, CcdaParserOptions limits)
    {
        XElement? root = xml.Root;
        if (root is null || root.Name != CcdaXml.V3 + "ClinicalDocument")
        {
            throw new CcdaParseException(CcdaParseFailure.NotClinicalDocument);
        }

        EnsureDepth(root, limits.MaxElementDepth);
        return new CcdaDocumentBuilder(root, limits).Build();
    }

    /// <summary>Iterative depth check, run before any recursive narrative rendering.</summary>
    private static void EnsureDepth(XElement root, int maxDepth)
    {
        Stack<(XElement Element, int Depth)> pending = new();
        pending.Push((root, 1));
        while (pending.Count > 0)
        {
            (XElement element, int depth) = pending.Pop();
            if (depth > maxDepth)
            {
                throw new CcdaParseException(CcdaParseFailure.TooDeep);
            }

            foreach (XElement child in element.Elements())
            {
                pending.Push((child, depth + 1));
            }
        }
    }
}
