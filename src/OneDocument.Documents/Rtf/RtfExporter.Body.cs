using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Equations;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Rtf;

internal sealed partial class RtfExporter
{
    // ---- sections (ECMA-376 Part 1 §17.6; RTF 1.9.1 "Section Formatting Properties") ---------------------------------------------

    // WordprocessingML puts a section's properties at its end (in the last paragraph's pPr); RTF puts them at its start. The sections
    // are found first, in document order.
    private void FindSections(MarkupNode body)
    {
        _sections.Clear();
        if (body.IsNull)
        {
            return;
        }

        foreach (MarkupNode c in body.ChildElements)
        {
            if (IsW(c, "p") && Child(c, "pPr") is { IsNull: false } ppr && Child(ppr, "sectPr") is { IsNull: false } sp)
            {
                _sections.Add(sp);
            }
            else if (IsW(c, "sectPr"))
            {
                _sections.Add(c);
            }
        }
    }

    private readonly Dictionary<string, string> _lastStory = new(StringComparer.Ordinal);

    private void SectionProps(MarkupNode sect)
    {
        _w.Word("sectd");
        if (sect.IsNull)
        {
            return;
        }

        string type = Child(sect, "type") is { IsNull: false } t ? A(t, "val") ?? "nextPage" : "nextPage";
        _w.Word(type switch { "continuous" => "sbknone", "nextColumn" => "sbkcol", "evenPage" => "sbkeven", "oddPage" => "sbkodd", _ => "sbkpage" });
        MarkupNode size = Child(sect, "pgSz");
        if (!size.IsNull)
        {
            _w.Word("pgwsxn", Int(A(size, "w"), 12240));
            _w.Word("pghsxn", Int(A(size, "h"), 15840));
            if (A(size, "orient") == "landscape")
            {
                _w.Word("lndscpsxn");
            }
        }

        MarkupNode mar = Child(sect, "pgMar");
        if (!mar.IsNull)
        {
            _w.Word("marglsxn", Int(A(mar, "left") ?? A(mar, "start"), 1800));
            _w.Word("margrsxn", Int(A(mar, "right") ?? A(mar, "end"), 1800));
            _w.Word("margtsxn", Int(A(mar, "top"), 1440));
            _w.Word("margbsxn", Int(A(mar, "bottom"), 1440));
            _w.Word("guttersxn", Int(A(mar, "gutter")));
            _w.Word("headery", Int(A(mar, "header"), 720));
            _w.Word("footery", Int(A(mar, "footer"), 720));
        }

        MarkupNode cols = Child(sect, "cols");
        if (!cols.IsNull && Int(A(cols, "num"), 1) > 1)
        {
            _w.Word("cols", Int(A(cols, "num"), 1));
            _w.Word("colsx", Int(A(cols, "space"), 720));
            if (A(cols, "sep") is "1" or "true")
            {
                _w.Word("linebetween");
            }
        }

        MarkupNode pgn = Child(sect, "pgNumType");
        if (!pgn.IsNull)
        {
            _w.Word(A(pgn, "fmt") switch
            {
                "upperRoman" => "pgnucrm",
                "lowerRoman" => "pgnlcrm",
                "upperLetter" => "pgnucltr",
                "lowerLetter" => "pgnlcltr",
                _ => "pgndec",
            });
            if (A(pgn, "start") is { } st)
            {
                _w.Word("pgnstart", Int(st, 1));
            }
        }

        if (!Child(sect, "titlePg").IsNull)
        {
            _w.Word("titlepg");
        }

        if (Child(sect, "bidi") is { IsNull: false } bidi && On(bidi))
        {
            _w.Word("rtlsect");
        }

        _w.Newline();
        foreach (MarkupNode r in sect.ChildElements)
        {
            bool header = IsW(r, "headerReference");
            if (!header && !IsW(r, "footerReference"))
            {
                continue;
            }

            string kind = header ? "header" : "footer";
            string refType = A(r, "type") ?? "default";
            string word = kind + refType switch { "first" => "f", "even" => "l", _ => _facing ? "r" : string.Empty };
            string? rid = RA(r, "id");
            IPackagePart? part = rid is null ? null : Target(rid);
            if (part is null)
            {
                continue;
            }

            // A section without a header of a kind shows the one before it, so a part used again by the next section is written once.
            if (_lastStory.TryGetValue(word, out string? previous) && previous == part.Name)
            {
                continue;
            }

            _lastStory[word] = part.Name;

            MarkupNode root = LoadPart(part.Name);
            if (root.IsNull)
            {
                continue;
            }

            string saved = _part;
            _part = part.Name;
            _w.StartGroup();
            _w.Word(word);
            _headerDepth++;
            try
            {
                Blocks(root, 0, false);
                CloseFields();
            }
            finally
            {
                _headerDepth--;
                _part = saved;
            }

            _w.EndGroup();
            _w.Newline();
        }
    }

