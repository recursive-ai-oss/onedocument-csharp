using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Rtf;

namespace RecursiveAI.OneDocument.Documents.Rtf;

// RTF -> WordprocessingML. The interpreter walks the group tree once, in file order, keeping RTF's group-scoped state in St, and writes
// the XML of each story (body, headers, footnotes, comments) as it goes. Parts are assembled at the end (RtfImporter.Package.cs).
// RTF 1.9.1 is the source for every control word handled here; ECMA-376 Part 1 for the elements written.
internal sealed partial class RtfImporter
{
    private readonly RtfTree _tree;
    private readonly OpenContext _ctx;
    private readonly CancellationToken _ct;
    private readonly Dictionary<int, FontEntry> _fonts = [];
    private readonly List<int> _colors = [];
    private readonly List<string> _authors = [];
    private readonly Dictionary<string, StyleDef> _styles = [];
    private readonly List<ListDef> _lists = [];
    private readonly List<ListOverrideDef> _overrides = [];
    private readonly Dictionary<int, RowDef> _rowDefs = [];
    private readonly Dictionary<int, int> _bookmarkIds = new();
    private readonly Dictionary<string, int> _bookmarkByName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> _commentRanges = [];
    private readonly List<(int Id, string Xml)> _footnotes = [];
    private readonly List<(int Id, string Xml)> _endnotes = [];
    private readonly List<(int Id, string Xml)> _comments = [];
    private readonly List<byte[]> _preserved = [];
    private readonly Dictionary<string, string> _info = new(StringComparer.Ordinal);
    private readonly SectionDef _docSec = new();
    private readonly Dictionary<int, Encoding?> _encodings = [];
    private readonly List<byte> _pending = [];

    private SectionDef _sec;
    private Story _body;
    private Story _story;
    private RelSet _bodyRels = new();
    private RelSet _fnRels = new();
    private RelSet _enRels = new();
    private RelSet _cmRels = new();
    private int _ansiCp = 1252;
    private int _defFont;
    private int _defLang = 1033;
    private int _defTab = 720;
    private bool _facingPages;
    private bool _widowCtl;
    private bool _trackRevisions;
    private int _nest;
    private int _nextBookmark = 1;
    private int _nextRevision = 1;
    private int _nextDocPr = 1;
    private int _fnSeq;
    private int _enSeq;
    private int _cmSeq;
    private int _headerSeq;
    private int _footerSeq;
    private int _picSeq;
    private long _pictureBytes;
    private string? _atnInitials;
    private string? _atnAuthor;
    private bool _inFootnote;
    private bool _inEndnote;
    private StyleDef? _curStyle;
    private StringBuilder? _styleName;
    private Brdr[]? _brdrTarget;
    private CellDef _pendingCell = new();
    private bool _atRootLevel;

    internal RtfImporter(RtfTree tree, OpenContext context, CancellationToken cancellationToken)
    {
        _tree = tree;
        _ctx = context;
        _ct = cancellationToken;
        _sec = _docSec.Clone();
        _body = new Story { Rels = _bodyRels };
        _story = _body;
    }

    /// <summary>What the conversion could not carry into the hub.</summary>
    public LossReport Loss { get; } = new();

    private const string Stream = "rtf";

    private void Defect(string code, DefectKind kind, DefectSeverity severity, string message, int offset) =>
        _ctx.Defects.Report(new Defect(kind, code, severity, message, DefectLocation.InRecord(Stream, offset)));

    // ---- the walk ---------------------------------------------------------------------------------------------------------------

    internal void Run()
    {
        RtfNode root = _tree.Root;
        var st = new St();
        _atRootLevel = true;
        Walk(root, st, 0);
        _atRootLevel = false;
        FinishBody(st);
    }

    private void Walk(RtfNode group, St st, int from)
    {
        IReadOnlyList<RtfNode> kids = group.Children;
        for (int i = from; i < kids.Count; i++)
        {
            RtfNode n = kids[i];
            switch (n.Kind)
            {
                case RtfNodeKind.Group:
                    FlushBytes(st);
                    ProcessGroup(n, st, group);
                    break;
                case RtfNodeKind.ControlWord:
                    FlushBytes(st);
                    Word(n, st, group);
                    break;
                case RtfNodeKind.ControlSymbol:
                    FlushBytes(st);
                    Symbol(n, st);
                    break;
                case RtfNodeKind.Hex:
                    if (st.Skip > 0)
                    {
                        st.Skip--;
                    }
                    else
                    {
                        _pending.Add(n.Hex);
                    }

                    break;
                case RtfNodeKind.Text:
                    {
                        ReadOnlySpan<byte> data = n.Data.Span;
                        if (st.Skip > 0)
                        {
                            int skip = Math.Min(st.Skip, data.Length);
                            st.Skip -= skip;
                            data = data[skip..];
                        }

                        foreach (byte b in data)
                        {
                            _pending.Add(b);
                        }

                        break;
                    }
            }
        }

        FlushBytes(st);
    }

