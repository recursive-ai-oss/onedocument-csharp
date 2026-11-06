using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// The body: block elements (paragraphs, headings, lists, sections, indexes) and, in OdtImporter.Inline.cs, what lives inside a paragraph.
internal sealed partial class OdtImporter
{
    private sealed class ChangeInfo
    {
        public string Kind = string.Empty;
        public string? Author;
        public string? Date;
        public MarkupNode Deleted;
    }

    private readonly Dictionary<string, ChangeInfo> _changes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _annotationEnds = new(StringComparer.Ordinal);
    private readonly HashSet<MarkupNode> _fieldEndAt = [];
    private readonly Dictionary<MarkupNode, string> _fieldBeginAt = [];
    private readonly List<(int Id, MarkupNode Node)> _footnotes = [];
    private readonly List<(int Id, MarkupNode Node)> _endnotes = [];
    private readonly List<(int Id, MarkupNode Node)> _comments = [];
    private readonly Dictionary<string, int> _commentIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _bookmarkIds = new(StringComparer.Ordinal);

    private sealed class ListCtx
    {
        public ListFrame Frame = null!;
        public bool Pending;
        public bool Header;
    }

    private sealed class ListFrame
    {
        public int NumId;
        public int Level;
        public string StyleName = string.Empty;
    }

    // Pre-pass: tracked changes, where comment ranges end, and a count of drawing objects the converter cannot carry.
    private void ScanDocument(MarkupNode body)
    {
        Scan(body, 0);
    }

    private void Scan(MarkupNode node, int depth)
    {
        OfficeLimitExceeded(depth, node);
        foreach (MarkupNode c in node.ChildElements)
        {
            if (c.NamespaceUri == OdfNamespaces.Text && c.LocalNameString == "changed-region")
            {
                string? id = A(c, OdfNamespaces.Text, "id") ?? A(c, "http://www.w3.org/XML/1998/namespace", "id");
                if (id is not null)
                {
                    var info = new ChangeInfo();
                    foreach (MarkupNode k in c.ChildElements)
                    {
                        if (k.NamespaceUri != OdfNamespaces.Text)
                        {
                            continue;
                        }

                        info.Kind = k.LocalNameString;
                        MarkupNode ci = OdfMarkup.Child(k, OdfNamespaces.Office, "change-info");
                        if (!ci.IsNull)
                        {
                            info.Author = OdfMarkup.Child(ci, OdfNamespaces.DublinCore, "creator") is { IsNull: false } cr ? cr.InnerText : null;
                            info.Date = OdfMarkup.Child(ci, OdfNamespaces.DublinCore, "date") is { IsNull: false } dt ? NormalizeDate(dt.InnerText) : null;
                        }

                        if (info.Kind == "deletion")
                        {
                            info.Deleted = k;
                        }
                    }

                    _changes[id] = info;
                }

                continue;
            }

            if (c.NamespaceUri == OdfNamespaces.Office && c.LocalNameString == "annotation-end" && A(c, OdfNamespaces.Office, "name") is { } n)
            {
                _annotationEnds.Add(n);
            }

            Scan(c, depth + 1);
        }
    }

