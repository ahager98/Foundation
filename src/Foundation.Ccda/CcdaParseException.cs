namespace Foundation.Ccda;

/// <summary>
/// Thrown when a document cannot be parsed. C-CDA documents carry patient data, so the exception
/// reports only the reason and, for malformed XML, the line and position; never any document text.
/// The underlying <see cref="System.Xml.XmlException"/> is deliberately not attached because its
/// message can quote document content.
/// </summary>
public sealed class CcdaParseException : FormatException
{
    /// <summary>Creates the exception for a failure reason and, when known, its location.</summary>
    /// <param name="reason">Why the document was rejected.</param>
    /// <param name="lineNumber">1-based line of the XML error, or 0 when not location-specific.</param>
    /// <param name="linePosition">1-based position of the XML error, or 0 when not location-specific.</param>
    public CcdaParseException(CcdaParseFailure reason, int lineNumber = 0, int linePosition = 0)
        : base(BuildMessage(reason, lineNumber, linePosition))
    {
        Reason = reason;
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    /// <summary>Why the document was rejected.</summary>
    public CcdaParseFailure Reason { get; }

    /// <summary>1-based line of the XML error, or 0 when not location-specific.</summary>
    public int LineNumber { get; }

    /// <summary>1-based position of the XML error, or 0 when not location-specific.</summary>
    public int LinePosition { get; }

    private static string BuildMessage(CcdaParseFailure reason, int lineNumber, int linePosition)
    {
        return lineNumber > 0
            ? $"C-CDA document rejected: {reason} (line {lineNumber}, position {linePosition})."
            : $"C-CDA document rejected: {reason}.";
    }
}
