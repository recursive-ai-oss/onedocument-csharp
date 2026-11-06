using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Doc.Binary;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Doc;

internal sealed partial class DocConverter
{
    /// <summary>One rendered paragraph of a story, before tables are assembled.</summary>
    private sealed class Block
    {
        internal DocParagraph P = null!;
        internal string Xml = string.Empty;
        internal TableRowFormat? Row;
    }

    /// <summary>The state one story's conversion carries: the part it writes into, field nesting and the open tracked-change wrapper.</summary>
    private sealed class StoryState(Part part, StoryKind kind)
    {
        internal Part Part { get; } = part;

        internal StoryKind Kind { get; } = kind;

        internal List<(bool Instruction, StringBuilder Text)> Fields { get; } = [];

        internal int OpenRevision { get; set; }

        internal string OpenRevisionKey { get; set; } = string.Empty;

        internal bool InInstruction => Fields.Count > 0 && Fields[^1].Instruction;

        /// <summary>Per open field: where its XML starts in the paragraph being written, and the object it stands for.</summary>
        internal List<FieldMark> Marks { get; } = [];

        /// <summary>True once the picture-frame shape type that object shapes refer to is in this part.</summary>
        internal bool ShapeTypeWritten { get; set; }
    }

    private sealed class FieldMark(int begin, StringBuilder owner)
    {
        internal int Begin { get; } = begin;

        /// <summary>The paragraph builder the field began in: an object is written only when the field ends in the same paragraph.</summary>
        internal StringBuilder Owner { get; } = owner;

        /// <summary>The number the object storage in the <c>ObjectPool</c> is named after ([MS-DOC] sprmCPicLocation with sprmCFOle2), when the field's separator is an object placeholder.</summary>
        internal uint? ObjectId { get; set; }

        internal (string Rid, long Cx, long Cy)? Preview { get; set; }
    }

    // Renders [start, start+length) of a story to block-level XML.
    private string WriteStory(int start, int length, StoryKind kind, Part part, IReadOnlyCollection<int>? sectionEnds, Func<int, string>? sectionXml)
    {
        List<DocParagraph> paras = _r.Paragraphs(start, length, sectionEnds);
        var st = new StoryState(part, kind);
        var blocks = new List<Block>(paras.Count);
        foreach (DocParagraph p in paras)
        {
            var b = new Block { P = p };
            if (p.IsRowEnd)
            {
                b.Row = TableRowFormat.From(p.Props);
                if (b.Row.Unmapped > 0)
                {
                    _loss.Report("loss.doc-table-merge-split", "table cell merge and split edits", b.Row.Unmapped, "sprmTMerge and sprmTSplit edits to a table row were not applied.");
                }
            }
            else
            {
                b.Xml = WriteParagraph(p, st, sectionXml);
            }

            blocks.Add(b);
        }

        var sb = new StringBuilder();
        int pos = 0;
        EmitLevel(blocks, ref pos, 0, sb);
        // Anything left (a cell with no end) is written flat so no text is lost.
        while (pos < blocks.Count)
        {
            if (!blocks[pos].P.IsRowEnd)
            {
                sb.Append(blocks[pos].Xml);
            }

            pos++;
        }

        if (sb.Length == 0)
        {
            sb.Append("<w:p/>");
        }

        return sb.ToString();
    }

    // How a table level finished.
    private enum LevelEnd
    {
        Exhausted,
        CellEnd,
        RowEnd,
    }

    // Writes the blocks at table depth `level` (0 = body). Blocks deeper than `level` are tables nested here.
    private LevelEnd EmitLevel(List<Block> blocks, ref int pos, int level, StringBuilder sb)
    {
        while (pos < blocks.Count)
        {
            Block b = blocks[pos];
            int depth = b.P.Itap;
            if (depth > level)
            {
                EmitTable(blocks, ref pos, level + 1, sb);
                continue;
            }

            if (depth < level)
            {
                return LevelEnd.Exhausted;
            }

            if (b.P.IsRowEnd)
            {
                if (level == 0)
                {
                    pos++;
                    continue;
                }

                return LevelEnd.RowEnd;
            }

            sb.Append(b.Xml);
            pos++;
            if (b.P.IsCellEnd && level > 0)
            {
                return LevelEnd.CellEnd;
            }
        }

        return LevelEnd.Exhausted;
    }

    private sealed class RowOut
    {
        internal TableRowFormat? Format;
        internal List<string> Cells = [];
    }

