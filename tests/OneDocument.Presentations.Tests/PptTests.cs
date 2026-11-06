using System.Buffers.Binary;
using System.Text;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Ppt.Binary;
using RecursiveAI.OneDocument.Presentations;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>Builds small PowerPoint 97-2003 files from records, so the structure the reader depends on can be shaped exactly: edit chains, directories, damage.</summary>
internal sealed class PptBuilder
{
    private readonly List<byte> _stream = [];

    public int Position => _stream.Count;

    public static byte[] Rec(int version, int instance, int type, params byte[][] parts)
    {
        int length = parts.Sum(p => p.Length);
        var bytes = new byte[8 + length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)(version | (instance << 4)));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)length);
        int at = 8;
        foreach (byte[] part in parts)
        {
            part.CopyTo(bytes, at);
            at += part.Length;
        }

        return bytes;
    }

    public static byte[] U32(params uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
        }

        return bytes;
    }

    public static byte[] U16(params ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), values[i]);
        }

        return bytes;
    }

    public static byte[] Utf16(string s) => Encoding.Unicode.GetBytes(s);

    public int Add(byte[] record)
    {
        int at = _stream.Count;
        _stream.AddRange(record);
        return at;
    }

    // DocumentAtom: slide size 10 x 7.5 inch in master units, notes size, zoom, notes and handout master refs, first slide number.
    public static byte[] DocumentAtom() => Rec(0, 0, 0x3E9, U32(5760, 4320, 4320, 5760, 1, 2, 1, 2, 0, 0), U16(1, 0), new byte[4]);

    public static byte[] SlideAtom(int layout, uint masterId, uint notesId) =>
        Rec(0, 0, 0x3EF, U32((uint)layout), new byte[8], U32(masterId, notesId), U16(7, 0));

    public static byte[] SlidePersist(uint persist, uint id) => Rec(0, 0, 0x3F3, U32(persist, 0, 0, id, 0));

    // A text box shape holding one body of text (header, characters and optionally a style record).
    public static byte[] TextBoxShape(int spid, string text, byte[]? style = null)
    {
        byte[] fsp = Rec(2, 202, 0xF00A, U32((uint)spid, 0x0A00));
        byte[] anchor = Rec(0, 0, 0xF010, U16(100, 200, 4000, 1000)); // top, left, right, bottom
        byte[] header = Rec(0, 0, 0xFA0 - 1, U32(4)); // RT_TextHeaderAtom 0x0F9F
        byte[] chars = Rec(0, 0, 0xFA0, Utf16(text));
        byte[] box = Rec(0, 0, 0xF00D, header, chars, style ?? []);
        return Rec(0xF, 0, 0xF004, fsp, anchor, box);
    }

    public static byte[] Drawing(params byte[][] shapes)
    {
        byte[] patriarch = Rec(0xF, 0, 0xF004, Rec(2, 0, 0xF00A, U32(1024, 5)), Rec(1, 0, 0xF009, U32(0, 0, 5760, 4320)));
        byte[] group = Rec(0xF, 0, 0xF003, [patriarch, .. shapes]);
        byte[] dg = Rec(0xF, 1, 0xF002, Rec(0, 1, 0xF008, U32(2, 1025)), group);
        return Rec(0xF, 0, 0x40C, dg);
    }

    public static byte[] MainMaster() => Rec(0xF, 0, 0x3F8, SlideAtom(1, 0, 0), Drawing());

    public static byte[] Slide(uint masterId, string text, byte[]? style = null) =>
        Rec(0xF, 0, 0x3EE, SlideAtom(1, masterId, 0), Drawing(TextBoxShape(1026, text, style)));

    public static byte[] Document(uint masterId, uint masterPersist, uint slidePersist, uint slideId) =>
        Rec(0xF, 0, 0x3E8,
            DocumentAtom(),
            Rec(0xF, 1, 0xFF0, Rec(0, 0, 0x3F3, U32(masterPersist, 0, 0, masterId, 0))),
            Rec(0xF, 0, 0xFF0, SlidePersist(slidePersist, slideId)));

    /// <summary>Writes a persist directory for the entries and a UserEditAtom after it; returns the offset of the UserEditAtom.</summary>
    public int AddEdit(IEnumerable<(uint Id, int Offset)> entries, int lastEdit, uint seed, int overrideDirectory = -1)
    {
        var payload = new List<byte>();
        foreach ((uint id, int offset) in entries)
        {
            payload.AddRange(U32(id | (1u << 20)));
            payload.AddRange(U32((uint)offset));
        }

        int directory = Add(Rec(0, 0, 0x1772, payload.ToArray()));
        return Add(Rec(0, 0, 0xFF5, U32(0, 0, (uint)lastEdit, (uint)(overrideDirectory >= 0 ? overrideDirectory : directory), 1, seed), U16(0, 0)));
    }


    /// <summary>A shape container with the given header type and flags, property entries (id, value; complex data after), client data and text.</summary>
    public static byte[] Shape(int spid, int type, int flags, (int Id, uint Value, byte[]? Complex)[] props, byte[]? clientData = null, byte[]? textbox = null, byte[]? anchor = null)
    {
        byte[] fsp = Rec(2, type, 0xF00A, U32((uint)spid, (uint)flags));
        var fixedPart = new List<byte>();
        var complexPart = new List<byte>();
        foreach ((int id, uint value, byte[]? complex) in props)
        {
            fixedPart.AddRange(U16((ushort)(id | (complex is null ? 0 : 0x8000))));
            fixedPart.AddRange(U32(complex is null ? value : (uint)complex.Length));
            if (complex is not null)
            {
                complexPart.AddRange(complex);
            }
        }

        byte[] fopt = props.Length == 0 ? [] : Rec(3, props.Length, 0xF00B, fixedPart.ToArray(), complexPart.ToArray());
        byte[] a = Rec(0, 0, 0xF010, anchor ?? U16(100, 200, 4000, 1000));
        return Rec(0xF, 0, 0xF004, fsp, fopt, a, clientData ?? [], textbox ?? []);
    }

    public static byte[] TextBody(string text) => Rec(0, 0, 0xF00D, Rec(0, 0, 0xF9F, U32(4)), Rec(0, 0, 0xFA0, Utf16(text)));

    /// <summary>A presentation with one slide whose drawing holds the given shapes, written as a single edit; extras are added to the document container.</summary>
    public static byte[] SlideWith(byte[][] shapes, byte[][]? documentExtras = null, byte[]? slideExtra = null, byte[]? pictures = null, byte[]? dggContainer = null, params (string Name, byte[] Data)[] extraStreams)
    {
        var b = new PptBuilder();
        int master = b.Add(MainMaster());
        int slide = b.Add(Rec(0xF, 0, 0x3EE, SlideAtom(1, 0x80000000, 0), Drawing(shapes), slideExtra ?? []));
        byte[][] parts =
        [
            DocumentAtom(),
            dggContainer ?? [],
            Rec(0xF, 1, 0xFF0, Rec(0, 0, 0x3F3, U32(2, 0, 0, 0x80000000, 0))),
            Rec(0xF, 0, 0xFF0, SlidePersist(3, 256)),
            .. documentExtras ?? [],
        ];
        int doc = b.Add(Rec(0xF, 0, 0x3E8, parts));
        int edit = b.AddEdit([(1, doc), (2, master), (3, slide)], 0, 4);
        return File(b.Stream, CurrentUser(edit), pictures, extraStreams);
    }

    public byte[] Stream => [.. _stream];

    public static byte[] CurrentUser(int editOffset, uint token = 0xE391C05F) =>
        Rec(0, 0, 0xFF6, U32(0x14, token, (uint)editOffset), U16(0, 0x3F4), [3, 0], U16(0), U32(8));

    public static byte[] File(byte[] documentStream, byte[]? currentUser, byte[]? pictures = null, params (string Name, byte[] Data)[] extraStreams)
    {
        using var package = CfbPackage.Create();
        package.AddPart("/PowerPoint Document", "", documentStream);
        if (currentUser is not null)
        {
            package.AddPart("/Current User", "", currentUser);
        }

        if (pictures is not null)
        {
            package.AddPart("/Pictures", "", pictures);
        }

        foreach ((string name, byte[] data) in extraStreams)
        {
            package.AddPart(name, "", data);
        }

        using var ms = new MemoryStream();
        package.WriteTo(ms);
        return ms.ToArray();
    }

    /// <summary>A one-slide presentation with the given slide text, written as a single edit.</summary>
    public static byte[] OneSlide(string text, byte[]? style = null)
    {
        var b = new PptBuilder();
        int master = b.Add(MainMaster());
        int slide = b.Add(Slide(0x80000000, text, style));
        int doc = b.Add(Document(0x80000000, 2, 3, 256));
        int edit = b.AddEdit([(1, doc), (2, master), (3, slide)], 0, 4);
        return File(b.Stream, CurrentUser(edit));
    }
}

