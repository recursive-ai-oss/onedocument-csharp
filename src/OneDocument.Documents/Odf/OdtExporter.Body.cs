using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// The body: blocks, paragraphs, runs, fields, bookmarks, notes, comments and tracked changes.
internal sealed partial class OdtExporter
{
    private sealed class ParaCtx
    {
        public StringBuilder Cur = new();
        public readonly List<(string Xml, string? Break)> Done = [];
        public string? CurBreak;
        public bool CurHasContent;
        public bool LastText;
        public bool TocOpen;
        public bool TocClose;
        public string TocLevels = "3";
    }

    private sealed class FieldFrame
    {
        public readonly StringBuilder Instr = new();
        public bool InInstr = true;
        public bool Collect;
        public readonly StringBuilder Xml = new();
        public readonly StringBuilder Plain = new();
        public string Instruction = string.Empty;
    }

    private Stack<FieldFrame> _fields = new();
    private readonly StringBuilder _trash = new();
    private readonly Dictionary<string, string> _bookmarkNames = new(StringComparer.Ordinal);
    private int _footnoteCount, _endnoteCount, _changeCount, _tableCount, _frameCount, _imageCount;
    private readonly HashSet<string> _annotationOpen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _annotationEmitted = new(StringComparer.Ordinal);

    // ---- blocks ----------------------------------------------------------------------------------------------------------------

    private void BodyBlocks(StringBuilder sb, MarkupNode body)
    {
        foreach (MarkupNode c in body.ChildElements)
        {
            _ct.ThrowIfCancellationRequested();
            Block(sb, c);
        }
    }

    private void Block(StringBuilder sb, MarkupNode n)
    {
        if (n.NamespaceUri is not (W or WStrict))
        {
            if (n.NamespaceUri == Mc)
            {
                // A block-level alternate content: the first choice carries the blocks.
                foreach (MarkupNode choice in n.ChildElements)
                {
                    foreach (MarkupNode k in choice.ChildElements)
                    {
                        Block(sb, k);
                    }

                    break;
                }
            }

            return;
        }

        switch (n.LocalNameString)
        {
            case "p":
                Paragraph(sb, n);
                break;
            case "tbl":
                CloseLists(sb);
                Table(sb, n);
                break;
            case "sdt":
            {
                Lost("loss.content-controls", "content controls", "Content controls were read through: their text is kept, the controls and data bindings are not.");
                MarkupNode content = Child(n, "sdtContent");
                foreach (MarkupNode k in content.ChildElements)
                {
                    Block(sb, k);
                }

                break;
            }

            case "ins":
            case "del":
            case "moveTo":
            case "moveFrom":
            case "customXml":
            case "smartTag":
                foreach (MarkupNode k in n.ChildElements)
                {
                    Block(sb, k);
                }

                break;
        }
    }

    // ---- paragraphs --------------------------------------------------------------------------------------------------------------

    private void Paragraph(StringBuilder sb, MarkupNode p)
    {
        _depth++;
        try
        {
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecursionDepth), _ctx.Limits.MaxRecursionDepth, _depth, DefectLocation.InPart(_part, Math.Max(0, p.SourceStart)));
            MarkupNode pPr = Child(p, "pPr");
            string? styleId = A(Child(pPr, "pStyle"), "val") ?? _defaultParagraph?.Id;
            var c = new ParaCtx();
            Inline(c, p);
            if (c.Cur.Length > 0 || c.Done.Count == 0 || c.CurBreak is not null)
            {
                c.Done.Add((c.Cur.ToString(), c.CurBreak));
            }
            else
            {
                c.Done.Add((string.Empty, null));
            }

            bool numbered = NumberingOf(pPr, styleId, out int numId, out int ilvl);
            if (c.TocOpen)
            {
                CloseLists(sb);
                sb.Append("<text:table-of-content text:protected=\"false\" text:name=\"Table of Contents").Append(OdfUnits.I(++_tocCount)).Append("\"><text:table-of-content-source text:outline-level=\"").Append(c.TocLevels).Append("\"/><text:index-body>");
            }

            if (numbered)
            {
                EnterListItem(sb, numId, ilvl);
            }
            else
            {
                CloseLists(sb);
            }

