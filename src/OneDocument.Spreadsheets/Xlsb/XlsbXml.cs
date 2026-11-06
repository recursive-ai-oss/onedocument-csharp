using System.Globalization;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>Small readers over the markup tree for the XML-to-binary direction.</summary>
internal static class XlsbXml
{
    public static IEnumerable<MarkupNode> Elements(this MarkupNode parent, string local)
    {
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (c.LocalName.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(local)))
            {
                yield return c;
            }
        }
    }

    public static MarkupNode Element(this MarkupNode parent, string local)
    {
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (c.LocalName.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(local)))
            {
                return c;
            }
        }

        return default;
    }

    public static bool Is(this MarkupNode n, string local) => !n.IsNull && n.LocalName.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(local));

    public static int Int(this MarkupNode n, string attribute, int fallback = 0) =>
        n.GetAttribute(attribute) is { } v && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : fallback;

    public static long Long(this MarkupNode n, string attribute, long fallback = 0) =>
        n.GetAttribute(attribute) is { } v && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long r) ? r : fallback;

    public static double Dbl(this MarkupNode n, string attribute, double fallback = 0) =>
        n.GetAttribute(attribute) is { } v && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : fallback;

    /// <summary>Reads an XML boolean (<c>1</c>, <c>true</c>, <c>0</c>, <c>false</c>); an absent attribute gives <paramref name="fallback"/>.</summary>
    public static bool Bool(this MarkupNode n, string attribute, bool fallback = false) =>
        n.GetAttribute(attribute) switch { "1" or "true" => true, "0" or "false" => false, _ => fallback };

    /// <summary>Reads an element used as a switch whose <c>val</c> defaults to true (<c>&lt;b/&gt;</c>, <c>&lt;b val="0"/&gt;</c>).</summary>
    public static bool Switch(this MarkupNode n) => n.GetAttribute("val") is not ("0" or "false");

    public static string Str(this MarkupNode n, string attribute, string fallback = "") => n.GetAttribute(attribute) ?? fallback;

    public static bool TryRaw(this MarkupNode n, out int type, out byte[] data)
    {
        type = 0;
        data = [];
        if (!XlsbMarkup.IsRaw(n))
        {
            return false;
        }

        type = n.Int("t");
        try
        {
            data = Convert.FromBase64String(n.InnerText);
        }
        catch (FormatException)
        {
            data = [];
        }

        return true;
    }
}
