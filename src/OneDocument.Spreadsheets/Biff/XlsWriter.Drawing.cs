using System.Linq;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

internal sealed partial class XlsWriter
{
    private readonly Dictionary<int, List<NoteOut>> _noteCache = [];

    // Cell notes: BIFF8 stores each as a drawing shape ([MS-ODRAW]), an object, a text box and a NOTE record ([MS-XLS] §2.4.178 Note,
    // §2.4.181 Obj, §2.4.329 TxO).
    private List<NoteOut> Notes(int si)
    {
        if (_noteCache.TryGetValue(si, out List<NoteOut>? known))
        {
            return known;
        }

        var list = new List<NoteOut>();
        _noteCache[si] = list;
        Worksheet ws = _sheets[si];
        if (ws.PartName is null)
        {
            return list;
        }

        foreach (Relationship rel in _wb.Package.GetRelationships(ws.PartName))
        {
            if (!rel.Type.EndsWith("/comments", StringComparison.Ordinal) || !OpcPackage.TryResolveTarget(ws.PartName, rel, out PartUri target) || _wb.Package.FindPart(target.ToString()) is not { } part)
            {
                continue;
            }

            try
            {
                MarkupDocument doc = MarkupDocument.Parse(part.GetBytes(), _wb.OpenContext, part.Name);
                var authors = new List<string>();
                foreach (MarkupNode n in doc.Root.ChildElements)
                {
                    if (n.LocalNameString == "authors")
                    {
                        foreach (MarkupNode a in n.ChildElements)
                        {
                            authors.Add(a.InnerText);
                        }
                    }
                    else if (n.LocalNameString == "commentList")
                    {
                        foreach (MarkupNode c in n.ChildElements)
                        {
                            if (c.GetAttribute("ref") is not { } r || !CellAddress.TryParse(r, out CellAddress at) || at.Row > MaxRows || at.Column > MaxColumns)
                            {
                                _loss.Report("loss.comments", "comments", 1, "A comment outside the BIFF8 grid was not written.");
                                continue;
                            }

                            int ai = AttrInt(c, "authorId", 0);
                            string text = CommentText(Child(c, "text"));
                            list.Add(new NoteOut(at.Row - 1, at.Column - 1, ai >= 0 && ai < authors.Count ? authors[ai] : string.Empty, text.Length > 32767 ? text[..32767] : text));
                        }
                    }
                }
            }
            catch (OfficeException)
            {
                _loss.Report("loss.comments", "comments", 1, "A comments part that could not be read was not written.");
            }
        }

        return list;
    }

    private static string CommentText(MarkupNode text)
    {
        if (text.IsNull)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        foreach (MarkupNode c in text.ChildElements)
        {
            if (c.LocalNameString == "t")
            {
                sb.Append(c.InnerText);
            }
            else if (c.LocalNameString == "r")
            {
                foreach (MarkupNode t in c.ChildElements)
                {
                    if (t.LocalNameString == "t")
                    {
                        sb.Append(t.InnerText);
                    }
                }
            }
        }

        return sb.ToString();
    }

    private int TotalNotes() => Enumerable.Range(0, _sheets.Count).Sum(i => Notes(i).Count);

    private static void Escher(BiffBuffer b, int ver, int inst, int type, int length)
    {
        b.U16((inst << 4) | ver);
        b.U16(type);
        b.U32((uint)length);
    }

    // The drawing of a sheet with notes gets the number 1, 2, ... in order; its shapes are numbered from drawing*1024.
    private int DrawingNumber(int si)
    {
        int n = 0;
        for (int i = 0; i <= si; i++)
        {
            if (Notes(i).Count > 0)
            {
                n++;
            }
        }

        return n;
    }