            int outline = EffectiveOutline(styleId, pPr);
            string? parent = OdfStyleName(styleId, "paragraph") ?? "Standard";
            string attrs = ParaAttrs(pPr, out string children);
            string? master = TakeMaster();
            bool allEmpty = System.Linq.Enumerable.All(c.Done, d => d.Xml.Length == 0);
            string markAttrs = string.Empty;
            if (allEmpty)
            {
                // The paragraph mark's own formatting sizes an empty paragraph.
                markAttrs = TextAttrs(Child(pPr, "rPr"));
            }

            for (int i = 0; i < c.Done.Count; i++)
            {
                (string xml, string? brk) = c.Done[i];
                string a = attrs;
                if (i > 0 && brk is null && c.Done[i - 1].Break is null)
                {
                    brk = null;
                }

                string brkAttr = brk == "column" ? " fo:break-before=\"column\"" : brk == "page" ? " fo:break-before=\"page\"" : string.Empty;
                if (brkAttr.Length > 0 && !a.Contains("fo:break-before", StringComparison.Ordinal))
                {
                    a += brkAttr;
                }

                string extra = string.Empty;
                if (i == 0 && master is not null)
                {
                    extra = " style:master-page-name=\"" + master + "\"";
                    a = a.Replace(" fo:break-before=\"page\"", string.Empty, StringComparison.Ordinal);
                }

                string styleName;
                if (a.Length == 0 && children.Length == 0 && extra.Length == 0 && markAttrs.Length == 0)
                {
                    styleName = parent;
                }
                else
                {
                    string kids = ParaPropsXml(a, children) + (allEmpty ? TextPropsXml(markAttrs) : string.Empty);
                    styleName = AddAuto("paragraph", "P", parent, extra, kids);
                }

                string tag = outline > 0 ? "text:h" : "text:p";
                sb.Append('<').Append(tag);
                if (outline > 0)
                {
                    sb.Append(" text:outline-level=\"").Append(OdfUnits.I(outline)).Append('"');
                }

                sb.Append(" text:style-name=\"").Append(Esc(styleName)).Append('"');
                if (xml.Length == 0)
                {
                    sb.Append("/>");
                }
                else
                {
                    sb.Append('>').Append(xml).Append("</").Append(tag).Append('>');
                }
            }

            if (c.TocClose)
            {
                CloseLists(sb);
                sb.Append("</text:index-body></text:table-of-content>");
            }

