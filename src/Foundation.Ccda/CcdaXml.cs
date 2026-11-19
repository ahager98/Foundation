using System.Xml.Linq;

namespace Foundation.Ccda;

/// <summary>XML namespaces and small null-tolerant element helpers shared by the readers.</summary>
internal static class CcdaXml
{
    /// <summary>The HL7 v3 namespace every CDA element lives in.</summary>
    internal static readonly XNamespace V3 = "urn:hl7-org:v3";

    /// <summary>The SDTC extension namespace (e.g. <c>sdtc:raceCode</c>).</summary>
    internal static readonly XNamespace Sdtc = "urn:hl7-org:sdtc";

    /// <summary>The XML Schema instance namespace (<c>xsi:type</c>).</summary>
    internal static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    internal static XElement? Child(this XElement? parent, string localName)
    {
        return parent?.Element(V3 + localName);
    }

    internal static IEnumerable<XElement> Children(this XElement? parent, string localName)
    {
        return parent is null ? Enumerable.Empty<XElement>() : parent.Elements(V3 + localName);
    }

    internal static string? Attr(this XElement? element, string name)
    {
        string? value = element?.Attribute(name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static string? TextOf(this XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        string text = CcdaPatterns.Whitespace().Replace(element.Value, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    internal static string? XsiType(this XElement element)
    {
        string? value = element.Attribute(Xsi + "type")?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        int colon = value.IndexOf(':');
        return (colon < 0 ? value : value.Substring(colon + 1)).Trim();
    }

    internal static bool Is(this XElement element, string localName)
    {
        return element.Name == V3 + localName;
    }

    internal static bool HasTemplate(this XElement element, string root)
    {
        return element.Children("templateId").Any(t => t.Attr("root") == root);
    }

    internal static bool IsNegated(this XElement element)
    {
        return string.Equals(element.Attr("negationInd"), "true", StringComparison.OrdinalIgnoreCase);
    }
}
