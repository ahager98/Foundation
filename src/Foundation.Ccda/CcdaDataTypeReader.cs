using System.Globalization;
using System.Xml.Linq;

namespace Foundation.Ccda;

/// <summary>Reads HL7 v3 data types (II, CD, PQ, TS, IVL_TS, PN, AD, TEL, ANY) from elements.</summary>
internal sealed class CcdaDataTypeReader
{
    private readonly CcdaNarrativeIndex _references;

    internal CcdaDataTypeReader(CcdaNarrativeIndex references)
    {
        _references = references;
    }

    internal static IReadOnlyList<CcdaIdentifier> ReadIdentifiers(XElement? parent, string localName = "id")
    {
        List<CcdaIdentifier> identifiers = new();
        foreach (XElement id in parent.Children(localName))
        {
            string? root = id.Attr("root");
            string? extension = id.Attr("extension");
            string? nullFlavor = id.Attr("nullFlavor");
            if (root is null && extension is null && nullFlavor is null)
            {
                continue;
            }

            identifiers.Add(new CcdaIdentifier { Root = root, Extension = extension, NullFlavor = nullFlavor });
        }

        return identifiers;
    }

    internal static CcdaQuantity? ReadQuantity(XElement? element)
    {
        string? raw = element.Attr("value");
        string? unit = element.Attr("unit");
        if (raw is null && unit is null)
        {
            return null;
        }

        bool isNumber = decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value);
        return new CcdaQuantity { Value = isNumber ? value : null, Unit = unit, RawValue = raw };
    }

    internal static CcdaTimestamp? ReadTimestamp(XElement? element)
    {
        return CcdaTimestamp.TryParse(element.Attr("value"), out CcdaTimestamp? timestamp) ? timestamp : null;
    }

    internal static CcdaInterval? ReadInterval(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        CcdaTimestamp? value = ReadTimestamp(element);
        CcdaTimestamp? low = ReadTimestamp(element.Child("low"));
        CcdaTimestamp? high = ReadTimestamp(element.Child("high"));
        if (value is null && low is null && high is null)
        {
            return null;
        }

        return new CcdaInterval { Low = low, High = high, Value = value };
    }

    internal static IReadOnlyList<CcdaPersonName> ReadNames(XElement? parent)
    {
        List<CcdaPersonName> names = new();
        foreach (XElement name in parent.Children("name"))
        {
            CcdaPersonName? read = ReadName(name);
            if (read is not null)
            {
                names.Add(read);
            }
        }

        return names;
    }

    internal static IReadOnlyList<CcdaAddress> ReadAddresses(XElement? parent)
    {
        List<CcdaAddress> addresses = new();
        foreach (XElement address in parent.Children("addr"))
        {
            CcdaAddress read = new()
            {
                Use = address.Attr("use"),
                StreetLines = Texts(address, "streetAddressLine"),
                City = address.Child("city").TextOf(),
                State = address.Child("state").TextOf(),
                PostalCode = address.Child("postalCode").TextOf(),
                Country = address.Child("country").TextOf(),
                NullFlavor = address.Attr("nullFlavor"),
            };

            bool isEmpty = read.StreetLines.Count == 0 && read.City is null && read.State is null
                && read.PostalCode is null && read.Country is null && read.NullFlavor is null;
            if (!isEmpty)
            {
                addresses.Add(read);
            }
        }

        return addresses;
    }

    internal static IReadOnlyList<CcdaTelecom> ReadTelecoms(XElement? parent)
    {
        List<CcdaTelecom> telecoms = new();
        foreach (XElement telecom in parent.Children("telecom"))
        {
            string? value = telecom.Attr("value");
            if (value is not null)
            {
                telecoms.Add(new CcdaTelecom { Value = value, Use = telecom.Attr("use") });
            }
        }

        return telecoms;
    }

    internal CcdaCode? ReadCode(XElement? element)
    {
        return ReadCode(element, isTranslation: false);
    }

    internal IReadOnlyList<CcdaCode> ReadCodes(IEnumerable<XElement?> elements)
    {
        List<CcdaCode> codes = new();
        foreach (XElement? element in elements)
        {
            CcdaCode? code = ReadCode(element);
            if (code is not null)
            {
                codes.Add(code);
            }
        }

        return codes;
    }

    internal CcdaValue? ReadValue(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        string? type = element.XsiType();
        CcdaQuantity? quantity = null;
        CcdaCode? code = null;
        string? text = null;
        switch (type)
        {
            case "PQ" or "INT" or "REAL" or "MO":
                quantity = ReadQuantity(element);
                break;
            case "CD" or "CE" or "CV" or "CO" or "CS":
                code = ReadCode(element);
                break;
            case "ST" or "ED":
                text = ResolveText(element);
                break;
            case "IVL_PQ":
                text = DescribeRange(element);
                break;
            default:
                if (element.Attr("code") is not null)
                {
                    code = ReadCode(element);
                }
                else
                {
                    text = element.Attr("value") ?? ResolveText(element);
                }

                break;
        }

        string? nullFlavor = element.Attr("nullFlavor");
        if (quantity is null && code is null && text is null && nullFlavor is null)
        {
            return null;
        }

        return new CcdaValue { XsiType = type, Quantity = quantity, Code = code, Text = text, NullFlavor = nullFlavor };
    }

    /// <summary>
    /// Text of an <c>originalText</c> or <c>text</c> element: the narrative it references when the
    /// reference resolves, otherwise its own content.
    /// </summary>
    internal string? ResolveText(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        string? reference = element.Child("reference").Attr("value");
        if (reference is not null && reference.StartsWith('#'))
        {
            string? resolved = _references.Resolve(reference.Substring(1));
            if (resolved is not null)
            {
                return resolved;
            }
        }

        string own = CcdaNarrative.ToPlainText(element);
        return own.Length == 0 ? null : own;
    }

    /// <summary>Renders an <c>IVL_PQ</c> as <c>low-high unit</c>, <c>&gt;=low unit</c> or <c>&lt;=high unit</c>.</summary>
    internal static string? DescribeRange(XElement element)
    {
        CcdaQuantity? low = ReadQuantity(element.Child("low"));
        CcdaQuantity? high = ReadQuantity(element.Child("high"));
        string? unit = low?.Unit ?? high?.Unit;
        string? range = (low?.RawValue, high?.RawValue) switch
        {
            (string l, string h) => $"{l}-{h}",
            (string l, null) => $">={l}",
            (null, string h) => $"<={h}",
            _ => null,
        };

        if (range is null)
        {
            return null;
        }

        return unit is null ? range : $"{range} {unit}";
    }

    private static CcdaPersonName? ReadName(XElement name)
    {
        IReadOnlyList<string> prefixes = Texts(name, "prefix");
        IReadOnlyList<string> given = Texts(name, "given");
        string? family = Texts(name, "family").FirstOrDefault();
        IReadOnlyList<string> suffixes = Texts(name, "suffix");
        string? nullFlavor = name.Attr("nullFlavor");

        List<string> parts = new(prefixes);
        parts.AddRange(given);
        if (family is not null)
        {
            parts.Add(family);
        }

        parts.AddRange(suffixes);
        string? text = parts.Count > 0 ? string.Join(' ', parts) : name.TextOf();

        if (text is null && nullFlavor is null)
        {
            return null;
        }

        return new CcdaPersonName
        {
            Use = name.Attr("use"),
            Prefixes = prefixes,
            Given = given,
            Family = family,
            Suffixes = suffixes,
            Text = text,
            NullFlavor = nullFlavor,
        };
    }

    private static IReadOnlyList<string> Texts(XElement parent, string localName)
    {
        List<string> texts = new();
        foreach (XElement child in parent.Children(localName))
        {
            string? text = child.TextOf();
            if (text is not null)
            {
                texts.Add(text);
            }
        }

        return texts;
    }

    private CcdaCode? ReadCode(XElement? element, bool isTranslation)
    {
        if (element is null)
        {
            return null;
        }

        // Translations of translations are not followed: the depth is bounded and real documents do not nest them.
        List<CcdaCode> translations = new();
        if (!isTranslation)
        {
            foreach (XElement translation in element.Children("translation"))
            {
                CcdaCode? read = ReadCode(translation, isTranslation: true);
                if (read is not null)
                {
                    translations.Add(read);
                }
            }
        }

        CcdaCode code = new()
        {
            Code = element.Attr("code"),
            CodeSystem = element.Attr("codeSystem"),
            CodeSystemName = element.Attr("codeSystemName"),
            DisplayName = element.Attr("displayName"),
            OriginalText = ResolveText(element.Child("originalText")),
            NullFlavor = element.Attr("nullFlavor"),
            Translations = translations,
        };

        bool isEmpty = code.Code is null && code.DisplayName is null && code.OriginalText is null
            && code.NullFlavor is null && translations.Count == 0;
        return isEmpty ? null : code;
    }
}
