using System.Buffers.Binary;
using System.Text;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Doc.Binary;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;

namespace RecursiveAI.OneDocument.Documents.Tests;

public sealed class DocReaderTests
{
    static DocReaderTests() => DocumentFormats.Register(FormatRegistry.Default);

    private static string Text(byte[] doc, OpenContext? ctx = null)
    {
        using IByteSource src = ByteSource.FromMemory(doc);
        using var w = new StringWriter();
        Office.ExtractText(src, w, ctx);
        return w.ToString();
    }

    private static Document Open(byte[] doc, DefectCollection? defects = null) =>
        Document.Open(new MemoryStream(doc), new OpenContext(defects: defects), leaveOpen: false);

    private static string Xml(Document d) => d.MainMarkup.Root.OuterXml;

    // Rewrites one stream of a compound file.
    private static byte[] Mutate(byte[] cfbBytes, string stream, Func<byte[], byte[]> change)
    {
        using CfbPackage cfb = CfbPackage.Open(new MemoryStream(cfbBytes));
        var part = (CfbStreamPart)cfb.FindPart(stream)!;
        part.SetBytes(change(part.GetBytes().ToArray()));
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        return ms.ToArray();
    }

    private static OfficeException Fails(byte[] doc, DefectCollection? defects = null)
    {
        var ctx = new OpenContext(defects: defects);
        using IByteSource src = ByteSource.FromMemory(doc);
        return Assert.ThrowsAny<OfficeException>(() => DocFile.Open(src, ctx));
    }

    [Fact]
    public void Text_of_one_eight_bit_piece()
    {
        byte[] doc = new DocBuilder().Piece("Hello world\rSecond line\r").Build();
        Assert.Equal("Hello world\nSecond line\n", Text(doc));
        Assert.Equal(OfficeFormat.Doc, FormatSniffer.Detect(ByteSource.FromMemory(doc)));
    }

    [Fact]
    public void Text_of_a_unicode_piece()
    {
        byte[] doc = new DocBuilder().Piece("H\u00E9llo \u4E2D\u6587\r", eightBit: false).Build();
        Assert.Equal("H\u00E9llo \u4E2D\u6587\n", Text(doc));
    }

    [Fact]
    public void Eight_bit_text_maps_the_windows_punctuation_range()
    {
        // 0x93 and 0x94 are the curly quotes, 0x85 the ellipsis ([MS-DOC] FcCompressed).
        byte[] doc = new DocBuilder().Piece("\u0093quoted\u0094\u0085\r").Build();
        Assert.Equal("\u201Cquoted\u201D\u2026\n", Text(doc));
    }

    [Fact]
    public void Fast_saved_pieces_are_read_in_character_order_whatever_their_place_in_the_file()
    {
        var b = new DocBuilder { ReversePieces = true };
        b.Piece("One\r").Piece("Two\r", eightBit: false).Piece("Three\r");
        byte[] doc = b.Build();
        Assert.Equal("One\nTwo\nThree\n", Text(doc));
        using IByteSource src = ByteSource.FromMemory(doc);
        DocFile f = DocFile.Open(src, new OpenContext());
        Assert.True(f.Fib.IsComplex);
        Assert.Equal(3, f.Pieces.Pieces.Count);
        Assert.True(f.Pieces.Pieces[0].Offset > f.Pieces.Pieces[2].Offset);
    }