    // [MS-ODRAW] §2.2.12 OfficeArtDggContainer, §2.2.46 OfficeArtFDGG: the drawing group names the clusters of shape identifiers.
    private void WriteDrawingGroup()
    {
        var clusters = new List<(int Drawing, int Shapes)>();
        for (int i = 0; i < _sheets.Count; i++)
        {
            if (Notes(i).Count > 0)
            {
                clusters.Add((DrawingNumber(i), Notes(i).Count));
            }
        }

        int totalShapes = clusters.Sum(c => c.Shapes + 1);
        int spidMax = clusters.Max(c => c.Drawing * 1024 + c.Shapes) + 1;
        var dgg = new BiffBuffer();
        dgg.U32((uint)spidMax);
        dgg.U32((uint)(clusters.Count + 1));
        dgg.U32((uint)totalShapes);
        dgg.U32((uint)clusters.Count);
        foreach ((int d, int shapes) in clusters)
        {
            dgg.U32((uint)d);
            dgg.U32((uint)(shapes + 1));
        }

        var opt = new BiffBuffer();
        opt.U16(0x00BF);
        opt.U32(0x00080008);
        opt.U16(0x0181);
        opt.U32(0x08000041);
        opt.U16(0x01C0);
        opt.U32(0x08000040);
        var split = new BiffBuffer();
        split.U32(0x0800000D);
        split.U32(0x0800000C);
        split.U32(0x08000017);
        split.U32(0x100000F7);
        var all = new BiffBuffer();
        int inner = 8 + dgg.Length + 8 + opt.Length + 8 + split.Length;
        Escher(all, 0xF, 0, 0xF000, inner);
        Escher(all, 0, 0, 0xF006, dgg.Length);
        all.Bytes(dgg.Span);
        Escher(all, 3, 3, 0xF00B, opt.Length);
        all.Bytes(opt.Span);
        Escher(all, 0, 4, 0xF11E, split.Length);
        all.Bytes(split.Span);
        _s.Record(0x00EB, all);
    }

    private void WriteObjects(int si, Worksheet ws)
    {
        _ = ws;
        List<NoteOut> notes = Notes(si);
        if (notes.Count == 0)
        {
            return;
        }

        int drawing = DrawingNumber(si);
        int baseId = drawing * 1024;
        var shapes = new List<byte[]>();
        for (int k = 0; k < notes.Count; k++)
        {
            NoteOut n = notes[k];
            var sp = new BiffBuffer();
            Escher(sp, 2, 202, 0xF00A, 8);
            sp.U32((uint)(baseId + 1 + k));
            sp.U32(0x00000A00);
            var opt = new BiffBuffer();
            // [MS-ODRAW] §2.3.6 (text properties): the shape is a text box, so it names its text (lTxid), wraps it and anchors it at the
            // top, as the notes written by other applications do.
            opt.U16(0x0080);
            opt.U32(0);
            opt.U16(0x0085);
            opt.U32(2);
            opt.U16(0x0087);
            opt.U32(1);
            opt.U16(0x00BF);
            opt.U32(0x00080008);
            opt.U16(0x0181);
            opt.U32(0x08000050);
            opt.U16(0x01BF);
            opt.U32(0x00110010);
            opt.U16(0x01FF);
            opt.U32(0x00080008);
            opt.U16(0x03BF);
            opt.U32(0x00020002);
            var anchor = new BiffBuffer();
            anchor.U16(3);
            anchor.U16(Math.Min(n.Col + 1, 255));
            anchor.U16(15);
            anchor.U16(Math.Max(n.Row - 1, 0));
            anchor.U16(10);
            anchor.U16(Math.Min(n.Col + 3, 255));
            anchor.U16(15);
            anchor.U16(n.Row + 3);
            anchor.U16(4);
            var body = new BiffBuffer();
            body.Bytes(sp.Span);
            Escher(body, 3, 8, 0xF00B, opt.Length);
            body.Bytes(opt.Span);
            Escher(body, 0, 0, 0xF010, anchor.Length);
            body.Bytes(anchor.Span);
            Escher(body, 0, 0, 0xF011, 0);
            var container = new BiffBuffer();
            Escher(container, 0xF, 0, 0xF004, body.Length);
            container.Bytes(body.Span);
            shapes.Add(container.Span.ToArray());
        }

        var patriarch = new BiffBuffer();
        Escher(patriarch, 1, 0, 0xF009, 16);
        patriarch.Zeros(16);
        Escher(patriarch, 2, 0, 0xF00A, 8);
        patriarch.U32((uint)baseId);
        patriarch.U32(5);
        int patriarchLength = 8 + patriarch.Length;
        int groupLength = patriarchLength + shapes.Sum(x => x.Length);
        int drawingLength = 16 + 8 + groupLength;
        var head = new BiffBuffer();
        Escher(head, 0xF, 0, 0xF002, drawingLength);
        Escher(head, 0, drawing, 0xF008, 8);
        head.U32((uint)(notes.Count + 1));
        head.U32((uint)(baseId + notes.Count));
        Escher(head, 0xF, 0, 0xF003, groupLength);
        Escher(head, 0xF, 0, 0xF004, patriarch.Length);
        head.Bytes(patriarch.Span);
        head.Bytes(shapes[0]);
        _s.Record(0x00EC, head);
        for (int k = 0; k < notes.Count; k++)
        {
            if (k > 0)
            {
                _s.Record(0x00EC, shapes[k]);
            }

            WriteNoteObject(k + 1);
            // [MS-XLS] §2.4.178 (MsoDrawing) and [MS-ODRAW] OfficeArtClientTextbox: the text box marker of a note is a record of its own
            // between the Obj and the TxO, not part of the shape container. Excel refuses a workbook whose notes carry it inside the
            // container (observed: Excel 16.0.20527 fails to open it); a file written by another application has this layout.
            var textbox = new BiffBuffer();
            Escher(textbox, 0, 0, 0xF00D, 0);
            _s.Record(0x00EC, textbox);
            WriteTextObject(notes[k].Text);
        }

        for (int k = 0; k < notes.Count; k++)
        {
            NoteOut n = notes[k];
            var b = new BiffBuffer();
            b.U16(n.Row);
            b.U16(n.Col);
            b.U16(0);
            b.U16(k + 1);
            string author = n.Author.Length > 54 ? n.Author[..54] : n.Author;
            b.String16(author);
            b.U8(0);
            _s.Record(0x001C, b);
        }
    }