public class PptReaderTests
{
    private static Presentation Open(byte[] bytes, OpenContext? context = null) => Presentation.Open(new MemoryStream(bytes), context);

    [Fact]
    public void A_minimal_file_opens_and_its_text_is_read()
    {
        using Presentation p = Open(PptBuilder.OneSlide("Hello legacy"));
        Assert.Equal(OfficeFormat.Ppt, p.SourceFormat);
        Assert.Single(p.Slides);
        Assert.Contains("Hello legacy", p.Slides[0].Text);
        Assert.NotNull(p.ImportLoss);
    }

    [Fact]
    public void The_slide_size_is_converted_from_master_units()
    {
        using Presentation p = Open(PptBuilder.OneSlide("x"));
        string xml = Decks.Xml(Decks.Save(p), "ppt/presentation.xml");
        // 5760 master units of 1/576 inch are 10 inches: 9,144,000 EMU.
        Assert.Contains("<p:sldSz cx=\"9144000\" cy=\"6858000\"/>", xml);
    }

    [Fact]
    public void The_newest_edit_wins_and_the_older_object_stays_in_the_stream()
    {
        var b = new PptBuilder();
        int master = b.Add(PptBuilder.MainMaster());
        int oldSlide = b.Add(PptBuilder.Slide(0x80000000, "Old text"));
        int doc = b.Add(PptBuilder.Document(0x80000000, 2, 3, 256));
        int firstEdit = b.AddEdit([(1, doc), (2, master), (3, oldSlide)], 0, 4);
        int newSlide = b.Add(PptBuilder.Slide(0x80000000, "New text"));
        int secondEdit = b.AddEdit([(3, newSlide)], firstEdit, 4);
        byte[] file = PptBuilder.File(b.Stream, PptBuilder.CurrentUser(secondEdit));

        using Presentation p = Open(file);

        Assert.Contains("New text", p.Slides[0].Text);
        Assert.DoesNotContain("Old text", p.Slides[0].Text);
        Assert.Contains("Old text", Encoding.Unicode.GetString(b.Stream));
        var ppt = PptFile.Open(ByteSource.FromMemory(file), new OpenContext());
        Assert.Equal(2, ppt.UserEdits.Count);
        Assert.Equal(newSlide, ppt.PersistOffsets[3]);
    }

