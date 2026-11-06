using RecursiveAI.OneDocument.Diagnostics;
using static RecursiveAI.OneDocument.Presentations.Tests.PptBuilder;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>One crafted file per defect and loss code of the PowerPoint 97-2003 reader (the catalogue is in docs/defects.md).</summary>
public class PptDefectTests
{
    private static Presentation Open(byte[] bytes) => Presentation.Open(new MemoryStream(bytes));

    private static (int Id, uint Value, byte[]? Complex) P(int id, uint value) => (id, value, null);

    private static byte[] Array(int elementSize, params byte[][] elements)
    {
        byte[] header = [.. U16((ushort)elements.Length), .. U16((ushort)elements.Length), .. U16((ushort)elementSize)];
        return [.. header, .. elements.SelectMany(e => e)];
    }

    private static void AssertDefect(Presentation p, string code, DefectLocationKind kind = DefectLocationKind.Record)
    {
        Defect? d = p.Defects.FirstOrDefault(x => x.Code == code);
        Assert.True(d is not null, "missing defect " + code + "; have: " + string.Join(", ", p.Defects.Select(x => x.Code).Distinct()));
        Assert.Equal(kind, d!.Location.Kind);
    }

    private static void AssertLoss(Presentation p, string code)
    {
        Assert.True(p.ConversionLoss!.CountOf(code) >= 1, "missing loss item " + code + ": " + p.ConversionLoss);
        AssertDefect(p, code);
    }

    private static byte[] Plain(int type = 1, params (int, uint, byte[]?)[] props) => Shape(1030, type, 0x200, props);

    [Fact]
    public void A_file_without_the_document_stream_is_fatal()
    {
        using var package = RecursiveAI.OneDocument.Cfb.CfbPackage.Create();
        package.AddPart("/Current User", "", CurrentUser(0));
        using var ms = new MemoryStream();
        package.WriteTo(ms);
        // The sniffer already calls such a file unknown; the record layer names the problem when it is used directly.
        var ex = Assert.Throws<OfficeFormatException>(() => RecursiveAI.OneDocument.Ppt.Binary.PptFile.Open(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(ms.ToArray()), new OpenContext()));
        Assert.Equal("ppt.no-document-stream", ex.Defect.Code);
    }

    [Fact]
    public void A_bad_current_user_offset_is_repaired_by_scanning()
    {
        byte[] good = OneSlide("Found");
        using var cfb = RecursiveAI.OneDocument.Cfb.CfbPackage.Open(new MemoryStream(good));
        byte[] stream = cfb.FindPart("/PowerPoint Document")!.GetBytes().ToArray();
        using Presentation p = Open(File(stream, CurrentUser(3)));
        Assert.Contains("Found", p.Slides[0].Text);
        AssertDefect(p, "ppt.edit-chain-broken");
    }

    [Fact]
    public void A_persist_directory_that_lists_more_offsets_than_it_holds_is_reported()
    {
        var b = new PptBuilder();
        int master = b.Add(MainMaster());
        int slide = b.Add(Slide(0x80000000, "Short"));
        int doc = b.Add(Document(0x80000000, 2, 3, 256));
        // One entry that claims three offsets, then only two follow.
        int directory = b.Add(Rec(0, 0, 0x1772, U32(1u | (3u << 20), (uint)doc, (uint)master)));
        _ = slide;
        int edit = b.Add(Rec(0, 0, 0xFF5, U32(0, 0, 0, (uint)directory, 1, 4), U16(0, 0)));
        using Presentation p = Open(File(b.Stream, CurrentUser(edit)));
        AssertDefect(p, "ppt.persist-directory-truncated");
        AssertDefect(p, "ppt.persist-object-missing");
        AssertDefect(p, "ppt.slide-missing");
    }

    [Fact]
    public void A_document_persist_id_that_is_not_listed_is_found_by_scanning()
    {
        var b = new PptBuilder();
        int master = b.Add(MainMaster());
        int slide = b.Add(Slide(0x80000000, "Scanned doc"));
        int doc = b.Add(Document(0x80000000, 2, 3, 256));
        int edit = b.AddEdit([(2, master), (3, slide)], 0, 4);
        _ = doc;
        using Presentation p = Open(File(b.Stream, CurrentUser(edit)));
        Assert.Contains("Scanned doc", p.Slides[0].Text);
        AssertDefect(p, "ppt.document-container-recovered");
    }

