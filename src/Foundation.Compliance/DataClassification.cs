namespace Foundation.Compliance;

/// <summary>Kind of regulated data a field holds.</summary>
public enum DataClassification
{
    /// <summary>Protected health information: care level, health and mobility details, insurance, notes, form responses.</summary>
    Phi,

    /// <summary>Personally identifiable information that is not health data: names, birth dates, contact details, addresses.</summary>
    Pii
}
