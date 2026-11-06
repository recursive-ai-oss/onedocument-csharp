using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;

namespace RecursiveAI.OneDocument.Documents.Xml2003;

internal sealed partial class WordMl2003Converter
{
    // ECMA-376 Part 1 §17.3.1.26 (CT_PPrBase) then rPr, sectPr, pPrChange: children of a schema sequence must come in this order.
    private static readonly string[] PPrOrder =
    [
        "pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr", "widowControl", "numPr", "suppressLineNumbers", "pBdr", "shd", "tabs",
        "suppressAutoHyphens", "kinsoku", "wordWrap", "overflowPunct", "topLinePunct", "autoSpaceDE", "autoSpaceDN", "bidi", "adjustRightInd",
        "snapToGrid", "spacing", "ind", "contextualSpacing", "mirrorIndents", "suppressOverlap", "jc", "textDirection", "textAlignment",
        "textboxTightWrap", "outlineLvl", "divId", "cnfStyle", "rPr", "sectPr", "pPrChange",
    ];

    // ECMA-376 Part 1 §17.3.2 (EG_RPrBase).
    private static readonly string[] RPrOrder =
    [
        "ins", "del", "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike", "outline", "shadow", "emboss", "imprint",
        "noProof", "snapToGrid", "vanish", "webHidden", "color", "spacing", "w", "kern", "position", "sz", "szCs", "highlight", "u", "effect", "bdr",
        "shd", "fitText", "vertAlign", "rtl", "cs", "em", "lang", "eastAsianLayout", "specVanish", "oMath",
    ];

    // §17.4.58 (CT_TblPrBase), §17.4.82 (trPr is a choice, any order), §17.4.70 (CT_TcPr).
    private static readonly string[] TblPrOrder =
    [
        "tblStyle", "tblpPr", "tblOverlap", "bidiVisual", "tblStyleRowBandSize", "tblStyleColBandSize", "tblW", "jc", "tblCellSpacing", "tblInd",
        "tblBorders", "shd", "tblLayout", "tblCellMar", "tblLook",
    ];

    private static readonly string[] TrPrOrder =
        ["cnfStyle", "divId", "gridBefore", "gridAfter", "wBefore", "wAfter", "cantSplit", "trHeight", "tblHeader", "tblCellSpacing", "jc", "hidden"];

    private static readonly string[] TcPrOrder =
        ["cnfStyle", "tcW", "gridSpan", "hMerge", "vMerge", "tcBorders", "shd", "noWrap", "tcMar", "textDirection", "tcFitText", "vAlign", "hideMark"];

    // §17.6.17 (CT_SectPrBase); header and footer references come first.
    private static readonly string[] SectPrOrder =
    [
        "hdr", "ftr", "footnotePr", "endnotePr", "type", "pgSz", "pgMar", "paperSrc", "pgBorders", "lnNumType", "pgNumType", "cols", "formProt",
        "vAlign", "noEndnote", "titlePg", "textDirection", "bidi", "rtlGutter", "docGrid",
    ];