    // ---- text --------------------------------------------------------------------------------------------------------------------

    private int CodePageOf(St st)
    {
        if (st.Font is int f && _fonts.TryGetValue(f, out FontEntry? fe))
        {
            if (fe.CodePage > 0)
            {
                return fe.CodePage;
            }
        }
        else if (st.Font is null && _fonts.TryGetValue(_defFont, out FontEntry? def) && def.CodePage > 0)
        {
            return def.CodePage;
        }

        return _ansiCp;
    }

    private Encoding? EncodingFor(int cp)
    {
        if (!_encodings.TryGetValue(cp, out Encoding? e))
        {
            e = RtfCodePages.Decoder(cp);
            _encodings[cp] = e;
        }

        return e;
    }

    private void FlushBytes(St st)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        byte[] bytes = [.. _pending];
        _pending.Clear();
        if (_curStyle is not null)
        {
            // Inside a style definition the text is the style's name.
            _styleName?.Append(Latin(bytes, CodePageOf(st)));
            return;
        }

        if (_collect is not null)
        {
            _collect.Append(Latin(bytes, CodePageOf(st)));
            return;
        }

        if (st.SymbolFont)
        {
            foreach (byte b in bytes)
            {
                if (b >= 0x20)
                {
                    AddSymbol(st, b);
                }
            }

            return;
        }

