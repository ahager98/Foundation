namespace Foundation.Hl7;

internal static class Hl7Index
{
    internal static void Require(int number, string paramName)
    {
        if (number < 1)
        {
            throw new ArgumentOutOfRangeException(paramName, "HL7 positions are 1-based.");
        }
    }
}

/// <summary>One component of a field repetition, split into subcomponents. Text is raw (still escaped).</summary>
public sealed class Hl7Component
{
    /// <summary>Creates a component.</summary>
    /// <param name="raw">The raw component text.</param>
    /// <param name="subComponents">The raw subcomponents; never empty.</param>
    public Hl7Component(string raw, IReadOnlyList<string> subComponents)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(subComponents);
        Raw = raw;
        SubComponents = subComponents;
    }

    /// <summary>A component holding a single empty subcomponent.</summary>
    public static Hl7Component Empty { get; } = new(string.Empty, new[] { string.Empty });

    /// <summary>The raw component text.</summary>
    public string Raw { get; }

    /// <summary>The raw subcomponents.</summary>
    public IReadOnlyList<string> SubComponents { get; }

    /// <summary>Gets a subcomponent by 1-based position; empty text when the position is absent.</summary>
    /// <param name="number">1-based position.</param>
    /// <returns>The raw subcomponent text.</returns>
    public string SubComponent(int number)
    {
        Hl7Index.Require(number, nameof(number));
        return number <= SubComponents.Count ? SubComponents[number - 1] : string.Empty;
    }
}

/// <summary>One repetition of a field, split into components.</summary>
public sealed class Hl7Repetition
{
    /// <summary>Creates a repetition.</summary>
    /// <param name="raw">The raw repetition text.</param>
    /// <param name="components">The components; never empty.</param>
    public Hl7Repetition(string raw, IReadOnlyList<Hl7Component> components)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(components);
        Raw = raw;
        Components = components;
    }

    /// <summary>A repetition holding a single empty component.</summary>
    public static Hl7Repetition Empty { get; } = new(string.Empty, new[] { Hl7Component.Empty });

    /// <summary>The raw repetition text.</summary>
    public string Raw { get; }

    /// <summary>The components.</summary>
    public IReadOnlyList<Hl7Component> Components { get; }

    /// <summary>Gets a component by 1-based position; an empty component when the position is absent.</summary>
    /// <param name="number">1-based position.</param>
    /// <returns>The component.</returns>
    public Hl7Component Component(int number)
    {
        Hl7Index.Require(number, nameof(number));
        return number <= Components.Count ? Components[number - 1] : Hl7Component.Empty;
    }
}

/// <summary>One field of a segment, split into repetitions.</summary>
public sealed class Hl7Field
{
    /// <summary>Creates a field.</summary>
    /// <param name="raw">The raw field text.</param>
    /// <param name="repetitions">The repetitions; never empty.</param>
    public Hl7Field(string raw, IReadOnlyList<Hl7Repetition> repetitions)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(repetitions);
        Raw = raw;
        Repetitions = repetitions;
    }

    /// <summary>A field holding a single empty repetition.</summary>
    public static Hl7Field Empty { get; } = new(string.Empty, new[] { Hl7Repetition.Empty });

    /// <summary>The raw field text.</summary>
    public string Raw { get; }

    /// <summary>The repetitions.</summary>
    public IReadOnlyList<Hl7Repetition> Repetitions { get; }

    /// <summary>True when the field has no text.</summary>
    public bool IsEmpty => Raw.Length == 0;

    /// <summary>Gets a repetition by 1-based position; an empty repetition when the position is absent.</summary>
    /// <param name="number">1-based position.</param>
    /// <returns>The repetition.</returns>
    public Hl7Repetition Repetition(int number)
    {
        Hl7Index.Require(number, nameof(number));
        return number <= Repetitions.Count ? Repetitions[number - 1] : Hl7Repetition.Empty;
    }
}

/// <summary>One segment: a three-character id and its fields, numbered the HL7 way (1-based).</summary>
public sealed class Hl7Segment
{
    /// <summary>Creates a segment.</summary>
    /// <param name="id">The three-character segment id.</param>
    /// <param name="fields">The fields; <c>Fields[0]</c> is field 1.</param>
    public Hl7Segment(string id, IReadOnlyList<Hl7Field> fields)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(fields);
        Id = id;
        Fields = fields;
    }

    /// <summary>The three-character segment id, e.g. <c>PID</c>.</summary>
    public string Id { get; }

    /// <summary>
    /// The fields in order; <c>Fields[0]</c> is field 1. For MSH, field 1 is the field separator and
    /// field 2 the encoding characters, both kept as single unsplit values.
    /// </summary>
    public IReadOnlyList<Hl7Field> Fields { get; }

    /// <summary>Gets a field by its HL7 number (1-based); an empty field when the segment is shorter.</summary>
    /// <param name="number">The field number, e.g. 5 for PID-5.</param>
    /// <returns>The field.</returns>
    public Hl7Field Field(int number)
    {
        Hl7Index.Require(number, nameof(number));
        return number <= Fields.Count ? Fields[number - 1] : Hl7Field.Empty;
    }
}

/// <summary>A parsed HL7 v2 message: its delimiters and segments, in message order.</summary>
public sealed class Hl7Message
{
    /// <summary>Creates a message.</summary>
    /// <param name="delimiters">The delimiters the message declared.</param>
    /// <param name="segments">The segments in order; the first is MSH.</param>
    public Hl7Message(Hl7Delimiters delimiters, IReadOnlyList<Hl7Segment> segments)
    {
        ArgumentNullException.ThrowIfNull(delimiters);
        ArgumentNullException.ThrowIfNull(segments);
        Delimiters = delimiters;
        Segments = segments;
    }

    /// <summary>The delimiters the message declared in MSH-1/MSH-2.</summary>
    public Hl7Delimiters Delimiters { get; }

    /// <summary>The segments in message order; the first is MSH.</summary>
    public IReadOnlyList<Hl7Segment> Segments { get; }

    /// <summary>Gets the first segment with the id, or null when the message has none.</summary>
    /// <param name="id">The segment id, e.g. <c>PID</c> (case-sensitive).</param>
    /// <returns>The segment, or null.</returns>
    public Hl7Segment? GetSegment(string id)
    {
        return Segments.FirstOrDefault(s => s.Id == id);
    }

    /// <summary>Gets every segment with the id, in message order (e.g. each <c>OBX</c>).</summary>
    /// <param name="id">The segment id (case-sensitive).</param>
    /// <returns>The matching segments; empty when there are none.</returns>
    public IEnumerable<Hl7Segment> GetSegments(string id)
    {
        return Segments.Where(s => s.Id == id);
    }

    /// <summary>
    /// Reads one value from the first segment with the id and resolves its escape sequences.
    /// Returns an empty string, never null, when the segment or position is absent.
    /// </summary>
    /// <param name="segmentId">The segment id, e.g. <c>PID</c>.</param>
    /// <param name="field">1-based field number.</param>
    /// <param name="component">1-based component number.</param>
    /// <param name="subComponent">1-based subcomponent number.</param>
    /// <param name="repetition">1-based repetition number.</param>
    /// <returns>The decoded value, or an empty string.</returns>
    public string GetValue(
        string segmentId,
        int field,
        int component = 1,
        int subComponent = 1,
        int repetition = 1)
    {
        Hl7Segment? segment = GetSegment(segmentId);
        if (segment is null)
        {
            return string.Empty;
        }

        string raw = segment.Field(field).Repetition(repetition).Component(component).SubComponent(subComponent);
        return Delimiters.Decode(raw);
    }
}
