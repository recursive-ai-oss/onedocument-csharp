using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.IO;
using RecursiveAI.OneOffice.TextExtraction;
using static RecursiveAI.OneOffice.Tests.Text.OoxmlSamples;

namespace RecursiveAI.OneOffice.Tests.Text;

public class SheetAndSlideTextTests
{
    [Fact]
    public void Workbook_string_cells_come_out_in_sheet_row_and_cell_order()
    {
        string sst = $"<?xml version=\"1.0\"?><sst {S} count=\"4\" uniqueCount=\"4\"><si><t>Name</t></si><si><r><t>Rich </t></r><r><t>text</t></r><rPh sb=\"0\" eb=\"1\"><t>PHONETIC</t></rPh></si><si><t>a &amp; b</t></si><si/></sst>";
        string one = $"<?xml version=\"1.0\"?><worksheet {S}><sheetData>" +
            "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\"><v>42</v></c><c r=\"C1\" t=\"s\"><v>1</v></c></row>" +
            "<row r=\"2\"><c r=\"A2\"><v>1</v></c></row>" +
            "<row r=\"3\"><c r=\"A3\" t=\"inlineStr\"><is><t>inline</t><rPh><t>X</t></rPh></is></c><c r=\"B3\" t=\"str\"><f>A1</f><v>Name</v></c><c r=\"C3\" t=\"s\"><v>2</v></c><c r=\"D3\" t=\"s\"/><c r=\"E3\" t=\"e\"><v>#N/A</v></c></row>" +
            "<row r=\"4\"><c r=\"A4\" t=\"s\"><v>3</v></c></row></sheetData></worksheet>";
        string two = $"<?xml version=\"1.0\"?><worksheet {S}><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><r><t>second</t></r><r><t> sheet</t></r></is></c></row></sheetData></worksheet>";
        byte[] book = Xlsx(sst, ("First", one), ("Second", two));
        Assert.Equal("Name\tRich text\ninline\tName\ta & b\n\nsecond sheet\n", WordTextTests.Extract(book));
    }

    [Fact]
    public void A_shared_string_index_past_the_table_is_a_located_defect()
    {
        string sst = $"<sst {S}><si><t>only</t></si></sst>";
        string sheet = $"<worksheet {S}><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>9</v></c></row></sheetData></worksheet>";
        var defects = new DefectCollection();
        Assert.Equal("only\n", WordTextTests.Extract(Xlsx(sst, ("S", sheet)), context: new OpenContext(defects: defects)));
        Defect d = Assert.Single(defects);
        Assert.Equal("text.shared-string-out-of-range", d.Code);
        Assert.Equal("/xl/worksheets/S.xml", d.Location.Container);
        Assert.Equal(sheet.IndexOf("<c r=\"B1\"", StringComparison.Ordinal), d.Location.Offset);
    }

    [Fact]
    public void Slides_come_out_in_order_with_tables_and_notes()
    {
        string slide1 = $"<?xml version=\"1.0\"?><p:sld {P}><p:cSld><p:spTree>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Title\"/><p:cNvSpPr/><p:nvPr><p:ph type=\"title\"/></p:nvPr></p:nvSpPr><p:txBody><a:bodyPr/><a:p><a:r><a:t>Title</a:t></a:r></a:p></p:txBody></p:sp>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Body\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:txBody><a:p><a:r><a:t>one</a:t></a:r><a:br/><a:r><a:t>two</a:t></a:r></a:p><a:p/><a:p><a:fld type=\"datetime\"><a:t>1/1</a:t></a:fld></a:p></p:txBody></p:sp>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"4\" name=\"Num\"/><p:cNvSpPr/><p:nvPr><p:ph type=\"sldNum\"/></p:nvPr></p:nvSpPr><p:txBody><a:p><a:fld type=\"slidenum\"><a:t>‹#›</a:t></a:fld></a:p></p:txBody></p:sp>" +
            "<p:graphicFrame><a:graphic><a:graphicData><a:tbl><a:tr><a:tc><a:txBody><a:p><a:r><a:t>h1</a:t></a:r></a:p></a:txBody></a:tc><a:tc><a:txBody><a:p><a:r><a:t>h2</a:t></a:r></a:p></a:txBody></a:tc></a:tr>" +
            "<a:tr><a:tc><a:txBody><a:p><a:r><a:t>c1</a:t></a:r></a:p></a:txBody></a:tc><a:tc><a:txBody><a:p/></a:txBody></a:tc></a:tr></a:tbl></a:graphicData></a:graphic></p:graphicFrame>" +
            "<mc:AlternateContent><mc:Choice Requires=\"a14\"><p:sp><p:txBody><a:p><a:r><a:t>chosen</a:t></a:r></a:p></p:txBody></p:sp></mc:Choice><mc:Fallback><p:pic/><p:sp><p:txBody><a:p><a:r><a:t>fallback</a:t></a:r></a:p></p:txBody></p:sp></mc:Fallback></mc:AlternateContent>" +
            "</p:spTree></p:cSld></p:sld>";
        string slide2 = $"<p:sld {P}><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>second slide</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>";
        string notes = $"<p:notes {P}><p:cSld><p:spTree><p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"img\"/><p:cNvSpPr/><p:nvPr><p:ph type=\"sldImg\"/></p:nvPr></p:nvSpPr></p:sp><p:sp><p:txBody><a:p><a:r><a:t>speaker says</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:notes>";
        byte[] deck = Pptx(("slide1", slide1.Replace("<mc:AlternateContent>", "<mc:AlternateContent xmlns:a14=\"http://schemas.microsoft.com/office/drawing/2010/main\">", StringComparison.Ordinal), notes), ("slide2", slide2, null));
        Assert.Equal("Title\none\ntwo\n1/1\nh1\th2\nc1\t\nchosen\n\nspeaker says\n\nsecond slide\n", WordTextTests.Extract(deck));
        Assert.Equal("Title\none\ntwo\n1/1\nh1\th2\nc1\t\nchosen\n\nsecond slide\n", WordTextTests.Extract(deck, new TextExtractionOptions { IncludeNotes = false }));
    }

    [Fact]
    public void Normalized_cells_say_where_they_are()
    {
        string sst = $"<sst {S}><si><t>x</t></si></sst>";
        string sheet = $"<worksheet {S}><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row></sheetData></worksheet>";
        using IByteSource source = ByteSource.FromMemory(Xlsx(sst, ("S", sheet)));
        NormalizedParagraph p = Assert.Single(OoxmlTextExtractor.ExtractNormalized(source).Paragraphs);
        Assert.Equal(TextStory.Sheet, p.Story);
        Assert.Equal(sheet.IndexOf("<c r=", StringComparison.Ordinal), p.PartOffset);
        Assert.True(p.InTable);
    }
}
