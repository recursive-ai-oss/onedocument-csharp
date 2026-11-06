using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Rtf;

namespace RecursiveAI.OneDocument.Documents.Rtf;

internal sealed partial class RtfImporter
{
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    private sealed class Media
    {
        public string Name = string.Empty;
        public string ContentType = string.Empty;
        public byte[] Data = [];
    }

    private readonly List<Media> _media = [];
    private readonly Dictionary<string, Media> _mediaByHash = new(StringComparer.Ordinal);

    // ---- headers, footers, notes, comments ------------------------------------------------------------------------------------------

    private void ReadHeaderFooter(RtfNode g, St st, string dest)
    {
        bool footer = dest.StartsWith("footer", StringComparison.Ordinal);
        string type = dest[^1] switch { 'l' => "even", 'f' => "first", _ => "default" };
        var hs = new Story { Rels = new RelSet() };
        Story prev = _story;
        _story = hs;
        string xml;
        try
        {
            St hst = st.Clone();
            hst.P = new ParaFmt();
            Walk(g, hst, 0);
            xml = FinishStory(hs, hst);
        }
        finally
        {
            _story = prev;
        }

        string kind = footer ? "footer" : "header";
        string name = "/word/" + kind + X.N(footer ? ++_footerSeq : ++_headerSeq) + ".xml";
        _headerParts.Add((name, xml, hs.Rels, footer));
        string id = _bodyRels.Add(RelNs + kind, name[6..]);
        _sec.Refs.RemoveAll(r => r.Kind == kind && r.Type == type);
        _sec.Refs.Add((kind, type, id));
        if (type == "even")
        {
            _facingPages = true;
        }
    }

    private void ReadFootnote(RtfNode g, St st)
    {
        bool endnote = HasWord(g, "ftnalt");
        RelSet rels = endnote ? _enRels : _fnRels;
        int id = endnote ? ++_enSeq : ++_fnSeq;
        St refSt = st.Clone();
        refSt.Script = 2;
        AppendOwnRun(refSt, endnote ? "<w:endnoteReference w:id=\"" + X.N(id) + "\"/>" : "<w:footnoteReference w:id=\"" + X.N(id) + "\"/>");

        var fs = new Story { Rels = rels };
        Story prev = _story;
        bool pf = _inFootnote, pe = _inEndnote;
        _story = fs;
        _inFootnote = !endnote;
        _inEndnote = endnote;
        string xml;
        try
        {
            St fst = st.Clone();
            fst.P = new ParaFmt();
            fst.Script = null;
            Walk(g, fst, 0);
            xml = FinishStory(fs, fst);
        }
        finally
        {
            _story = prev;
            _inFootnote = pf;
            _inEndnote = pe;
        }

        string tag = endnote ? "w:endnote" : "w:footnote";
        (endnote ? _endnotes : _footnotes).Add((id, "<" + tag + " w:id=\"" + X.N(id) + "\">" + xml + "</" + tag + ">"));
    }

    private static bool HasWord(RtfNode g, string name)
    {
        foreach (RtfNode c in g.Children)
        {
            if (c.Kind == RtfNodeKind.ControlWord && c.Name == name)
            {
                return true;
            }
        }

        return false;
    }

    // RTF 1.9.1 \chftn: the footnote reference mark. In the body it is replaced by the footnote reference; in the note it is the mark
    // that starts the note's text.
    private void FootnoteMark(St st)
    {
        if (_inFootnote)
        {
            St c = st.Clone();
            c.Script = 2;
            AppendOwnRun(c, "<w:footnoteRef/>");
        }
        else if (_inEndnote)
        {
            St c = st.Clone();
            c.Script = 2;
            AppendOwnRun(c, "<w:endnoteRef/>");
        }
    }