            if (!Child(pPr, "sectPr").IsNull)
            {
                SectionEnded();
            }
        }
        finally
        {
            _depth--;
        }
    }

    private int _tocCount;

    // ---- inline content ------------------------------------------------------------------------------------------------------------

    private StringBuilder Sink(ParaCtx c)
    {
        if (_fields.Count > 0)
        {
            FieldFrame f = _fields.Peek();
            if (f.InInstr)
            {
                _trash.Clear();
                return _trash;
            }

            if (f.Collect)
            {
                return f.Xml;
            }
        }

        return c.Cur;
    }

    private void Put(ParaCtx c, string xml, string plain)
    {
        StringBuilder sink = Sink(c);
        sink.Append(xml);
        if (ReferenceEquals(sink, c.Cur))
        {
            c.CurHasContent = true;
        }
        else if (!ReferenceEquals(sink, _trash) && _fields.Count > 0)
        {
            _fields.Peek().Plain.Append(plain);
        }
    }

    private void Inline(ParaCtx c, MarkupNode parent)
    {
        foreach (MarkupNode n in parent.ChildElements)
        {
            _ct.ThrowIfCancellationRequested();
            if (n.NamespaceUri == MNs)
            {
                if (n.LocalNameString is "oMath" or "oMathPara")
                {
                    Lost("loss.equations", "equations", "Equations are written as their linear text; the equation objects are not carried.");
                    string text = n.InnerText;
                    var rb = new RunBuf();
                    AppendText(c, rb, text);
                    Put(c, rb.Xml.ToString(), rb.Plain.ToString());
                }

                continue;
            }

            if (n.NamespaceUri == Mc)
            {
                InlineAlternate(c, n);
                continue;
            }

            if (n.NamespaceUri is not (W or WStrict))
            {
                continue;
            }

            switch (n.LocalNameString)
            {
                case "r":
                    Run(c, n);
                    break;
                case "hyperlink":
                    Hyperlink(c, n);
                    break;
                case "fldSimple":
                    SimpleField(c, n);
                    break;
                case "bookmarkStart":
                {
                    string? name = A(n, "name");
                    string? id = A(n, "id");
                    if (name is not null && id is not null && name != "_GoBack")
                    {
                        _bookmarkNames[id] = name;
                        Put(c, "<text:bookmark-start text:name=\"" + Esc(name) + "\"/>", string.Empty);
                    }

                    break;
                }

                case "bookmarkEnd":
                    if (A(n, "id") is { } bid && _bookmarkNames.TryGetValue(bid, out string? bn))
                    {
                        Put(c, "<text:bookmark-end text:name=\"" + Esc(bn) + "\"/>", string.Empty);
                    }

                    break;
                case "ins":
                case "moveTo":
                    InsertionRegion(c, n);
                    break;
                case "del":
                case "moveFrom":
                    DeletionRegion(c, n);
                    break;
                case "sdt":
                    Lost("loss.content-controls", "content controls", "Content controls were read through: their text is kept, the controls and data bindings are not.");
                    Inline(c, Child(n, "sdtContent"));
                    break;
                case "smartTag":
                case "customXml":
                case "dir":
                case "bdo":
                    Inline(c, n);
                    break;
                case "commentRangeStart":
                    CommentStart(c, A(n, "id"));
                    break;
                case "commentRangeEnd":
                    CommentEnd(c, A(n, "id"));
                    break;
            }
        }
    }

    private void InlineAlternate(ParaCtx c, MarkupNode alt)
    {
        foreach (MarkupNode choice in alt.ChildElements)
        {
            Inline(c, choice);
            break;
        }
    }

    // ---- runs -----------------------------------------------------------------------------------------------------------------------

    private sealed class RunBuf
    {
        public readonly StringBuilder Xml = new();
        public readonly StringBuilder Plain = new();
    }

    private static void AppendText(ParaCtx c, RunBuf rb, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == ' ')
            {
                int n = 1;
                while (i + 1 < text.Length && text[i + 1] == ' ')
                {
                    n++;
                    i++;
                }

                rb.Plain.Append(' ', n);
                int rest = n;
                if (c.LastText)
                {
                    rb.Xml.Append(' ');
                    rest--;
                }

                if (rest > 0)
                {
                    rb.Xml.Append("<text:s").Append(rest > 1 ? " text:c=\"" + OdfUnits.I(rest) + "\"" : string.Empty).Append("/>");
                }

                c.LastText = false;
            }
            else if (ch == '\t')
            {
                rb.Xml.Append("<text:tab/>");
                rb.Plain.Append('\t');
                c.LastText = false;
            }
            else if (ch is '\n' or '\r')
            {
                rb.Xml.Append("<text:line-break/>");
                rb.Plain.Append('\n');
                c.LastText = false;
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
            }
            else if (ch < ' ' || ch is '\uFFFE' or '\uFFFF')
            {
                // Characters XML cannot carry are left out.
            }
            else
            {
                string s = ch switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", _ => ch.ToString() };
                rb.Xml.Append(s);
                rb.Plain.Append(ch);
                c.LastText = true;
            }
        }
    }

    private string? SpanStyle(MarkupNode rPr, string? charStyleId)
    {
        string? parent = OdfStyleName(charStyleId, "character");
        string attrs = TextAttrs(rPr);
        if (attrs.Length == 0)
        {
            return parent;
        }

        return AddAuto("text", "T", parent, string.Empty, TextPropsXml(attrs));
    }

    private void FlushRun(ParaCtx c, RunBuf rb, string? style)
    {
        if (rb.Xml.Length == 0)
        {
            return;
        }

        string xml = style is null ? rb.Xml.ToString() : "<text:span text:style-name=\"" + Esc(style) + "\">" + rb.Xml + "</text:span>";
        Put(c, xml, rb.Plain.ToString());
        rb.Xml.Clear();
        rb.Plain.Clear();
    }

    private void Run(ParaCtx c, MarkupNode r)
    {
        MarkupNode rPr = Child(r, "rPr");
        string? style = SpanStyle(rPr, A(Child(rPr, "rStyle"), "val"));
        var rb = new RunBuf();
        foreach (MarkupNode n in r.ChildElements)
        {
            if (n.NamespaceUri == Mc)
            {
                FlushRun(c, rb, style);
                InlineAlternate2(c, n);
                continue;
            }

            if (n.NamespaceUri is not (W or WStrict))
            {
                continue;
            }

            switch (n.LocalNameString)
            {
                case "t":
                case "delText":
                    AppendText(c, rb, n.InnerText);
                    break;
                case "tab":
                    AppendText(c, rb, "\t");
                    break;
                case "cr":
                    AppendText(c, rb, "\n");
                    break;
                case "br":
                {
                    string? type = A(n, "type");
                    if (type is "page" or "column")
                    {
                        FlushRun(c, rb, style);
                        Break(c, type);
                    }
                    else
                    {
                        AppendText(c, rb, "\n");
                    }

                    break;
                }

                case "noBreakHyphen":
                    AppendText(c, rb, "\u2011");
                    break;
                case "softHyphen":
                    // The ODF 1.3 schema has no soft-hyphen element: a soft hyphen is the character U+00AD in the text.
                    AppendText(c, rb, "\u00AD");
                    break;
                case "sym":
                    if (A(n, "char") is { } hex && int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int cp) && cp > 0)
                    {
                        AppendText(c, rb, ((char)cp).ToString());
                    }

                    break;
                case "pgNum":
                    FlushRun(c, rb, style);
                    Put(c, "<text:page-number text:select-page=\"current\">1</text:page-number>", "1");
                    break;
                case "drawing":
                case "pict":
                case "object":
                    FlushRun(c, rb, style);
                    Put(c, Drawing(n), string.Empty);
                    c.LastText = false;
                    break;
                case "footnoteReference":
                case "endnoteReference":
                    FlushRun(c, rb, style);
                    Put(c, Note(n, n.LocalNameString == "endnoteReference"), string.Empty);
                    c.LastText = false;
                    break;
                case "commentReference":
                    FlushRun(c, rb, style);
                    CommentReference(c, A(n, "id"));
                    break;
                case "fldChar":
                    FlushRun(c, rb, style);
                    FieldChar(c, A(n, "fldCharType"));
                    break;
                case "instrText":
                    if (_fields.Count > 0 && _fields.Peek().InInstr)
                    {
                        _fields.Peek().Instr.Append(n.InnerText);
                    }

                    break;
                case "ruby":
                    Lost("loss.ruby", "ruby annotations", "Ruby text is written as plain text.");
                    AppendText(c, rb, n.InnerText);
                    break;
            }
        }

        FlushRun(c, rb, style);
    }

    private void InlineAlternate2(ParaCtx c, MarkupNode alt)
    {
        foreach (MarkupNode choice in alt.ChildElements)
        {
            foreach (MarkupNode k in choice.ChildElements)
            {
                if (k.NamespaceUri is W or WStrict && k.LocalNameString is "drawing" or "pict")
                {
                    Put(c, Drawing(k), string.Empty);
                    c.LastText = false;
                }
            }

            break;
        }
    }

    // A page or column break inside a paragraph: the text after it goes in a new paragraph that starts a page (ODF has no inline page break).
    private void Break(ParaCtx c, string type)
    {
        if (_fields.Count > 0 && (_fields.Peek().InInstr || _fields.Peek().Collect))
        {
            return;
        }

        if (c.CurHasContent)
        {
            c.Done.Add((c.Cur.ToString(), c.CurBreak));
            c.Cur = new StringBuilder();
            c.CurHasContent = false;
            c.LastText = false;
        }

        c.CurBreak = type;
    }

    // ---- hyperlinks and fields ---------------------------------------------------------------------------------------------------------

    private void Hyperlink(ParaCtx c, MarkupNode h)
    {
        string? href = null;
        if (RA(h, "id") is { } rid && RelationshipOf(rid) is { } rel)
        {
            href = rel.Target;
        }

        if (A(h, "anchor") is { } anchor)
        {
            href = (href ?? string.Empty) + "#" + anchor;
        }

        var frame = new FieldFrame { InInstr = false, Collect = href is not null };
        if (href is null)
        {
            Inline(c, h);
            return;
        }

        _fields.Push(frame);
        Inline(c, h);
        _fields.Pop();
        Put(c, "<text:a xlink:type=\"simple\" xlink:href=\"" + Esc(href) + "\">" + frame.Xml + "</text:a>", frame.Plain.ToString());
    }

    private void SimpleField(ParaCtx c, MarkupNode f)
    {
        string instr = A(f, "instr") ?? string.Empty;
        var frame = new FieldFrame { InInstr = false };
        frame.Instr.Append(instr);
        ResolveField(c, frame);
        _fields.Push(frame);
        Inline(c, f);
        _fields.Pop();
        EndField(c, frame);
    }

    private void FieldChar(ParaCtx c, string? type)
    {
        switch (type)
        {
            case "begin":
                _fields.Push(new FieldFrame());
                break;
            case "separate":
                if (_fields.Count > 0 && _fields.Peek().InInstr)
                {
                    FieldFrame f = _fields.Peek();
                    f.InInstr = false;
                    ResolveField(c, f);
                }

                break;
            case "end":
                if (_fields.Count > 0)
                {
                    FieldFrame f = _fields.Pop();
                    if (f.InInstr)
                    {
                        f.InInstr = false;
                        ResolveField(c, f);
                    }

                    EndField(c, f);
                }

                break;
        }
    }

    private static string FieldCode(string instr)
    {
        string t = instr.Trim();
        int sp = t.IndexOfAny([' ', '\t']);
        return (sp < 0 ? t : t[..sp]).ToUpperInvariant();
    }

    private void ResolveField(ParaCtx c, FieldFrame f)
    {
        f.Instruction = f.Instr.ToString();
        switch (FieldCode(f.Instruction))
        {
            case "PAGE" or "NUMPAGES" or "SECTIONPAGES" or "DATE" or "CREATEDATE" or "SAVEDATE" or "PRINTDATE" or "TIME" or "TITLE" or "SUBJECT" or "AUTHOR" or "KEYWORDS" or "COMMENTS"
                or "FILENAME" or "HYPERLINK" or "REF" or "PAGEREF" or "NOTEREF" or "SEQ":
                f.Collect = true;
                break;
            case "TOC":
                c.TocOpen = true;
                c.TocLevels = TocLevels(f.Instruction);
                f.Collect = false;
                break;
            default:
                Lost("loss.fields", "fields", "Fields other than page numbers, dates, document properties, references, hyperlinks, sequences and the table of contents keep their last result as text.");
                f.Collect = false;
                break;
        }
    }

    private static string TocLevels(string instr)
    {
        int o = instr.IndexOf("\\o", StringComparison.Ordinal);
        if (o >= 0)
        {
            int dash = instr.IndexOf('-', o);
            if (dash > 0)
            {
                int end = dash + 1;
                while (end < instr.Length && char.IsAsciiDigit(instr[end]))
                {
                    end++;
                }

                if (end > dash + 1)
                {
                    return instr[(dash + 1)..end];
                }
            }
        }

        return "3";
    }

    private static string? Switch(string instr, string name)
    {
        int i = instr.IndexOf(name, StringComparison.Ordinal);
        if (i < 0)
        {
            return null;
        }

        int p = i + name.Length;
        while (p < instr.Length && instr[p] == ' ')
        {
            p++;
        }

        if (p >= instr.Length)
        {
            return null;
        }

        if (instr[p] == '"')
        {
            int q = instr.IndexOf('"', p + 1);
            return q < 0 ? instr[(p + 1)..] : instr[(p + 1)..q];
        }

        int e = p;
        while (e < instr.Length && instr[e] != ' ')
        {
            e++;
        }

        return instr[p..e];
    }

    private static string? FirstArgument(string instr)
    {
        string t = instr.Trim();
        int sp = t.IndexOf(' ', StringComparison.Ordinal);
        if (sp < 0)
        {
            return null;
        }

        string rest = t[(sp + 1)..].TrimStart();
        if (rest.StartsWith('"'))
        {
            int q = rest.IndexOf('"', 1);
            return q < 0 ? rest[1..] : rest[1..q];
        }

        int e = rest.IndexOf(' ', StringComparison.Ordinal);
        return e < 0 ? rest : rest[..e];
    }

    private void EndField(ParaCtx c, FieldFrame f)
    {
        string code = FieldCode(f.Instruction);
        if (code == "TOC")
        {
            c.TocClose = true;
            return;
        }

        if (!f.Collect)
        {
            return;
        }

        string plain = f.Plain.ToString();
        string text = Esc(plain);
        string xml;
        switch (code)
        {
            case "PAGE":
                xml = "<text:page-number text:select-page=\"current\">" + text + "</text:page-number>";
                break;
            case "NUMPAGES" or "SECTIONPAGES":
                xml = "<text:page-count>" + text + "</text:page-count>";
                break;
            case "DATE" or "CREATEDATE" or "SAVEDATE" or "PRINTDATE":
                xml = "<text:date text:fixed=\"false\">" + text + "</text:date>";
                break;
            case "TIME":
                xml = "<text:time text:fixed=\"false\">" + text + "</text:time>";
                break;
            case "TITLE":
                xml = "<text:title>" + text + "</text:title>";
                break;
            case "SUBJECT":
                xml = "<text:subject>" + text + "</text:subject>";
                break;
            case "AUTHOR":
                xml = "<text:initial-creator>" + text + "</text:initial-creator>";
                break;
            case "KEYWORDS":
                xml = "<text:keywords>" + text + "</text:keywords>";
                break;
            case "COMMENTS":
                xml = "<text:description>" + text + "</text:description>";
                break;
            case "FILENAME":
                xml = "<text:file-name text:display=\"name-and-extension\">" + text + "</text:file-name>";
                break;
            case "HYPERLINK":
            {
                string? target = FirstArgument(f.Instruction);
                string? local = Switch(f.Instruction, "\\l");
                string href = (target ?? string.Empty) + (local is null ? string.Empty : "#" + local);
                xml = href.Length == 0 ? f.Xml.ToString() : "<text:a xlink:type=\"simple\" xlink:href=\"" + Esc(href) + "\">" + f.Xml + "</text:a>";
                break;
            }

            case "REF" or "NOTEREF":
                xml = FirstArgument(f.Instruction) is { } rn ? "<text:bookmark-ref text:reference-format=\"text\" text:ref-name=\"" + Esc(rn) + "\">" + text + "</text:bookmark-ref>" : f.Xml.ToString();
                break;
            case "PAGEREF":
                xml = FirstArgument(f.Instruction) is { } pn ? "<text:bookmark-ref text:reference-format=\"page\" text:ref-name=\"" + Esc(pn) + "\">" + text + "</text:bookmark-ref>" : f.Xml.ToString();
                break;
            case "SEQ":
                xml = FirstArgument(f.Instruction) is { } sn
                    ? "<text:sequence text:name=\"" + Esc(sn) + "\" text:formula=\"ooow:" + Esc(sn) + "+1\" style:num-format=\"1\">" + text + "</text:sequence>"
                    : f.Xml.ToString();
                break;
            default:
                xml = f.Xml.ToString();
                break;
        }

        Put(c, xml, plain);
    }

    // ---- notes ------------------------------------------------------------------------------------------------------------------------

    private string Note(MarkupNode reference, bool endnote)
    {
        MarkupNode root = endnote ? _endnotesRoot : _footnotesRoot;
        string? id = A(reference, "id");
        MarkupNode note = default;
        if (!root.IsNull && id is not null)
        {
            foreach (MarkupNode n in root.ChildElements)
            {
                if (IsW(n, endnote ? "endnote" : "footnote") && A(n, "id") == id)
                {
                    note = n;
                    break;
                }
            }
        }

        if (note.IsNull)
        {
            return string.Empty;
        }

        int count = endnote ? ++_endnoteCount : ++_footnoteCount;
        var body = new StringBuilder();
        SubContext(() =>
        {
            foreach (MarkupNode c in note.ChildElements)
            {
                Block(body, c);
            }

            CloseLists(body);
        });
        string cls = endnote ? "endnote" : "footnote";
        return "<text:note text:id=\"" + (endnote ? "edn" : "ftn") + OdfUnits.I(count) + "\" text:note-class=\"" + cls + "\"><text:note-citation>" + OdfUnits.I(count)
            + "</text:note-citation><text:note-body>" + body + "</text:note-body></text:note>";
    }

    // ---- comments ---------------------------------------------------------------------------------------------------------------------

    private MarkupNode CommentNode(string? id)
    {
        if (id is null || _commentsRoot.IsNull)
        {
            return default;
        }

        foreach (MarkupNode n in _commentsRoot.ChildElements)
        {
            if (IsW(n, "comment") && A(n, "id") == id)
            {
                return n;
            }
        }

        return default;
    }

    private string AnnotationXml(string id, MarkupNode comment)
    {
        var body = new StringBuilder();
        SubContext(() =>
        {
            foreach (MarkupNode c in comment.ChildElements)
            {
                Block(body, c);
            }

            CloseLists(body);
        });
        string author = A(comment, "author") ?? string.Empty;
        string date = A(comment, "date") ?? string.Empty;
        string initials = A(comment, "initials") ?? string.Empty;
        return "<office:annotation office:name=\"__Annotation__" + Esc(id) + "\">"
            + (author.Length > 0 ? "<dc:creator>" + Esc(author) + "</dc:creator>" : string.Empty)
            + (date.Length > 0 ? "<dc:date>" + Esc(date.TrimEnd('Z')) + "</dc:date>" : string.Empty)
            + (initials.Length > 0 ? "<meta:creator-initials>" + Esc(initials) + "</meta:creator-initials>" : string.Empty)
            + body + "</office:annotation>";
    }

    private void CommentStart(ParaCtx c, string? id)
    {
        MarkupNode comment = CommentNode(id);
        if (comment.IsNull || id is null || !_annotationEmitted.Add(id))
        {
            return;
        }

        _annotationOpen.Add(id);
        Put(c, AnnotationXml(id, comment), string.Empty);
    }

    private void CommentEnd(ParaCtx c, string? id)
    {
        if (id is not null && _annotationOpen.Remove(id))
        {
            Put(c, "<office:annotation-end office:name=\"__Annotation__" + Esc(id) + "\"/>", string.Empty);
        }
    }

    private void CommentReference(ParaCtx c, string? id)
    {
        // A comment whose range markers are missing is written where it is referenced, with no range.
        MarkupNode comment = CommentNode(id);
        if (comment.IsNull || id is null || !_annotationEmitted.Add(id))
        {
            return;
        }

        Put(c, AnnotationXml(id, comment), string.Empty);
    }

    // ---- tracked changes -------------------------------------------------------------------------------------------------------------------

    private static string ChangeInfo(MarkupNode n) =>
        "<office:change-info>" + (A(n, "author") is { Length: > 0 } a ? "<dc:creator>" + Esc(a) + "</dc:creator>" : string.Empty)
        + (A(n, "date") is { Length: > 0 } d ? "<dc:date>" + Esc(d.TrimEnd('Z')) + "</dc:date>" : string.Empty) + "</office:change-info>";

    private void InsertionRegion(ParaCtx c, MarkupNode ins)
    {
        string id = "ct" + OdfUnits.I(++_changeCount);
        _changeRegions.Append("<text:changed-region xml:id=\"").Append(id).Append("\" text:id=\"").Append(id).Append("\"><text:insertion>").Append(ChangeInfo(ins)).Append("</text:insertion></text:changed-region>");
        Put(c, "<text:change-start text:change-id=\"" + id + "\"/>", string.Empty);
        Inline(c, ins);
        Put(c, "<text:change-end text:change-id=\"" + id + "\"/>", string.Empty);
    }

    private void DeletionRegion(ParaCtx c, MarkupNode del)
    {
        string id = "ct" + OdfUnits.I(++_changeCount);
        var text = new StringBuilder();
        CollectDeleted(del, text, 0);
        var rb = new RunBuf();
        bool saved = c.LastText;
        c.LastText = false;
        AppendText(c, rb, text.ToString());
        c.LastText = saved;
        _changeRegions.Append("<text:changed-region xml:id=\"").Append(id).Append("\" text:id=\"").Append(id).Append("\"><text:deletion>").Append(ChangeInfo(del)).Append("<text:p>").Append(rb.Xml).Append("</text:p></text:deletion></text:changed-region>");
        Put(c, "<text:change text:change-id=\"" + id + "\"/>", string.Empty);
    }

    private static void CollectDeleted(MarkupNode n, StringBuilder text, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        foreach (MarkupNode k in n.ChildElements)
        {
            if (k.NamespaceUri is W or WStrict)
            {
                switch (k.LocalNameString)
                {
                    case "delText" or "t":
                        text.Append(k.InnerText);
                        continue;
                    case "tab":
                        text.Append('\t');
                        continue;
                    case "br" or "cr":
                        text.Append('\n');
                        continue;
                }
            }

            CollectDeleted(k, text, depth + 1);
        }
    }
}
