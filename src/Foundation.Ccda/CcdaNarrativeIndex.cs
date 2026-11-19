using System.Xml.Linq;

namespace Foundation.Ccda;

/// <summary>
/// Resolves <c>reference value="#id"</c> pointers to the plain text of the narrative element carrying
/// that <c>ID</c>. Elements are indexed once; text is rendered on first lookup and cached.
/// </summary>
internal sealed class CcdaNarrativeIndex
{
    private readonly Dictionary<string, XElement> _elements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _texts = new(StringComparer.Ordinal);

    internal CcdaNarrativeIndex(XElement root)
    {
        foreach (XElement element in root.Descendants())
        {
            string? id = element.Attr("ID");
            if (id is not null)
            {
                _elements.TryAdd(id, element);
            }
        }
    }

    internal string? Resolve(string id)
    {
        if (_texts.TryGetValue(id, out string? cached))
        {
            return cached;
        }

        string? text = null;
        if (_elements.TryGetValue(id, out XElement? element))
        {
            string rendered = CcdaNarrative.ToPlainText(element);
            text = rendered.Length == 0 ? null : rendered;
        }

        _texts[id] = text;
        return text;
    }
}