    private void EmitTable(List<Block> blocks, ref int pos, int level, StringBuilder sb)
    {
        var rows = new List<RowOut>();
        while (pos < blocks.Count && blocks[pos].P.Itap >= level)
        {
            var row = new RowOut();
            bool ended = false;
            while (pos < blocks.Count && blocks[pos].P.Itap >= level && !ended)
            {
                var cell = new StringBuilder();
                LevelEnd end = EmitLevel(blocks, ref pos, level, cell);
                if (end == LevelEnd.CellEnd)
                {
                    row.Cells.Add(cell.ToString());
                }
                else if (end == LevelEnd.RowEnd)
                {
                    if (cell.Length > 0)
                    {
                        row.Cells.Add(cell.ToString());
                    }

                    row.Format = blocks[pos].Row;
                    pos++;
                    ended = true;
                }
                else
                {
                    if (cell.Length > 0)
                    {
                        row.Cells.Add(cell.ToString());
                    }

                    ended = true;
                }
            }

            rows.Add(row);
            if (!ended)
            {
                break;
            }
        }

        WriteTable(rows, sb);
    }

    private static void WriteTable(List<RowOut> rows, StringBuilder sb)
    {
        if (rows.Count == 0)
        {
            return;
        }

        // The grid is the union of every row's cell edges, so rows with different cell boundaries still share one grid.
        var edgeSet = new SortedSet<int>();
        var rowEdges = new List<int[]>();
        foreach (RowOut r in rows)
        {
            int[] e;
            if (r.Format is { Cells.Count: > 0 } f)
            {
                e = f.Edges();
            }
            else
            {
                // No cell definitions (a damaged or Word 6/95 row): equal columns of one inch.
                e = new int[r.Cells.Count + 1];
                for (int i = 0; i <= r.Cells.Count; i++)
                {
                    e[i] = i * 1440;
                }
            }

            rowEdges.Add(e);
            foreach (int v in e)
            {
                edgeSet.Add(v);
            }
        }

        int[] grid = [.. edgeSet];
        sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/>");
        TableRowFormat? first = rows[0].Format;
        first?.WriteTableProperties(sb);
        sb.Append("<w:tblLook w:val=\"04A0\"/></w:tblPr><w:tblGrid>");
        for (int i = 0; i + 1 < grid.Length; i++)
        {
            sb.Append("<w:gridCol w:w=\"").Append(WmlWrite.I(Math.Max(1, grid[i + 1] - grid[i]))).Append("\"/>");
        }

        if (grid.Length < 2)
        {
            sb.Append("<w:gridCol w:w=\"1440\"/>");
        }

        sb.Append("</w:tblGrid>");
        for (int ri = 0; ri < rows.Count; ri++)
        {
            RowOut r = rows[ri];
            int[] e = rowEdges[ri];
            sb.Append("<w:tr>");
            var trPr = new StringBuilder();
            r.Format?.WriteRowProperties(trPr);
            if (trPr.Length > 0)
            {
                sb.Append("<w:trPr>").Append(trPr).Append("</w:trPr>");
            }

            int ci = 0;
            while (ci < r.Cells.Count)
            {
                CellDef def = r.Format is { } rf && ci < rf.Cells.Count ? rf.Cells[ci] : new CellDef { Width = e.Length > ci + 1 ? e[ci + 1] - e[ci] : 1440 };
                int left = ci < e.Length ? e[ci] : e[^1];
                int last = ci;
                string content = r.Cells[ci];
                // Horizontally merged cells: the first (merge kind 2 or 3) takes the cells marked 1 that follow it.
                if (def.HorzMerge >= 2 && r.Format is { } rf2)
                {
                    while (last + 1 < r.Cells.Count && last + 1 < rf2.Cells.Count && rf2.Cells[last + 1].HorzMerge == 1)
                    {
                        last++;
                        string extra = r.Cells[last];
                        if (extra.Length > 0 && extra != "<w:p/>")
                        {
                            content += extra;
                        }
                    }
                }
                else if (def.HorzMerge == 1 && ci > 0)
                {
                    // A merged cell with no first cell before it: write it as a cell of its own.
                }

                int right = last + 1 < e.Length ? e[last + 1] : left;
                int a = Array.BinarySearch(grid, left);
                int b = Array.BinarySearch(grid, right);
                if (a < 0)
                {
                    a = ~a;
                }

                if (b < 0)
                {
                    b = ~b;
                }

                int span = Math.Max(1, b - a);
                sb.Append("<w:tc><w:tcPr>");
                TableRowFormat.WriteCellProperties(sb, def, Math.Max(0, right - left), span, def.VertMerge);
                sb.Append("</w:tcPr>");
                if (content.Length == 0)
                {
                    content = "<w:p/>";
                }

                // A cell must end with a paragraph (ECMA-376 Part 1 17.4.66); a nested table at its end gets one.
                if (content.EndsWith("</w:tbl>", StringComparison.Ordinal))
                {
                    content += "<w:p/>";
                }

                sb.Append(content).Append("</w:tc>");
                ci = last + 1;
            }

            if (r.Cells.Count == 0)
            {
                sb.Append("<w:tc><w:tcPr><w:tcW w:w=\"1440\" w:type=\"dxa\"/></w:tcPr><w:p/></w:tc>");
            }

            sb.Append("</w:tr>");
        }

        sb.Append("</w:tbl>");
    }