    [Fact]
    public void A_document_without_a_document_atom_uses_the_default_size()
    {
        var b = new PptBuilder();
        int master = b.Add(MainMaster());
        int doc = b.Add(Rec(0xF, 0, 0x3E8, Rec(0xF, 1, 0xFF0, Rec(0, 0, 0x3F3, U32(2, 0, 0, 0x80000000, 0)))));
        int edit = b.AddEdit([(1, doc), (2, master)], 0, 3);
        using Presentation p = Open(File(b.Stream, CurrentUser(edit)));
        AssertDefect(p, "ppt.document-atom-missing");
    }

    [Fact]
    public void A_master_list_entry_that_is_not_a_master_is_dropped()
    {
        var b = new PptBuilder();
        int slide = b.Add(Slide(0x80000000, "No master"));
        int doc = b.Add(Document(0x80000000, 3, 3, 256));
        int edit = b.AddEdit([(1, doc), (3, slide)], 0, 4);
        using Presentation p = Open(File(b.Stream, CurrentUser(edit)));
        AssertDefect(p, "ppt.master-missing");
        Assert.Contains("No master", p.Slides[0].Text);
    }

    [Fact]
    public void A_notes_list_entry_that_leads_nowhere_is_dropped()
    {
        var b = new PptBuilder();
        int master = b.Add(MainMaster());
        int slide = b.Add(Slide(0x80000000, "s"));
        int doc = b.Add(Rec(0xF, 0, 0x3E8, DocumentAtom(), Rec(0xF, 1, 0xFF0, Rec(0, 0, 0x3F3, U32(2, 0, 0, 0x80000000, 0))),
            Rec(0xF, 0, 0xFF0, SlidePersist(3, 256)), Rec(0xF, 2, 0xFF0, Rec(0, 0, 0x3F3, U32(9, 0, 0, 5, 0)))));
        int edit = b.AddEdit([(1, doc), (2, master), (3, slide)], 0, 4);
        using Presentation p = Open(File(b.Stream, CurrentUser(edit)));
        AssertDefect(p, "ppt.notes-missing");
    }

    [Fact]
    public void A_text_style_cut_short_is_reported()
    {
        byte[] style = Rec(0, 0, 0xFA1, U32(11), U16(0), U32(1u << 11));
        using Presentation p = Open(OneSlide("Cut style", style));
        AssertDefect(p, "ppt.text-style-truncated");
        Assert.Contains("Cut style", p.Slides[0].Text);
    }

    [Fact]
    public void A_text_special_info_with_smart_tags_stops_reading_language_runs()
    {
        byte[] info = Rec(0, 0, 0xFAA, U32(5, 1u << 9));
        using Presentation p = Open(OneSlide("Tags", info));
        AssertDefect(p, "ppt.text-special-info-unread");
    }

    [Fact]
    public void A_reference_to_outline_text_that_does_not_exist_is_reported()
    {
        byte[] shape = Shape(1030, 202, 0x200, [], textbox: Rec(0, 0, 0xF00D, Rec(0, 0, 0xF9E, U32(5))));
        using Presentation p = Open(SlideWith([shape]));
        AssertDefect(p, "ppt.outline-reference");
    }

    [Fact]
    public void A_shape_type_without_a_preset_is_written_as_a_rectangle()
    {
        using Presentation p = Open(SlideWith([Plain(187)]));
        AssertLoss(p, "ppt.shape-approximated");
        Assert.Contains("prst=\"rect\"", Decks.Xml(Decks.Save(p), "ppt/slides/slide1.xml"));
    }

    [Fact]
    public void Adjustments_of_a_preset_are_dropped_except_the_rounded_corner()
    {
        using Presentation p = Open(SlideWith([Plain(12, P(0x147, 5000))]));
        AssertLoss(p, "ppt.shape-adjust-dropped");
        using Presentation round = Open(SlideWith([Plain(2, P(0x147, 5400))]));
        Assert.Contains("fmla=\"val 25000\"", Decks.Xml(Decks.Save(round), "ppt/slides/slide1.xml"));
    }