    private void OfficeLimitExceeded(int depth, MarkupNode at) =>
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecursionDepth), _ctx.Limits.MaxRecursionDepth, depth, DefectLocation.InPart("content.xml", Math.Max(0, at.SourceStart)));

    // ---- the body with its sections ----------------------------------------------------------------------------------------------

    private string? _currentMaster;

    private void EmitBody(MarkupNode body)
    {
        var blocks = new List<MarkupNode>();
        foreach (MarkupNode c in body.ChildElements)
        {
            blocks.Add(c);
        }

        _currentMaster = FirstMasterName(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            _ct.ThrowIfCancellationRequested();
            MarkupNode b = blocks[i];
            string? startsMaster = BlockMasterPage(b);
            if (i > 0 && startsMaster is not null)
            {
                if (!string.Equals(startsMaster, _currentMaster, StringComparison.Ordinal))
                {
                    // The previous block was the last of its section; its paragraph carried the section properties (see NextStartsSection).
                    _currentMaster = startsMaster;
                }
                else
                {
                    _forcePageBreak = true;
                }
            }

            string? sectPr = null;
            if (NextStartsSection(blocks, i, out string? nextMaster))
            {
                sectPr = SectPr(_currentMaster, continuous: false);
                _ = nextMaster;
            }

            EmitBlock(_main, b, null, sectPr);
        }

        if (!_sectionWritten)
        {
            // The final section's properties close the body.
        }

        _main.Raw(SectPr(_currentMaster, continuous: false, final: true));
    }

    private bool _sectionWritten;
    private bool _forcePageBreak;

    private string? FirstMasterName(List<MarkupNode> blocks)
    {
        foreach (MarkupNode b in blocks)
        {
            if (BlockMasterPage(b) is { } m)
            {
                return m;
            }

            // Only the first real block decides which master page the document starts with.
            if (b.NamespaceUri == OdfNamespaces.Text && b.LocalNameString is "p" or "h" or "list" or "section" || b.NamespaceUri == OdfNamespaces.Table)
            {
                break;
            }
        }

        foreach (OdfStyle s in _styles.All)
        {
            if (s.Family == "master-page")
            {
                return s.Name;
            }
        }

        return null;
    }

    // The master page a block starts, when its paragraph style (or a table's style) names one.
    private string? BlockMasterPage(MarkupNode b)
    {
        if (b.NamespaceUri == OdfNamespaces.Text && b.LocalNameString is "p" or "h")
        {
            return ResolveParagraph(A(b, OdfNamespaces.Text, "style-name")).MasterPage;
        }

        if (b.NamespaceUri == OdfNamespaces.Table && b.LocalNameString == "table" && A(b, OdfNamespaces.Table, "style-name") is { } t)
        {
            OdfStyle? s = _styles.Find("table", t);
            return s?.MasterPageName is { Length: > 0 } m ? m : null;
        }

        return null;
    }

    // Looks ahead: the block at i is the last of its section when the next block starts a different master page.
    private bool NextStartsSection(List<MarkupNode> blocks, int i, out string? nextMaster)
    {
        nextMaster = null;
        if (i + 1 >= blocks.Count)
        {
            return false;
        }

        string? m = BlockMasterPage(blocks[i + 1]);
        if (m is null || string.Equals(m, _currentMaster, StringComparison.Ordinal))
        {
            return false;
        }

        nextMaster = m;
        return true;
    }

    // ---- block dispatch ---------------------------------------------------------------------------------------------------------

    private void EmitBlocks(Story s, MarkupNode parent, ListCtx? list)
    {
        _depth++;
        try
        {
            CheckDepth(parent);
            foreach (MarkupNode c in parent.ChildElements)
            {
                EmitBlock(s, c, list, null);
            }
        }
        finally
        {
            _depth--;
        }
    }

    private void EmitBlock(Story s, MarkupNode n, ListCtx? list, string? sectPr)
    {
        _ct.ThrowIfCancellationRequested();
        string ns = n.NamespaceUri;
        string local = n.LocalNameString;
        if (ns == OdfNamespaces.Text)
        {
            switch (local)
            {
                case "p":
                    EmitParagraph(s, n, list, sectPr, heading: false);
                    return;
                case "h":
                    EmitParagraph(s, n, list, sectPr, heading: true);
                    return;
                case "list":
                    EmitList(s, n, list?.Frame);
                    EndSectionIfNeeded(s, sectPr);
                    return;
                case "section":
                    EmitSection(s, n, list);
                    EndSectionIfNeeded(s, sectPr);
                    return;
                case "table-of-content":
                case "illustration-index":
                case "table-index":
                case "object-index":
                case "user-index":
                case "alphabetical-index":
                case "bibliography":
                    EmitIndex(s, n, list);
                    EndSectionIfNeeded(s, sectPr);
                    return;
                case "numbered-paragraph":
                case "index-title":
                case "index-body":
                    EmitBlocks(s, n, list);
                    return;
                case "tracked-changes":
                case "sequence-decls":
                case "variable-decls":
                case "user-field-decls":
                case "dde-connection-decls":
                case "alphabetical-index-auto-mark-file":
                case "soft-page-break":
                    return;
                case "change":
                case "change-start":
                case "change-end":
                    HandleChangeMarker(s, n, null);
                    return;
            }
        }
        else if (ns == OdfNamespaces.Table && local == "table")
        {
            EmitTable(s, n, list);
            EndSectionIfNeeded(s, sectPr);
            return;
        }
        else if (ns == OdfNamespaces.Office && local is "forms" or "scripts" or "annotation" or "annotation-end")
        {
            if (local == "forms")
            {
                Lost("loss.forms", "form controls", "Form controls were dropped.");
            }

            return;
        }
        else if (ns == OdfNamespaces.Draw)
        {
            Lost("loss.shapes", "drawing shapes", "Drawing shapes outside a paragraph were dropped.");
            return;
        }

        Unmapped(n);
    }

    // Anything that is not a paragraph ends its section in an empty paragraph that carries the section properties.
    private void EndSectionIfNeeded(Story s, string? sectPr)
    {
        if (sectPr is null)
        {
            return;
        }

        s.Raw("<w:p><w:pPr>" + sectPr + "</w:pPr></w:p>");
        s.EndsWithParagraph = true;
        _sectionWritten = true;
    }

    private void EmitSection(Story s, MarkupNode section, ListCtx? list)
    {
        string? styleName = A(section, OdfNamespaces.Text, "style-name");
        OdfStyle? style = styleName is null ? null : _styles.Find("section", styleName);
        MarkupNode sp = style is null ? default : style.Properties("section-properties");
        if (!sp.IsNull && !OdfMarkup.Child(sp, OdfNamespaces.Style, "columns").IsNull
            && int.TryParse(A(OdfMarkup.Child(sp, OdfNamespaces.Style, "columns"), OdfNamespaces.Fo, "column-count"), NumberStyles.None, CultureInfo.InvariantCulture, out int cols) && cols > 1)
        {
            Lost("loss.section-columns", "columns in a text section", "A text section with columns reads as ordinary paragraphs.");
        }

        EmitBlocks(s, section, list);
    }

    // ---- lists ------------------------------------------------------------------------------------------------------------------

    private readonly Dictionary<string, int> _lastNumForStyle = new(StringComparer.Ordinal);

    private void EmitList(Story s, MarkupNode list, ListFrame? parent)
    {
        _depth++;
        try
        {
            CheckDepth(list);
            string styleName = A(list, OdfNamespaces.Text, "style-name") ?? parent?.StyleName ?? string.Empty;
            int level = parent is null ? 0 : parent.Level + 1;
            int? numId;
            bool sameAsParent = parent is not null && (A(list, OdfNamespaces.Text, "style-name") is null || A(list, OdfNamespaces.Text, "style-name") == parent.StyleName);
            string? xmlId = A(list, "http://www.w3.org/XML/1998/namespace", "id");
            if (sameAsParent)
            {
                numId = parent!.NumId;
            }
            else
            {
                string key = styleName.Length == 0 ? string.Empty : styleName;
                string? continueList = A(list, OdfNamespaces.Text, "continue-list");
                if (continueList is not null && _listIdNums.TryGetValue(continueList, out int cn))
                {
                    numId = cn;
                }
                else if (IsTrue(A(list, OdfNamespaces.Text, "continue-numbering")) && _lastNumForStyle.TryGetValue(key, out int ln))
                {
                    numId = ln;
                }
                else
                {
                    numId = key.Length == 0 ? null : NewNumForList(key);
                }

                if (numId is { } id)
                {
                    _lastNumForStyle[key] = id;
                }
            }

            if (xmlId is not null && numId is { } nid)
            {
                _listIdNums[xmlId] = nid;
            }

            var frame = new ListFrame { NumId = numId ?? 0, Level = level, StyleName = styleName };
            foreach (MarkupNode item in list.ChildElements)
            {
                if (item.NamespaceUri != OdfNamespaces.Text || item.LocalNameString is not ("list-item" or "list-header"))
                {
                    HandleListChild(s, item);
                    continue;
                }

                if (frame.NumId > 0 && int.TryParse(A(item, OdfNamespaces.Text, "start-value"), NumberStyles.None, CultureInfo.InvariantCulture, out int start))
                {
                    frame.NumId = NewNumWithStart(frame.NumId, Math.Min(level, 8), start);
                    if (xmlId is not null)
                    {
                        _listIdNums[xmlId] = frame.NumId;
                    }

                    _lastNumForStyle[styleName] = frame.NumId;
                }

                var ctx = new ListCtx { Frame = frame, Pending = true, Header = item.LocalNameString == "list-header" };
                foreach (MarkupNode c in item.ChildElements)
                {
                    if (c.NamespaceUri == OdfNamespaces.Text && c.LocalNameString == "list")
                    {
                        EmitList(s, c, frame);
                    }
                    else
                    {
                        EmitBlock(s, c, ctx, null);
                    }
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    private void HandleListChild(Story s, MarkupNode n)
    {
        if (n.NamespaceUri == OdfNamespaces.Text && n.LocalNameString is "change" or "change-start" or "change-end")
        {
            HandleChangeMarker(s, n, null);
        }
    }

    // ---- indexes (table of contents and the others) ------------------------------------------------------------------------------

    private void EmitIndex(Story s, MarkupNode index, ListCtx? list)
    {
        MarkupNode body = OdfMarkup.Child(index, OdfNamespaces.Text, "index-body");
        if (body.IsNull)
        {
            return;
        }

        string local = index.LocalNameString;
        string? instr = null;
        if (local == "table-of-content")
        {
            MarkupNode src = OdfMarkup.Child(index, OdfNamespaces.Text, "table-of-content-source");
            string levels = src.IsNull ? "3" : A(src, OdfNamespaces.Text, "outline-level") ?? "3";
            instr = " TOC \\o \"1-" + (int.TryParse(levels, NumberStyles.None, CultureInfo.InvariantCulture, out int lv) ? Math.Clamp(lv, 1, 9) : 3).ToString(CultureInfo.InvariantCulture) + "\" \\h \\z \\u ";
        }
        else if (local == "alphabetical-index")
        {
            instr = " INDEX \\h \"A\" \\c \"2\" ";
        }
        else
        {
            Lost("loss.index-fields", "index fields", "An index other than the table of contents and the alphabetical index keeps its text but is no longer an updatable field.");
        }

        // The field wraps the entries; the index title stays outside it, as in Word.
        MarkupNode first = default, last = default;
        foreach (MarkupNode c in body.ChildElements)
        {
            if (c.NamespaceUri == OdfNamespaces.Text && c.LocalNameString == "index-title")
            {
                continue;
            }

            if (first.IsNull && c.NamespaceUri == OdfNamespaces.Text && c.LocalNameString is "p" or "h")
            {
                first = c;
            }

            if (c.NamespaceUri == OdfNamespaces.Text && c.LocalNameString is "p" or "h")
            {
                last = c;
            }
        }

        if (instr is not null && !first.IsNull)
        {
            _fieldBeginAt[first] = instr;
            _fieldEndAt.Add(last);
        }

        EmitBlocks(s, body, list);
    }
}