    // ---- blocks -------------------------------------------------------------------------------------------------------------------

    private IEnumerable<MarkupNode> BlockChildren(MarkupNode container)
    {
        foreach (MarkupNode c in container.ChildElements)
        {
            if (c.NamespaceUri is not (W or WStrict))
            {
                if (c.LocalNameString == "AlternateContent")
                {
                    foreach (MarkupNode choice in c.ChildElements)
                    {
                        if (choice.LocalNameString == "Choice" || choice.LocalNameString == "Fallback")
                        {
                            foreach (MarkupNode inner in BlockChildren(choice))
                            {
                                yield return inner;
                            }

                            break;
                        }
                    }
                }

                continue;
            }

            switch (c.LocalNameString)
            {
                case "sdt":
                    _loss.Report("loss.rtf-content-control", "content controls", 1, "Content controls are written as their content; the control and its data binding are not.");
                    MarkupNode content = Child(c, "sdtContent");
                    if (!content.IsNull)
                    {
                        foreach (MarkupNode inner in BlockChildren(content))
                        {
                            yield return inner;
                        }
                    }

                    break;
                case "customXml" or "ins" or "del" or "moveTo" or "moveFrom":
                    foreach (MarkupNode inner in BlockChildren(c))
                    {
                        yield return inner;
                    }

                    break;
                default:
                    yield return c;
                    break;
            }
        }
    }

    private void Blocks(MarkupNode container, int itap, bool inCell)
    {
        List<MarkupNode> items = [.. BlockChildren(container)];
        int lastPara = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (IsW(items[i], "p"))
            {
                lastPara = i;
            }
        }

        bool endsWithTable = items.Count > 0 && IsW(items[^1], "tbl");
        for (int i = 0; i < items.Count; i++)
        {
            _ct.ThrowIfCancellationRequested();
            MarkupNode n = items[i];
            if (IsW(n, "p"))
            {
                Paragraph(n, itap, inCell && i == items.Count - 1 && lastPara == i);
            }
            else if (IsW(n, "tbl"))
            {
                Table(n, itap + 1);
            }
            else if (IsW(n, "bookmarkStart") || IsW(n, "bookmarkEnd"))
            {
                Bookmark(n);
            }
        }