    [Fact]
    public void A_pattern_fill_becomes_a_solid_fill()
    {
        using Presentation p = Open(SlideWith([Plain(1, P(0x180, 1), P(0x181, 0x000000FF), P(0x1BF, 0x00100010))]));
        AssertLoss(p, "ppt.fill-approximated");
        Assert.Contains("<a:srgbClr val=\"FF0000\"", Decks.Xml(Decks.Save(p), "ppt/slides/slide1.xml"));
    }

    [Fact]
    public void A_freeform_with_escapes_and_guides_loses_them_with_a_report()
    {
        byte[] vertices = Array(8, U32(0, 0), U32(100, 0), U32(0x80000001, 100));
        byte[] segments = Array(2, U16(0x4000), U16(0x0002), U16(0xA000), U16(0x6001), U16(0x8000));
        var shape = Shape(1030, 0, 0x200, [(0x145, 0, vertices), (0x146, 0, segments), (0x142, 100, null), (0x143, 100, null)]);
        using Presentation p = Open(SlideWith([shape]));
        AssertLoss(p, "ppt.path-escape");
        AssertLoss(p, "ppt.geometry-guides");
        Assert.Contains("<a:custGeom>", Decks.Xml(Decks.Save(p), "ppt/slides/slide1.xml"));
    }

    [Fact]
    public void An_unknown_placeholder_is_written_as_a_plain_shape()
    {
        byte[] data = Rec(0xF, 0, 0xF011, Rec(0, 0, 0xBC3, U32(1), [99, 0, 0, 0]));
        using Presentation p = Open(SlideWith([Shape(1030, 202, 0x200, [], data, TextBody("Odd"))]));
        AssertLoss(p, "ppt.placeholder-unknown");
    }

    [Fact]
    public void Word_art_text_is_kept_as_plain_text()
    {
        var shape = Shape(1030, 136, 0x200, [(0xC0, 0, Utf16("Fancy words"))]);
        using Presentation p = Open(SlideWith([shape]));
        AssertLoss(p, "ppt.wordart-flattened");
        Assert.Contains("Fancy words", p.Slides[0].Text);
    }

    [Fact]
    public void A_footer_field_with_no_footer_text_reports_the_loss()
    {
        byte[] box = Rec(0, 0, 0xF00D, Rec(0, 0, 0xF9F, U32(4)), Rec(0, 0, 0xFA0, Utf16("*")), Rec(0, 0, 0xFFA, U32(0)));
        using Presentation p = Open(SlideWith([Shape(1030, 202, 0x200, [], null, box)]));
        AssertLoss(p, "ppt.header-footer-field");
    }

    [Fact]
    public void A_footer_field_takes_the_footer_text_of_the_document()
    {
        byte[] box = Rec(0, 0, 0xF00D, Rec(0, 0, 0xF9F, U32(4)), Rec(0, 0, 0xFA0, Utf16("*")), Rec(0, 0, 0xFFA, U32(0)));
        byte[] hf = Rec(0xF, 0, 0xFD9, Rec(0, 0, 0xFDA, U16(0, 0x20)), Rec(0, 2, 0xFBA, Utf16("Footer here")));
        using Presentation p = Open(SlideWith([Shape(1030, 202, 0x200, [], null, box)], [hf]));
        Assert.Contains("Footer here", p.Slides[0].Text);
    }

    [Fact]
    public void An_unknown_transition_and_animations_are_reported()
    {
        byte[] info = Rec(0, 0, 0x3F9, U32(0, 0), [0, 99, 0, 0, 1, 0, 0, 0]);
        byte[] animated = Shape(1030, 1, 0x200, [], Rec(0xF, 0, 0xF011, Rec(0xF, 0, 0x1014)));
        using Presentation p = Open(SlideWith([animated], slideExtra: info));
        AssertLoss(p, "ppt.transition-dropped");
        AssertLoss(p, "ppt.animations-dropped");
    }

