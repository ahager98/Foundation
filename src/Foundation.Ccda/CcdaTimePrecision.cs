namespace Foundation.Ccda;

/// <summary>How much of an HL7 timestamp was present in the document.</summary>
public enum CcdaTimePrecision
{
    /// <summary><c>YYYY</c>.</summary>
    Year = 1,

    /// <summary><c>YYYYMM</c>.</summary>
    Month,

    /// <summary><c>YYYYMMDD</c>.</summary>
    Day,

    /// <summary><c>YYYYMMDDHH</c>.</summary>
    Hour,

    /// <summary><c>YYYYMMDDHHMM</c>.</summary>
    Minute,

    /// <summary><c>YYYYMMDDHHMMSS</c>, with or without fractional seconds.</summary>
    Second,
}
