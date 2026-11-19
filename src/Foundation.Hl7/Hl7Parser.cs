using System.Diagnostics.CodeAnalysis;

namespace Foundation.Hl7;

/// <summary>
/// Parses an HL7 v2 message into segments, fields, repetitions, components and subcomponents using the
/// delimiters the message declares in its own MSH segment. Segments may end in CR, LF or CRLF.
/// MLLP framing is not handled; strip the frame bytes before parsing. Parsing is eager and the result
/// is immutable. Text is kept raw (escaped); use <see cref="Hl7Delimiters.Decode"/> or
/// <see cref="Hl7Message.GetValue"/> to resolve escapes.
/// </summary>
public static class Hl7Parser
{
    private const string MshId = "MSH";

    /// <summary>Parses a message, throwing <see cref="Hl7ParseException"/> when it is rejected.</summary>
    /// <param name="text">The message text.</param>
    /// <param name="options">Input caps; defaults are used when null.</param>
    /// <returns>The parsed message.</returns>
    public static Hl7Message Parse(string? text, Hl7ParserOptions? options = null)
    {
        Hl7ParserOptions limits = options ?? new Hl7ParserOptions();
        if (limits.MaxMessageLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxMessageLength must be positive.");
        }

        if (limits.MaxSegments < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxSegments must be positive.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new Hl7ParseException(Hl7ParseFailure.Empty);
        }

        if (text.Length > limits.MaxMessageLength)
        {
            throw new Hl7ParseException(Hl7ParseFailure.TooLong);
        }

        string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > limits.MaxSegments)
        {
            throw new Hl7ParseException(Hl7ParseFailure.TooManySegments);
        }

        Hl7Delimiters delimiters = ReadDelimiters(lines[0]);
        List<Hl7Segment> segments = new(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            segments.Add(ParseSegment(lines[i], i, delimiters));
        }

        return new Hl7Message(delimiters, segments);
    }

    /// <summary>Parses a message, returning false instead of throwing when it is rejected.</summary>
    /// <param name="text">The message text.</param>
    /// <param name="message">The parsed message when the result is true.</param>
    /// <param name="options">Input caps; defaults are used when null.</param>
    /// <returns>True when the message parsed.</returns>
    public static bool TryParse(
        string? text,
        [NotNullWhen(true)] out Hl7Message? message,
        Hl7ParserOptions? options = null)
    {
        try
        {
            message = Parse(text, options);
            return true;
        }
        catch (Hl7ParseException)
        {
            message = null;
            return false;
        }
    }

    private static Hl7Delimiters ReadDelimiters(string msh)
    {
        if (!msh.StartsWith(MshId, StringComparison.Ordinal) || msh.Length < 4)
        {
            throw new Hl7ParseException(Hl7ParseFailure.MissingMsh, 0);
        }

        char field = msh[3];
        int end = msh.IndexOf(field, 4);
        int length = (end < 0 ? msh.Length : end) - 4;
        if (length < 4 || length > 5)
        {
            throw new Hl7ParseException(Hl7ParseFailure.InvalidDelimiters, 0);
        }

        char? truncation = length == 5 ? msh[8] : null;
        Hl7Delimiters delimiters = new(field, msh[4], msh[5], msh[6], msh[7], truncation);
        if (!delimiters.IsValid)
        {
            throw new Hl7ParseException(Hl7ParseFailure.InvalidDelimiters, 0);
        }

        return delimiters;
    }

    private static Hl7Segment ParseSegment(string line, int index, Hl7Delimiters delimiters)
    {
        if (!IsSegmentStart(line, delimiters.Field))
        {
            throw new Hl7ParseException(Hl7ParseFailure.InvalidSegmentId, index);
        }

        string id = line.Substring(0, 3);
        List<Hl7Field> fields = new();
        if (line.Length == 3)
        {
            return new Hl7Segment(id, fields);
        }

        string[] parts = line.Substring(4).Split(delimiters.Field);
        if (id == MshId)
        {
            fields.Add(Atomic(delimiters.Field.ToString()));
            fields.Add(Atomic(parts[0]));
            for (int i = 1; i < parts.Length; i++)
            {
                fields.Add(ParseField(parts[i], delimiters));
            }
        }
        else
        {
            foreach (string part in parts)
            {
                fields.Add(ParseField(part, delimiters));
            }
        }

        return new Hl7Segment(id, fields);
    }

    private static bool IsSegmentStart(string line, char fieldSeparator)
    {
        if (line.Length < 3 || !IsUpperLetter(line[0]) || !IsIdChar(line[1]) || !IsIdChar(line[2]))
        {
            return false;
        }

        return line.Length == 3 || line[3] == fieldSeparator;
    }

    private static bool IsUpperLetter(char c) => c >= 'A' && c <= 'Z';

    private static bool IsIdChar(char c) => IsUpperLetter(c) || (c >= '0' && c <= '9');

    private static Hl7Field Atomic(string raw)
    {
        Hl7Component component = new(raw, new[] { raw });
        Hl7Repetition repetition = new(raw, new[] { component });
        return new Hl7Field(raw, new[] { repetition });
    }

    private static Hl7Field ParseField(string raw, Hl7Delimiters delimiters)
    {
        string[] repetitionParts = raw.Split(delimiters.Repetition);
        List<Hl7Repetition> repetitions = new(repetitionParts.Length);
        foreach (string repetitionRaw in repetitionParts)
        {
            string[] componentParts = repetitionRaw.Split(delimiters.Component);
            List<Hl7Component> components = new(componentParts.Length);
            foreach (string componentRaw in componentParts)
            {
                components.Add(new Hl7Component(componentRaw, componentRaw.Split(delimiters.SubComponent)));
            }

            repetitions.Add(new Hl7Repetition(repetitionRaw, components));
        }

        return new Hl7Field(raw, repetitions);
    }
}