    // Writes one paragraph: its properties, then its runs.
    private string WriteParagraph(DocParagraph p, StoryState st, Func<int, string>? sectionXml)
    {
        var props = new List<Prl>();
        foreach (Prl pr in p.Props)
        {
            props.Add(pr);
        }

        ParaFormat pf = ParaFormat.From(props);
        _fmt.ParagraphStyle = p.Istd;
        var sb = new StringBuilder("<w:p>");
        var ppr = new StringBuilder();
        int istd = pf.Istd >= 0 ? pf.Istd : p.Istd;
        if (istd != 0 && _styleIds.TryGetValue(istd, out string? sid))
        {
            ppr.Append("<w:pStyle w:val=\"").Esc(sid).Append("\"/>");
        }

        pf.WriteInner(ppr, _fmt, ilfo => NumId(ilfo));
        CountUnmapped(p.Props, ParaHandled, p.Start);

        // The paragraph mark's own formatting, including its tracked change.
        var mark = new StringBuilder();
        if (p.Mark < _f.Text.Length)
        {
            RunFormat mf = RunFormat.From(_r.CharacterProperties(p.Mark, p.Mark + 1, out _));
            if (mf.Revision == 1)
            {
                AppendRevision(mark, "w:ins", mf.InsAuthor, mf.InsDate);
            }
            else if (mf.Revision == 2)
            {
                AppendRevision(mark, "w:del", mf.DelAuthor, mf.DelDate);
            }

            mf.WriteInner(mark, _fmt);
        }

        if (mark.Length > 0)
        {
            ppr.Append("<w:rPr>").Append(mark).Append("</w:rPr>");
        }

        if (p.EndsSection && sectionXml is not null)
        {
            ppr.Append(sectionXml(p.Mark + 1));
        }

        if (ppr.Length > 0)
        {
            sb.Append("<w:pPr>").Append(ppr).Append("</w:pPr>");
        }

        WriteRuns(sb, p, st);
        CloseRevision(sb, st);
        sb.Append("</w:p>");
        return sb.ToString();
    }

    private void AppendRevision(StringBuilder sb, string element, int author, uint dttm)
    {
        sb.Append('<').Append(element).Append(" w:id=\"").Append(WmlWrite.I(_revisionId++)).Append("\" w:author=\"").Esc(AuthorOf(_revAuthors, author)).Append('"');
        if (DttmToIso(dttm) is { } iso)
        {
            sb.Append(" w:date=\"").Append(iso).Append('"');
        }

        sb.Append("/>");
    }

    private static void CloseRevision(StringBuilder sb, StoryState st)
    {
        if (st.OpenRevision != 0)
        {
            sb.Append(st.OpenRevision == 1 ? "</w:ins>" : "</w:del>");
            st.OpenRevision = 0;
            st.OpenRevisionKey = string.Empty;
        }
    }

    private void SwitchRevision(StringBuilder sb, StoryState st, RunFormat rf)
    {
        int kind = rf.Revision;
        string key = kind == 0 ? string.Empty : kind + ":" + (kind == 1 ? rf.InsAuthor : rf.DelAuthor) + ":" + (kind == 1 ? rf.InsDate : rf.DelDate);
        if (kind == st.OpenRevision && key == st.OpenRevisionKey)
        {
            return;
        }

        CloseRevision(sb, st);
        if (kind == 0)
        {
            return;
        }

        sb.Append(kind == 1 ? "<w:ins" : "<w:del").Append(" w:id=\"").Append(WmlWrite.I(_revisionId++)).Append("\" w:author=\"").Esc(AuthorOf(_revAuthors, kind == 1 ? rf.InsAuthor : rf.DelAuthor)).Append('"');
        if (DttmToIso(kind == 1 ? rf.InsDate : rf.DelDate) is { } iso)
        {
            sb.Append(" w:date=\"").Append(iso).Append('"');
        }

        sb.Append('>');
        st.OpenRevision = kind;
        st.OpenRevisionKey = key;
    }

