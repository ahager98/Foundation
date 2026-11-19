namespace Foundation.Hl7;

/// <summary>Why an HL7 message was rejected by <see cref="Hl7Parser"/>.</summary>
public enum Hl7ParseFailure
{
    /// <summary>The message was null, empty or only whitespace.</summary>
    Empty = 1,

    /// <summary>The message is longer than <see cref="Hl7ParserOptions.MaxMessageLength"/>.</summary>
    TooLong,

    /// <summary>The first segment is not <c>MSH</c> (a leading MLLP frame byte also lands here).</summary>
    MissingMsh,

    /// <summary>MSH-1/MSH-2 do not declare a usable, distinct delimiter set.</summary>
    InvalidDelimiters,

    /// <summary>A segment id is not three upper-case letters or digits followed by a field separator.</summary>
    InvalidSegmentId,

    /// <summary>The message has more than <see cref="Hl7ParserOptions.MaxSegments"/> segments.</summary>
    TooManySegments,
}