        if (inCell && (endsWithTable || items.Count == 0))
        {
            // A cell has to end with its own \cell; the table inside does not provide one.
            _w.Word("pard");
            _w.Word("plain");
            _w.Word("intbl");
            if (itap > 1)
            {
                _w.Word("itap", itap);
            }

            _w.Word(itap > 1 ? "nestcell" : "cell");
        }
    }

    private readonly Dictionary<string, string> _bookmarkNames = new(StringComparer.Ordinal);

    private void Bookmark(MarkupNode n)
    {
        string id = A(n, "id") ?? string.Empty;
        if (n.LocalNameString == "bookmarkStart")
        {
            string name = A(n, "name") ?? "bookmark" + id;
            _bookmarkNames[id] = name;
            _w.StartDestination("bkmkstart");
            _w.Text(name);
            _w.EndGroup();
        }
        else if (_bookmarkNames.TryGetValue(id, out string? name))
        {
            _w.StartDestination("bkmkend");
            _w.Text(name);
            _w.EndGroup();
        }
    }

    // ---- paragraphs --------------------------------------------------------------------------------------------------------------

    private void Paragraph(MarkupNode p, int itap, bool cellEnd)
    {
        MarkupNode ppr = Child(p, "pPr");
        _w.Word("pard");
        _w.Word("plain");
        if (itap > 0)
        {
            _w.Word("intbl");
            if (itap > 1)
            {
                _w.Word("itap", itap);
            }
        }

        if (!ppr.IsNull)
        {
            MarkupNode ps = Child(ppr, "pStyle");
            int sn = ps.IsNull ? -1 : StyleNumber("paragraph", A(ps, "val"));
            if (sn > 0)
            {
                _w.Word("s", sn);
            }

            ParagraphWords(ppr, inStyle: false);
        }

        _w.Newline();
        Inline(p, default, default);
        MarkupNode sect = ppr.IsNull ? default : Child(ppr, "sectPr");
        if (cellEnd)
        {
            _w.Word(itap > 1 ? "nestcell" : "cell");
        }
        else if (!sect.IsNull && itap == 0)
        {
            _w.Word("par");
            _w.Word("sect");
            _sectionIndex++;
            if (_sectionIndex < _sections.Count)
            {
                SectionProps(_sections[_sectionIndex]);
            }
        }
        else
        {
            _w.Word("par");
        }

        _w.Newline();
    }

    private readonly record struct Rev(string? Author, string? Date, bool Present);

    // The content of a paragraph, hyperlink or other run container.
    private void Inline(MarkupNode container, Rev ins, Rev del)
    {
        foreach (MarkupNode c in container.ChildElements)
        {
            if (c.NamespaceUri is not (W or WStrict))
            {
                if (c.LocalNameString == "AlternateContent")
                {
                    foreach (MarkupNode choice in c.ChildElements)
                    {
                        if (choice.LocalNameString is "Choice" or "Fallback")
                        {
                            Inline(choice, ins, del);
                            break;
                        }
                    }
                }
                else if (c.LocalNameString == "oMath" || c.LocalNameString == "oMathPara")
                {
                    _loss.Report("loss.rtf-equation", "equations", 1, "Equations are written as their linear text (for example (a+b)/c), not as math.");
                    Text(MathLinearizer.Linearize(OmmlReader.Read(c, out _)), ins, del, default);
                }

                continue;
            }

            switch (c.LocalNameString)
            {
                case "r": Run(c, ins, del); break;
                case "hyperlink": Hyperlink(c, ins, del); break;
                case "fldSimple":
                    _w.StartGroup();
                    _w.Word("field");
                    _w.StartDestination("fldinst");
                    _w.Text(A(c, "instr") ?? string.Empty);
                    _w.EndGroup();
                    _w.StartGroup();
                    _w.Word("fldrslt");
                    Inline(c, ins, del);
                    _w.EndGroup();
                    _w.EndGroup();
                    break;
                case "ins" or "moveTo":
                    Inline(c, new Rev(A(c, "author"), A(c, "date"), true), del);
                    break;
                case "del" or "moveFrom":
                    Inline(c, ins, new Rev(A(c, "author"), A(c, "date"), true));
                    break;
                case "bookmarkStart" or "bookmarkEnd": Bookmark(c); break;
                case "commentRangeStart":
                    _w.StartDestination("atrfstart");
                    _w.Text(A(c, "id") ?? "0");
                    _w.EndGroup();
                    break;
                case "commentRangeEnd":
                    _w.StartDestination("atrfend");
                    _w.Text(A(c, "id") ?? "0");
                    _w.EndGroup();
                    break;
                case "sdt":
                    {
                        _loss.Report("loss.rtf-content-control", "content controls", 1, "Content controls are written as their content; the control and its data binding are not.");
                        MarkupNode content = Child(c, "sdtContent");
                        if (!content.IsNull)
                        {
                            Inline(content, ins, del);
                        }

                        break;
                    }

                case "smartTag" or "customXml" or "dir" or "bdo":
                    Inline(c, ins, del);
                    break;
            }
        }
    }

    private void Hyperlink(MarkupNode h, Rev ins, Rev del)
    {
        string? anchor = A(h, "anchor");
        string? rid = RA(h, "id");
        string target = string.Empty;
        if (rid is not null && RelationshipOf(rid) is { } rel)
        {
            target = rel.Target;
        }

        string instr;
        if (target.Length > 0)
        {
            instr = " HYPERLINK \"" + target + (anchor is not null ? "#" + anchor : string.Empty) + "\" ";
        }
        else if (anchor is not null)
        {
            instr = " HYPERLINK \\l \"" + anchor + "\" ";
        }
        else
        {
            Inline(h, ins, del);
            return;
        }

        _w.StartGroup();
        _w.Word("field");
        _w.StartDestination("fldinst");
        _w.Text(instr);
        _w.EndGroup();
        _w.StartGroup();
        _w.Word("fldrslt");
        Inline(h, ins, del);
        _w.EndGroup();
        _w.EndGroup();
    }

    // ---- runs --------------------------------------------------------------------------------------------------------------------

    private sealed class FieldState
    {
        public StringBuilder Instr = new();
        public bool Separated;
    }

    private readonly Stack<FieldState> _fields = new();

    private bool InInstruction => _fields.Count > 0 && !_fields.Peek().Separated;

    // Notes and comments are written in the middle of a paragraph; the fields open around them stay open and are put back after.
    private FieldState[] SuspendFields()
    {
        FieldState[] saved = _fields.ToArray();
        _fields.Clear();
        return saved;
    }

    private void ResumeFields(FieldState[] saved)
    {
        for (int i = saved.Length - 1; i >= 0; i--)
        {
            _fields.Push(saved[i]);
        }
    }

    private void CloseFields()
    {
        while (_fields.Count > 0)
        {
            EndField();
        }
    }

    private void BeginField() => _fields.Push(new FieldState());

    private void SeparateField()
    {
        if (_fields.Count == 0 || _fields.Peek().Separated)
        {
            return;
        }

        FieldState f = _fields.Peek();
        f.Separated = true;
        _w.StartGroup();
        _w.Word("field");
        _w.StartDestination("fldinst");
        _w.Text(f.Instr.ToString());
        _w.EndGroup();
        _w.StartGroup();
        _w.Word("fldrslt");
        _w.Newline();
    }

    private void EndField()
    {
        if (_fields.Count == 0)
        {
            return;
        }

        if (!_fields.Peek().Separated)
        {
            SeparateField();
        }

        _fields.Pop();
        _w.EndGroup();
        _w.EndGroup();
    }

    private void Run(MarkupNode r, Rev ins, Rev del)
    {
        MarkupNode rpr = Child(r, "rPr");
        bool group = !rpr.IsNull || ins.Present || del.Present;
        bool opened = false;
        foreach (MarkupNode c in r.ChildElements)
        {
            if (c.NamespaceUri is not (W or WStrict))
            {
                if (c.LocalNameString == "AlternateContent")
                {
                    Drawing(c);
                }

                continue;
            }

            string name = c.LocalNameString;
            switch (name)
            {
                case "rPr":
                    continue;
                case "fldChar":
                    switch (A(c, "fldCharType"))
                    {
                        case "begin": BeginField(); break;
                        case "separate": SeparateField(); break;
                        case "end": EndField(); break;
                    }

                    continue;
                case "instrText" or "delInstrText":
                    if (InInstruction)
                    {
                        _fields.Peek().Instr.Append(c.InnerText);
                    }

                    continue;
            }

            if (InInstruction)
            {
                continue; // nothing but the field code lives between begin and separate
            }

            if (group && !opened)
            {
                opened = true;
                _w.StartGroup();
                if (!rpr.IsNull)
                {
                    RunWords(rpr);
                }

                if (ins.Present)
                {
                    _w.Word("revised");
                    _w.Word("revauth", AuthorIndex(ins.Author));
                    if (RevDttm(ins.Date) is int d)
                    {
                        _w.Word("revdttm", d);
                    }
                }

                if (del.Present)
                {
                    _w.Word("deleted");
                    _w.Word("revauthdel", AuthorIndex(del.Author));
                    if (RevDttm(del.Date) is int d)
                    {
                        _w.Word("revdttmdel", d);
                    }
                }
            }

            RunChild(c, name, rpr);
        }

        if (opened)
        {
            _w.EndGroup();
        }
    }

    private void RunChild(MarkupNode c, string name, MarkupNode rpr)
    {
        switch (name)
        {
            case "t" or "delText":
                _w.Text(c.InnerText.Replace('\n', ' ').Replace('\r', ' '));
                break;
            case "tab" or "ptab":
                _w.Word("tab");
                break;
            case "br":
                _w.Word(A(c, "type") switch { "page" => "page", "column" => "column", _ => "line" });
                break;
            case "cr":
                _w.Word("line");
                break;
            case "noBreakHyphen":
                _w.Symbol('_');
                break;
            case "softHyphen":
                _w.Symbol('-');
                break;
            case "sym":
                {
                    string font = A(c, "font") ?? "Symbol";
                    string ch = A(c, "char") ?? "F020";
                    int code = int.TryParse(ch, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cc) ? cc & 0xFF : 0x20;
                    _w.StartGroup();
                    _w.Word("f", FontIndex(font, symbol: true));
                    _w.Hex((byte)code);
                    _w.EndGroup();
                    break;
                }

            case "footnoteReference":
                Note(c, endnote: false);
                break;
            case "endnoteReference":
                Note(c, endnote: true);
                break;
            case "footnoteRef" or "endnoteRef":
                _w.StartGroup();
                _w.Word("super");
                _w.Word("chftn");
                _w.EndGroup();
                break;
            case "annotationRef":
                break;
            case "commentReference":
                Comment(c);
                break;
            case "pgNum":
                _w.Word("chpgn");
                break;
            case "drawing" or "pict" or "object":
                Drawing(c);
                break;
            case "lastRenderedPageBreak" or "separator" or "continuationSeparator" or "contentPart" or "fldData":
                break;
            default:
                break;
        }

        _ = rpr;
    }

    // ---- notes and comments -------------------------------------------------------------------------------------------------------

    private void Note(MarkupNode reference, bool endnote)
    {
        string id = A(reference, "id") ?? string.Empty;
        MarkupNode root = endnote ? _endnotesRoot : _footnotesRoot;
        if (root.IsNull)
        {
            return;
        }

        MarkupNode note = default;
        foreach (MarkupNode n in root.ChildElements)
        {
            if (IsW(n, endnote ? "endnote" : "footnote") && A(n, "id") == id)
            {
                note = n;
                break;
            }
        }

        if (note.IsNull)
        {
            return;
        }

        string saved = _part;
        _part = (endnote ? _endnotesPart : _footnotesPart) ?? saved;
        FieldState[] outer = SuspendFields();
        _w.StartGroup();
        _w.Word("super");
        _w.Word("chftn");
        _w.StartGroup();
        _w.Word("footnote");
        if (endnote)
        {
            _w.Word("ftnalt");
        }

        _w.Newline();
        Blocks(note, 0, false);
        CloseFields();
        _w.EndGroup();
        _w.EndGroup();
        ResumeFields(outer);

        _part = saved;
    }

    private void Comment(MarkupNode reference)
    {
        string id = A(reference, "id") ?? string.Empty;
        if (_commentsRoot.IsNull)
        {
            return;
        }

        MarkupNode comment = default;
        foreach (MarkupNode n in _commentsRoot.ChildElements)
        {
            if (IsW(n, "comment") && A(n, "id") == id)
            {
                comment = n;
                break;
            }
        }

        if (comment.IsNull)
        {
            return;
        }

        string saved = _part;
        _part = _commentsPart ?? saved;
        FieldState[] outer = SuspendFields();
        if (A(comment, "initials") is { Length: > 0 } initials)
        {
            _w.StartDestination("atnid");
            _w.Text(initials);
            _w.EndGroup();
        }

        _w.StartDestination("atnauthor");
        _w.Text(A(comment, "author") ?? "Unknown");
        _w.EndGroup();
        _w.Word("chatn");
        _w.StartDestination("annotation");
        _w.StartDestination("atnref");
        _w.Text(id);
        _w.EndGroup();
        if (RevDttm(A(comment, "date")) is int d)
        {
            _w.StartDestination("atndate");
            _w.Text(X.N(d));
            _w.EndGroup();
        }

        _w.Newline();
        Blocks(comment, 0, false);
        CloseFields();
        _w.EndGroup();
        ResumeFields(outer);

        _part = saved;
    }

    // RTF 1.9.1 \revdttm: minute, hour, day, month and year-1900 packed into one number (the day of week is bits 29-31).
    private static int? RevDttm(string? iso)
    {
        if (iso is null || !DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime d) || d.Year < 1900 || d.Year > 2300)
        {
            return null;
        }

        return d.Minute | (d.Hour << 6) | (d.Day << 11) | (d.Month << 16) | ((d.Year - 1900) << 20) | ((int)d.DayOfWeek << 29);
    }

    private void Text(string text, Rev ins, Rev del, MarkupNode rpr)
    {
        _ = ins;
        _ = del;
        _ = rpr;
        _w.Text(text.Replace('\n', ' ').Replace('\r', ' '));
    }
}