    private void WriteRuns(StringBuilder sb, DocParagraph p, StoryState st)
    {
        string text = _f.Text;
        int end = Math.Min(p.Mark, text.Length);
        int cp = p.Start;
        var buf = new StringBuilder();
        while (cp < end)
        {
            List<Prl> props = _r.CharacterProperties(cp, end, out int runEnd);
            CountUnmapped(props, CharHandled, cp);
            RunFormat rf = RunFormat.From(props);
            _fmt.ParagraphStyle = p.Istd;
            var rpr = new StringBuilder();
            rf.WriteInner(rpr, _fmt);
            string rprXml = rpr.Length > 0 ? "<w:rPr>" + rpr + "</w:rPr>" : string.Empty;
            SwitchRevision(sb, st, rf);
            bool deleted = rf.Revision == 2;
            buf.Clear();

            void FlushText()
            {
                if (buf.Length == 0)
                {
                    return;
                }

                string element = st.InInstruction ? (deleted ? "w:delInstrText" : "w:instrText") : (deleted ? "w:delText" : "w:t");
                if (st.InInstruction && st.Fields.Count > 0)
                {
                    st.Fields[^1].Text.Append(buf);
                }

                sb.Append("<w:r>").Append(rprXml).Append('<').Append(element).Append(" xml:space=\"preserve\">").Esc(buf.ToString()).Append("</w:").Append(element[2..]).Append("></w:r>");
                buf.Clear();
            }

            void Run(string inner)
            {
                FlushText();
                sb.Append("<w:r>").Append(rprXml).Append(inner).Append("</w:r>");
            }

            for (int i = cp; i < runEnd; i++)
            {
                if (_events.TryGetValue(i, out List<string>? ev))
                {
                    FlushText();
                    foreach (string x in ev)
                    {
                        sb.Append(x);
                    }
                }

                char c = text[i];
                switch (c)
                {
                    case '\u0013':
                        FlushText();
                        st.Marks.Add(new FieldMark(sb.Length, sb));
                        Run("<w:fldChar w:fldCharType=\"begin\"/>");
                        st.Fields.Add((true, new StringBuilder()));
                        break;
                    case '\u0014':
                        Run("<w:fldChar w:fldCharType=\"separate\"/>");
                        if (st.Fields.Count > 0)
                        {
                            (bool _, StringBuilder t) = st.Fields[^1];
                            st.Fields[^1] = (false, t);
                            if (rf.IsOle && rf.PicLocation >= 0 && st.Marks.Count == st.Fields.Count)
                            {
                                // [MS-DOC] sprmCPicLocation: with sprmCFOle2 the separator stands for an OLE object, named by the number.
                                st.Marks[^1].ObjectId = unchecked((uint)rf.PicLocation);
                            }
                        }

                        break;
                    case '\u0015':
                        Run("<w:fldChar w:fldCharType=\"end\"/>");
                        if (st.Fields.Count > 0)
                        {
                            string instr = st.Fields[^1].Text.ToString();
                            ClassifyField(instr);
                            st.Fields.RemoveAt(st.Fields.Count - 1);
                            if (st.Marks.Count > st.Fields.Count)
                            {
                                FieldMark mark = st.Marks[^1];
                                st.Marks.RemoveAt(st.Marks.Count - 1);
                                if (mark.ObjectId is { } objectId)
                                {
                                    WriteObject(sb, st, mark, instr, rprXml, objectId);
                                }
                            }
                        }

                        break;
                    case '\t':
                        if (st.InInstruction)
                        {
                            buf.Append(' ');
                        }
                        else
                        {
                            Run("<w:tab/>");
                        }

                        break;
                    case '\v':
                        Run("<w:br/>");
                        break;
                    case '\f':
                        Run("<w:br w:type=\"page\"/>");
                        break;
                    case '\u000E':
                        Run("<w:br w:type=\"column\"/>");
                        break;
                    case '\u001E':
                        Run("<w:noBreakHyphen/>");
                        break;
                    case '\u001F':
                        Run("<w:softHyphen/>");
                        break;
                    case '\u0001':
                        if (rf.IsSpecial && !st.InInstruction)
                        {
                            FlushText();
                            WritePicture(sb, rprXml, rf, st);
                        }

                        break;
                    case '\u0002':
                        if (rf.IsSpecial)
                        {
                            Run(NoteMark(i, st));
                        }

                        break;
                    case '\u0005':
                        if (rf.IsSpecial)
                        {
                            Run(AnnotationMark(i, st));
                        }

                        break;
                    case '\b':
                        if (rf.IsSpecial)
                        {
                            FlushText();
                            WriteShapeAnchor(sb, rprXml, i, st);
                        }

                        break;
                    case '(':
                        if (rf.IsSpecial && rf.Symbol.Font >= 0 && _fonts.Name(rf.Symbol.Font) is { } symFont)
                        {
                            Run("<w:sym w:font=\"" + Xml(symFont) + "\" w:char=\"" + rf.Symbol.Char.ToString("X4", CultureInfo.InvariantCulture) + "\"/>");
                        }
                        else
                        {
                            buf.Append(c);
                        }

                        break;
                    default:
                        if (c >= ' ')
                        {
                            buf.Append(c);
                        }

                        break;
                }
            }

            FlushText();
            cp = runEnd;
        }

        // Events at the paragraph mark (a bookmark ending after the last character).
        if (_events.TryGetValue(end, out List<string>? last) && end < text.Length)
        {
            foreach (string x in last)
            {
                sb.Append(x);
            }
        }

    }