    private void ReadAnnotation(RtfNode g, St st)
    {
        int? refNo = null;
        int dttm = 0;
        foreach (RtfNode c in g.Children)
        {
            if (c.Kind != RtfNodeKind.Group)
            {
                continue;
            }

            if (c.Destination == "atnref" && int.TryParse(PlainText(c, st).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r))
            {
                refNo = r;
            }
            else if (c.Destination == "atndate" && int.TryParse(PlainText(c, st).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int d))
            {
                dttm = d;
            }
        }

        bool ranged = refNo is int rn && _commentRanges.ContainsKey(rn);
        int id = ranged ? _commentRanges[refNo!.Value] : ++_cmSeq;
        var cs = new Story { Rels = _cmRels };
        Story prev = _story;
        string xml;
        try
        {
            _story = cs;
            St cst = st.Clone();
            cst.P = new ParaFmt();
            Walk(g, cst, 0);
            xml = FinishStory(cs, cst);
        }
        finally
        {
            _story = prev;
        }

        string author = _atnAuthor ?? "Unknown";
        string initials = _atnInitials ?? string.Empty;
        _atnAuthor = _atnInitials = null;
        _comments.Add((id, "<w:comment w:id=\"" + X.N(id) + "\" w:author=\"" + X.Esc(author) + "\"" + DateAttr(dttm) +
            (initials.Length > 0 ? " w:initials=\"" + X.Esc(initials) + "\"" : string.Empty) + ">" + xml + "</w:comment>"));
        if (!ranged)
        {
            AppendInline("<w:commentRangeStart w:id=\"" + X.N(id) + "\"/><w:commentRangeEnd w:id=\"" + X.N(id) + "\"/>");
        }

        AppendInline("<w:r><w:commentReference w:id=\"" + X.N(id) + "\"/></w:r>");
    }

