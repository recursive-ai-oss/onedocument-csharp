using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class RtfTests
{
    private const string Head =
        "{\\rtf1\\ansi\\ansicpg1252\\deff0\\deflang1033{\\fonttbl{\\f0\\froman\\fcharset0 Times New Roman;}{\\f1\\fswiss\\fcharset0 Arial;}{\\f2\\fnil\\fcharset2 Symbol;}}" +
        "{\\colortbl;\\red255\\green0\\blue0;\\red0\\green128\\blue0;\\red255\\green255\\blue0;}" +
        "{\\stylesheet{\\ql \\li0\\ri0 \\fs24 \\snext0 Normal;}{\\s1\\ql \\sb240\\sa60\\keepn \\b\\f1\\fs32 \\sbasedon0 \\snext0 heading 1;}{\\*\\cs10 \\additive Default Paragraph Font;}{\\*\\cs11 \\additive \\b\\sbasedon10 Strong;}}";

    private static Document Open(string rtf, OpenContext? ctx = null) =>
        Document.Open(new MemoryStream(Encoding.Latin1.GetBytes(rtf)), ctx);

    private static string Xml(Document d) => d.MainMarkup.Root.OuterXml;

    private static string Text(Document d) => d.ExtractText();

    [Fact]
    public void ParagraphsAndRunsKeepTheirFormatting()
    {
        using Document d = Open(Head + "\\pard\\plain\\s1 Title\\par \\pard\\plain Plain {\\b bold \\i bolditalic}{\\cf1\\ul  red}\\par}");
        Assert.Equal(OfficeFormat.Rtf, d.Format);
        string xml = Xml(d);
        Assert.Contains("<w:pStyle w:val=\"Heading1\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:b/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:i/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:color w:val=\"FF0000\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:u w:val=\"single\"/>", xml, StringComparison.Ordinal);
        Assert.Equal("Title\nPlain bold bolditalic red\n", Text(d).Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal(2, d.Paragraphs.Count());
    }

    [Fact]
    public void StyleSheetBecomesStyles()
    {
        using Document d = Open(Head + "\\pard\\plain\\s1 T\\par}");
        MarkupAndStyles(d, out string styles);
        Assert.Contains("w:styleId=\"Heading1\"", styles, StringComparison.Ordinal);
        Assert.Contains("<w:basedOn w:val=\"Normal\"/>", styles, StringComparison.Ordinal);
        Assert.Contains("w:default=\"1\" w:styleId=\"Normal\"", styles, StringComparison.Ordinal);
        Assert.Contains("w:type=\"character\"", styles, StringComparison.Ordinal);
        Assert.Contains("<w:keepNext/>", styles, StringComparison.Ordinal);
    }

    private static void MarkupAndStyles(Document d, out string styles)
    {
        var part = d.Package.FindPart("/word/styles.xml");
        Assert.NotNull(part);
        styles = Encoding.UTF8.GetString(part.GetBytes().Span);
    }

    [Fact]
    public void CodePagesHexEscapesAndUnicodeAreDecoded()
    {
        using Document d = Open(Head + "\\pard caf\\'e9 \\u8364?\\u26085?\\uc2\\u27700\\'41\\'42 \\uc1\\emdash\\~x\\par}");
        Assert.Equal("caf\u00e9 \u20ac\u65e5\u6c34 \u2014\u00a0x", Text(d).TrimEnd('\r', '\n'));
    }

    [Fact]
    public void DoubleByteFontsDecodeWithTheirCodePage()
    {
        const string rtf = "{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\fnil\\fcharset0 Arial;}{\\f1\\fnil\\fcharset128 MS Mincho;}}\\pard {\\f1\\'93\\'fa\\'96\\'7b}\\par}";
        using Document d = Open(rtf);
        Assert.Equal("\u65e5\u672c", Text(d).TrimEnd('\r', '\n'));
    }

    [Fact]
    public void TablesBecomeTablesWithGridAndMerges()
    {
        string row1 = "\\trowd\\trgaph108\\trleft-108\\clbrdrb\\brdrs\\brdrw10\\cellx3000\\clvmgf\\cellx6000\\pard\\intbl A\\cell B\\cell\\row";
        string row2 = "\\trowd\\trgaph108\\trleft-108\\cellx3000\\clvmrg\\cellx6000\\pard\\intbl C\\cell \\cell\\row";
        using Document d = Open(Head + row1 + row2 + "\\pard after\\par}");
        Assert.Single(d.Tables);
        string xml = Xml(d);
        Assert.Contains("<w:vMerge w:val=\"restart\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:vMerge/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:tblInd w:w=\"0\"", xml, StringComparison.Ordinal);
        Assert.Equal("A\tB\nC\t\nafter\n", Text(d).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedTablesNest()
    {
        string outer = "\\trowd\\trgaph108\\cellx8000";
        string nested = "\\pard\\intbl\\itap2 in1\\nestcell{\\*\\nesttableprops\\trowd\\trgaph108\\cellx4000\\nestrow}{\\nonesttables\\par}";
        using Document d = Open(Head + outer + "\\pard\\intbl outer text\\par " + nested + "\\pard\\intbl last\\cell\\row\\pard x\\par}");
        string xml = Xml(d);
        int outerAt = xml.IndexOf("<w:tbl>", StringComparison.Ordinal);
        Assert.True(outerAt >= 0);
        Assert.True(xml.IndexOf("<w:tbl>", outerAt + 1, StringComparison.Ordinal) > outerAt, xml);
    }

    [Fact]
    public void ListsComeFromTheListTable()
    {
        const string lists = "{\\*\\listtable{\\list\\listtemplateid1\\listsimple1{\\listlevel\\levelnfc0\\leveljc0\\levelfollow0\\levelstartat3{\\leveltext\\'02\\'00.;}{\\levelnumbers\\'01;}\\fi-360\\li720}\\listid7}" +
            "{\\list\\listtemplateid2\\listsimple1{\\listlevel\\levelnfc23\\leveljc0\\levelfollow0\\levelstartat1{\\leveltext\\'01\\u-3913 ?;}{\\levelnumbers;}\\f2\\fi-360\\li720}\\listid8}}" +
            "{\\*\\listoverridetable{\\listoverride\\listid7\\listoverridecount0\\ls1}{\\listoverride\\listid8\\listoverridecount0\\ls2}}";
        using Document d = Open(Head + lists + "\\pard\\ls1\\ilvl0\\fi-360\\li720 one\\par\\pard\\ls2\\ilvl0 dot\\par}");
        string xml = Xml(d);
        Assert.Contains("<w:numId w:val=\"1\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:numId w:val=\"2\"/>", xml, StringComparison.Ordinal);
        string numbering = Encoding.UTF8.GetString(d.Package.FindPart("/word/numbering.xml")!.GetBytes().Span);
        Assert.Contains("<w:lvlText w:val=\"%1.\"/>", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:start w:val=\"3\"/>", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:numFmt w:val=\"bullet\"/>", numbering, StringComparison.Ordinal);
        Assert.Contains("\uF0B7", numbering, StringComparison.Ordinal);
        Assert.Contains("w:hanging=\"360\"", numbering, StringComparison.Ordinal);
    }

    [Fact]
    public void FieldsBookmarksNotesAndCommentsAreKept()
    {
        string body = "\\pard Go {\\field{\\*\\fldinst {HYPERLINK \"http://example.com/\"}}{\\fldrslt {\\ul\\cf1 there}}}" +
            "{\\*\\bkmkstart mark1}marked{\\*\\bkmkend mark1} text{\\super\\chftn {\\footnote \\pard\\plain {\\super\\chftn } The note.\\par}}" +
            "{\\*\\atrfstart 5}anno{\\*\\atrfend 5}{\\*\\atnid AB}{\\*\\atnauthor Ann}\\chatn{\\*\\annotation{\\*\\atnref 5}{\\*\\atndate 1234567890}\\pard\\plain A comment.\\par}\\par}";
        using Document d = Open(Head + body);
        string xml = Xml(d);
        Assert.Contains("<w:instrText xml:space=\"preserve\">HYPERLINK &quot;http://example.com/&quot;</w:instrText>", xml, StringComparison.Ordinal);
        Assert.Contains("w:fldCharType=\"separate\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:bookmarkStart w:id=\"1\" w:name=\"mark1\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:bookmarkEnd w:id=\"1\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:footnoteReference w:id=\"1\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:commentReference w:id=\"1\"/>", xml, StringComparison.Ordinal);
        string notes = Encoding.UTF8.GetString(d.Package.FindPart("/word/footnotes.xml")!.GetBytes().Span);
        Assert.Contains("The note.", notes, StringComparison.Ordinal);
        Assert.Contains("<w:footnoteRef/>", notes, StringComparison.Ordinal);
        string comments = Encoding.UTF8.GetString(d.Package.FindPart("/word/comments.xml")!.GetBytes().Span);
        Assert.Contains("w:author=\"Ann\"", comments, StringComparison.Ordinal);
        Assert.Contains("w:initials=\"AB\"", comments, StringComparison.Ordinal);
        Assert.Contains("A comment.", comments, StringComparison.Ordinal);
        Assert.Contains("<w:commentRangeStart w:id=\"1\"/>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void HeadersFootersAndSectionsAreReadPerSection()
    {
        string rtf = Head + "\\sectd\\pgwsxn12240\\pghsxn15840\\margl1440\\margr1440\\margt1440\\margb1440{\\header \\pard\\plain Head one\\par}{\\footer \\pard\\plain Foot\\par}" +
            "\\pard First section\\par\\sect\\sectd\\sbknone\\lndscpsxn\\pgwsxn15840\\pghsxn12240\\titlepg\\pard Second\\par}";
        using Document d = Open(rtf);
        string xml = Xml(d);
        Assert.Equal(2, CountOf(xml, "<w:sectPr>"));
        Assert.Contains("<w:headerReference w:type=\"default\" r:id=", xml, StringComparison.Ordinal);
        Assert.Contains("w:orient=\"landscape\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:type w:val=\"continuous\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:titlePg/>", xml, StringComparison.Ordinal);
        string header = Encoding.UTF8.GetString(d.Package.FindPart("/word/header1.xml")!.GetBytes().Span);
        Assert.Contains("Head one", header, StringComparison.Ordinal);
        Assert.NotNull(d.Package.FindPart("/word/footer1.xml"));
        // The relationship ids written into the body resolve in the document's relationships.
        foreach (var r in d.Package.GetRelationships(d.MainPartName))
        {
            Assert.NotNull(d.Package.FindPart("/word/" + r.Target));
        }

        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(xml, "r:id=\"(rId[0-9]+)\""))
        {
            Assert.Contains(d.Package.GetRelationships(d.MainPartName), r => r.Id == m.Groups[1].Value);
        }

        Assert.Contains(d.Package.GetRelationships(d.MainPartName), r => r.Target == "footer1.xml");
    }

    private static int CountOf(string s, string what)
    {
        int n = 0;
        for (int i = s.IndexOf(what, StringComparison.Ordinal); i >= 0; i = s.IndexOf(what, i + 1, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }

    [Fact]
    public void TrackedChangesBecomeRevisions()
    {
        string rtf = Head + "{\\*\\revtbl{Unknown;}{Ann;}}\\pard keep {\\revised\\revauth1\\revdttm1234567890 added}{\\deleted\\revauthdel1\\revdttmdel1234567890  gone}\\par}";
        using Document d = Open(rtf);
        string xml = Xml(d);
        Assert.Contains("<w:ins ", xml, StringComparison.Ordinal);
        Assert.Contains("w:author=\"Ann\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:del ", xml, StringComparison.Ordinal);
        Assert.Contains("<w:delText xml:space=\"preserve\"> gone</w:delText>", xml, StringComparison.Ordinal);
        Assert.Contains("keep added", Text(d), StringComparison.Ordinal);
        Assert.DoesNotContain("gone", Text(d), StringComparison.Ordinal);
    }

    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    [Fact]
    public void PicturesBecomeMediaParts()
    {
        string hex = Convert.ToHexString(Png).ToLowerInvariant();
        string rtf = Head + "\\pard {\\pict\\pngblip\\picw1\\pich1\\picwgoal1440\\pichgoal720 " + hex + "}{\\*\\shppict{\\pict\\pngblip\\picw1\\pich1 " + hex + "}}{\\nonshppict{\\pict\\pngblip\\picw1\\pich1 " + hex + "}}\\par}";
        using Document d = Open(rtf);
        string xml = Xml(d);
        Assert.Equal(2, CountOf(xml, "<w:drawing>"));
        Assert.Contains("<wp:extent cx=\"914400\" cy=\"457200\"/>", xml, StringComparison.Ordinal);
        var media = d.Package.Parts.Where(p => p.Name.StartsWith("/word/media/", StringComparison.Ordinal)).ToList();
        Assert.Single(media); // identical pictures share one part
        Assert.Equal(Png, media[0].GetBytes().ToArray());
        Assert.Equal("image/png", media[0].ContentType);
    }

    [Fact]
    public void UnknownDestinationsAreReportedAndReadingIsTyped()
    {
        using Document d = Open(Head + "{\\*\\madeup\\foo bar}\\pard text\\par}");
        Assert.NotNull(d.ImportLoss);
        Assert.Equal(1, d.ImportLoss!.CountOf("loss.rtf-unknown-destination"));
    }

    [Fact]
    public void UnclosedGroupsAreADefectNotAnException()
    {
        var defects = new DefectCollection();
        using Document d = Open(Head.TrimEnd('}') + "}}{\\b text", new OpenContext(defects: defects));
        Assert.Contains(defects, x => x.Code == "rtf.unclosed-groups" && x.Kind == DefectKind.Record);
        Assert.Contains("text", Text(d), StringComparison.Ordinal);
    }

    [Fact]
    public void NotRtfRaisesATypedError()
    {
        // The sniffer only calls it RTF when it starts with {\rtf; the reader refuses anything it cannot start.
        Assert.Throws<OfficeFormatException>(() => Document.Open(new MemoryStream(Encoding.ASCII.GetBytes("plain text"))));
    }

    [Fact]
    public void RtfOpenedAndSavedWithoutEditsWritesTheOriginalBytes()
    {
        byte[] original = Encoding.Latin1.GetBytes(Head + "\\pard Hello {\\*\\madeup x}\\par}\0\0");
        using Document d = Document.Open(new MemoryStream(original));
        using var output = new MemoryStream();
        d.SaveAs(output, OfficeFormat.Rtf);
        Assert.Equal(original, output.ToArray());
    }

    [Fact]
    public void EditedRtfIsWrittenAgainAndReadsBack()
    {
        string rtf = Head + "\\pard\\plain\\s1 Caf\\'e9 \\u8364? and \\u26085?\\par \\pard\\plain\\qc\\li720 Plain {\\b bold}{\\i\\fs40  big} \\u26085?\\par}";
        using Document d = Open(rtf);
        d.Paragraphs.First().SetText("Changed \u20ac");
        using var output = new MemoryStream();
        d.SaveAs(output, OfficeFormat.Rtf);
        string written = Encoding.Latin1.GetString(output.ToArray());
        Assert.StartsWith("{\\rtf1\\ansi\\ansicpg1252", written, StringComparison.Ordinal);
        Assert.True(written.All(c => c < 128), "RTF output is 7-bit ASCII");
        Assert.Matches(@"\\pard\\plain\\s[1-9]", written);
        output.Position = 0;
        using Document back = Document.Open(output);
        string a = d.ExtractText().Replace("\r\n", "\n", StringComparison.Ordinal);
        string b = back.ExtractText().Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(a, b);
        Assert.Contains("Changed", b, StringComparison.Ordinal);
        Assert.Contains("\u20ac", b, StringComparison.Ordinal);
        Assert.Contains("\u65e5", b, StringComparison.Ordinal);
        Assert.NotNull(d.ExportLoss);
    }

    [Fact]
    public void OfficeStaticsWorkOnRtf()
    {
        DocumentFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        string dir = Path.Combine(Path.GetTempPath(), "rtf-statics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string src = Path.Combine(dir, "a.rtf");
            File.WriteAllBytes(src, Encoding.Latin1.GetBytes(Head + "\\pard Hello \\b world\\par}"));
            Assert.Equal("Hello world", Office.ExtractText(src).Trim());
            string docx = Path.Combine(dir, "a.docx");
            Office.Convert(src, docx);
            using (Document dd = Document.Open(docx))
            {
                Assert.Equal(OfficeFormat.Docx, dd.Format);
                Assert.Equal("Hello world", dd.ExtractText().Trim());
            }

            string back = Path.Combine(dir, "b.rtf");
            Office.Convert(docx, back);
            Assert.Equal("Hello world", Office.ExtractText(back).Trim());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void DocxExportsFieldsListsTablesAndNotesToRtfAndBack()
    {
        string rtf = Head + "\\trowd\\trgaph108\\cellx3000\\cellx6000\\pard\\intbl A\\cell B\\cell\\row" +
            "\\pard Go {\\field{\\*\\fldinst {HYPERLINK \"http://example.com/\"}}{\\fldrslt {\\ul there}}} x{\\super\\chftn {\\footnote \\pard\\plain {\\super\\chftn } The note.\\par}}\\par}";
        using Document d = Open(rtf);
        using var docx = new MemoryStream();
        d.SaveAs(docx, OfficeFormat.Docx);
        docx.Position = 0;
        using Document asDocx = Document.Open(docx);
        using var rtf2 = new MemoryStream();
        asDocx.SaveAs(rtf2, OfficeFormat.Rtf);
        rtf2.Position = 0;
        using Document again = Document.Open(rtf2);
        Assert.Single(again.Tables);
        string t = again.ExtractText().Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("A\tB", t, StringComparison.Ordinal);
        Assert.Contains("Go there x", t, StringComparison.Ordinal);
        Assert.Contains("The note.", t, StringComparison.Ordinal);
        Assert.Contains("HYPERLINK", Xml(again), StringComparison.Ordinal);
    }

    [Fact]
    public void DamagedInputNeverThrowsAnUntypedException()
    {
        byte[] seed = Encoding.Latin1.GetBytes(Head + "\\sectd{\\header h\\par}\\trowd\\cellx1000\\pard\\intbl a\\cell\\row\\pard {\\field{\\*\\fldinst X}{\\fldrslt y}} \\u1234?{\\pict\\pngblip 89504e47}\\par}");
        var rng = new Random(7);
        for (int i = 0; i < 400; i++)
        {
            byte[] d = (byte[])seed.Clone();
            for (int k = rng.Next(1, 6); k > 0; k--)
            {
                int at = rng.Next(12, d.Length);
                switch (rng.Next(3))
                {
                    case 0: d[at] = (byte)rng.Next(256); break;
                    case 1: d[at] = (byte)"{}\\;'"[rng.Next(5)]; break;
                    default: d = [.. d.AsSpan(0, at), .. d.AsSpan(Math.Min(at + rng.Next(1, 8), d.Length))]; break;
                }
            }

            try
            {
                using Document doc = Document.Open(new MemoryStream(d));
                _ = doc.ExtractText();
                using var o = new MemoryStream();
                doc.SaveAs(o, OfficeFormat.Docx);
                if (doc.Paragraphs.Any())
                {
                    doc.Paragraphs.First().Runs();
                }
            }
            catch (OfficeException)
            {
            }
        }
    }

    [Fact]
    public void MathZonesBecomeOfficeMathMarkup()
    {
        const string math = "\\pard Inline {\\mmath{\\*\\moMath\\f0{\\mf{\\mfPr{\\mctrlPr}{\\mtype bar}}{\\mnum{\\mr\\mscr0\\msty2 a}}{\\mden{\\mr b+1}}}{\\msSup{\\msSupPr{\\mctrlPr}}{\\me x}{\\msup 2}}}} end\\par";
        using Document d = Open(Head + math + "}");
        string xml = Xml(d);
        Assert.Contains("<m:f><m:fPr><m:ctrlPr/><m:type m:val=\"bar\"/></m:fPr><m:num><m:r><m:rPr><m:scr m:val=\"0\"/><m:sty m:val=\"2\"/></m:rPr><m:t xml:space=\"preserve\">a</m:t></m:r></m:num>", xml, StringComparison.Ordinal);
        Assert.Contains("<m:sSup>", xml, StringComparison.Ordinal);
        Assert.Contains("<m:sup><m:r><m:t xml:space=\"preserve\">2</m:t></m:r></m:sup>", xml, StringComparison.Ordinal);
        Assert.Contains("Inline a/(b+1)x^2 end", Text(d).Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void CroppedPicturesKeepTheirCropAndShownSize()
    {
        string hex = Convert.ToHexString(Png).ToLowerInvariant();
        using Document d = Open(Head + "\\pard {\\pict\\picscalex400\\picscaley400\\piccropl240\\piccropr240\\piccropt180\\piccropb180\\picw64\\pich48\\picwgoal960\\pichgoal720\\pngblip " + hex + "}\\par}");
        string xml = Xml(d);
        Assert.Contains("<a:srcRect l=\"25000\" t=\"25000\" r=\"25000\" b=\"25000\"/>", xml, StringComparison.Ordinal);
        // (960 - 480) twips * 400 % = 1920 twips = 1219200 EMU wide
        Assert.Contains("<wp:extent cx=\"1219200\" cy=\"914400\"/>", xml, StringComparison.Ordinal);
    }
}