        AddText(st, Latin(bytes, CodePageOf(st)));
    }

    private string Latin(byte[] bytes, int cp)
    {
        Encoding? enc = EncodingFor(cp) ?? EncodingFor(1252);
        return enc is null ? Encoding.Latin1.GetString(bytes) : enc.GetString(bytes);
    }

    private StringBuilder? _collect;

    // The plain text of a group (field instructions, info strings, names): decoded, with no formatting or structure.
    private string PlainText(RtfNode group, St st)
    {
        StringBuilder? saved = _collect;
        var sb = new StringBuilder();
        _collect = sb;
        St copy = st.Clone();
        copy.SymbolFont = false;
        try
        {
            PlainWalk(group, copy);
        }
        finally
        {
            _collect = saved;
        }

        return sb.ToString();
    }

    private void PlainWalk(RtfNode group, St st)
    {
        foreach (RtfNode n in group.Children)
        {
            switch (n.Kind)
            {
                case RtfNodeKind.Group:
                    {
                        FlushBytes(st);
                        string? d = n.Destination;
                        if (n.IsStarred && d is not ("fldinst" or "fldrslt"))
                        {
                            break;
                        }

                        if (d is "fldrslt" or "pict" or "fonttbl" or "colortbl")
                        {
                            break;
                        }

                        St c = st.Clone();
                        PlainWalk(n, c);
                        FlushBytes(c);
                        break;
                    }

                case RtfNodeKind.ControlWord:
                    FlushBytes(st);
                    switch (n.Name)
                    {
                        case "f":
                            st.Font = n.Parameter;
                            break;
                        case "uc":
                            st.Uc = Math.Max(0, n.Parameter);
                            break;
                        case "u":
                            AppendUnicode(_collect!, n.Parameter);
                            st.Skip = st.Uc;
                            break;
                        case "tab":
                            _collect!.Append('\t');
                            break;
                        case "par" or "line":
                            _collect!.Append(' ');
                            break;
                    }

                    break;
                case RtfNodeKind.ControlSymbol:
                    FlushBytes(st);
                    switch (n.Name)
                    {
                        case "\\" or "{" or "}":
                            _collect!.Append(n.Name);
                            break;
                        case "~":
                            _collect!.Append('\u00A0');
                            break;
                    }

                    break;
                case RtfNodeKind.Hex:
                    if (st.Skip > 0)
                    {
                        st.Skip--;
                    }
                    else
                    {
                        _pending.Add(n.Hex);
                    }

                    break;
                case RtfNodeKind.Text:
                    {
                        ReadOnlySpan<byte> data = n.Data.Span;
                        if (st.Skip > 0)
                        {
                            int skip = Math.Min(st.Skip, data.Length);
                            st.Skip -= skip;
                            data = data[skip..];
                        }

                        foreach (byte b in data)
                        {
                            _pending.Add(b);
                        }

                        break;
                    }
            }
        }

        FlushBytes(st);
    }

    // RTF 1.9.1 \uN: a UTF-16 code unit as a signed 16-bit number.
    private static void AppendUnicode(StringBuilder sb, int n) => sb.Append((char)(ushort)(short)n);

    // ---- runs and paragraphs -----------------------------------------------------------------------------------------------------

    private void AddText(St st, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        Story s = _story;
        SetRunProps(st);
        s.PendingText.Append(text);
        s.HasContent = true;
    }

    private void AddSymbol(St st, int code)
    {
        string font = st.Font is int f && _fonts.TryGetValue(f, out FontEntry? fe) ? fe.Name : "Symbol";
        SetRunProps(st);
        Story s = _story;
        FlushT(s);
        s.PendingXml.Append("<w:sym w:font=\"").Append(X.Esc(font)).Append("\" w:char=\"").Append((0xF000 + code).ToString("X4", CultureInfo.InvariantCulture)).Append("\"/>");
        s.HasContent = true;
    }

    // Adds a non-text child (tab, break, ...) to the open run.
    private void AddSpecial(St st, string xml)
    {
        Story s = _story;
        SetRunProps(st);
        FlushT(s);
        s.PendingXml.Append(xml);
        s.HasContent = true;
    }

    // The pending run holds one set of properties; a change of properties closes it.
    private void SetRunProps(St st)
    {
        Story s = _story;
        string props = RunProps(st);
        string rev = RevisionKey(st);
        if (s.PendingProps is not null && (s.PendingProps != props || s.PendingRev != rev))
        {
            FlushRun(s);
        }

        if (s.PendingProps is null)
        {
            s.PendingProps = props;
            s.PendingRev = rev;
            s.PendingDeleted = st.DelOn;
            s.PendingRevState = (st.InsOn ? (st.Ins, st.InsDttm) : (-1, 0), st.DelOn ? (st.Del, st.DelDttm) : (-1, 0));
        }
    }

    private static string RevisionKey(St st) =>
        (st.InsOn ? "i" + X.N(st.Ins) + "@" + X.N(st.InsDttm) : string.Empty) + (st.DelOn ? "d" + X.N(st.Del) + "@" + X.N(st.DelDttm) : string.Empty);

    private static void FlushT(Story s)
    {
        if (s.PendingText.Length == 0)
        {
            return;
        }

        string tag = s.PendingDeleted ? "w:delText" : "w:t";
        s.PendingXml.Append('<').Append(tag).Append(" xml:space=\"preserve\">").Append(X.Esc(s.PendingText.ToString())).Append("</").Append(tag).Append('>');
        s.PendingText.Clear();
    }

    // Closes the pending run into the paragraph's content.
    private void FlushRun(Story s)
    {
        FlushT(s);
        if (s.PendingProps is null)
        {
            return;
        }

        if (s.PendingXml.Length > 0)
        {
            var run = new StringBuilder("<w:r>");
            if (s.PendingProps.Length > 0)
            {
                run.Append("<w:rPr>").Append(s.PendingProps).Append("</w:rPr>");
            }

            run.Append(s.PendingXml).Append("</w:r>");
            string xml = run.ToString();
            ((int Id, int Dttm) ins, (int Id, int Dttm) del) = s.PendingRevState;
            if (del.Id >= 0 || s.PendingRev.Contains('d', StringComparison.Ordinal))
            {
                xml = "<w:del w:id=\"" + X.N(_nextRevision++) + "\" w:author=\"" + X.Esc(AuthorName(del.Id)) + "\"" + DateAttr(del.Dttm) + ">" + xml + "</w:del>";
            }

            if (ins.Id >= 0 || s.PendingRev.Contains('i', StringComparison.Ordinal))
            {
                xml = "<w:ins w:id=\"" + X.N(_nextRevision++) + "\" w:author=\"" + X.Esc(AuthorName(ins.Id)) + "\"" + DateAttr(ins.Dttm) + ">" + xml + "</w:ins>";
            }

            s.Runs.Append(xml);
        }

        s.PendingXml.Clear();
        s.PendingProps = null;
        s.PendingRev = string.Empty;
        s.PendingDeleted = false;
        s.PendingRevState = ((-1, 0), (-1, 0));
    }

    private string AuthorName(int index) => index >= 0 && index < _authors.Count && _authors[index].Length > 0 ? _authors[index] : "Unknown";

    private static string DateAttr(int dttm) => X.Dttm(dttm) is { } d ? " w:date=\"" + d + "\"" : string.Empty;

    // Appends content that is not a run (bookmarks, comment anchors, field characters already wrapped in runs).
    private void AppendInline(string xml)
    {
        Story s = _story;
        FlushRun(s);
        s.Runs.Append(xml);
        s.HasContent = true;
    }

    // A run of its own with the given properties, so that fields and notes keep the formatting they sit in.
    private void AppendOwnRun(St st, string content)
    {
        Story s = _story;
        FlushRun(s);
        string props = RunProps(st);
        s.Runs.Append("<w:r>").Append(props.Length > 0 ? "<w:rPr>" + props + "</w:rPr>" : string.Empty).Append(content).Append("</w:r>");
        s.HasContent = true;
    }

    private void EndParagraph(St st)
    {
        _bodyStarted = true;
        Story s = _story;
        FlushRun(s);
        ParaFmt p = st.P;
        int itap = p.InTable ? Math.Max(1, p.Itap) : 0;
        AdjustTables(s, itap);
        var block = new Block { ParaProps = ParaProps(st, p), Content = s.Runs.ToString() };
        s.Target.Add(block);
        s.Runs.Clear();
        s.HasContent = false;
    }

    private void AdjustTables(Story s, int itap)
    {
        while (s.Tables.Count > itap)
        {
            CloseTable(s);
        }

        while (s.Tables.Count < itap)
        {
            s.Tables.Add(new TableFrame { Depth = s.Tables.Count + 1 });
        }
    }

    private void EndCell(St st)
    {
        Story s = _story;
        int itap = st.P.InTable ? Math.Max(1, st.P.Itap) : Math.Max(1, s.Tables.Count);
        AdjustTables(s, itap);
        TableFrame t = s.Tables[^1];
        if (s.HasContent || s.PendingProps is not null || t.Cell.Blocks.Count == 0)
        {
            ParaFmt saved = st.P;
            if (!saved.InTable)
            {
                st.P = saved.Clone();
                st.P.InTable = true;
                st.P.Itap = itap;
            }

            EndParagraph(st);
            st.P = saved;
        }

        t.Row.Cells.Add(t.Cell);
        t.Cell = new CellFrame();
    }

    private void EndRow(St st)
    {
        Story s = _story;
        if (s.Tables.Count == 0)
        {
            return;
        }

        if (s.HasContent || s.PendingProps is not null)
        {
            EndCell(st);
        }

        TableFrame t = s.Tables[^1];
        if (t.Row.Cells.Count == 0 && t.Cell.Blocks.Count > 0)
        {
            t.Row.Cells.Add(t.Cell);
            t.Cell = new CellFrame();
        }

        t.Row.Def = _rowDefs.GetValueOrDefault(t.Depth);
        t.Rows.Add(t.Row);
        t.Row = new RowFrame();
    }

    private void CloseTable(Story s)
    {
        TableFrame t = s.Tables[^1];
        s.Tables.RemoveAt(s.Tables.Count - 1);
        if (t.Cell.Blocks.Count > 0)
        {
            t.Row.Cells.Add(t.Cell);
            t.Cell = new CellFrame();
        }

        if (t.Row.Cells.Count > 0)
        {
            Defect("rtf.unclosed-row", DefectKind.Record, DefectSeverity.Repaired, "a table row has no \\row; it is closed at the end of the table", 0);
            t.Row.Def = _rowDefs.GetValueOrDefault(t.Depth);
            t.Rows.Add(t.Row);
        }

        if (t.Rows.Count > 0)
        {
            s.Target.Add(new Block { IsTable = true, Content = TableXml(t) });
        }
    }

    // Ends a story: the open paragraph (if it has text) and every open table. A table at the end of a cell or part gets the empty
    // paragraph WordprocessingML requires after it (ECMA-376 Part 1 §17.4.4: a cell ends with a paragraph).
    private string FinishStory(Story s, St st, bool forceEndPara = false)
    {
        FlushRun(s);
        if (s.HasContent || forceEndPara)
        {
            EndParagraph(st);
        }

        while (s.Tables.Count > 0)
        {
            CloseTable(s);
        }

        if (s.Blocks.Count == 0 || s.Blocks[^1].IsTable)
        {
            s.Blocks.Add(new Block { ParaProps = string.Empty, Content = string.Empty });
        }

        var sb = new StringBuilder();
        foreach (Block b in s.Blocks)
        {
            sb.Append(b.ToXml());
        }

        return sb.ToString();
    }

    private void EndSection(St st)
    {
        Story s = _story;
        if (!ReferenceEquals(s, _body))
        {
            return;
        }

        if (s.HasContent || s.PendingProps is not null)
        {
            EndParagraph(st);
        }

        while (s.Tables.Count > 0)
        {
            CloseTable(s);
        }

        string sect = SectPr(_sec);
        if (s.Blocks.Count > 0 && !s.Blocks[^1].IsTable && s.Blocks[^1].SectPr is null)
        {
            s.Blocks[^1].SectPr = sect;
        }
        else
        {
            s.Blocks.Add(new Block { ParaProps = string.Empty, Content = string.Empty, SectPr = sect });
        }

        _sec.Refs = [];
    }

    private void FinishBody(St st)
    {
        Story s = _body;
        _story = s;
        _bodyXml = FinishStory(s, st);
        _finalSect = SectPr(_sec);
    }

    private string _bodyXml = string.Empty;
    private string _finalSect = string.Empty;
}
