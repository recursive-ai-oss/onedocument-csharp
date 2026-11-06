using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// Paragraph content: text with ODF's whitespace rules, character formatting, hyperlinks, bookmarks, fields, notes, comments and tracked changes.
internal sealed partial class OdtImporter
{
    private sealed class Inl
    {
        public Inl(Story story, PropBag props)
        {
            S = story;
            Props = props;
        }

        public Story S;
        public PropBag Props;
        public string? RStyle;
        public bool LastSpace = true;
        public bool Deleted;
        public readonly StringBuilder Xml = new();
        public readonly StringBuilder Text = new();

        public void AddText(string t) => Text.Append(t);

        public void FlushText(bool deleted)
        {
            if (Text.Length == 0)
            {
                return;
            }

            string tag = deleted ? "w:delText" : "w:t";
            Xml.Append('<').Append(tag).Append(" xml:space=\"preserve\">").Append(OdfUnits.Esc(Text.ToString()).Replace("&quot;", "\"", StringComparison.Ordinal)).Append("</").Append(tag).Append('>');
            Text.Clear();
        }

        public void AddRaw(string xml)
        {
            FlushText(Deleted);
            Xml.Append(xml);
        }
    }

    private readonly List<string> _activeIns = [];
    private ChangeInfo? _activeDeletion;
    private string? _pendingNoteRef;

    private int NextRevisionId() => _revisionId++;

    private static string BmName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        if (sb.Length == 0 || char.IsDigit(sb[0]))
        {
            sb.Insert(0, '_');
        }

