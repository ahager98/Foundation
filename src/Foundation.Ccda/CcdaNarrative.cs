using System.Text;
using System.Xml.Linq;

namespace Foundation.Ccda;

/// <summary>
/// Renders CDA narrative markup (<c>paragraph</c>, <c>list</c>, <c>table</c>, <c>content</c>, ...) as
/// plain text. Recursion is bounded because the parser enforces the maximum element depth first.
/// </summary>
internal static class CcdaNarrative
{
    private const char CellBreak = '\t';
    private const char LineBreak = '\n';

    private static readonly HashSet<string> BlockElements = new(StringComparer.Ordinal)
    {
        "paragraph", "list", "item", "table", "caption", "thead", "tbody", "tfoot", "tr",
    };

    private static readonly HashSet<string> CellElements = new(StringComparer.Ordinal) { "td", "th" };

    internal static string ToPlainText(XElement element)
    {
        StringBuilder builder = new();
        Append(element, builder);
        return Normalize(builder.ToString());
    }

    private static void Append(XElement element, StringBuilder builder)
    {
        foreach (XNode node in element.Nodes())
        {
            if (node is XText text)
            {
                // Source line breaks and tabs are just whitespace; only markup creates structure.
                builder.Append(text.Value.Replace(CellBreak, ' ').Replace(LineBreak, ' '));
                continue;
            }

            if (node is not XElement child)
            {
                continue;
            }

            string name = child.Name.LocalName;
            if (name == "br")
            {
                builder.Append(LineBreak);
                continue;
            }

            bool isBlock = BlockElements.Contains(name);
            if (isBlock)
            {
                builder.Append(LineBreak);
            }

            Append(child, builder);
            if (isBlock)
            {
                builder.Append(LineBreak);
            }
            else if (CellElements.Contains(name))
            {
                builder.Append(CellBreak);
            }
        }
    }

    private static string Normalize(string raw)
    {
        List<string> lines = new();
        foreach (string line in raw.Split(LineBreak))
        {
            List<string> cells = new();
            foreach (string cell in line.Split(CellBreak))
            {
                string collapsed = CcdaPatterns.Whitespace().Replace(cell, " ").Trim();
                if (collapsed.Length > 0)
                {
                    cells.Add(collapsed);
                }
            }

            if (cells.Count > 0)
            {
                lines.Add(string.Join(" | ", cells));
            }
        }

        return string.Join(LineBreak, lines);
    }
}
