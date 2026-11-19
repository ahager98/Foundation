namespace Foundation.Ccda;

/// <summary>Why a document was rejected by <see cref="CcdaParser"/>.</summary>
public enum CcdaParseFailure
{
    /// <summary>The input was null, empty or only whitespace.</summary>
    Empty = 1,

    /// <summary>The input is larger than <see cref="CcdaParserOptions.MaxDocumentBytes"/>.</summary>
    TooLarge,

    /// <summary>The input is not well-formed XML, or it declares a DTD (DTDs are always prohibited).</summary>
    MalformedXml,

    /// <summary>The root element is not <c>ClinicalDocument</c> in the <c>urn:hl7-org:v3</c> namespace.</summary>
    NotClinicalDocument,

    /// <summary>Elements are nested deeper than <see cref="CcdaParserOptions.MaxElementDepth"/>.</summary>
    TooDeep,

    /// <summary>The body has more than <see cref="CcdaParserOptions.MaxSections"/> sections.</summary>
    TooManySections,

    /// <summary>A section has more than <see cref="CcdaParserOptions.MaxEntriesPerSection"/> entries.</summary>
    TooManyEntries,
}
