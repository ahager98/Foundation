namespace Foundation.Ccda.Tests;

/// <summary>Loads the synthetic fixtures and builds minimal documents for edge cases.</summary>
[ExcludeFromCodeCoverage]
internal static class CcdaSamples
{
    internal const string Namespaces =
        "xmlns=\"urn:hl7-org:v3\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"";

    private static readonly Lazy<string> Ccd = new(() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "ccd-sample.xml")));

    /// <summary>The synthetic CCD (no real patient data).</summary>
    internal static string CcdText => Ccd.Value;

    /// <summary>A minimal document whose structured body holds the given section markup.</summary>
    internal static string WithSections(string sectionsXml)
    {
        return $"<ClinicalDocument {Namespaces}><component><structuredBody>{sectionsXml}</structuredBody></component></ClinicalDocument>";
    }

    /// <summary>A section wrapped in its <c>component</c>.</summary>
    internal static string Section(string innerXml)
    {
        return $"<component><section>{innerXml}</section></component>";
    }
}