    // [MS-XLS] §2.4.181 Obj: FtCmo (the object type 0x19 is a note), FtNts, FtEnd.
    private void WriteNoteObject(int id)
    {
        var b = new BiffBuffer();
        b.U16(0x15);
        b.U16(0x12);
        b.U16(0x19);
        b.U16(id);
        b.U16(0x4011);
        b.Zeros(12);
        b.U16(0x0D);
        b.U16(0x16);
        b.Zeros(16);
        b.U16(0);
        b.U32(0);
        b.U16(0);
        b.U16(0);
        _s.Record(0x005D, b);
    }

    // [MS-XLS] §2.4.329 TxO: the text and its formatting runs follow in CONTINUE records, each text part led by a flag byte.
    private void WriteTextObject(string text)
    {
        var b = new BiffBuffer();
        b.U16(0x0212);
        b.U16(0);
        b.Zeros(6);
        b.U16(text.Length);
        b.U16(text.Length > 0 ? 16 : 0);
        b.U16(0);
        b.U16(0);
        _s.Record(0x01B6, b);
        if (text.Length == 0)
        {
            return;
        }

        bool wide = !BiffBuffer.IsCompressible(text);
        int width = wide ? 2 : 1;
        int per = (BiffSink.MaxData - 1) / width;
        for (int done = 0; done < text.Length; done += per)
        {
            string part = text.Substring(done, Math.Min(per, text.Length - done));
            var c = new BiffBuffer();
            c.U8(wide ? 1 : 0);
            c.Chars(part, wide);
            _s.Exact(0x003C, c.Span);
        }

        var runs = new BiffBuffer();
        runs.U16(0);
        runs.U16(0);
        runs.U32(0);
        runs.U16(text.Length);
        runs.U16(0);
        runs.U32(0);
        _s.Exact(0x003C, runs.Span);
    }
}