    [Fact]
    public void A_missing_current_user_stream_is_recovered_by_scanning()
    {
        var b = new PptBuilder();
        int master = b.Add(PptBuilder.MainMaster());
        int slide = b.Add(PptBuilder.Slide(0x80000000, "Scanned"));
        int doc = b.Add(PptBuilder.Document(0x80000000, 2, 3, 256));
        b.AddEdit([(1, doc), (2, master), (3, slide)], 0, 4);

        using Presentation p = Open(PptBuilder.File(b.Stream, null));

        Assert.Contains("Scanned", p.Slides[0].Text);
        Assert.Contains(p.Defects, d => d.Code == "ppt.current-user-missing" && d.Location.Kind == DefectLocationKind.Record);
    }

    [Fact]
    public void A_persist_directory_that_is_not_there_is_reported_and_older_edits_still_serve()
    {
        var b = new PptBuilder();
        int master = b.Add(PptBuilder.MainMaster());
        int slide = b.Add(PptBuilder.Slide(0x80000000, "Survives"));
        int doc = b.Add(PptBuilder.Document(0x80000000, 2, 3, 256));
        int firstEdit = b.AddEdit([(1, doc), (2, master), (3, slide)], 0, 4);
        int secondEdit = b.AddEdit([(9, doc)], firstEdit, 4, overrideDirectory: 7);

        using Presentation p = Open(PptBuilder.File(b.Stream, PptBuilder.CurrentUser(secondEdit)));

        Assert.Contains("Survives", p.Slides[0].Text);
        Defect d = Assert.Single(p.Defects, x => x.Code == "ppt.persist-directory-missing");
        Assert.Equal(secondEdit, d.Location.Offset);
    }

    [Fact]
    public void An_edit_chain_that_does_not_move_backwards_stops()
    {
        var b = new PptBuilder();
        int master = b.Add(PptBuilder.MainMaster());
        int slide = b.Add(PptBuilder.Slide(0x80000000, "Loop"));
        int doc = b.Add(PptBuilder.Document(0x80000000, 2, 3, 256));
        int edit = b.AddEdit([(1, doc), (2, master), (3, slide)], 0, 4);
        // Point the edit at itself.
        byte[] stream = b.Stream;
        BinaryPrimitives.WriteUInt32LittleEndian(stream.AsSpan(edit + 8 + 8), (uint)edit);

        using Presentation p = Open(PptBuilder.File(stream, PptBuilder.CurrentUser(edit)));

        Assert.Contains("Loop", p.Slides[0].Text);
        Assert.Contains(p.Defects, d => d.Code == "ppt.edit-chain-loop");
    }

