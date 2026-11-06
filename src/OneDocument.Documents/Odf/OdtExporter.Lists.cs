using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// Lists: each w:num becomes a text:list-style; consecutive numbered paragraphs become nested text:list / text:list-item elements.
internal sealed partial class OdtExporter
{
    private sealed class ListState
    {
        public readonly List<int> Stack = [];
        public int NumId;
        public readonly HashSet<int> Started = [];
    }

    private ListState _lists = new();
    private readonly SortedSet<int> _usedLists = [];

    private string ListStyleName(int numId)
    {
        _usedLists.Add(numId);
        return "WWNum" + OdfUnits.I(numId);
    }

    // The numbering definition of a paragraph: direct w:numPr, else the one its style carries (ECMA-376 Part 1 §17.9.23).
    private bool NumberingOf(MarkupNode pPr, string? styleId, out int numId, out int ilvl)
    {
        MarkupNode numPr = Child(pPr, "numPr");
        MarkupNode styleNum = default;
        if (numPr.IsNull || Child(numPr, "numId").IsNull)
        {
            styleNum = StyleProp(styleId, "pPr", "numPr");
        }

        MarkupNode idNode = numPr.IsNull ? default : Child(numPr, "numId");
        MarkupNode lvlNode = numPr.IsNull ? default : Child(numPr, "ilvl");
        if (idNode.IsNull && !styleNum.IsNull)
        {
            idNode = Child(styleNum, "numId");
            if (lvlNode.IsNull)
            {
                lvlNode = Child(styleNum, "ilvl");
            }
        }

        numId = idNode.IsNull ? 0 : Int(A(idNode, "val"));
        ilvl = Math.Clamp(lvlNode.IsNull ? 0 : Int(A(lvlNode, "val")), 0, 8);
        return numId > 0 && !_numberingRoot.IsNull;
    }

    private void EnterListItem(StringBuilder sb, int numId, int ilvl)
    {
        if (_lists.Stack.Count > 0 && _lists.NumId != numId)
        {
            CloseLists(sb);
        }

        if (_lists.Stack.Count == 0)
        {
            bool cont = !_lists.Started.Add(numId);
            sb.Append("<text:list text:style-name=\"").Append(ListStyleName(numId)).Append('"');
            if (cont)
            {
                sb.Append(" text:continue-numbering=\"true\"");
            }

            sb.Append("><text:list-item>");
            _lists.Stack.Add(numId);
            _lists.NumId = numId;
        }
        else
        {
            while (_lists.Stack.Count - 1 > ilvl)
            {
                sb.Append("</text:list-item></text:list>");
                _lists.Stack.RemoveAt(_lists.Stack.Count - 1);
            }

            if (_lists.Stack.Count - 1 == ilvl)
            {
                sb.Append("</text:list-item><text:list-item>");
            }
        }

        while (_lists.Stack.Count - 1 < ilvl)
        {
            sb.Append("<text:list><text:list-item>");
            _lists.Stack.Add(numId);
        }
    }

    private void CloseLists(StringBuilder sb)
    {
        while (_lists.Stack.Count > 0)
        {
            sb.Append("</text:list-item></text:list>");
            _lists.Stack.RemoveAt(_lists.Stack.Count - 1);
        }
    }