    private static string Xml(string s) => new StringBuilder().Esc(s).ToString();

    private string NoteMark(int cp, StoryState st)
    {
        if (st.Kind == StoryKind.Footnote)
        {
            return "<w:footnoteRef/>";
        }

        if (st.Kind == StoryKind.Endnote)
        {
            return "<w:endnoteRef/>";
        }

        if (_fndRefs.TryGetValue(cp, out int fi))
        {
            return "<w:footnoteReference w:id=\"" + WmlWrite.I(fi + 1) + "\"/>";
        }

        if (_ednRefs.TryGetValue(cp, out int ei))
        {
            return "<w:endnoteReference w:id=\"" + WmlWrite.I(ei + 1) + "\"/>";
        }

        return string.Empty;
    }

    private string AnnotationMark(int cp, StoryState st)
    {
        if (st.Kind == StoryKind.Comment)
        {
            return "<w:annotationRef/>";
        }

        return _atnRefs.TryGetValue(cp, out int ai) ? "<w:commentReference w:id=\"" + WmlWrite.I(ai) + "\"/>" : string.Empty;
    }

    private static bool IsObjectField(string instr)
    {
        string t = instr.TrimStart();
        return t.StartsWith("EMBED", StringComparison.OrdinalIgnoreCase) || t.StartsWith("LINK", StringComparison.OrdinalIgnoreCase);
    }

    private void ClassifyField(string instr)
    {
        _fields++;
        string t = instr.TrimStart();
        if (IsObjectField(t))
        {
            _oleObjects++;
        }

        if (t.StartsWith("FORMTEXT", StringComparison.OrdinalIgnoreCase) || t.StartsWith("FORMCHECKBOX", StringComparison.OrdinalIgnoreCase) || t.StartsWith("FORMDROPDOWN", StringComparison.OrdinalIgnoreCase))
        {
            _formFields++;
        }
    }

    private void CountUnmapped(List<Prl> props, HashSet<ushort> handled, int cp)
    {
        foreach (Prl pr in props)
        {
            if (handled.Contains(pr.Sprm))
            {
                continue;
            }

            _unmappedSprms++;
            if (_reportedSprms.Add(pr.Sprm))
            {
                string name = pr.Name ?? "0x" + pr.Sprm.ToString("X4", CultureInfo.InvariantCulture);
                _sink.Report(new Defect(DefectKind.Unsupported, "doc.sprm-not-mapped", DefectSeverity.Dropped,
                    $"{name} is not carried into the document model (first seen at character {cp}).", DefectLocation.InRecord(DocFile.WordPath, FcOf(cp))));
            }
        }
    }

    private long FcOf(int cp)
    {
        int pi = _f.Pieces.Find(cp);
        return pi < 0 ? -1 : _f.Pieces.Pieces[pi].OffsetOf(cp);
    }
}
