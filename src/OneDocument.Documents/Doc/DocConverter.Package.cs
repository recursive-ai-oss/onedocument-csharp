using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Doc.Binary;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Doc;

internal sealed partial class DocConverter
{
    private const string NsDecl =
        "xmlns:w=\"" + W + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"";

    private const string Header = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";

    // Properties the formats above write; anything else on a run, paragraph or section is counted as not carried over.
    private static readonly HashSet<ushort> CharHandled =
    [
        0x0800, 0x0801, 0x0802, 0x6A03, 0x4804, 0x6805, 0x0806, 0x6A09, 0x080A, 0x2A0C, 0x0811, 0x0818, 0x4A30, 0x2A33, 0x2A34, 0x0835, 0x0836, 0x0837, 0x0838,
        0x0839, 0x083A, 0x083B, 0x083C, 0x2A3E, 0x8840, 0x2A42, 0x4A43, 0x4845, 0x2A48, 0x484B, 0x4A4F, 0x4A50, 0x4A51, 0x4852, 0x2A53, 0x0854, 0x0855, 0x0858,
        0x2859, 0x085A, 0x085C, 0x085D, 0x4A5E, 0x485F, 0x4A60, 0x4A61, 0x4863, 0x6864, 0x6865, 0x4866, 0x6870, 0x4873, 0x4874, 0x0875, 0x0882, 0xCA71, 0xCA72,
        0x6815, 0x6816, 0x6817, 0x4807, 0x4867, 0x286F, 0x486D, 0x486E, 0x2A83, 0x2A86, 0x2A90, 0x0868, 0x0856, 0x484E, 0x2879,
    ];

    private static readonly HashSet<ushort> ParaHandled =
    [
        0x4600, 0x2405, 0x2406, 0x2407, 0x2431, 0x260A, 0x460B, 0x240C, 0x6424, 0x6425, 0x6426, 0x6427, 0x6428, 0x6629, 0xC64E, 0xC64F, 0xC650, 0xC651, 0xC652,
        0xC653, 0xC64D, 0x442D, 0xC60D, 0xC615, 0x242A, 0x2433, 0x2434, 0x2435, 0x2436, 0x2437, 0x2438, 0x2441, 0x2448, 0xA413, 0xA414, 0x245B, 0x245C, 0x4458,
        0x4459, 0x6412, 0x845E, 0x840F, 0x845D, 0x840E, 0x8460, 0x8411, 0x246D, 0x2470, 0x2461, 0x2403, 0x2640, 0x8418, 0x8419, 0x841A, 0x442B, 0x261B, 0x2423,
        0x442C, 0x842F, 0x842E, 0x4439,

        // Table structure, read from the row-end mark's own modifier list.
        0x2416, 0x2417, 0x6649, 0x664A, 0x244B, 0x244C, 0x6646, 0xD608, 0x9601, 0x9602, 0x5400, 0x548A, 0x3403, 0x3466, 0x3404, 0x9407, 0x560B, 0x5664, 0x3615,
        0xD605, 0xD613, 0xD634, 0x7621, 0x5622, 0x7623, 0x5624, 0x5625, 0xD62F, 0xD62D, 0xD612, 0xD616, 0xD60C, 0xD609, 0xD62C, 0xD62B, 0x6467, 0x6465,
        0x2602, 0x2443, 0x2447, 0x245A, 0x2430, 0x2664, 0x2462, 0x4455, 0x4456, 0x4457,
    ];

    private static readonly HashSet<ushort> SectionHandled =
    [
        0x3009, 0x300A, 0x500B, 0x900C, 0xF203, 0xF204, 0x3005, 0x3019, 0x300E, 0x3011, 0x501C, 0x301D, 0xB01F, 0xB020, 0xB021, 0xB022, 0x9023, 0x9024, 0xB025,
        0xB017, 0xB018, 0x301A, 0x3013, 0x5015, 0x9016, 0x501B, 0x3228, 0x322A, 0x702B, 0x702C, 0x702D, 0x702E, 0x522F, 0x7030, 0x9031, 0x5032, 0x3012,
        0x3000, 0x3001, 0x3006, 0x5007, 0x5008, 0x5026, 0x3012, 0x703A, 0x5040, 0x503F, 0x303C, 0x5042, 0x5041, 0x303E,
    ];

