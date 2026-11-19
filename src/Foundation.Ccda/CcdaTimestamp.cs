using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Foundation.Ccda;

/// <summary>
/// An HL7 point in time (<c>TS</c>), with the precision the document gave it. A timestamp without a
/// zone offset is read as UTC and <see cref="HasOffset"/> is false.
/// </summary>
public sealed class CcdaTimestamp
{
    /// <summary>Creates a timestamp.</summary>
    /// <param name="value">The instant; missing parts take their earliest value (month 1, day 1, 00:00).</param>
    /// <param name="precision">How much of the timestamp the document gave.</param>
    /// <param name="hasOffset">True when the document gave a zone offset.</param>
    public CcdaTimestamp(DateTimeOffset value, CcdaTimePrecision precision, bool hasOffset)
    {
        Value = value;
        Precision = precision;
        HasOffset = hasOffset;
    }

    /// <summary>The instant; missing parts take their earliest value (month 1, day 1, 00:00).</summary>
    public DateTimeOffset Value { get; }

    /// <summary>How much of the timestamp the document gave.</summary>
    public CcdaTimePrecision Precision { get; }

    /// <summary>True when the document gave a zone offset; otherwise <see cref="Value"/> is in UTC.</summary>
    public bool HasOffset { get; }

    /// <summary>Parses an HL7 <c>TS</c> value such as <c>19800212</c> or <c>20240115103000-0500</c>.</summary>
    /// <param name="text">The raw value attribute.</param>
    /// <param name="timestamp">The timestamp when the result is true.</param>
    /// <returns>True when the text is a valid timestamp.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out CcdaTimestamp? timestamp)
    {
        timestamp = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        Match match = CcdaPatterns.Timestamp().Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        bool hasFraction = match.Groups[7].Success;
        if (hasFraction && !match.Groups[6].Success)
        {
            return false;
        }

        // Groups 2..6 are month..second; each is present only if every coarser part is.
        CcdaTimePrecision precision = CcdaTimePrecision.Year;
        for (int group = 2; group <= 6 && match.Groups[group].Success; group++)
        {
            precision = (CcdaTimePrecision)group;
        }

        int year = Number(match.Groups[1], 0);
        int month = Number(match.Groups[2], 1);
        int day = Number(match.Groups[3], 1);
        int hour = Number(match.Groups[4], 0);
        int minute = Number(match.Groups[5], 0);
        int second = Number(match.Groups[6], 0);
        long fractionTicks = hasFraction
            ? long.Parse(match.Groups[7].Value.PadRight(7, '0'), CultureInfo.InvariantCulture)
            : 0;

        bool hasOffset = match.Groups[8].Success;
        try
        {
            TimeSpan offset = hasOffset ? ParseOffset(match.Groups[8].Value) : TimeSpan.Zero;
            DateTimeOffset value = new DateTimeOffset(year, month, day, hour, minute, second, offset)
                .AddTicks(fractionTicks);
            timestamp = new CcdaTimestamp(value, precision, hasOffset);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static int Number(Group group, int fallback)
    {
        return group.Success ? int.Parse(group.Value, CultureInfo.InvariantCulture) : fallback;
    }

    private static TimeSpan ParseOffset(string raw)
    {
        int hours = int.Parse(raw.AsSpan(1, 2), CultureInfo.InvariantCulture);
        int minutes = int.Parse(raw.AsSpan(3, 2), CultureInfo.InvariantCulture);
        if (minutes > 59)
        {
            throw new ArgumentOutOfRangeException(nameof(raw));
        }

        TimeSpan offset = new(hours, minutes, 0);
        return raw[0] == '-' ? offset.Negate() : offset;
    }
}
