namespace Foundation.Hl7;

/// <summary>
/// Thrown when a message cannot be parsed. HL7 messages routinely carry patient data, so the
/// exception reports only the reason and the zero-based segment index, never any message text.
/// </summary>
public sealed class Hl7ParseException : FormatException
{
    /// <summary>Creates the exception for a failure reason and, when known, the segment it occurred in.</summary>
    /// <param name="reason">Why the message was rejected.</param>
    /// <param name="segmentIndex">Zero-based index of the offending segment, or -1 when not segment-specific.</param>
    public Hl7ParseException(Hl7ParseFailure reason, int segmentIndex = -1)
        : base(BuildMessage(reason, segmentIndex))
    {
        Reason = reason;
        SegmentIndex = segmentIndex;
    }

    /// <summary>Why the message was rejected.</summary>
    public Hl7ParseFailure Reason { get; }

    /// <summary>Zero-based index of the offending segment, or -1 when not segment-specific.</summary>
    public int SegmentIndex { get; }

    private static string BuildMessage(Hl7ParseFailure reason, int segmentIndex)
    {
        return segmentIndex < 0
            ? $"HL7 message rejected: {reason}."
            : $"HL7 message rejected: {reason} (segment {segmentIndex}).";
    }
}