    /// <summary>Converts the file into the parts of a WordprocessingML package and writes them to <paramref name="pkg"/>.</summary>
    internal void Build(OpcPackage pkg)
    {
        var main = new Part("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", string.Empty);
        var extra = new List<Part>();
        ReadSideTables();

        // Sections come first: each needs the header and footer parts it refers to.
        IReadOnlyList<int> sectionEnds = _r.SectionEnds;
        var sectPrs = new Dictionary<int, string>();
        var headerParts = new List<Part>();
        Plc hdd = _r.ReadPlc(DocFib.PlcfHdd, 0);
        (int hs, int hl) = _f.StoryRange(DocStory.Headers);
        bool facing = _r.IsFacingPages;
        int headerNo = 0;
        int footerNo = 0;
        for (int s = 0; s < sectionEnds.Count; s++)
        {
            var refs = new StringBuilder();
            List<Prl> sectionProps = SectionProps(s);
            bool titlePage = sectionProps.Exists(x => x.Sprm == 0x300A && x.OperandByte != 0);
            string[] types = ["even", "default", "even", "default", "first", "first"];
            for (int k = 0; k < 6; k++)
            {
                int j = 6 + (6 * s) + k;
                if (j + 1 >= hdd.Cps.Length)
                {
                    break;
                }

                int a = hdd.Cps[j];
                int b = hdd.Cps[j + 1];
                if (a < 0 || b <= a || b > hl)
                {
                    continue;
                }

                if (b - a == 1 && s == 0)
                {
                    continue;
                }

                // Even-page headers show only with odd/even pages, first-page ones only with a title page ([MS-DOC] Dop.fFacingPages,
                // sprmSFTitlePage); the other copies a writer may store are not carried over.
                if ((k is 0 or 2 && !facing) || (k is 4 or 5 && !titlePage))
                {
                    continue;
                }

                bool footer = k is 2 or 3 or 5;
                string name = footer ? $"/word/footer{++footerNo}.xml" : $"/word/header{++headerNo}.xml";
                string root = footer ? "w:ftr" : "w:hdr";
                var part = new Part(name, footer ? "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml" : "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml", string.Empty);
                string body = WriteStory(hs + a, b - a, StoryKind.Header, part, null, null);
                part.Xml = Header + "<" + root + " " + NsDecl + ">" + body + "</" + root + ">";
                headerParts.Add(part);
                string rid = main.AddRel(Rel + (footer ? "footer" : "header"), name[6..]);
                refs.Append(footer ? "<w:footerReference" : "<w:headerReference").Append(" w:type=\"").Append(types[k]).Append("\" r:id=\"").Append(rid).Append("\"/>");
            }

            SectionFormat sf = SectionFormat.From(sectionProps);
            var sx = new StringBuilder();
            sf.Write(sx, refs.ToString());
            sectPrs[sectionEnds[s]] = sx.ToString();
        }

        // The main story.
        (int ms, int ml) = _f.StoryRange(DocStory.Main);
        var ends = new HashSet<int>(sectionEnds);
        int lastEnd = sectionEnds.Count > 0 ? sectionEnds[^1] : -1;
        ends.Remove(lastEnd);
        string body0 = WriteStory(ms, ml, StoryKind.Main, main, ends, end => sectPrs.TryGetValue(end, out string? x) ? x : string.Empty);

        // Text boxes are not placed in the page; their paragraphs follow the body.
        (int ts, int tl) = _f.StoryRange(DocStory.TextBoxes);
        var tail = new StringBuilder();
        if (tl > 1)
        {
            int count = Math.Max(1, _textBoxes);
            _loss.Report("loss.doc-text-boxes", "text boxes", count, "Text box text was appended after the body as ordinary paragraphs; the boxes' positions and frames are not kept.");
            tail.Append(WriteStory(ts, tl, StoryKind.TextBox, main, null, null));
        }

        (int hts, int htl) = _f.StoryRange(DocStory.HeaderTextBoxes);
        if (htl > 1)
        {
            _loss.Report("loss.doc-header-text-boxes", "header text boxes", 1, "Text boxes anchored in headers and footers were dropped.");
        }

        string finalSect = sectPrs.TryGetValue(lastEnd, out string? fs) ? fs : SectionDefault();
        main.Xml = Header + "<w:document " + NsDecl + "><w:body>" + body0 + tail + finalSect + "</w:body></w:document>";

        // Notes and comments.
        Part? footnotes = BuildNotes(StoryKind.Footnote, DocStory.Footnotes, DocFib.PlcffndTxt, _fndRefs.Count);
        Part? endnotes = BuildNotes(StoryKind.Endnote, DocStory.Endnotes, DocFib.PlcfendTxt, _ednRefs.Count);
        Part? comments = BuildComments();
        Part styles = BuildStyles();
        Part? numbering = BuildNumbering();
        Part settings = BuildSettings(footnotes is not null, endnotes is not null);
        Part fontTable = BuildFontTable();

        main.AddRel(Rel + "styles", "styles.xml");
        main.AddRel(Rel + "settings", "settings.xml");
        main.AddRel(Rel + "fontTable", "fontTable.xml");
        if (numbering is not null)
        {
            main.AddRel(Rel + "numbering", "numbering.xml");
        }

        if (footnotes is not null)
        {
            main.AddRel(Rel + "footnotes", "footnotes.xml");
        }

        if (endnotes is not null)
        {
            main.AddRel(Rel + "endnotes", "endnotes.xml");
        }

        if (comments is not null)
        {
            main.AddRel(Rel + "comments", "comments.xml");
        }

        extra.AddRange(headerParts);
        extra.AddRange([styles, settings, fontTable]);
        if (numbering is not null)
        {
            extra.Add(numbering);
        }

        foreach (Part? p in (Part?[])[footnotes, endnotes, comments])
        {
            if (p is not null)
            {
                extra.Add(p);
            }
        }

        var package = new List<Relationship>
        {
            new("rId1", Rel + "officeDocument", "word/document.xml"),
        };

        pkg.SetRelationships(null, package);
        pkg.AddPart(main.Name, main.ContentType, Encoding.UTF8.GetBytes(main.Xml));
        foreach (Part p in extra)
        {
            pkg.AddPart(p.Name, p.ContentType, Encoding.UTF8.GetBytes(p.Xml));
        }

        for (int i = 0; i < _media.Count; i++)
        {
            pkg.AddPart(_media[i].Name, _media[i].ContentType, _media[i].Bytes);
        }

        pkg.SetRelationships(main.Name, main.Rels);
        foreach (Part p in extra)
        {
            if (p.Rels.Count > 0)
            {
                pkg.SetRelationships(p.Name, p.Rels);
            }
        }

        ApplyProperties(pkg);
        ReportLosses();
    }