    // A nested part (table cell, note, comment, header, text box) has its own list and field state.
    private void SubContext(Action body)
    {
        ListState savedLists = _lists;
        Stack<FieldFrame> savedFields = _fields;
        string? savedMaster = _pendingMaster;
        _lists = new ListState();
        _fields = new Stack<FieldFrame>();
        _pendingMaster = null;
        _depth++;
        try
        {
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecursionDepth), _ctx.Limits.MaxRecursionDepth, _depth, DefectLocation.InPart(_part));
            body();
        }
        finally
        {
            _depth--;
            _lists = savedLists;
            _fields = savedFields;
            _pendingMaster = savedMaster;
        }
    }

    // ---- list styles ----------------------------------------------------------------------------------------------------------------

    private MarkupNode NumNode(int numId)
    {
        foreach (MarkupNode n in _numberingRoot.ChildElements)
        {
            if (IsW(n, "num") && Int(A(n, "numId"), -1) == numId)
            {
                return n;
            }
        }

        return default;
    }

    private MarkupNode AbstractNode(int abstractId)
    {
        foreach (MarkupNode n in _numberingRoot.ChildElements)
        {
            if (IsW(n, "abstractNum") && Int(A(n, "abstractNumId"), -1) == abstractId)
            {
                return n;
            }
        }

        return default;
    }

    private static string BulletChar(string text)
    {
        if (text.Length == 0)
        {
            return "\u2022";
        }

        char c = text[0];
        return c switch
        {
            '\uF0B7' => "\u2022",
            '\uF0A7' => "\u25AA",
            '\uF0D8' => "\u27A2",
            '\uF0FC' => "\u2714",
            '\uF076' => "\u2756",
            >= '\uF000' and <= '\uF0FF' => "\u2022",
            'o' => "\u25E6",
            _ => text[..1],
        };
    }

    private string BuildListStyles()
    {
        if (_numberingRoot.IsNull)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (int numId in _usedLists.ToArray())
        {
            MarkupNode num = NumNode(numId);
            if (num.IsNull)
            {
                continue;
            }

            MarkupNode abs = AbstractNode(Int(A(Child(num, "abstractNumId"), "val"), -1));
            sb.Append("<text:list-style style:name=\"").Append(ListStyleName(numId)).Append("\">");
            for (int ilvl = 0; ilvl < 9; ilvl++)
            {
                MarkupNode lvl = default;
                MarkupNode overrideStart = default;
                foreach (MarkupNode o in num.ChildElements)
                {
                    if (IsW(o, "lvlOverride") && Int(A(o, "ilvl"), -1) == ilvl)
                    {
                        MarkupNode ol = Child(o, "lvl");
                        if (!ol.IsNull)
                        {
                            lvl = ol;
                        }

                        overrideStart = Child(o, "startOverride");
                    }
                }

                if (lvl.IsNull && !abs.IsNull)
                {
                    foreach (MarkupNode l in abs.ChildElements)
                    {
                        if (IsW(l, "lvl") && Int(A(l, "ilvl"), -1) == ilvl)
                        {
                            lvl = l;
                            break;
                        }
                    }
                }

                if (lvl.IsNull)
                {
                    continue;
                }

                EmitListLevel(sb, lvl, ilvl + 1, overrideStart);
            }

            sb.Append("</text:list-style>");
        }

        return sb.ToString();
    }

    private void EmitListLevel(StringBuilder sb, MarkupNode lvl, int level, MarkupNode startOverride)
    {
        string fmt = A(Child(lvl, "numFmt"), "val") ?? "decimal";
        string text = A(Child(lvl, "lvlText"), "val") ?? string.Empty;
        int start = !startOverride.IsNull ? Int(A(startOverride, "val"), 1) : Int(A(Child(lvl, "start"), "val"), 1);
        MarkupNode pPr = Child(lvl, "pPr");
        MarkupNode ind = Child(pPr, "ind");
        int left = Int(A(ind, "left") ?? A(ind, "start"));
        int hanging = Int(A(ind, "hanging"));
        int first = Int(A(ind, "firstLine"));
        string suff = A(Child(lvl, "suff"), "val") ?? "tab";
        string followed = suff switch { "space" => "space", "nothing" => "nothing", _ => "listtab" };
        string props = "<style:list-level-properties text:list-level-position-and-space-mode=\"label-alignment\"><style:list-level-label-alignment text:label-followed-by=\"" + followed + "\""
            + (followed == "listtab" ? " text:list-tab-stop-position=\"" + Cm(left) + "\"" : string.Empty)
            + " fo:text-indent=\"" + Cm(hanging > 0 ? -hanging : first) + "\" fo:margin-left=\"" + Cm(left) + "\"/></style:list-level-properties>";
        string rText = TextPropsXml(TextAttrs(Child(lvl, "rPr")));
        if (fmt == "bullet")
        {
            sb.Append("<text:list-level-style-bullet text:level=\"").Append(OdfUnits.I(level)).Append("\" text:bullet-char=\"").Append(Esc(BulletChar(text))).Append("\">").Append(props).Append(rText).Append("</text:list-level-style-bullet>");
            return;
        }

        string numFormat = fmt switch
        {
            "lowerLetter" => "a",
            "upperLetter" => "A",
            "lowerRoman" => "i",
            "upperRoman" => "I",
            "none" => string.Empty,
            _ => "1",
        };

        // lvlText is "prefix%1.%2suffix": the placeholders give display-levels, the text before the first and after the last give prefix and suffix.
        int firstPh = text.IndexOf('%', StringComparison.Ordinal);
        int lastPh = text.LastIndexOf('%');
        int displayLevels = 0;
        for (int i = 0; i < text.Length - 1; i++)
        {
            if (text[i] == '%' && char.IsAsciiDigit(text[i + 1]))
            {
                displayLevels++;
            }
        }

        string prefix = firstPh > 0 ? text[..firstPh] : string.Empty;
        string suffix = lastPh >= 0 && lastPh + 2 <= text.Length ? text[(lastPh + 2)..] : string.Empty;
        if (firstPh < 0)
        {
            prefix = text;
        }

        sb.Append("<text:list-level-style-number text:level=\"").Append(OdfUnits.I(level)).Append('"');
        if (prefix.Length > 0)
        {
            sb.Append(" style:num-prefix=\"").Append(Esc(prefix)).Append('"');
        }

        if (suffix.Length > 0)
        {
            sb.Append(" style:num-suffix=\"").Append(Esc(suffix)).Append('"');
        }

        sb.Append(" style:num-format=\"").Append(numFormat).Append('"');
        if (displayLevels > 1)
        {
            sb.Append(" text:display-levels=\"").Append(OdfUnits.I(Math.Min(displayLevels, level))).Append('"');
        }

        if (start != 1)
        {
            sb.Append(" text:start-value=\"").Append(OdfUnits.I(start)).Append('"');
        }

        sb.Append('>').Append(props).Append(rText).Append("</text:list-level-style-number>");
    }
}
