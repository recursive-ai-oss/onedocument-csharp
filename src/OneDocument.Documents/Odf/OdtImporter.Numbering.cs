using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// Lists: each text:list-style (and the outline style used for chapter numbering) becomes one abstract numbering definition; each text:list
// that starts a fresh numbering becomes a w:num that points at it, with start overrides when it restarts a definition already in use.
internal sealed partial class OdtImporter
{
    private sealed class NumInstance
    {
        public int NumId;
        public int AbstractId;
        public Dictionary<int, int> StartOverrides = [];
    }

    private readonly Dictionary<string, int> _abstractIds = new(StringComparer.Ordinal);
    private readonly List<(int Id, MarkupNode Style)> _abstractStyles = [];
    private readonly List<NumInstance> _nums = [];
    private readonly Dictionary<string, int> _sharedNums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _listIdNums = new(StringComparer.Ordinal);

    // The chapter numbering (text:outline-style) is a list style by another element name; headings refer to it by its style:name.
    private MarkupNode ListStyleElement(string name) =>
        (_styles.Find("list", name) ?? _styles.Find("outline", name))?.Element ?? default;

    private int AbstractFor(string styleName)
    {
        if (_abstractIds.TryGetValue(styleName, out int id))
        {
            return id;
        }

        MarkupNode el = ListStyleElement(styleName);
        if (el.IsNull)
        {
            return -1;
        }

        id = _abstractStyles.Count;
        _abstractIds[styleName] = id;
        _abstractStyles.Add((id, el));
        return id;
    }

    // The num shared by everything that uses a list style without starting its own list (a paragraph style's numbering, the first list of a style).
    private int? NumIdForStyle(string styleName)
    {
        if (_sharedNums.TryGetValue(styleName, out int n))
        {
            return n;
        }

        int abs = AbstractFor(styleName);
        if (abs < 0)
        {
            return null;
        }

        var inst = new NumInstance { NumId = _nums.Count + 1, AbstractId = abs };
        _nums.Add(inst);
        _sharedNums[styleName] = inst.NumId;
        return inst.NumId;
    }

    // A list that restarts its style's numbering: the first use of a style takes the shared num, later ones a new num with start overrides.
    private int? NewNumForList(string styleName)
    {
        int abs = AbstractFor(styleName);
        if (abs < 0)
        {
            return null;
        }

        if (!_sharedNums.ContainsKey(styleName) || !_sharedUsedByList.Contains(styleName))
        {
            _sharedUsedByList.Add(styleName);
            return NumIdForStyle(styleName);
        }

        var inst = new NumInstance { NumId = _nums.Count + 1, AbstractId = abs };
        MarkupNode style = _abstractStyles[abs].Style;
        foreach (MarkupNode lvl in style.ChildElements)
        {
            if (int.TryParse(A(lvl, OdfNamespaces.Text, "level"), NumberStyles.None, CultureInfo.InvariantCulture, out int level) && level is >= 1 and <= 9)
            {
                inst.StartOverrides[level - 1] = int.TryParse(A(lvl, OdfNamespaces.Text, "start-value"), NumberStyles.None, CultureInfo.InvariantCulture, out int sv) ? sv : 1;
            }
        }

        _nums.Add(inst);
        return inst.NumId;
    }

    private readonly HashSet<string> _sharedUsedByList = new(StringComparer.Ordinal);

    // A list item that restarts at a value: a new num over the same definition with an override for its level.
    private int NewNumWithStart(int numId, int level, int start)
    {
        NumInstance current = _nums[numId - 1];
        var inst = new NumInstance { NumId = _nums.Count + 1, AbstractId = current.AbstractId };
        inst.StartOverrides[level] = start;
        _nums.Add(inst);
        return inst.NumId;
    }

    private Story? BuildNumbering()
    {
        if (_nums.Count == 0 && _abstractStyles.Count == 0)
        {
            return null;
        }

        var story = new Story("/word/numbering.xml");
        story.Begin("w:numbering");
        foreach ((int id, MarkupNode style) in _abstractStyles)
        {
            var sb = new StringBuilder();
            sb.Append("<w:abstractNum w:abstractNumId=\"").Append(OdfUnits.I(id)).Append("\"><w:multiLevelType w:val=\"multilevel\"/>");
            var done = new HashSet<int>();
            foreach (MarkupNode lvl in style.ChildElements)
            {
                if (!int.TryParse(A(lvl, OdfNamespaces.Text, "level"), NumberStyles.None, CultureInfo.InvariantCulture, out int level) || level is < 1 or > 9 || !done.Add(level))
                {
                    continue;
                }

                EmitLevel(sb, style, lvl, level);
            }

            sb.Append("</w:abstractNum>");
            story.Raw(sb.ToString());
        }

        foreach (NumInstance n in _nums)
        {
            var sb = new StringBuilder();
            sb.Append("<w:num w:numId=\"").Append(OdfUnits.I(n.NumId)).Append("\"><w:abstractNumId w:val=\"").Append(OdfUnits.I(n.AbstractId)).Append("\"/>");
            foreach (KeyValuePair<int, int> o in n.StartOverrides.OrderBy(k => k.Key))
            {
                sb.Append("<w:lvlOverride w:ilvl=\"").Append(OdfUnits.I(o.Key)).Append("\"><w:startOverride w:val=\"").Append(OdfUnits.I(o.Value)).Append("\"/></w:lvlOverride>");
            }

            sb.Append("</w:num>");
            story.Raw(sb.ToString());
        }

        story.End();
        return story;
    }