    private static readonly string[] ParagraphBorderOrder = ["top", "left", "bottom", "right", "between", "bar"];
    private static readonly string[] TableBorderOrder = ["top", "left", "bottom", "right", "insideH", "insideV"];
    private static readonly string[] CellBorderOrder = ["top", "left", "bottom", "right", "insideH", "insideV", "tl2br", "tr2bl"];
    private static readonly string[] MarginOrder = ["top", "left", "bottom", "right"];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["listPr"] = "numPr",
        ["ilfo"] = "numId",
        ["nfc"] = "numFmt",
        ["legal"] = "isLgl",
        ["lsid"] = "nsid",
        ["plt"] = "multiLevelType",
    };

    private static string Canon(string local) => Aliases.TryGetValue(local, out string? alias) ? alias : Camel(local);

    private Revision? _paraMark;

    private readonly record struct PropEntry(string Name, MarkupNode Node);

    // Reads the property children of a bag (flattening the wx: groups that wrap borders), puts them in schema order and writes them under
    // the wrapper; writes nothing when no child is known. Unknown children are counted as loss, never written.
    private void EmitProps(Story s, MarkupNode props, string wrapper, string[] order, List<PropEntry>? extras = null, int gridSpan = 0, Action? attributes = null)
    {
        var entries = new List<PropEntry>();
        if (!props.IsNull)
        {
            Gather(props, entries, 0);
        }

        if (extras is not null)
        {
            entries.AddRange(extras);
        }

        if (gridSpan > 1)
        {
            entries.Add(new PropEntry("gridSpan", default));
        }

        var ranked = new List<(int Rank, int Index, PropEntry Entry)>();
        for (int i = 0; i < entries.Count; i++)
        {
            int rank = Array.IndexOf(order, entries[i].Name);
            if (rank < 0)
            {
                if (entries[i].Name is not ("pPrChange" or "rPrChange" or "tblPrChange" or "trPrChange" or "tcPrChange" or "sectPrChange" or "tblPrEx"))
                {
                    Unmapped(entries[i].Name);
                }
                else
                {
                    Lost("loss.formatting-revisions", "formatting revisions", "Tracked formatting changes were dropped; the current formatting was kept.");
                }

                continue;
            }

            ranked.Add((rank, i, entries[i]));
        }

        if (ranked.Count == 0 && attributes is null)
        {
            return;
        }

        ranked = Deduplicate(ranked, props);
        ranked.Sort(static (a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Index.CompareTo(b.Index));
        s.W.WriteStartElement(wrapper);
        attributes?.Invoke();
        foreach ((_, _, PropEntry e) in ranked)
        {
            if (e.Name == "gridSpan" && e.Node.IsNull)
            {
                s.W.WriteStartElement("w:gridSpan");
                s.W.WriteAttribute("w:val", gridSpan.ToString(CultureInfo.InvariantCulture));
                s.W.WriteEndElement();
                continue;
            }

            EmitProp(s, e);
        }

        s.W.WriteEndElement();
    }

    // A property that appears twice in one bag (LibreOffice writes w:ind twice) is invalid; the last one wins, as it would on reading.
    private List<(int Rank, int Index, PropEntry Entry)> Deduplicate(List<(int Rank, int Index, PropEntry Entry)> ranked, MarkupNode at)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<(int Rank, int Index, PropEntry Entry)>(ranked.Count);
        for (int i = ranked.Count - 1; i >= 0; i--)
        {
            string name = ranked[i].Entry.Name;
            if (name is "hdr" or "ftr" or "lvl" or "tblStylePr" || seen.Add(name))
            {
                kept.Add(ranked[i]);
            }
            else
            {
                Defect(DefectKind.Structure, "wml2003.duplicate-property", DefectSeverity.Repaired, "The property " + name + " appears twice in one property list; the last one was kept.", at);
            }
        }

        kept.Reverse();
        return kept;
    }

    private static void Gather(MarkupNode props, List<PropEntry> into, int depth)
    {
        if (depth > 8)
        {
            return;
        }

        foreach (MarkupNode c in props.ChildElements)
        {
            int ns = Ns2003.Of(c);
            if (ns == Ns2003.Word)
            {
                into.Add(new PropEntry(Canon(c.LocalNameString), c));
            }
            else if (ns == Ns2003.Aux && HasElementChild(c))
            {
                Gather(c, into, depth + 1);
            }
        }
    }

    private static bool HasElementChild(MarkupNode n)
    {
        foreach (MarkupNode unused in n.ChildElements)
        {
            _ = unused;
            return true;
        }

        return false;
    }

    private void EmitProp(Story s, PropEntry e)
    {
        switch (e.Name)
        {
            case "numPr":
                EmitNumPr(s, e.Node);
                break;
            case "rPr":
                EmitRPr(s, e.Node, _paraMark);
                break;
            case "sectPr":
                EmitSectPr(s, e.Node);
                break;
            case "hdr" or "ftr":
                EmitHeaderFooterReference(s, e.Node, e.Name == "hdr");
                break;
            case "pBdr":
                EmitGroup(s, e.Node, "w:pBdr", ParagraphBorderOrder);
                break;
            case "tblBorders":
                EmitGroup(s, e.Node, "w:tblBorders", TableBorderOrder);
                break;
            case "tcBorders":
                EmitGroup(s, e.Node, "w:tcBorders", CellBorderOrder);
                break;
            case "tblCellMar":
                EmitGroup(s, e.Node, "w:tblCellMar", MarginOrder);
                break;
            case "tcMar":
                EmitGroup(s, e.Node, "w:tcMar", MarginOrder);
                break;
            case "pgBorders":
                EmitGroup(s, e.Node, "w:pgBorders", MarginOrder, copyAttributes: true);
                break;
            case "hMerge":
                break;
            case "tabs":
                if (HasElementChild(e.Node))
                {
                    EmitGeneric(s, e.Node, e.Name, 0);
                }

                break;
            case "pgMar":
                EmitPageMargins(s, e.Node);
                break;
            case "pPr":
                EmitProps(s, e.Node, "w:pPr", PPrOrder);
                break;
            case "tblPr":
                EmitProps(s, e.Node, "w:tblPr", TblPrOrder);
                break;
            case "trPr":
                EmitProps(s, e.Node, "w:trPr", TrPrOrder);
                break;
            case "tcPr":
                EmitProps(s, e.Node, "w:tcPr", TcPrOrder);
                break;
            case "tblStylePr":
                EmitTableStylePart(s, e.Node);
                break;
            case "lvl":
                EmitLvl(s, e.Node);
                break;
            case "numFmt" when e.Node.LocalNameString == "nfc":
                EmitNumberFormat(s, e.Node);
                break;
            case "suff" or "multiLevelType":
                s.W.WriteStartElement("w:" + e.Name);
                s.W.WriteAttribute("w:val", FirstLower(Attr(e.Node, "val") ?? string.Empty));
                s.W.WriteEndElement();
                break;
            case "nsid" or "tmpl":
                if (Attr(e.Node, "val") is { Length: 8 } hex && uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
                {
                    s.W.WriteStartElement("w:" + e.Name);
                    s.W.WriteAttribute("w:val", hex);
                    s.W.WriteEndElement();
                }

                break;
            case "lvlPicBulletId":
                break;
            default:
                EmitGeneric(s, e.Node, e.Name, 0);
                break;
        }
    }

    private void EmitGroup(Story s, MarkupNode group, string wrapper, string[] order, bool copyAttributes = false)
    {
        var entries = new List<PropEntry>();
        Gather(group, entries, 0);
        var kept = new List<(int Rank, int Index, PropEntry Entry)>();
        for (int i = 0; i < entries.Count; i++)
        {
            int rank = Array.IndexOf(order, entries[i].Name);
            if (rank >= 0)
            {
                kept.Add((rank, i, entries[i]));
            }
        }

        if (kept.Count == 0)
        {
            return;
        }

        kept.Sort(static (a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Index.CompareTo(b.Index));
        s.W.WriteStartElement(wrapper);
        if (copyAttributes)
        {
            WriteAttributes(s, group, group.LocalNameString);
        }

        foreach ((_, _, PropEntry e) in kept)
        {
            EmitGeneric(s, e.Node, e.Name, 0);
        }

        s.W.WriteEndElement();
    }

    private void WriteAttributes(Story s, MarkupNode n, string element)
    {
        foreach (MarkupAttributeRef a in n.Attributes)
        {
            if (a.IsNamespaceDeclaration)
            {
                continue;
            }

            string local = Encoding.UTF8.GetString(a.LocalName);
            if (a.Prefix.Length > 0 && Ns2003.OfAttribute(a) != Ns2003.Word)
            {
                continue;
            }

            if (MapValue(n, element, local, a.Value) is { } mapped)
            {
                s.W.WriteAttribute("w:" + Camel(local), mapped);
            }
        }
    }

    private void EmitGeneric(Story s, MarkupNode n, string name, int depth)
    {
        if (depth > 16)
        {
            return;
        }

        s.W.WriteStartElement("w:" + name);
        WriteAttributes(s, n, name);
        foreach (MarkupNode c in n.ChildElements)
        {
            if (Ns2003.Of(c) == Ns2003.Word)
            {
                EmitGeneric(s, c, Canon(c.LocalNameString), depth + 1);
            }
        }

        s.W.WriteEndElement();
    }

    // ECMA-376 Part 1 §17.6.11: all of top, right, bottom, left, header and footer are required, so a missing one gets Word's default.
    private void EmitPageMargins(Story s, MarkupNode pgMar)
    {
        s.W.WriteStartElement("w:pgMar");
        foreach ((string name, string fallback) in new[] { ("top", "1440"), ("right", "1440"), ("bottom", "1440"), ("left", "1440"), ("header", "720"), ("footer", "720"), ("gutter", "") })
        {
            string? value = Attr(pgMar, name);
            string? mapped = value is null ? null : MapValue(pgMar, "pgMar", name, value);
            if (mapped is null && fallback.Length > 0)
            {
                mapped = fallback;
            }

            if (mapped is not null)
            {
                s.W.WriteAttribute("w:" + name, mapped);
            }
        }

        s.W.WriteEndElement();
    }

    private static void EmitNumPr(Story s, MarkupNode listPr)
    {
        string? ilvl = null;
        string? ilfo = null;
        foreach (MarkupNode c in listPr.ChildElements)
        {
            if (Ns2003.Of(c) != Ns2003.Word)
            {
                continue;
            }

            string local = c.LocalNameString;
            if (local == "ilvl")
            {
                ilvl = Attr(c, "val");
            }
            else if (local == "ilfo")
            {
                ilfo = Attr(c, "val");
            }
        }

        s.W.WriteStartElement("w:numPr");
        if (ilvl is not null)
        {
            s.W.WriteStartElement("w:ilvl");
            s.W.WriteAttribute("w:val", ilvl);
            s.W.WriteEndElement();
        }

        if (ilfo is not null)
        {
            s.W.WriteStartElement("w:numId");
            s.W.WriteAttribute("w:val", ilfo);
            s.W.WriteEndElement();
        }

        s.W.WriteEndElement();
    }

    private void EmitPPr(Story s, MarkupNode pPr, MarkupNode section, Revision? mark)
    {
        var extras = new List<PropEntry>();
        bool ownSection = !pPr.IsNull && !Child(pPr, Ns2003.Word, "sectPr").IsNull;
        if (!section.IsNull && !ownSection)
        {
            extras.Add(new PropEntry("sectPr", section));
        }

        bool ownRPr = !pPr.IsNull && !Child(pPr, Ns2003.Word, "rPr").IsNull;
        if (mark is not null && !ownRPr)
        {
            extras.Add(new PropEntry("rPr", default));
        }

        _paraMark = mark;
        EmitProps(s, pPr, "w:pPr", PPrOrder, extras);
        _paraMark = null;
    }

    private void EmitRPr(Story s, MarkupNode rPr, Revision? mark)
    {
        var entries = new List<PropEntry>();
        if (!rPr.IsNull)
        {
            Gather(rPr, entries, 0);
        }

        var ranked = new List<(int Rank, int Index, PropEntry Entry)>();
        for (int i = 0; i < entries.Count; i++)
        {
            int rank = Array.IndexOf(RPrOrder, entries[i].Name);
            if (rank >= 0 && entries[i].Name is not ("ins" or "del"))
            {
                ranked.Add((rank, i, entries[i]));
            }
            else if (entries[i].Name is not ("ins" or "del" or "rPrChange" or "font"))
            {
                Unmapped(entries[i].Name);
            }
        }

        if (ranked.Count == 0 && mark is null)
        {
            return;
        }

        ranked.Sort(static (a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Index.CompareTo(b.Index));
        s.W.WriteStartElement("w:rPr");
        if (mark is { } m)
        {
            // The paragraph mark itself was inserted or deleted.
            s.W.WriteStartElement(m.Deleted ? "w:del" : "w:ins");
            s.W.WriteAttribute("w:id", m.Id.ToString(CultureInfo.InvariantCulture));
            s.W.WriteAttribute("w:author", m.Author ?? "unknown");
            if (m.Date is { Length: > 0 })
            {
                s.W.WriteAttribute("w:date", m.Date);
            }

            s.W.WriteEndElement();
        }

        foreach ((_, _, PropEntry e) in ranked)
        {
            EmitGeneric(s, e.Node, e.Name, 0);
        }

        s.W.WriteEndElement();
    }

    // ---- sections -----------------------------------------------------------------------------------------------------------------

    private void EmitSectPr(Story s, MarkupNode sect) => EmitProps(s, sect, "w:sectPr", SectPrOrder);

    private void EmitHeaderFooterReference(Story s, MarkupNode node, bool header)
    {
        string type = Attr(node, "type") switch
        {
            "even" => "even",
            "first" => "first",
            _ => "default",
        };
        if (type == "even")
        {
            _usesEvenOdd = true;
        }

        int number = header ? ++_headerCount : ++_footerCount;
        string partName = (header ? "/word/header" : "/word/footer") + number.ToString(CultureInfo.InvariantCulture) + ".xml";
        var story = new Story(partName);
        story.Begin(header ? "w:hdr" : "w:ftr");
        story.EndsWithParagraph = false;
        EmitBlocks(story, node, null, 4);
        if (!story.EndsWithParagraph)
        {
            story.W.WriteStartElement("w:p");
            story.W.WriteEndElement();
        }

        story.End();
        _extraParts.Add(story);
        string rid = _main.AddRel(RelBase + (header ? "header" : "footer"), partName[6..]);
        s.W.WriteStartElement(header ? "w:headerReference" : "w:footerReference");
        s.W.WriteAttribute("w:type", type);
        s.W.WriteAttribute("r:id", rid);
        s.W.WriteEndElement();
    }
}