    private void ReadRangeStart(RtfNode g, St st)
    {
        if (int.TryParse(PlainText(g, st).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
        {
            int id = ++_cmSeq;
            _commentRanges[n] = id;
            AppendInline("<w:commentRangeStart w:id=\"" + X.N(id) + "\"/>");
        }
    }

    private void ReadRangeEnd(RtfNode g, St st)
    {
        if (int.TryParse(PlainText(g, st).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && _commentRanges.TryGetValue(n, out int id))
        {
            AppendInline("<w:commentRangeEnd w:id=\"" + X.N(id) + "\"/>");
        }
    }

    // ---- bookmarks and fields -------------------------------------------------------------------------------------------------------

    private void ReadBookmarkStart(RtfNode g, St st)
    {
        string name = PlainText(g, st).Trim();
        if (name.Length == 0 || _bookmarkByName.ContainsKey(name))
        {
            return;
        }

        int id = _nextBookmark++;
        _bookmarkByName[name] = id;
        AppendInline("<w:bookmarkStart w:id=\"" + X.N(id) + "\" w:name=\"" + X.Esc(name.Length > 40 ? name[..40] : name) + "\"/>");
    }

    private void ReadBookmarkEnd(RtfNode g, St st)
    {
        string name = PlainText(g, st).Trim();
        if (_bookmarkByName.TryGetValue(name, out int id) && _bookmarkIds.TryAdd(id, id))
        {
            AppendInline("<w:bookmarkEnd w:id=\"" + X.N(id) + "\"/>");
        }
    }

    // ECMA-376 Part 1 §17.16.18: a complex field is begin, instruction, separate, result, end -- each in a run.
    private void ReadField(RtfNode g, St st)
    {
        RtfNode? inst = null, rslt = null;
        bool dirty = false, locked = false;
        foreach (RtfNode c in g.Children)
        {
            if (c.Kind == RtfNodeKind.Group)
            {
                if (c.Destination == "fldinst")
                {
                    inst = c;
                }
                else if (c.Destination == "fldrslt")
                {
                    rslt = c;
                }
            }
            else if (c.Kind == RtfNodeKind.ControlWord)
            {
                dirty |= c.Name == "flddirty";
                locked |= c.Name == "fldlock";
            }
        }

        string instr = inst is null ? string.Empty : PlainText(inst, st);
        string attrs = (dirty ? " w:dirty=\"1\"" : string.Empty) + (locked ? " w:fldLock=\"1\"" : string.Empty);
        AppendOwnRun(st, "<w:fldChar w:fldCharType=\"begin\"" + attrs + "/>");
        AppendOwnRun(st, "<w:instrText xml:space=\"preserve\">" + X.Esc(instr) + "</w:instrText>");
        if (rslt is not null)
        {
            AppendOwnRun(st, "<w:fldChar w:fldCharType=\"separate\"/>");
            St c = st.Clone();
            Walk(rslt, c, 0);
            FlushRun(_story);
        }

        AppendOwnRun(st, "<w:fldChar w:fldCharType=\"end\"/>");
    }

    private void AddField(St st, string instr, string result)
    {
        AppendOwnRun(st, "<w:fldChar w:fldCharType=\"begin\"/>");
        AppendOwnRun(st, "<w:instrText xml:space=\"preserve\">" + X.Esc(instr) + "</w:instrText>");
        AppendOwnRun(st, "<w:fldChar w:fldCharType=\"separate\"/>");
        if (result.Length > 0)
        {
            AppendOwnRun(st, "<w:t>" + X.Esc(result) + "</w:t>");
        }

        AppendOwnRun(st, "<w:fldChar w:fldCharType=\"end\"/>");
    }

    // ---- objects and shapes ---------------------------------------------------------------------------------------------------------

    private void ReadObject(RtfNode g, St st)
    {
        Loss.Report("loss.rtf-ole-object", "embedded objects", 1, "Embedded OLE objects are not carried; the picture Word stored for them is shown instead.");
        foreach (RtfNode c in g.Children)
        {
            if (c.Kind == RtfNodeKind.Group && c.Destination == "result")
            {
                WalkClone(c, st);
            }
        }
    }

    private void ReadShape(RtfNode g, St st)
    {
        Loss.Report("loss.rtf-shape", "drawing shapes", 1, "Shapes keep their picture or their text, as inline content; position, size, wrapping and geometry are not carried.");
        RtfNode? inst = null;
        foreach (RtfNode c in g.Children)
        {
            if (c.Kind == RtfNodeKind.Group && c.Destination == "shpinst")
            {
                inst = c;
            }
        }

        if (inst is null)
        {
            return;
        }

        RtfNode? pib = null, text = null;
        string? descr = null;
        foreach (RtfNode c in inst.Children)
        {
            if (c.Kind != RtfNodeKind.Group)
            {
                continue;
            }

            if (c.Destination == "sp")
            {
                RtfNode? sn = null, sv = null;
                foreach (RtfNode k in c.Children)
                {
                    if (k.Kind == RtfNodeKind.Group)
                    {
                        if (k.Destination == "sn")
                        {
                            sn = k;
                        }
                        else if (k.Destination == "sv")
                        {
                            sv = k;
                        }
                    }
                }

                if (sn is null || sv is null)
                {
                    continue;
                }

                string pname = PlainText(sn, st).Trim();
                if (pname == "pib")
                {
                    pib = sv;
                }
                else if (pname == "wzDescription")
                {
                    descr = PlainText(sv, st).Trim();
                }
            }
            else if (c.Destination == "shptxt")
            {
                text = c;
            }
        }

        if (pib is not null && FindPict(pib) is { } pict)
        {
            var info = new PictInfo { Descr = descr };
            CollectPict(pict, info);
            EmitPicture(st, info, pict.Offset);
        }
        else if (text is not null)
        {
            Loss.Report("loss.rtf-text-box", "text boxes", 1, "The text of a text box is read as ordinary paragraphs at the place of the shape.");
            WalkClone(text, st);
        }
    }

    private static RtfNode? FindPict(RtfNode g)
    {
        foreach (RtfNode c in g.Children)
        {
            if (c.Kind == RtfNodeKind.Group)
            {
                if (c.Destination == "pict")
                {
                    return c;
                }

                RtfNode? inner = FindPict(c);
                if (inner is not null)
                {
                    return inner;
                }
            }
        }

        return null;
    }

    // ---- pictures (RTF 1.9.1 "Pictures") ---------------------------------------------------------------------------------------------

    private sealed class PictInfo
    {
        public string? Format;
        public readonly List<byte> Data = [];
        public int W, H, WGoal, HGoal, ScaleX = 100, ScaleY = 100;
        public string? Descr;
        public int Nibble = -1;
        public bool TooBig;
    }

    private void ReadPicture(RtfNode g, St st)
    {
        var info = new PictInfo();
        CollectPict(g, info);
        EmitPicture(st, info, g.Offset);
    }

    private void CollectPict(RtfNode g, PictInfo pic)
    {
        foreach (RtfNode c in g.Children)
        {
            switch (c.Kind)
            {
                case RtfNodeKind.ControlWord:
                    switch (c.Name)
                    {
                        case "pngblip": pic.Format = "png"; break;
                        case "jpegblip": pic.Format = "jpeg"; break;
                        case "emfblip": pic.Format = "emf"; break;
                        case "wmetafile": pic.Format = "wmf"; break;
                        case "dibitmap": pic.Format = "dib"; break;
                        case "wbitmap": pic.Format = "wbitmap"; break;
                        case "macpict": pic.Format = "macpict"; break;
                        case "pmmetafile": pic.Format = "pmmetafile"; break;
                        case "picw": pic.W = c.Parameter; break;
                        case "pich": pic.H = c.Parameter; break;
                        case "picwgoal": pic.WGoal = c.Parameter; break;
                        case "pichgoal": pic.HGoal = c.Parameter; break;
                        case "picscalex": pic.ScaleX = c.Parameter; break;
                        case "picscaley": pic.ScaleY = c.Parameter; break;
                    }

                    break;
                case RtfNodeKind.Binary:
                    AddPictBytes(pic, c.Data.Span);
                    break;
                case RtfNodeKind.Text:
                    foreach (byte b in c.Data.Span)
                    {
                        int h = b switch
                        {
                            >= (byte)'0' and <= (byte)'9' => b - '0',
                            >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
                            >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
                            _ => -1,
                        };
                        if (h < 0)
                        {
                            continue;
                        }

                        if (pic.Nibble < 0)
                        {
                            pic.Nibble = h;
                        }
                        else
                        {
                            if (!pic.TooBig && pic.Data.Count < _ctx.Limits.MaxPartSize)
                            {
                                pic.Data.Add((byte)((pic.Nibble << 4) | h));
                            }
                            else
                            {
                                pic.TooBig = true;
                            }

                            pic.Nibble = -1;
                        }
                    }

                    break;
                case RtfNodeKind.Group:
                    if (c.Destination == "picprop")
                    {
                        pic.Descr ??= FindProp(c, "wzDescription");
                    }

                    break;
            }
        }
    }

    private string? FindProp(RtfNode g, string prop)
    {
        foreach (RtfNode c in g.Children)
        {
            if (c.Kind == RtfNodeKind.Group && c.Destination == "sp")
            {
                string? n = null, v = null;
                foreach (RtfNode k in c.Children)
                {
                    if (k.Kind == RtfNodeKind.Group && k.Destination == "sn")
                    {
                        n = PlainText(k, new St()).Trim();
                    }
                    else if (k.Kind == RtfNodeKind.Group && k.Destination == "sv")
                    {
                        v = PlainText(k, new St()).Trim();
                    }
                }

                if (n == prop)
                {
                    return v;
                }
            }
        }

        return null;
    }

    private void AddPictBytes(PictInfo pic, ReadOnlySpan<byte> data)
    {
        if (pic.TooBig || pic.Data.Count + data.Length > _ctx.Limits.MaxPartSize)
        {
            pic.TooBig = true;
            return;
        }

        pic.Data.AddRange(data.ToArray());
    }

    private void EmitPicture(St st, PictInfo pic, int offset)
    {
        if (pic.TooBig || (_pictureBytes += pic.Data.Count) > _ctx.Limits.MaxAllocationBytes)
        {
            Defect("rtf.picture-limit-exceeded", DefectKind.Limit, DefectSeverity.Dropped, "a picture is larger than the part size limit; it is left out", offset);
            Loss.Report("loss.rtf-picture", "pictures", 1, "A picture could not be carried (unsupported format, damaged data or too large).");
            return;
        }

        byte[] data = [.. pic.Data];
        string? ext = null, type = null;
        switch (pic.Format)
        {
            case "png" when data.Length > 8 && data[0] == 0x89 && data[1] == 'P':
                ext = "png";
                type = "image/png";
                break;
            case "jpeg" when data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8:
                ext = "jpeg";
                type = "image/jpeg";
                break;
            case "emf" when data.Length > 44 && data[40] == ' ' && data[41] == 'E' && data[42] == 'M' && data[43] == 'F':
                ext = "emf";
                type = "image/x-emf";
                break;
            case "wmf" when data.Length > 18:
                data = WithPlaceableHeader(data, pic);
                ext = "wmf";
                type = "image/x-wmf";
                break;
            case "dib" when data.Length > 40:
                data = DibToBmp(data);
                ext = "bmp";
                type = "image/bmp";
                break;
        }

        if (ext is null || type is null)
        {
            Defect("rtf.invalid-picture-data", DefectKind.Record, DefectSeverity.Dropped, "picture of kind '" + (pic.Format ?? "unknown") + "' is empty, damaged or not supported; it is left out", offset);
            Loss.Report("loss.rtf-picture", "pictures", 1, "A picture could not be carried (unsupported format, damaged data or too large).");
            return;
        }

        string hash = Convert.ToHexString(SHA256.HashData(data));
        if (!_mediaByHash.TryGetValue(hash, out Media? m))
        {
            m = new Media { Name = "/word/media/image" + X.N(++_picSeq) + "." + ext, ContentType = type, Data = data };
            _media.Add(m);
            _mediaByHash[hash] = m;
        }

        string rid = _story.Rels.Add(RelNs + "image", m.Name[6..]);

        // Size in EMU (914400 per inch = 635 per twip). \picwgoal is the desired size in twips; \picw is pixels for bitmaps and 0.01 mm for
        // metafiles (RTF 1.9.1 "Pictures").
        long cx, cy;
        if (pic.WGoal > 0 && pic.HGoal > 0)
        {
            cx = pic.WGoal * 635L * Math.Max(1, pic.ScaleX) / 100;
            cy = pic.HGoal * 635L * Math.Max(1, pic.ScaleY) / 100;
        }
        else if (pic.Format is "wmf" or "emf")
        {
            cx = pic.W * 360L * Math.Max(1, pic.ScaleX) / 100;
            cy = pic.H * 360L * Math.Max(1, pic.ScaleY) / 100;
        }
        else
        {
            cx = pic.W * 9525L * Math.Max(1, pic.ScaleX) / 100;
            cy = pic.H * 9525L * Math.Max(1, pic.ScaleY) / 100;
        }

        if (cx <= 0 || cy <= 0)
        {
            cx = cy = 914400;
        }

        cx = Math.Min(cx, 5_000_000_000L);
        cy = Math.Min(cy, 5_000_000_000L);
        int docPr = _nextDocPr++;
        string descr = pic.Descr is { Length: > 0 } d ? " descr=\"" + X.Esc(d) + "\"" : string.Empty;
        string xml = "<w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"" + cx.ToString(CultureInfo.InvariantCulture) + "\" cy=\"" + cy.ToString(CultureInfo.InvariantCulture) +
            "\"/><wp:docPr id=\"" + X.N(docPr) + "\" name=\"Picture " + X.N(docPr) + "\"" + descr + "/><wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>" +
            "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"" + X.N(docPr) + "\" name=\"Picture " + X.N(docPr) + "\"" + descr +
            "/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"" + rid + "\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"" +
            cx.ToString(CultureInfo.InvariantCulture) + "\" cy=\"" + cy.ToString(CultureInfo.InvariantCulture) + "\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing>";
        AppendOwnRun(st, xml);
    }

    // [MS-WMF] 2.2.2.6: a placeable header (key 0x9AC6CDD7, bounding box, units per inch, checksum) gives a bare metafile a size.
    private static byte[] WithPlaceableHeader(byte[] wmf, PictInfo pic)
    {
        if (wmf.Length >= 4 && wmf[0] == 0xD7 && wmf[1] == 0xCD && wmf[2] == 0xC6 && wmf[3] == 0x9A)
        {
            return wmf;
        }

        int w = pic.WGoal > 0 ? pic.WGoal : (int)(pic.W * 1440L / 2540);
        int h = pic.HGoal > 0 ? pic.HGoal : (int)(pic.H * 1440L / 2540);
        if (w <= 0 || h <= 0)
        {
            w = h = 1440;
        }

        var header = new byte[22];
        header[0] = 0xD7;
        header[1] = 0xCD;
        header[2] = 0xC6;
        header[3] = 0x9A;
        void Put16(int at, int v)
        {
            header[at] = (byte)v;
            header[at + 1] = (byte)(v >> 8);
        }

        Put16(6, 0);
        Put16(8, 0);
        Put16(10, Math.Min(w, 32767));
        Put16(12, Math.Min(h, 32767));
        Put16(14, 1440);
        int sum = 0;
        for (int i = 0; i < 20; i += 2)
        {
            sum ^= header[i] | (header[i + 1] << 8);
        }

        Put16(20, sum);
        return [.. header, .. wmf];
    }

    // A device-independent bitmap in RTF is the BITMAPINFO and the bits; a .bmp file puts a 14-byte file header in front of it.
    private static byte[] DibToBmp(byte[] dib)
    {
        int headerSize = BitConverter.ToInt32(dib, 0);
        int bits = BitConverter.ToUInt16(dib, 14);
        int compression = BitConverter.ToInt32(dib, 16);
        int used = BitConverter.ToInt32(dib, 32);
        int palette = used > 0 ? used : (bits is > 0 and <= 8 ? 1 << bits : 0);
        int offset = 14 + headerSize + (palette * 4) + (compression == 3 && headerSize == 40 ? 12 : 0);
        var file = new byte[14 + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.TryWriteBytes(file.AsSpan(2), file.Length);
        BitConverter.TryWriteBytes(file.AsSpan(10), offset);
        dib.CopyTo(file, 14);
        return file;
    }
}