    [Fact]
    public void Field_instructions_are_not_text_but_results_are()
    {
        byte[] doc = new DocBuilder().Piece("Page \u0013 PAGE \u00141\u0015 of text\r").Build();
        Assert.Equal("Page 1 of text\n", Text(doc));
        using Document d = Open(doc);
        string xml = Xml(d);
        Assert.Contains("w:instrText", xml, StringComparison.Ordinal);
        Assert.Contains("w:fldCharType=\"separate\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Character_properties_reach_the_model()
    {
        var b = new DocBuilder().Piece("plain bold plain\r");
        b.CharRuns.Add(new DocBuilder.Run(6, 10, DocBuilder.Sprm(0x0835, 1)));
        using Document d = Open(b.Build());
        string xml = Xml(d);
        Assert.Contains("<w:b/>", xml, StringComparison.Ordinal);
        Assert.Equal("plain bold plain", d.Paragraphs.First().Text);
    }

    [Fact]
    public void Tracked_deletions_are_left_out_of_the_text_and_kept_in_the_model()
    {
        var b = new DocBuilder().Piece("keep gone keep\r");
        b.CharRuns.Add(new DocBuilder.Run(5, 10, DocBuilder.Sprm(0x0800, 1)));
        byte[] doc = b.Build();
        Assert.Equal("keep keep\n", Text(doc));
        using Document d = Open(doc);
        string xml = Xml(d);
        Assert.Contains("<w:del ", xml, StringComparison.Ordinal);
        Assert.Contains("<w:delText", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_is_a_row_of_tab_separated_cells_and_a_model_table()
    {
        var b = new DocBuilder().Piece("a\ab\a\a");
        byte[] inTable = DocBuilder.Sprm(0x2416, 1);
        // sprmTDefTable: 16-bit size, column count, three cell edges, no cell descriptors.
        byte[] def = DocBuilder.Cat(DocBuilder.Sprm(0xD608, 8, 0, 2, 0, 0, 0xD0, 0x07, 0xA0, 0x0F));
        b.ParaRuns.Add(new DocBuilder.ParaProps(1, 0, inTable));
        b.ParaRuns.Add(new DocBuilder.ParaProps(3, 0, inTable));
        b.ParaRuns.Add(new DocBuilder.ParaProps(4, 0, DocBuilder.Cat(inTable, DocBuilder.Sprm(0x2417, 1), def)));
        byte[] doc = b.Build();
        Assert.Equal("a\tb\n", Text(doc));
        using Document d = Open(doc);
        string xml = Xml(d);
        Assert.Contains("<w:tbl>", xml, StringComparison.Ordinal);
        Assert.Equal(2, CountOf(xml, "<w:tc>"));
        Assert.Contains("<w:gridCol w:w=\"2000\"/>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Footnote_references_and_text_reach_the_model()
    {
        var b = new DocBuilder { Footnotes = "\u0002Note body\r" };
        b.Piece("Text\u0002 more\r");
        b.FootnoteRefs.Add((4, 1));
        b.CharRuns.Add(new DocBuilder.Run(4, 5, DocBuilder.Sprm(0x0855, 1)));
        byte[] doc = b.Build();
        using Document d = Open(doc);
        Assert.Contains("<w:footnoteReference w:id=\"1\"/>", Xml(d), StringComparison.Ordinal);
        Assert.Contains("Note body", Text(doc), StringComparison.Ordinal);
        IPackagePartAlias notes = new(d);
        Assert.Contains("Note body", notes.Footnotes, StringComparison.Ordinal);
    }

    [Fact]
    public void Section_properties_become_page_setup()
    {
        var b = new DocBuilder { Sepx = DocBuilder.Cat(DocBuilder.Sprm(0x301D, 2), DocBuilder.Sprm(0xB01F, 0x20, 0x3D), DocBuilder.Sprm(0xB020, 0xF0, 0x2F)) };
        b.Piece("Landscape\r");
        using Document d = Open(b.Build());
        string xml = Xml(d);
        Assert.Contains("w:orient=\"landscape\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:w=\"15648\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void An_encrypted_file_raises_the_encrypted_exception()
    {
        byte[] doc = new DocBuilder { Encrypted = true }.Piece("secret\r").Build();
        OfficeException ex = Fails(doc);
        Assert.IsType<OfficeEncryptedException>(ex);
        Assert.Equal("encryption.password-required", ex.Defect.Code);
    }

    [Fact]
    public void Word_6_text_is_read_and_the_loss_is_reported()
    {
        byte[] doc = DocBuilder.BuildWord6("Old text\rSecond\r");
        Assert.Equal("Old text\nSecond\n", Text(doc));
        using Document d = Open(doc);
        Assert.Equal(OfficeFormat.Doc, d.SourceFormat);
        Assert.Equal(1, d.ImportLoss!.CountOf("loss.doc-word6-formatting"));
        Assert.Equal("Old text", d.Paragraphs.First().Text);
    }

    [Fact]
    public void A_converted_document_saves_as_docx_and_reopens()
    {
        var b = new DocBuilder().Piece("Round trip\r");
        using Document d = Open(b.Build());
        using var ms = new MemoryStream();
        d.SaveAs(ms, OfficeFormat.Docx);
        using Document again = Document.Open(new MemoryStream(ms.ToArray()), new OpenContext(), leaveOpen: false);
        Assert.Equal("Round trip", again.Paragraphs.First().Text);
        Assert.Equal(OfficeFormat.Docx, again.SourceFormat);
        Assert.Throws<InvalidOperationException>(() => d.Save());
    }

    // ---- damaged files: typed failures and located repairs ----

    [Fact]
    public void Short_fib_is_fatal()
    {
        byte[] doc = Mutate(new DocBuilder().Piece("x\r").Build(), "/WordDocument", w => w[..20]);
        Assert.Equal("doc.fib-truncated", Fails(doc).Defect.Code);
    }

    [Fact]
    public void Wrong_identifier_is_fatal()
    {
        byte[] doc = Mutate(new DocBuilder().Piece("x\r").Build(), "/WordDocument", w =>
        {
            w[0] = 0x12;
            return w;
        });
        Assert.Equal("doc.bad-ident", Fails(doc).Defect.Code);
    }

    [Fact]
    public void Missing_word_stream_is_fatal()
    {
        using CfbPackage cfb = CfbPackage.Create();
        cfb.AddPart("/Other", string.Empty, new byte[600]);
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        Assert.Equal("doc.no-word-stream", Fails(ms.ToArray()).Defect.Code);
    }

    [Fact]
    public void Missing_table_stream_is_fatal()
    {
        byte[] doc = new DocBuilder().Piece("x\r").Build();
        using CfbPackage cfb = CfbPackage.Open(new MemoryStream(doc));
        cfb.RemovePart("/1Table");
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        Assert.Equal("doc.no-table-stream", Fails(ms.ToArray()).Defect.Code);
    }

    [Fact]
    public void A_clx_of_an_unknown_kind_is_fatal()
    {
        byte[] doc = Mutate(new DocBuilder().Piece("x\r").Build(), "/1Table", t =>
        {
            t[0] = 9;
            return t;
        });
        Assert.Equal("doc.clx-bad", Fails(doc).Defect.Code);
    }

    [Fact]
    public void A_missing_clx_is_fatal()
    {
        byte[] doc = Mutate(new DocBuilder().Piece("x\r").Build(), "/WordDocument", w =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(w.AsSpan(154 + (33 * 8) + 4), 0);
            return w;
        });
        Assert.Equal("doc.no-piece-table", Fails(doc).Defect.Code);
    }

    [Fact]
    public void A_piece_past_the_end_of_the_stream_is_repaired()
    {
        byte[] doc = new DocBuilder().Piece("abcdefghij\r").Build();
        // Cut the text short: the piece keeps its character positions and the missing characters read as spaces.
        doc = Mutate(doc, "/WordDocument", w => w[..(0x400 + 4)]);
        var defects = new DefectCollection();
        using IByteSource src = ByteSource.FromMemory(doc);
        DocFile f = DocFile.Open(src, new OpenContext(defects: defects));
        Assert.Equal(11, f.Text.Length);
        Assert.Contains(defects, d => d.Code == "doc.piece-out-of-range" && d.Severity == DefectSeverity.Repaired && d.Location.Kind == DefectLocationKind.Record);
    }

    [Fact]
    public void A_clx_cut_short_by_its_size_is_repaired()
    {
        byte[] doc = new DocBuilder().Piece("abc\r").Build();
        doc = Mutate(doc, "/WordDocument", w =>
        {
            // lcbClx says 1000 bytes; the table stream is shorter than that.
            BinaryPrimitives.WriteUInt32LittleEndian(w.AsSpan(154 + (33 * 8) + 4), 100000);
            return w;
        });
        var defects = new DefectCollection();
        using IByteSource src = ByteSource.FromMemory(doc);
        DocFile f = DocFile.Open(src, new OpenContext(defects: defects));
        Assert.Contains(defects, d => d.Code == "doc.clx-truncated");
        Assert.Equal("abc\r", f.Text);
    }

    [Fact]
    public void Pieces_with_decreasing_positions_are_dropped()
    {
        byte[] doc = new DocBuilder().Piece("abc\r").Piece("def\r").Build();
        doc = Mutate(doc, "/1Table", t =>
        {
            // PlcPcd after the 1-byte type and 4-byte size: cp0, cp1, cp2. Make cp1 beyond cp2.
            BinaryPrimitives.WriteInt32LittleEndian(t.AsSpan(5 + 4), 50);
            return t;
        });
        var defects = new DefectCollection();
        using IByteSource src = ByteSource.FromMemory(doc);
        DocFile.Open(src, new OpenContext(defects: defects));
        Assert.Contains(defects, d => d.Code == "doc.piece-cp-order");
    }

    [Fact]
    public void A_story_longer_than_the_text_is_clipped_with_a_defect()
    {
        byte[] doc = new DocBuilder { Footnotes = "n\r" }.Piece("abc\r").Build();
        doc = Mutate(doc, "/WordDocument", w =>
        {
            BinaryPrimitives.WriteInt32LittleEndian(w.AsSpan(64 + (4 * 4)), 5000);
            return w;
        });
        var defects = new DefectCollection();
        using IByteSource src = ByteSource.FromMemory(doc);
        DocFile f = DocFile.Open(src, new OpenContext(defects: defects));
        (int _, int len) = f.StoryRange(DocStory.Footnotes);
        Assert.True(len < 5000);
        Assert.Contains(defects, d => d.Code == "doc.story-out-of-range");
    }

    [Fact]
    public void A_pair_count_past_the_stream_is_repaired()
    {
        byte[] doc = new DocBuilder().Piece("abc\r").Build();
        doc = Mutate(doc, "/WordDocument", w =>
        {
            Array.Resize(ref w, 400);
            return w;
        });
        var defects = new DefectCollection();
        using IByteSource src = ByteSource.FromMemory(doc);
        Fails2(src, defects);
        Assert.Contains(defects, d => d.Code == "doc.fib-pairs-truncated");
    }

    private static void Fails2(IByteSource src, DefectCollection defects)
    {
        try
        {
            DocFile.Open(src, new OpenContext(defects: defects));
        }
        catch (OfficeException)
        {
            // The missing pairs may also take the piece table with them; the repair is what is asserted.
        }
    }

    [Fact]
    public void A_property_page_outside_the_stream_is_dropped_with_a_defect()
    {
        var b = new DocBuilder().Piece("bold\r");
        b.CharRuns.Add(new DocBuilder.Run(0, 4, DocBuilder.Sprm(0x0835, 1)));
        byte[] doc = b.Build();
        // Re-point the page number inside the character bin table (the entry after the piece table).
        doc = Mutate(doc, "/1Table", t =>
        {
            int clxLen = 1 + 4 + 8 + 8; // type, size, two positions, one Pcd
            BinaryPrimitives.WriteUInt32LittleEndian(t.AsSpan(clxLen + 8), 9999);
            return t;
        });
        var defects = new DefectCollection();
        using Document d = Open(doc, defects);
        _ = d.Paragraphs.First().Text;
        Assert.Contains(defects, x => x.Code == "doc.fkp-out-of-range");
    }

    [Fact]
    public void A_property_the_model_has_no_place_for_is_reported_once_with_its_location()
    {
        var b = new DocBuilder().Piece("one two\r");
        // sprmCFData-like unknown character modifier 0x0899 (not in the table), one byte operand.
        b.CharRuns.Add(new DocBuilder.Run(0, 3, DocBuilder.Sprm(0x0899, 1)));
        b.CharRuns.Add(new DocBuilder.Run(4, 7, DocBuilder.Sprm(0x0899, 1)));
        var defects = new DefectCollection();
        using Document d = Open(b.Build(), defects);
        Assert.Equal(1, defects.Count(x => x.Code == "doc.sprm-not-mapped"));
        Defect one = defects.First(x => x.Code == "doc.sprm-not-mapped");
        Assert.Equal(DefectLocationKind.Record, one.Location.Kind);
        Assert.Equal(DefectSeverity.Dropped, one.Severity);
        Assert.True(d.ImportLoss!.CountOf("loss.doc-sprms") >= 2);
    }

    [Fact]
    public void A_picture_location_outside_the_data_stream_is_dropped_with_a_defect()
    {
        var b = new DocBuilder { Data = new byte[200] }.Piece("pic\u0001 end\r");
        byte[] loc = [0x10, 0x27, 0, 0];
        b.CharRuns.Add(new DocBuilder.Run(3, 4, DocBuilder.Cat(DocBuilder.Sprm(0x0855, 1), DocBuilder.Sprm(0x6A03, loc))));
        var defects = new DefectCollection();
        using Document d = Open(b.Build(), defects);
        _ = Xml(d);
        Assert.Contains(defects, x => x.Code == "doc.picture-unreadable" && x.Location.Offset == 10000);
    }

    [Fact]
    public void An_inline_shape_without_a_picture_is_counted_as_a_dropped_shape()
    {
        var b = new DocBuilder { Data = new byte[200] }.Piece("pic\u0001 end\r");
        b.CharRuns.Add(new DocBuilder.Run(3, 4, DocBuilder.Cat(DocBuilder.Sprm(0x0855, 1), DocBuilder.Sprm(0x6A03, 0, 0, 0, 0))));
        using Document d = Open(b.Build());
        _ = Xml(d);
        Assert.Equal(1, d.ImportLoss!.CountOf("loss.doc-drawing-shapes"));
    }

    [Fact]
    public void Eight_bit_text_in_another_character_set_is_read_as_windows_1252_with_a_defect()
    {
        byte[] doc = DocBuilder.BuildWord6("text\r");
        doc = Mutate(doc, "/WordDocument", w =>
        {
            w[0x14] = 204;
            return w;
        });
        var defects = new DefectCollection();
        using IByteSource src = ByteSource.FromMemory(doc);
        DocFile f = DocFile.Open(src, new OpenContext(defects: defects));
        _ = f.Text;
        Assert.Contains(defects, d => d.Code == "doc.codepage-unsupported");
    }

    [Fact]
    public void Offsets_near_the_32_bit_limit_do_not_overflow()
    {
        // Found by the smoke fuzz: fcDop + 12 wrapped around in unsigned arithmetic, and a Plc of 1-3 bytes was read as having a position.
        byte[] doc = Mutate(new DocBuilder().Piece("abc\r").Build(), "/WordDocument", w =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(w.AsSpan(154 + (31 * 8)), 0xFFFFFFF8);
            BinaryPrimitives.WriteUInt32LittleEndian(w.AsSpan(154 + (31 * 8) + 4), 12);
            BinaryPrimitives.WriteUInt32LittleEndian(w.AsSpan(154 + (2 * 8)), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(w.AsSpan(154 + (2 * 8) + 4), 3);
            return w;
        });
        using Document d = Open(doc);
        Assert.Equal("abc", d.Paragraphs.First().Text);
    }

    [Fact]
    public void A_header_becomes_a_header_part_referenced_from_the_section()
    {
        var b = new DocBuilder { Headers = "Running head\r" };
        b.Piece("Body\r");
        byte[] doc = b.Build();
        using Document d = Open(doc);
        Assert.Contains("<w:headerReference w:type=\"default\"", Xml(d), StringComparison.Ordinal);
        Assert.Contains("Running head", Encoding.UTF8.GetString(d.Package.FindPart("/word/header1.xml")!.GetBytes().Span), StringComparison.Ordinal);
        string text = Text(doc);
        Assert.StartsWith("Body\n", text, StringComparison.Ordinal);
        Assert.Contains("Running head", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inline_picture_is_read_from_the_data_stream_and_kept_as_media()
    {
        // PICFAndOfficeArtData: a 68-byte header (size, header size, picture size in twips, scale), then a PNG picture record.
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
        byte[] blip = new byte[8 + 17 + png.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(blip, 0x6E0 << 4);
        BinaryPrimitives.WriteUInt16LittleEndian(blip.AsSpan(2), 0xF01E);
        BinaryPrimitives.WriteUInt32LittleEndian(blip.AsSpan(4), (uint)(17 + png.Length));
        png.CopyTo(blip, 8 + 17);
        byte[] data = new byte[68 + blip.Length];
        BinaryPrimitives.WriteInt32LittleEndian(data, data.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 68);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28), 1440);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(30), 720);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(32), 1000);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(34), 1000);
        blip.CopyTo(data, 68);
        var b = new DocBuilder { Data = data }.Piece("x\u0001y\r");
        b.CharRuns.Add(new DocBuilder.Run(1, 2, DocBuilder.Cat(DocBuilder.Sprm(0x0855, 1), DocBuilder.Sprm(0x6A03, 0, 0, 0, 0))));
        using Document d = Open(b.Build());
        string xml = Xml(d);
        Assert.Contains("<wp:inline", xml, StringComparison.Ordinal);
        Assert.Contains("<wp:extent cx=\"914400\" cy=\"457200\"/>", xml, StringComparison.Ordinal);
        Assert.Equal(png, d.Package.FindPart("/word/media/image1.png")!.GetBytes().ToArray());
    }

    [Fact]
    public void Office_convert_writes_a_docx_from_a_doc_file()
    {
        string dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "docconv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string source = Path.Combine(dir, "in.doc");
            File.WriteAllBytes(source, new DocBuilder().Piece("Converted text\r").Build());
            Office.Convert(source, Path.Combine(dir, "out.docx"));
            Assert.Equal("Converted text\n", Office.ExtractText(Path.Combine(dir, "out.docx")));
            Assert.Equal("Converted text\n", Office.ExtractText(source));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Random_damage_never_escapes_as_an_untyped_exception()
    {
        byte[] good = new DocBuilder().Piece("Some text here\r").Build();
        var rng = new Random(12345);
        for (int i = 0; i < 200; i++)
        {
            byte[] bad = Mutate(good, "/WordDocument", w =>
            {
                for (int k = 0; k < 6; k++)
                {
                    w[rng.Next(0, Math.Min(w.Length, 1024))] = (byte)rng.Next(256);
                }

                return w;
            });
            try
            {
                using Document d = Open(bad);
                _ = d.ExtractText();
            }
            catch (OfficeException)
            {
            }
        }
    }

    private static int CountOf(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }

    // Reads the footnotes part of a converted document.
    private sealed class IPackagePartAlias
    {
        internal IPackagePartAlias(Document d)
        {
            Footnotes = Encoding.UTF8.GetString(d.Package.FindPart("/word/footnotes.xml")!.GetBytes().Span);
        }

        internal string Footnotes { get; }
    }
}