    private static string NumFormat(string? f) => f switch
    {
        "1" => "decimal",
        "a" => "lowerLetter",
        "A" => "upperLetter",
        "i" => "lowerRoman",
        "I" => "upperRoman",
        "" => "none",
        null => "decimal",
        _ => "decimal",
    };

    private void EmitLevel(StringBuilder sb, MarkupNode style, MarkupNode lvl, int level)
    {
        string local = lvl.LocalNameString;
        bool bullet = local is "list-level-style-bullet" or "list-level-style-image";
        bool number = local is "list-level-style-number" or "outline-level-style";
        if (!bullet && !number)
        {
            return;
        }

        sb.Append("<w:lvl w:ilvl=\"").Append(OdfUnits.I(level - 1)).Append("\">");
        string text;
        if (bullet)
        {
            string ch = local == "list-level-style-bullet" ? A(lvl, OdfNamespaces.Text, "bullet-char") ?? "\u2022" : "\u2022";
            if (ch.Length == 0)
            {
                ch = "\u2022";
            }

            sb.Append("<w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/>");
            text = ch;
        }
        else
        {
            int start = int.TryParse(A(lvl, OdfNamespaces.Text, "start-value"), NumberStyles.None, CultureInfo.InvariantCulture, out int sv) ? sv : 1;
            string fmt = NumFormat(A(lvl, OdfNamespaces.Style, "num-format"));
            sb.Append("<w:start w:val=\"").Append(OdfUnits.I(start)).Append("\"/><w:numFmt w:val=\"").Append(fmt).Append("\"/>");
            int display = int.TryParse(A(lvl, OdfNamespaces.Text, "display-levels"), NumberStyles.None, CultureInfo.InvariantCulture, out int dl) ? Math.Clamp(dl, 1, level) : 1;
            string prefix = A(lvl, OdfNamespaces.Style, "num-prefix") ?? string.Empty;
            string suffix = A(lvl, OdfNamespaces.Style, "num-suffix") ?? string.Empty;
            var t = new StringBuilder(prefix);
            if (fmt != "none")
            {
                for (int k = level - display + 1; k <= level; k++)
                {
                    if (k > level - display + 1)
                    {
                        t.Append('.');
                    }

                    t.Append('%').Append(OdfUnits.I(k));
                }
            }

            t.Append(suffix);
            text = t.ToString();
        }

        MarkupNode props = OdfMarkup.Child(lvl, OdfNamespaces.Style, "list-level-properties");
        string followed = string.Empty;
        string ind = string.Empty;
        string tabs = string.Empty;
        if (!props.IsNull)
        {
            MarkupNode align = OdfMarkup.Child(props, OdfNamespaces.Style, "list-level-label-alignment");
            string? followedBy = align.IsNull ? null : A(align, OdfNamespaces.Text, "label-followed-by");
            followed = followedBy switch { "space" => "space", "nothing" => "nothing", _ => string.Empty };
            if (!align.IsNull)
            {
                int left = OdfUnits.Twips(A(align, OdfNamespaces.Fo, "margin-left"));
                int indent = OdfUnits.Twips(A(align, OdfNamespaces.Fo, "text-indent"));
                ind = "<w:ind w:left=\"" + OdfUnits.I(left) + "\"" + (indent < 0 ? " w:hanging=\"" + OdfUnits.I(-indent) + "\"" : indent > 0 ? " w:firstLine=\"" + OdfUnits.I(indent) + "\"" : string.Empty) + "/>";
                if (OdfUnits.TryTwips(A(align, OdfNamespaces.Text, "list-tab-stop-position"), out int tab) && tab > 0 && followedBy is null or "listtab")
                {
                    tabs = "<w:tabs><w:tab w:val=\"num\" w:pos=\"" + OdfUnits.I(tab) + "\"/></w:tabs>";
                }
            }
            else
            {
                int before = OdfUnits.Twips(A(props, OdfNamespaces.Text, "space-before"));
                int minWidth = OdfUnits.Twips(A(props, OdfNamespaces.Text, "min-label-width"));
                ind = "<w:ind w:left=\"" + OdfUnits.I(before + minWidth) + "\"" + (minWidth > 0 ? " w:hanging=\"" + OdfUnits.I(minWidth) + "\"" : string.Empty) + "/>";
            }
        }

        if (followed.Length > 0)
        {
            sb.Append("<w:suff w:val=\"").Append(followed).Append("\"/>");
        }

        sb.Append("<w:lvlText w:val=\"").Append(OdfUnits.Esc(text)).Append("\"/><w:lvlJc w:val=\"left\"/>");
        if (ind.Length > 0 || tabs.Length > 0)
        {
            sb.Append("<w:pPr>").Append(tabs).Append(ind).Append("</w:pPr>");
        }

        PropBag rpr = RunProps(OdfMarkup.Child(lvl, OdfNamespaces.Style, "text-properties"));
        if (A(lvl, OdfNamespaces.Text, "style-name") is { } cs)
        {
            (string? _, PropBag direct) = ResolveSpan(cs);
            rpr.Merge(direct);
        }

        if (bullet && !rpr.Has("rFonts") && IsSymbolBullet(text))
        {
            rpr.Set("rFonts", "<w:rFonts w:ascii=\"Symbol\" w:hAnsi=\"Symbol\" w:hint=\"default\"/>");
        }

        if (!rpr.IsEmpty)
        {
            sb.Append("<w:rPr>").Append(rpr.ToXml(WordOrder.RPr)).Append("</w:rPr>");
        }

        sb.Append("</w:lvl>");
        _ = style;
    }

    // A bullet in the private-use area of the Symbol font keeps that font; Word draws the glyph from it.
    private static bool IsSymbolBullet(string text) => text.Length == 1 && text[0] is >= '\uF000' and <= '\uF0FF';
}