    [Fact]
    public void An_encrypted_file_without_a_session_container_is_a_typed_error()
    {
        var b = new PptBuilder();
        int doc = b.Add(PptBuilder.Document(0x80000000, 2, 3, 256));
        int edit = b.AddEdit([(1, doc)], 0, 4);
        byte[] file = PptBuilder.File(b.Stream, PptBuilder.CurrentUser(edit, token: 0xF3D1C4DF));

        // Real encrypted files are covered by the encryption tests; this token alone names no scheme.
        var ex = Assert.ThrowsAny<OfficeException>(() => Open(file));
        Assert.StartsWith("encryption.", ex.Defect.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stream_without_user_edits_is_a_typed_format_error()
    {
        byte[] file = PptBuilder.File(PptBuilder.MainMaster(), null);
        var ex = Assert.Throws<OfficeFormatException>(() => Open(file));
        Assert.Equal("ppt.no-user-edit", ex.Defect.Code);
    }

    [Fact]
    public void A_record_that_claims_more_than_the_stream_holds_is_clamped_with_a_defect()
    {
        var b = new PptBuilder();
        int master = b.Add(PptBuilder.MainMaster());
        byte[] slideBytes = PptBuilder.Slide(0x80000000, "Cut off");
        int slide = b.Add(slideBytes);
        int doc = b.Add(PptBuilder.Document(0x80000000, 2, 3, 256));
        int edit = b.AddEdit([(1, doc), (2, master), (3, slide)], 0, 4);
        byte[] stream = b.Stream;
        // The slide container claims far more bytes than the stream has.
        BinaryPrimitives.WriteUInt32LittleEndian(stream.AsSpan(slide + 4), 0x7FFFFF00);

        using Presentation p = Open(PptBuilder.File(stream, PptBuilder.CurrentUser(edit)));

        Assert.Contains(p.Defects, d => d.Code == "art.record-overrun" && d.Location.Offset == slide);
    }

    [Fact]
    public void Garbage_in_the_document_stream_never_escapes_as_an_untyped_exception()
    {
        var random = new Random(7);
        for (int round = 0; round < 40; round++)
        {
            byte[] good = PptBuilder.OneSlide("Fuzzed " + round);
            using var cfb = CfbPackage.Open(new MemoryStream(good));
            byte[] stream = cfb.FindPart("/PowerPoint Document")!.GetBytes().ToArray();
            for (int k = 0; k < 6; k++)
            {
                stream[random.Next(stream.Length)] = (byte)random.Next(256);
            }

            byte[] file = PptBuilder.File(stream, PptBuilder.CurrentUser(random.Next(stream.Length)));
            try
            {
                using Presentation p = Open(file);
                _ = p.Slides.Count;
            }
            catch (OfficeException)
            {
            }
        }
    }

    [Fact]
    public void Character_and_paragraph_runs_become_run_properties()
    {
        // "Bold plain" with a bold, 20 pt run over the first four characters and a centred paragraph.
        var style = new List<byte>();
        style.AddRange(PptBuilder.U32(11));            // paragraph run: 11 characters (10 + break)
        style.AddRange(PptBuilder.U16(0));             // indent level
        style.AddRange(PptBuilder.U32(1u << 11));      // masks: alignment
        style.AddRange(PptBuilder.U16(1));             // centred
        style.AddRange(PptBuilder.U32(4));             // character run: first four characters
        style.AddRange(PptBuilder.U32(0x1 | 0x20000)); // masks: bold, size
        style.AddRange(PptBuilder.U16(1));             // font style: bold
        style.AddRange(PptBuilder.U16(20));            // 20 pt
        style.AddRange(PptBuilder.U32(7));             // character run: the rest
        style.AddRange(PptBuilder.U32(0));             // no fields
        byte[] record = PptBuilder.Rec(0, 0, 0xFA1, style.ToArray());

        using Presentation p = Open(PptBuilder.OneSlide("Bold plain", record));
        string xml = Decks.Xml(Decks.Save(p), "ppt/slides/slide1.xml");

        Assert.Contains("algn=\"ctr\"", xml);
        Assert.Contains("sz=\"2000\" b=\"1\"", xml);
        Assert.Contains("<a:t>Bold</a:t>", xml);
        Assert.Contains("<a:t> plain</a:t>", xml);
    }

    [Fact]
    public void Office_extract_text_and_convert_work_on_the_format()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ppt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string ppt = Path.Combine(dir, "in.ppt");
            File.WriteAllBytes(ppt, PptBuilder.OneSlide("Via the statics"));
            PresentationFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);

            Assert.Contains("Via the statics", Office.ExtractText(ppt));
            string pptx = Path.Combine(dir, "out.pptx");
            Office.Convert(ppt, pptx);
            using Presentation back = Presentation.Open(pptx);
            Assert.Contains("Via the statics", back.Slides[0].Text);
            Assert.Equal(OfficeFormat.Pptx, back.SourceFormat);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
