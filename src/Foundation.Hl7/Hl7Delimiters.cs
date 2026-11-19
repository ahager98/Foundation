using System.Text;

namespace Foundation.Hl7;

/// <summary>
/// The separator characters an HL7 v2 message declares in its MSH segment: MSH-1 (field separator)
/// and MSH-2 (component, repetition, escape and subcomponent characters, plus the optional
/// truncation character added in v2.7).
/// </summary>
/// <param name="Field">Field separator (MSH-1), normally <c>|</c>.</param>
/// <param name="Component">Component separator, normally <c>^</c>.</param>
/// <param name="Repetition">Repetition separator, normally <c>~</c>.</param>
/// <param name="Escape">Escape character, normally <c>\</c>.</param>
/// <param name="SubComponent">Subcomponent separator, normally <c>&amp;</c>.</param>
/// <param name="Truncation">Optional truncation character (v2.7+), normally <c>#</c> when present.</param>
public sealed record Hl7Delimiters(
    char Field,
    char Component,
    char Repetition,
    char Escape,
    char SubComponent,
    char? Truncation = null)
{
    /// <summary>The conventional delimiter set: <c>| ^ ~ \ &amp;</c>.</summary>
    public static Hl7Delimiters Standard { get; } = new('|', '^', '~', '\\', '&');

    /// <summary>
    /// True when every delimiter is distinct, none is a letter, digit or line break (a delimiter that
    /// could appear in a segment id or end a segment would make the message ambiguous).
    /// </summary>
    public bool IsValid
    {
        get
        {
            List<char> all = new() { Field, Component, Repetition, Escape, SubComponent };
            if (Truncation.HasValue)
            {
                all.Add(Truncation.Value);
            }

            foreach (char c in all)
            {
                if (char.IsLetterOrDigit(c) || c == '\r' || c == '\n')
                {
                    return false;
                }
            }

            return all.Distinct().Count() == all.Count;
        }
    }

    /// <summary>
    /// Resolves the standard escape sequences in <paramref name="raw"/>: <c>\F\</c> (field),
    /// <c>\S\</c> (component), <c>\T\</c> (subcomponent), <c>\R\</c> (repetition) and <c>\E\</c> (escape).
    /// Any other sequence (<c>\.br\</c>, <c>\X0D\</c>, ...) and any unterminated escape is preserved
    /// verbatim so no data is silently dropped.
    /// </summary>
    /// <param name="raw">Raw field, component or subcomponent text.</param>
    /// <returns>The text with the standard escapes replaced by the characters they stand for.</returns>
    public string Decode(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.IndexOf(Escape) < 0)
        {
            return raw;
        }

        StringBuilder result = new(raw.Length);
        int i = 0;
        while (i < raw.Length)
        {
            if (raw[i] != Escape)
            {
                result.Append(raw[i]);
                i++;
                continue;
            }

            int close = raw.IndexOf(Escape, i + 1);
            if (close < 0)
            {
                result.Append(raw, i, raw.Length - i);
                break;
            }

            string sequence = raw.Substring(i + 1, close - i - 1);
            char? replacement = Resolve(sequence);
            if (replacement.HasValue)
            {
                result.Append(replacement.Value);
            }
            else
            {
                result.Append(raw, i, close - i + 1);
            }

            i = close + 1;
        }

        return result.ToString();
    }

    private char? Resolve(string sequence)
    {
        return sequence switch
        {
            "F" => Field,
            "S" => Component,
            "T" => SubComponent,
            "R" => Repetition,
            "E" => Escape,
            _ => null,
        };
    }
}