    private void ReportLosses()
    {
        if (_f.Fib.IsLegacyWord6)
        {
            _loss.Report("loss.doc-word6-formatting", "Word 6/95 formatting", 1, "Character, paragraph, section and table formatting, styles, lists and pictures of Word 6.0 and Word 95 files are not read; the text, its paragraphs, tables, sections and notes are.");
        }

        if (_oleObjects > 0)
        {
            _loss.Report("loss.doc-ole-objects", "embedded and linked objects", _oleObjects, "Objects are kept as their preview picture; the embedded storage is not carried over.");
        }

        if (_formFields > 0)
        {
            _loss.Report("loss.doc-form-fields", "form fields", _formFields, "Form fields were kept as plain fields; their data (checkbox state, drop-down lists) was not carried over.");
        }

        if (_shapes > 0)
        {
            _loss.Report("loss.doc-drawing-shapes", "drawing shapes", _shapes, "Drawing shapes that are not pictures were dropped.");
        }

        if (_unmappedSprms > 0)
        {
            _loss.Report("loss.doc-sprms", "property modifiers", _unmappedSprms, "Property modifiers the document model has no counterpart for were dropped; each kind is a doc.sprm-not-mapped defect with its location.");
        }

        bool macros = false;
        foreach (string n in _f.StreamNames)
        {
            macros |= n == "/Macros" || n.StartsWith("/Macros/", StringComparison.Ordinal);
        }

        if (macros)
        {
            _loss.Report("loss.doc-vba", "macros", 1, "The VBA project was not carried over.");
        }
    }