        return sb.Length > 40 ? sb.ToString(0, 40) : sb.ToString();
    }

    // ---- paragraphs --------------------------------------------------------------------------------------------------------------

    private void EmitParagraph(Story s, MarkupNode p, ListCtx? list, string? sectPr, bool heading)
    {
        _depth++;
        try
        {
            CheckDepth(p);
            ResolvedParagraph rp = ResolveParagraph(A(p, OdfNamespaces.Text, "style-name"));
            PropBag ppr = rp.PPr.Clone();
            if (rp.StyleId is { } sid)
            {
                ppr.Set("pStyle", X("pStyle", ("val", sid)));
            }

            if (heading)
            {
                int level = int.TryParse(A(p, OdfNamespaces.Text, "outline-level"), NumberStyles.None, CultureInfo.InvariantCulture, out int ol) ? Math.Clamp(ol, 1, 10) : 1;
                ppr.Set("outlineLvl", X("outlineLvl", ("val", OdfUnits.I(level - 1))));
            }

            if (list is not null && list.Frame.NumId > 0 && !list.Header)
            {
                if (list.Pending)
                {
                    int ilvl = Math.Min(list.Frame.Level, 8);
                    ppr.Set("numPr", "<w:numPr><w:ilvl w:val=\"" + OdfUnits.I(ilvl) + "\"/><w:numId w:val=\"" + OdfUnits.I(list.Frame.NumId) + "\"/></w:numPr>");
                    list.Pending = false;
                }
                else if (!ppr.Has("ind"))
                {
                    // Later paragraphs of an item are not numbered but sit at the item's text indent.
                    ppr.Set("numPr", "<w:numPr><w:ilvl w:val=\"" + OdfUnits.I(Math.Min(list.Frame.Level, 8)) + "\"/><w:numId w:val=\"0\"/></w:numPr>");
                }
            }
            else if (list is not null && list.Header)
            {
                ppr.Set("numPr", "<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"0\"/></w:numPr>");
            }
            else if (rp.DirectListStyle is { } dls && NumIdForStyle(dls) is { } dn)
            {
                ppr.Set("numPr", "<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"" + OdfUnits.I(dn) + "\"/></w:numPr>");
            }

            if (_forcePageBreak && ReferenceEquals(s, _main))
            {
                ppr.Set("pageBreakBefore", "<w:pageBreakBefore/>");
                _forcePageBreak = false;
            }

            if (sectPr is not null)
            {
                ppr.Set("sectPr", sectPr);
                _sectionWritten = true;
            }

            bool deleted = _activeDeletion is not null;
            if (deleted)
            {
                ppr.Set("rPr", "<w:rPr>" + DelMark(_activeDeletion!) + "</w:rPr>");
            }

            s.Raw("<w:p>");
            if (!ppr.IsEmpty)
            {
                s.Raw("<w:pPr>" + ppr.ToXml(WordOrder.PPr) + "</w:pPr>");
            }

            if (_pendingNoteRef is { } noteStyle)
            {
                s.Raw("<w:r><w:rPr><w:rStyle w:val=\"" + noteStyle + "\"/></w:rPr>" + (noteStyle == "FootnoteReference" ? "<w:footnoteRef/>" : "<w:endnoteRef/>") + "</w:r>");
                _pendingNoteRef = null;
            }

            if (_fieldBeginAt.TryGetValue(p, out string? instr))
            {
                s.Raw("<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\">" + OdfUnits.Esc(instr) + "</w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>");
            }

            var st = new Inl(s, rp.RPr.Clone()) { Deleted = deleted };
            EmitInline(st, p);
            Flush(st);
            if (_fieldEndAt.Contains(p))
            {
                s.Raw("<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>");
            }

            s.Raw("</w:p>");
            s.EndsWithParagraph = true;
        }
        finally
        {
            _depth--;
        }
    }

    private string DelMark(ChangeInfo c) =>
        "<w:del w:id=\"" + OdfUnits.I(NextRevisionId()) + "\" w:author=\"" + OdfUnits.Esc(c.Author ?? "unknown") + "\"" + (c.Date is null ? string.Empty : " w:date=\"" + c.Date + "\"") + "/>";

    private string RevisionOpen(string tag, ChangeInfo c) =>
        "<w:" + tag + " w:id=\"" + OdfUnits.I(NextRevisionId()) + "\" w:author=\"" + OdfUnits.Esc(c.Author ?? "unknown") + "\"" + (c.Date is null ? string.Empty : " w:date=\"" + c.Date + "\"") + ">";

    private void Flush(Inl st)
    {
        st.FlushText(st.Deleted);
        if (st.Xml.Length == 0)
        {
            return;
        }

        PropBag props = st.Props;
        if (st.RStyle is { } rs)
        {
            props = props.Clone();
            props.Set("rStyle", X("rStyle", ("val", rs)));
        }

        string rpr = props.IsEmpty ? string.Empty : "<w:rPr>" + props.ToXml(WordOrder.RPr) + "</w:rPr>";
        string run = "<w:r>" + rpr + st.Xml + "</w:r>";
        st.Xml.Clear();
        if (st.Deleted && _activeDeletion is { } del)
        {
            run = RevisionOpen("del", del) + run + "</w:del>";
        }
        else if (_activeIns.Count > 0 && _changes.TryGetValue(_activeIns[^1], out ChangeInfo? ins))
        {
            run = RevisionOpen("ins", ins) + run + "</w:ins>";
        }

        st.S.Raw(run);
    }

    // ---- inline content ----------------------------------------------------------------------------------------------------------

    private void EmitInline(Inl st, MarkupNode parent)
    {
        _depth++;
        try
        {
            CheckDepth(parent);
            for (MarkupNode c = parent.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                switch (c.Kind)
                {
                    case MarkupNodeKind.Text:
                    case MarkupNodeKind.Whitespace:
                    case MarkupNodeKind.CData:
                        AppendCollapsed(st, c.InnerText);
                        break;
                    case MarkupNodeKind.Element:
                        EmitInlineElement(st, c);
                        break;
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    // Part 1, White space characters: runs of space, tab, CR and LF collapse to one space, and a space at the start of a paragraph is dropped.
    private static void AppendCollapsed(Inl st, string text)
    {
        foreach (char ch in text)
        {
            if (ch is ' ' or '\t' or '\r' or '\n')
            {
                if (!st.LastSpace)
                {
                    st.AddText(" ");
                    st.LastSpace = true;
                }
            }
            else
            {
                st.Text.Append(ch);
                st.LastSpace = false;
            }
        }
    }

    private void EmitInlineElement(Inl st, MarkupNode n)
    {
        string ns = n.NamespaceUri;
        string local = n.LocalNameString;
        if (ns == OdfNamespaces.Text)
        {
            switch (local)
            {
                case "span":
                    EmitSpan(st, n);
                    return;
                case "s":
                {
                    int count = int.TryParse(A(n, OdfNamespaces.Text, "c"), NumberStyles.None, CultureInfo.InvariantCulture, out int c) ? Math.Clamp(c, 1, 10000) : 1;
                    st.AddText(new string(' ', count));
                    st.LastSpace = false;
                    return;
                }

                case "tab":
                    st.AddRaw("<w:tab/>");
                    st.LastSpace = false;
                    return;
                case "line-break":
                    st.AddRaw("<w:br/>");
                    st.LastSpace = false;
                    return;
                case "a":
                    EmitHyperlink(st, n);
                    return;
                case "soft-page-break":
                    return;
                case "bookmark":
                case "reference-mark":
                {
                    Flush(st);
                    string? name = A(n, OdfNamespaces.Text, "name");
                    if (name is not null)
                    {
                        int id = _bookmarkId++;
                        st.S.Raw("<w:bookmarkStart w:id=\"" + OdfUnits.I(id) + "\" w:name=\"" + OdfUnits.Esc(BmName(name)) + "\"/><w:bookmarkEnd w:id=\"" + OdfUnits.I(id) + "\"/>");
                    }

                    return;
                }

                case "bookmark-start":
                case "reference-mark-start":
                {
                    Flush(st);
                    string? name = A(n, OdfNamespaces.Text, "name");
                    if (name is not null)
                    {
                        int id = _bookmarkId++;
                        _bookmarkIds[name] = id;
                        st.S.Raw("<w:bookmarkStart w:id=\"" + OdfUnits.I(id) + "\" w:name=\"" + OdfUnits.Esc(BmName(name)) + "\"/>");
                    }

                    return;
                }

                case "bookmark-end":
                case "reference-mark-end":
                {
                    Flush(st);
                    string? name = A(n, OdfNamespaces.Text, "name");
                    if (name is not null && _bookmarkIds.Remove(name, out int id))
                    {
                        st.S.Raw("<w:bookmarkEnd w:id=\"" + OdfUnits.I(id) + "\"/>");
                    }

                    return;
                }

                case "note":
                    EmitNote(st, n);
                    return;
                case "change":
                case "change-start":
                case "change-end":
                    Flush(st);
                    HandleChangeMarker(st.S, n, st);
                    return;
                case "meta":
                case "meta-field":
                case "hidden-text":
                case "conditional-text":
                case "text-input":
                case "expression":
                case "ruby-base":
                case "index-title-template":
                case "toc-mark":
                    EmitInline(st, n);
                    return;
                case "ruby":
                    EmitInline(st, n);
                    Lost("loss.ruby", "ruby annotations", "The text above a ruby base was dropped; the base text is kept.");
                    return;
                case "ruby-text":
                    return;
                case "page-number":
                    EmitField(st, n, "PAGE");
                    return;
                case "page-count":
                    EmitField(st, n, "NUMPAGES");
                    return;
                case "date":
                    EmitField(st, n, IsTrue(A(n, OdfNamespaces.Text, "fixed")) ? null : "DATE");
                    return;
                case "time":
                    EmitField(st, n, IsTrue(A(n, OdfNamespaces.Text, "fixed")) ? null : "TIME");
                    return;
                case "title":
                    EmitField(st, n, "TITLE");
                    return;
                case "subject":
                    EmitField(st, n, "SUBJECT");
                    return;
                case "author-name":
                case "initial-creator":
                case "creator":
                    EmitField(st, n, "AUTHOR");
                    return;
                case "keywords":
                    EmitField(st, n, "KEYWORDS");
                    return;
                case "description":
                    EmitField(st, n, "COMMENTS");
                    return;
                case "file-name":
                    EmitField(st, n, "FILENAME");
                    return;
                case "bookmark-ref":
                case "reference-ref":
                {
                    string? name = A(n, OdfNamespaces.Text, "ref-name");
                    string fmt = A(n, OdfNamespaces.Text, "reference-format") ?? "text";
                    string? instr = name is null ? null : fmt switch
                    {
                        "page" => "PAGEREF " + BmName(name) + " \\h",
                        "number" or "number-no-superior" or "number-all-superior" => "REF " + BmName(name) + " \\n \\h",
                        _ => "REF " + BmName(name) + " \\h",
                    };
                    EmitField(st, n, instr);
                    return;
                }

                case "note-ref":
                    EmitField(st, n, A(n, OdfNamespaces.Text, "ref-name") is { } nr ? "NOTEREF " + BmName(nr) + " \\h" : null);
                    return;
                case "sequence":
                    EmitField(st, n, A(n, OdfNamespaces.Text, "name") is { } sq ? "SEQ " + sq.Replace(' ', '_') + " \\* ARABIC" : null);
                    return;
                case "sequence-ref":
                    EmitField(st, n, A(n, OdfNamespaces.Text, "ref-name") is { } sr ? "REF " + BmName(sr) + " \\h" : null);
                    return;
                case "variable-set":
                case "variable-get":
                case "variable-input":
                case "user-field-get":
                case "user-field-input":
                case "database-display":
                case "page-variable-get":
                case "page-variable-set":
                case "chapter":
                case "sender-firstname":
                case "sender-lastname":
                case "sender-company":
                case "editing-cycles":
                case "editing-duration":
                case "modification-date":
                case "modification-time":
                case "creation-date":
                case "creation-time":
                case "print-date":
                case "print-time":
                case "word-count":
                case "paragraph-count":
                case "character-count":
                case "table-count":
                case "image-count":
                case "object-count":
                case "template-name":
                case "printed-by":
                case "dde-connection":
                case "bibliography-mark":
                case "alphabetical-index-mark":
                case "alphabetical-index-mark-start":
                case "alphabetical-index-mark-end":
                case "toc-mark-start":
                case "toc-mark-end":
                case "user-index-mark":
                case "user-index-mark-start":
                case "user-index-mark-end":
                    if (local.EndsWith("-mark", StringComparison.Ordinal) || local.EndsWith("-mark-start", StringComparison.Ordinal) || local.EndsWith("-mark-end", StringComparison.Ordinal))
                    {
                        return;
                    }

                    EmitField(st, n, null);
                    return;
            }
        }
        else if (ns == OdfNamespaces.Draw)
        {
            if (local == "frame")
            {
                Flush(st);
                EmitFrame(st, n);
            }
            else if (local == "a")
            {
                // A hyperlinked drawing: the drawing inside carries the picture, the link is not kept.
                foreach (MarkupNode c in n.ChildElements)
                {
                    if (c.IsElement(OdfNamespaces.Draw, "frame"))
                    {
                        Flush(st);
                        EmitFrame(st, c);
                    }
                }
            }
            else
            {
                EmitShape(st, n);
            }

            return;
        }
        else if (ns == OdfNamespaces.Office)
        {
            if (local == "annotation")
            {
                EmitAnnotation(st, n);
            }
            else if (local == "annotation-end")
            {
                EmitAnnotationEnd(st, n);
            }

            return;
        }

        switch (local)
        {
            case "content-control":
                Lost("loss.content-controls", "content controls", "Content controls were read through: their text is kept, the controls are not.");
                break;
            case "fieldmark":
            case "fieldmark-start":
            case "fieldmark-separator":
            case "fieldmark-end":
            case "param":
            case "user-defined":
            case "style-ref":
            case "conditional-text":
                // Form-field marks and cached fields: the text between them is the content, read as it stands.
                break;
            default:
                Unmapped(n);
                break;
        }

        EmitInline(st, n);
    }

    private void EmitSpan(Inl st, MarkupNode span)
    {
        (string? id, PropBag direct) = ResolveSpan(A(span, OdfNamespaces.Text, "style-name"));
        if (id is null && direct.IsEmpty)
        {
            EmitInline(st, span);
            return;
        }

        Flush(st);
        PropBag savedProps = st.Props;
        string? savedStyle = st.RStyle;
        st.Props = savedProps.Clone();
        st.Props.Merge(direct);
        if (id is not null)
        {
            st.RStyle = id;
        }

        EmitInline(st, span);
        Flush(st);
        st.Props = savedProps;
        st.RStyle = savedStyle;
    }

    private void EmitHyperlink(Inl st, MarkupNode a)
    {
        Flush(st);
        string? href = A(a, OdfNamespaces.XLink, "href");
        if (string.IsNullOrEmpty(href))
        {
            EmitSpanLike(st, a);
            return;
        }

        string open;
        if (href.StartsWith('#'))
        {
            string anchor = href[1..];
            int bar = anchor.IndexOf('|', StringComparison.Ordinal);
            if (bar >= 0)
            {
                anchor = anchor[..bar];
            }

            open = "<w:hyperlink w:anchor=\"" + OdfUnits.Esc(BmName(anchor)) + "\" w:history=\"1\">";
        }
        else
        {
            // Nothing is fetched: the target becomes an external relationship and is never followed.
            string rid = st.S.AddRel(RelBase + "hyperlink", href, external: true);
            open = "<w:hyperlink r:id=\"" + rid + "\" w:history=\"1\">";
        }

        st.S.Raw(open);
        EmitSpanLike(st, a);
        Flush(st);
        st.S.Raw("</w:hyperlink>");
    }

    // A text:a (or similar) element takes its character style like a span.
    private void EmitSpanLike(Inl st, MarkupNode n)
    {
        (string? id, PropBag direct) = ResolveSpan(A(n, OdfNamespaces.Text, "style-name"));
        Flush(st);
        PropBag savedProps = st.Props;
        string? savedStyle = st.RStyle;
        st.Props = savedProps.Clone();
        st.Props.Merge(direct);
        if (id is not null)
        {
            st.RStyle = id;
        }

        EmitInline(st, n);
        Flush(st);
        st.Props = savedProps;
        st.RStyle = savedStyle;
    }

    // A field with a known instruction becomes a simple field whose cached result is the text the file stores; Word and the hub
    // update it on demand. Without an instruction the stored text is kept as plain text.
    private void EmitField(Inl st, MarkupNode n, string? instruction)
    {
        string cached = n.InnerText;
        if (instruction is null || st.Deleted)
        {
            AppendCollapsed(st, cached);
            return;
        }

        Flush(st);
        PropBag props = st.Props;
        if (st.RStyle is { } rs)
        {
            props = props.Clone();
            props.Set("rStyle", X("rStyle", ("val", rs)));
        }

        string rpr = props.IsEmpty ? string.Empty : "<w:rPr>" + props.ToXml(WordOrder.RPr) + "</w:rPr>";
        string text = cached.Length == 0 ? string.Empty : "<w:t xml:space=\"preserve\">" + OdfUnits.Esc(cached) + "</w:t>";
        string field = "<w:fldSimple w:instr=\"" + OdfUnits.Esc(" " + instruction + " ") + "\"><w:r>" + rpr + text + "</w:r></w:fldSimple>";
        if (_activeIns.Count > 0 && _changes.TryGetValue(_activeIns[^1], out ChangeInfo? ins))
        {
            field = RevisionOpen("ins", ins) + field + "</w:ins>";
        }

        st.S.Raw(field);
        st.LastSpace = false;
    }

    // ---- notes, comments -----------------------------------------------------------------------------------------------------------

    private void EmitNote(Inl st, MarkupNode note)
    {
        Flush(st);
        bool endnote = A(note, OdfNamespaces.Text, "note-class") == "endnote";
        MarkupNode body = OdfMarkup.Child(note, OdfNamespaces.Text, "note-body");
        List<(int Id, MarkupNode Node)> list = endnote ? _endnotes : _footnotes;
        int id = list.Count + 1;
        list.Add((id, body));
        string style = endnote ? "EndnoteReference" : "FootnoteReference";
        string element = endnote ? "w:endnoteReference" : "w:footnoteReference";
        PropBag props = st.Props.Clone();
        props.Set("rStyle", X("rStyle", ("val", style)));
        st.S.Raw("<w:r><w:rPr>" + props.ToXml(WordOrder.RPr) + "</w:rPr><" + element + " w:id=\"" + OdfUnits.I(id) + "\"/></w:r>");
        st.LastSpace = false;
    }

    private void EmitAnnotation(Inl st, MarkupNode a)
    {
        Flush(st);
        int id = _comments.Count;
        _comments.Add((id, a));
        string? name = A(a, OdfNamespaces.Office, "name");
        st.S.Raw("<w:commentRangeStart w:id=\"" + OdfUnits.I(id) + "\"/>");
        if (name is not null)
        {
            _commentIds[name] = id;
        }

        if (name is null || !_annotationEnds.Contains(name))
        {
            st.S.Raw(CommentEnd(id));
        }
    }

    private void EmitAnnotationEnd(Inl st, MarkupNode e)
    {
        Flush(st);
        if (A(e, OdfNamespaces.Office, "name") is { } name && _commentIds.TryGetValue(name, out int id))
        {
            st.S.Raw(CommentEnd(id));
        }
    }

    private static string CommentEnd(int id) =>
        "<w:commentRangeEnd w:id=\"" + OdfUnits.I(id) + "\"/><w:r><w:commentReference w:id=\"" + OdfUnits.I(id) + "\"/></w:r>";

    private Story BuildNotes(string root, string element, string partName, List<(int Id, MarkupNode Node)> notes, string refStyle)
    {
        var story = new Story(partName);
        story.Begin(root);
        foreach ((string type, string id) in new[] { ("separator", "-1"), ("continuationSeparator", "0") })
        {
            story.Raw("<" + element + " w:type=\"" + type + "\" w:id=\"" + id + "\"><w:p><w:r><w:" + type + "/></w:r></w:p></" + element + ">");
        }

        // Notes found inside notes would be numbered after the ones queued; ODF does not nest notes, so the list is fixed here.
        for (int i = 0; i < notes.Count; i++)
        {
            story.Raw("<" + element + " w:id=\"" + OdfUnits.I(notes[i].Id) + "\">");
            story.EndsWithParagraph = false;
            _pendingNoteRef = refStyle;
            if (!notes[i].Node.IsNull)
            {
                EmitBlocks(story, notes[i].Node, null);
            }

            if (!story.EndsWithParagraph)
            {
                story.Raw("<w:p>" + (_pendingNoteRef is null ? string.Empty : "<w:r><w:rPr><w:rStyle w:val=\"" + refStyle + "\"/></w:rPr>" + (refStyle == "FootnoteReference" ? "<w:footnoteRef/>" : "<w:endnoteRef/>") + "</w:r>") + "</w:p>");
            }

            _pendingNoteRef = null;
            story.Raw("</" + element + ">");
        }

        story.End();
        return story;
    }

    private Story BuildComments()
    {
        var story = new Story("/word/comments.xml");
        story.Begin("w:comments");
        for (int i = 0; i < _comments.Count; i++)
        {
            (int id, MarkupNode a) = _comments[i];
            string author = OdfMarkup.Child(a, OdfNamespaces.DublinCore, "creator") is { IsNull: false } c ? c.InnerText : "unknown";
            string? date = OdfMarkup.Child(a, OdfNamespaces.DublinCore, "date") is { IsNull: false } d ? NormalizeDate(d.InnerText) : null;
            string? initials = OdfMarkup.Child(a, OdfNamespaces.Meta, "creator-initials") is { IsNull: false } ci ? ci.InnerText : null;
            story.Raw("<w:comment w:id=\"" + OdfUnits.I(id) + "\" w:author=\"" + OdfUnits.Esc(author.Length > 0 ? author : "unknown") + "\""
                + (date is null ? string.Empty : " w:date=\"" + date + "\"")
                + (string.IsNullOrEmpty(initials) ? string.Empty : " w:initials=\"" + OdfUnits.Esc(initials) + "\"") + ">");
            story.EndsWithParagraph = false;
            foreach (MarkupNode child in a.ChildElements)
            {
                if (child.NamespaceUri == OdfNamespaces.Text)
                {
                    EmitBlock(story, child, null, null);
                }
            }

            if (!story.EndsWithParagraph)
            {
                story.Raw("<w:p/>");
            }

            story.Raw("</w:comment>");
        }

        story.End();
        return story;
    }

    // ---- tracked changes --------------------------------------------------------------------------------------------------------

    private void HandleChangeMarker(Story s, MarkupNode n, Inl? st)
    {
        string? id = A(n, OdfNamespaces.Text, "change-id");
        if (id is null || !_changes.TryGetValue(id, out ChangeInfo? info))
        {
            Defect(DefectKind.Reference, "odf.unknown-change", DefectSeverity.Repaired, "A change mark refers to a changed region the file does not hold.", n);
            return;
        }

        string local = n.LocalNameString;
        switch (info.Kind)
        {
            case "insertion":
                if (local == "change-start")
                {
                    _activeIns.Add(id);
                }
                else if (local == "change-end")
                {
                    _activeIns.Remove(id);
                }

                break;
            case "deletion":
                if (local is "change" or "change-start")
                {
                    EmitDeletion(s, info, st);
                }

                break;
            default:
                if (local == "change-start")
                {
                    Lost("loss.formatting-revisions", "formatting revisions", "Tracked formatting changes were dropped; the formatting is kept.");
                }

                break;
        }
    }

    // The deleted text is stored in the changed region; it goes back where it was deleted as a w:del of runs with w:delText.
    private void EmitDeletion(Story s, ChangeInfo info, Inl? st)
    {
        if (info.Deleted.IsNull)
        {
            return;
        }

        ChangeInfo? savedDeletion = _activeDeletion;
        _activeDeletion = info;
        try
        {
            if (st is not null)
            {
                bool first = true;
                foreach (MarkupNode p in info.Deleted.ChildElements)
                {
                    if (p.NamespaceUri != OdfNamespaces.Text || p.LocalNameString is not ("p" or "h"))
                    {
                        continue;
                    }

                    var del = new Inl(s, st.Props.Clone()) { Deleted = true, RStyle = st.RStyle, LastSpace = false };
                    if (!first)
                    {
                        del.AddRaw("<w:br/>");
                    }

                    first = false;
                    EmitInline(del, p);
                    Flush(del);
                }
            }
            else
            {
                foreach (MarkupNode p in info.Deleted.ChildElements)
                {
                    if (p.NamespaceUri == OdfNamespaces.Text && p.LocalNameString is "p" or "h")
                    {
                        EmitParagraph(s, p, null, null, p.LocalNameString == "h");
                    }
                }
            }
        }
        finally
        {
            _activeDeletion = savedDeletion;
        }
    }
}