    [Fact]
    public void Embedded_objects_and_a_vba_project_are_reported()
    {
        byte[] list = Rec(0xF, 0, 0x409, Rec(0xF, 0, 0xFCC));
        using Presentation p = Open(SlideWith([Plain()], [list], extraStreams: [("/_VBA_PROJECT_CUR/VBA/Module1", [1, 2, 3])]));
        AssertLoss(p, "ppt.ole-object-flattened");
        AssertLoss(p, "ppt.vba-not-carried");
    }

    [Fact]
    public void A_picture_that_is_not_in_the_store_is_reported_and_a_pict_picture_is_flagged()
    {
        byte[] emptyStore = Rec(0xF, 0, 0x40B, Rec(0xF, 0, 0xF000, Rec(0, 0, 0xF006, U32(2000, 2, 1, 1)), Rec(0xF, 0, 0xF001)));
        using Presentation missing = Open(SlideWith([Shape(1030, 75, 0x200, [P(0x104, 5)])], dggContainer: emptyStore));
        AssertLoss(missing, "ppt.picture-missing");
        AssertDefect(missing, "art.blip-index");

        // A PICT BLIP (instance 0x542, one UID, uncompressed metafile header) inside a store entry.
        byte[] pictBlip = Rec(0, 0x542, 0xF01C, new byte[16], new byte[34].Select((_, i) => i == 32 ? (byte)0xFE : (byte)0).ToArray(), [1, 2, 3, 4]);
        byte[] bse = Rec(2, 4, 0xF007, [4, 4], new byte[16], U16(0xFF), U32((uint)pictBlip.Length, 1, 0xFFFFFFFF), [0, 0, 0, 0], pictBlip);
        byte[] dgg = Rec(0xF, 0, 0x40B, Rec(0xF, 0, 0xF000, Rec(0, 0, 0xF006, U32(2000, 2, 1, 1)), Rec(0xF, 1, 0xF001, bse)));
        using Presentation pict = Open(SlideWith([Shape(1030, 75, 0x200, [P(0x104, 1)])], dggContainer: dgg));
        AssertLoss(pict, "ppt.picture-pict");
    }

    [Fact]
    public void A_regular_table_group_becomes_a_table_and_an_irregular_one_stays_a_group()
    {
        byte[] cell = Shape(1031, 1, 0x202, [], null, TextBody("Cell"), U16(100, 200, 1000, 400));
        byte[] rows = Array(4, U32(300));
        byte[] own = Rec(0xF, 0, 0xF004, Rec(2, 0, 0xF00A, U32(1040, 0x201)), Rec(1, 0, 0xF009, U32(0, 0, 5760, 4320)),
            Rec(3, 2, 0xF00B, U16(0x39F), U32(1), U16(0x3A0 | 0x8000), U32((uint)rows.Length), rows),
            Rec(0, 0, 0xF010, U16(100, 200, 4000, 1000)));
        byte[] table = Rec(0xF, 0, 0xF003, own, cell);
        using Presentation regular = Open(SlideWith([table]));
        AssertLoss(regular, "ppt.table-borders-approximated");
        Assert.Contains("<a:tbl>", Decks.Xml(Decks.Save(regular), "ppt/slides/slide1.xml"));

        byte[] twoRows = Array(4, U32(300), U32(300));
        byte[] own2 = Rec(0xF, 0, 0xF004, Rec(2, 0, 0xF00A, U32(1040, 0x201)), Rec(1, 0, 0xF009, U32(0, 0, 5760, 4320)),
            Rec(3, 2, 0xF00B, U16(0x39F), U32(1), U16(0x3A0 | 0x8000), U32((uint)twoRows.Length), twoRows),
            Rec(0, 0, 0xF010, U16(100, 200, 4000, 1000)));
        using Presentation irregular = Open(SlideWith([Rec(0xF, 0, 0xF003, own2, cell)]));
        AssertLoss(irregular, "ppt.table-as-group");
    }

    [Fact]
    public void A_powerpoint_95_file_is_unsupported()
    {
        byte[] file = File(MainMaster(), CurrentUser(0), null, ("/PersistentStorage Directory", [1]));
        var ex = Assert.Throws<OfficeUnsupportedException>(() => Open(file));
        Assert.StartsWith("unsupported.", ex.Defect.Code, StringComparison.Ordinal);
    }
}