    private List<Prl> SectionProps(int s)
    {
        List<Prl> list = _r.SectionProperties(s);
        CountUnmapped(list, SectionHandled, -1);
        return list;
    }

    private static string SectionDefault()
    {
        var sb = new StringBuilder();
        SectionFormat.From([]).Write(sb, string.Empty);
        return sb.ToString();
    }

    private void ReadSideTables()
    {
        Plc fnd = _r.ReadPlc(DocFib.PlcffndRef, 2);
        for (int i = 0; i < fnd.Count; i++)
        {
            _fndRefs[fnd.Cps[i]] = i;
        }

        Plc edn = _r.ReadPlc(DocFib.PlcfendRef, 2);
        for (int i = 0; i < edn.Count; i++)
        {
            _ednRefs[edn.Cps[i]] = i;
        }

        Plc atn = _r.ReadPlc(DocFib.PlcfandRef, 30);
        for (int i = 0; i < atn.Count; i++)
        {
            _atnRefs[atn.Cps[i]] = i;
        }

        // Bookmarks: names, start positions (Plcfbkf) and end positions (Plcfbkl) ([MS-DOC] 2.8.10 and 2.8.12).
        List<string> names = ReadSttb(DocFib.SttbfBkmk);
        Plc bkf = _r.ReadPlc(DocFib.PlcfBkf, 4);
        Plc bkl = _r.ReadPlc(DocFib.PlcfBkl, 0);
        int total = _f.Text.Length;
        for (int i = 0; i < bkf.Count && i < names.Count; i++)
        {
            string name = names[i];
            if (name.Length == 0)
            {
                continue;
            }

            int ibkl = BinaryPrimitives.ReadUInt16LittleEndian(bkf.Record(i));
            int start = bkf.Cps[i];
            if (ibkl >= bkl.Cps.Length || start < 0 || start > total)
            {
                continue;
            }

            int end = Math.Clamp(bkl.Cps[ibkl], start, total);
            int id = _bookmarkId++;
            Add(start, "<w:bookmarkStart w:id=\"" + WmlWrite.I(id) + "\" w:name=\"" + Xml(name.Length > 40 ? name[..40] : name) + "\"/>");
            Add(end, "<w:bookmarkEnd w:id=\"" + WmlWrite.I(id) + "\"/>");
        }

        // Floating objects: where each is anchored (Plcspa) and which picture it shows.
        (uint dfc, uint dlcb) = _f.Fib.Pair(DocFib.DggInfo);
        _art = DocArt.ReadDrawing(_f.Table, dfc, dlcb);
        Plc spa = _r.ReadPlc(DocFib.PlcSpaMom, 26);
        for (int i = 0; i < spa.Count; i++)
        {
            ReadOnlySpan<byte> r = spa.Record(i);
            _spa[spa.Cps[i]] = new SpaInfo(
                BinaryPrimitives.ReadInt32LittleEndian(r),
                BinaryPrimitives.ReadInt32LittleEndian(r[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(r[8..]),
                BinaryPrimitives.ReadInt32LittleEndian(r[12..]),
                BinaryPrimitives.ReadInt32LittleEndian(r[16..]),
                BinaryPrimitives.ReadUInt16LittleEndian(r[20..]));
        }

        Plc tbx = _r.ReadPlc(DocFib.PlcftxbxTxt, 22);
        _textBoxes = Math.Max(0, tbx.Cps.Length - 2);

        void Add(int cp, string xml)
        {
            if (!_events.TryGetValue(cp, out List<string>? l))
            {
                _events[cp] = l = [];
            }

            l.Add(xml);
        }
    }

    private Part? BuildNotes(StoryKind kind, DocStory story, int txtPair, int refCount)
    {
        if (refCount == 0)
        {
            return null;
        }

        Plc txt = _r.ReadPlc(txtPair, 0);
        (int s, int l) = _f.StoryRange(story);
        bool foot = kind == StoryKind.Footnote;
        string root = foot ? "w:footnotes" : "w:endnotes";
        string item = foot ? "w:footnote" : "w:endnote";
        var part = new Part(foot ? "/word/footnotes.xml" : "/word/endnotes.xml", foot ? "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml" : "application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml", string.Empty);
        var sb = new StringBuilder(Header);
        sb.Append('<').Append(root).Append(' ').Append(NsDecl).Append('>');
        sb.Append('<').Append(item).Append(" w:type=\"separator\" w:id=\"-1\"><w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r><w:separator/></w:r></w:p></").Append(item).Append('>');
        sb.Append('<').Append(item).Append(" w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r><w:continuationSeparator/></w:r></w:p></").Append(item).Append('>');
        for (int i = 0; i < refCount; i++)
        {
            int a = i < txt.Cps.Length ? txt.Cps[i] : -1;
            int b = i + 1 < txt.Cps.Length ? txt.Cps[i + 1] : -1;
            sb.Append('<').Append(item).Append(" w:id=\"").Append(WmlWrite.I(i + 1)).Append("\">");
            if (a < 0 || b <= a || b > l)
            {
                sb.Append("<w:p/>");
            }
            else
            {
                sb.Append(WriteStory(s + a, b - a, kind, part, null, null));
            }

            sb.Append("</").Append(item).Append('>');
        }

        sb.Append("</").Append(root).Append('>');
        part.Xml = sb.ToString();
        return part;
    }

    private Part? BuildComments()
    {
        if (_atnRefs.Count == 0)
        {
            return null;
        }

        Plc atn = _r.ReadPlc(DocFib.PlcfandRef, 30);
        Plc txt = _r.ReadPlc(DocFib.PlcfandTxt, 0);
        (int s, int l) = _f.StoryRange(DocStory.Comments);
        var part = new Part("/word/comments.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml", string.Empty);
        var sb = new StringBuilder(Header);
        sb.Append("<w:comments ").Append(NsDecl).Append('>');
        for (int i = 0; i < atn.Count; i++)
        {
            ReadOnlySpan<byte> rec = atn.Record(i);
            int cch = Math.Min((int)BinaryPrimitives.ReadUInt16LittleEndian(rec), 9);
            string initials = Encoding.Unicode.GetString(rec.Slice(2, cch * 2));
            int ibst = BinaryPrimitives.ReadUInt16LittleEndian(rec[20..]);
            sb.Append("<w:comment w:id=\"").Append(WmlWrite.I(i)).Append("\" w:author=\"").Esc(AuthorOf(_commentAuthors, ibst)).Append('"');
            if (initials.Length > 0)
            {
                sb.Append(" w:initials=\"").Esc(initials).Append('"');
            }

            sb.Append('>');
            int a = i < txt.Cps.Length ? txt.Cps[i] : -1;
            int b = i + 1 < txt.Cps.Length ? txt.Cps[i + 1] : -1;
            if (a < 0 || b <= a || b > l)
            {
                sb.Append("<w:p/>");
            }
            else
            {
                sb.Append(WriteStory(s + a, b - a, StoryKind.Comment, part, null, null));
            }

            sb.Append("</w:comment>");
        }

        sb.Append("</w:comments>");
        part.Xml = sb.ToString();
        return part;
    }

    private Part BuildStyles()
    {
        var sb = new StringBuilder(Header);
        sb.Append("<w:styles ").Append(NsDecl).Append("><w:docDefaults><w:rPrDefault><w:rPr>");
        var fonts = _styles.DefaultFonts;
        string? a = _fonts.Name(fonts[0]);
        string? fe = _fonts.Name(fonts[1]);
        string? ot = _fonts.Name(fonts[2]);
        string? bi = _fonts.Name(fonts[3]);
        if (a is not null || fe is not null || ot is not null || bi is not null)
        {
            sb.Append("<w:rFonts");
            if (a is not null)
            {
                sb.Append(" w:ascii=\"").Esc(a).Append('"');
            }

            if (ot is not null || a is not null)
            {
                sb.Append(" w:hAnsi=\"").Esc(ot ?? a!).Append('"');
            }

            if (fe is not null)
            {
                sb.Append(" w:eastAsia=\"").Esc(fe).Append('"');
            }

            if (bi is not null)
            {
                sb.Append(" w:cs=\"").Esc(bi).Append('"');
            }

            sb.Append("/>");
        }

        // [MS-DOC] sprmCHps: the default character size is 10 points.
        sb.Append("<w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/></w:rPr></w:rPrDefault><w:pPrDefault/></w:docDefaults>");
        for (int i = 0; i < _styles.Styles.Count; i++)
        {
            DocStyle? s = _styles.Styles[i];
            if (s is null || !_styleIds.TryGetValue(i, out string? id))
            {
                continue;
            }

            string type = s.Stk switch { 2 => "character", 3 => "table", 4 => "numbering", _ => "paragraph" };
            string name = StyleName(s);
            sb.Append("<w:style w:type=\"").Append(type).Append("\" w:styleId=\"").Esc(id).Append('"');
            if ((s.Stk == 1 && s.Sti == 0) || (s.Stk == 2 && s.Sti == 65) || (s.Stk == 3 && s.Sti == 105) || (s.Stk == 4 && s.Sti == 107))
            {
                sb.Append(" w:default=\"1\"");
            }

            sb.Append("><w:name w:val=\"").Esc(name).Append("\"/>");
            int comma = s.Name.IndexOf(',', StringComparison.Ordinal);
            if (comma >= 0 && comma + 1 < s.Name.Length)
            {
                sb.Append("<w:aliases w:val=\"").Esc(s.Name[(comma + 1)..]).Append("\"/>");
            }

            if (s.IstdBase != 0x0FFF && s.IstdBase != i && _styleIds.TryGetValue(s.IstdBase, out string? bid))
            {
                sb.Append("<w:basedOn w:val=\"").Esc(bid).Append("\"/>");
            }

            if (s.Stk != 4 && s.IstdNext != i && s.IstdNext != 0x0FFF && _styleIds.TryGetValue(s.IstdNext, out string? nid))
            {
                sb.Append("<w:next w:val=\"").Esc(nid).Append("\"/>");
            }

            if (s.Sti < 0x0FFE)
            {
                sb.Append("<w:qFormat/>");
            }

            _fmt.ParagraphStyle = s.IstdBase;
            var ppr = new StringBuilder();
            ParaFormat.From(s.ParagraphProps).WriteInner(ppr, _fmt, ilfo => NumId(ilfo));
            CountUnmapped(s.ParagraphProps, ParaHandled, -1);
            if (ppr.Length > 0 && s.Stk != 2)
            {
                sb.Append("<w:pPr>").Append(ppr).Append("</w:pPr>");
            }

            var rpr = new StringBuilder();
            RunFormat.From(s.CharacterProps).WriteInner(rpr, _fmt, withStyle: false);
            CountUnmapped(s.CharacterProps, CharHandled, -1);
            if (rpr.Length > 0)
            {
                sb.Append("<w:rPr>").Append(rpr).Append("</w:rPr>");
            }

            sb.Append("</w:style>");
        }

        sb.Append("</w:styles>");
        return new Part("/word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml", sb.ToString());
    }

    private Part? BuildNumbering()
    {
        if (_lists.Lists.Count == 0 || _lists.Overrides.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder(Header);
        sb.Append("<w:numbering ").Append(NsDecl).Append('>');
        for (int i = 0; i < _lists.Lists.Count; i++)
        {
            DocList l = _lists.Lists[i];
            sb.Append("<w:abstractNum w:abstractNumId=\"").Append(WmlWrite.I(i)).Append("\"><w:nsid w:val=\"").Append(((uint)l.Lsid).ToString("X8", CultureInfo.InvariantCulture)).Append("\"/><w:multiLevelType w:val=\"")
                .Append(l.IsSimple ? "singleLevel" : "multilevel").Append("\"/>");
            for (int k = 0; k < l.Levels.Count; k++)
            {
                string? linked = k < l.LinkedStyles.Length && l.LinkedStyles[k] != 0x0FFF && _styleIds.TryGetValue(l.LinkedStyles[k], out string? sid) ? sid : null;
                WriteLevel(sb, l.Levels[k], k, linked);
            }

            sb.Append("</w:abstractNum>");
        }

        for (int i = 0; i < _lists.Overrides.Count; i++)
        {
            DocListOverride o = _lists.Overrides[i];
            int abs = _lists.Lists.FindIndex(x => x.Lsid == o.Lsid);
            if (abs < 0)
            {
                continue;
            }

            sb.Append("<w:num w:numId=\"").Append(WmlWrite.I(i + 1)).Append("\"><w:abstractNumId w:val=\"").Append(WmlWrite.I(abs)).Append("\"/>");
            foreach ((int level, int? startAt, DocListLevel? format) in o.Levels)
            {
                sb.Append("<w:lvlOverride w:ilvl=\"").Append(WmlWrite.I(Math.Min(8, level))).Append("\">");
                if (startAt is { } st)
                {
                    sb.Append("<w:startOverride w:val=\"").Append(WmlWrite.I(st)).Append("\"/>");
                }

                if (format is not null)
                {
                    WriteLevel(sb, format, level, null);
                }

                sb.Append("</w:lvlOverride>");
            }

            sb.Append("</w:num>");
        }

        sb.Append("</w:numbering>");
        return new Part("/word/numbering.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml", sb.ToString());
    }

    // [MS-OSHARED] MSONFC: the numbering format codes, mapped to ST_NumberFormat by the generated table; an unknown code is decimal.
    internal static string NumFormat(int nfc)
    {
        foreach ((byte code, string format) in Binary.SprmTable.NumberFormats)
        {
            if (code == nfc)
            {
                return format;
            }
        }

        return "decimal";
    }

    private void WriteLevel(StringBuilder sb, DocListLevel lv, int ilvl, string? linkedStyle)
    {
        sb.Append("<w:lvl w:ilvl=\"").Append(WmlWrite.I(Math.Min(8, ilvl))).Append("\"><w:start w:val=\"").Append(WmlWrite.I(Math.Max(0, lv.StartAt))).Append("\"/><w:numFmt w:val=\"").Append(NumFormat(lv.Nfc)).Append("\"/>");
        if (lv.NoRestart)
        {
            sb.Append("<w:lvlRestart w:val=\"0\"/>");
        }

        if (linkedStyle is not null)
        {
            sb.Append("<w:pStyle w:val=\"").Esc(linkedStyle).Append("\"/>");
        }

        if (lv.Legal)
        {
            sb.Append("<w:isLgl/>");
        }

        if (lv.Follow == 1)
        {
            sb.Append("<w:suff w:val=\"space\"/>");
        }
        else if (lv.Follow == 2)
        {
            sb.Append("<w:suff w:val=\"nothing\"/>");
        }

        var text = new StringBuilder();
        for (int j = 0; j < lv.Text.Length; j++)
        {
            char c = lv.Text[j];
            if (Array.IndexOf(lv.Placeholders, (byte)(j + 1)) >= 0 && c < 9)
            {
                text.Append('%').Append(WmlWrite.I(c + 1));
            }
            else
            {
                text.Append(c);
            }
        }

        sb.Append("<w:lvlText w:val=\"").Esc(text.ToString()).Append("\"/><w:lvlJc w:val=\"").Append(lv.Justify switch { 1 => "center", 2 => "right", _ => "left" }).Append("\"/>");
        _fmt.ParagraphStyle = 0;
        var ppr = new StringBuilder();
        ParaFormat.From(lv.ParagraphProps).WriteInner(ppr, _fmt, _ => null);
        if (ppr.Length > 0)
        {
            sb.Append("<w:pPr>").Append(ppr).Append("</w:pPr>");
        }

        var rpr = new StringBuilder();
        RunFormat.From(lv.CharacterProps).WriteInner(rpr, _fmt, withStyle: false);
        if (rpr.Length > 0)
        {
            sb.Append("<w:rPr>").Append(rpr).Append("</w:rPr>");
        }

        sb.Append("</w:lvl>");
    }

    private Part BuildSettings(bool footnotes, bool endnotes)
    {
        var sb = new StringBuilder(Header);
        sb.Append("<w:settings ").Append(NsDecl).Append('>');
        (uint fc, uint lcb) = _f.Fib.Pair(DocFib.Dop);
        byte[] t = _f.Table;
        int tab = 720;
        bool facing = false;
        bool track = false;
        if (lcb >= 12 && (long)fc + 12 <= t.Length)
        {
            // [MS-DOC] DopBase: fFacingPages is bit 0 of the first word, fRevMarking bit 15 of the third, dxaTab the sixth.
            facing = (t[(int)fc] & 1) != 0;
            track = (BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan((int)fc + 4)) & 0x8000) != 0;
            int v = BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan((int)fc + 10));
            if (v > 0)
            {
                tab = v;
            }
        }

        if (track)
        {
            sb.Append("<w:trackRevisions/>");
        }

        sb.Append("<w:defaultTabStop w:val=\"").Append(WmlWrite.I(tab)).Append("\"/>");
        if (facing)
        {
            sb.Append("<w:evenAndOddHeaders/>");
        }

        if (footnotes)
        {
            sb.Append("<w:footnotePr><w:footnote w:id=\"-1\"/><w:footnote w:id=\"0\"/></w:footnotePr>");
        }

        if (endnotes)
        {
            sb.Append("<w:endnotePr><w:endnote w:id=\"-1\"/><w:endnote w:id=\"0\"/></w:endnotePr>");
        }

        // A Word binary file lays out as Word 2003 does.
        sb.Append("<w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"11\"/></w:compat></w:settings>");
        return new Part("/word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml", sb.ToString());
    }

    private Part BuildFontTable()
    {
        var sb = new StringBuilder(Header);
        sb.Append("<w:fonts ").Append(NsDecl).Append('>');
        for (int i = 0; i < _fonts.Count; i++)
        {
            if (_fonts.Name(i) is { Length: > 0 } n)
            {
                sb.Append("<w:font w:name=\"").Esc(n).Append("\"/>");
            }
        }

        sb.Append("</w:fonts>");
        return new Part("/word/fontTable.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml", sb.ToString());
    }

    private static string Seq(int n) => n.ToString(CultureInfo.InvariantCulture);
}
