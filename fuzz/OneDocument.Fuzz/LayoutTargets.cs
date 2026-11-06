using System;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Rendering;
using RecursiveAI.OneDocument.Rendering.Tests;
using RecursiveAI.OneDocument.Rendering.Presentations;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// Page layout and painting over a hostile part of an otherwise sound file. The first input byte picks which part of a generated document or deck is
/// replaced by the rest of the input; the file is opened, laid out with a small page ceiling, and its first pages painted into display lists that
/// must validate. <c>docxlayout</c> replaces the document, style, numbering or settings part, <c>pptxlayout</c> a slide, layout, master, theme or
/// presentation part (and writes the PDF, uncompressed, so the paint is consumed). Only <see cref="OfficeException"/> may escape.
/// </summary>
internal static class LayoutTargets
{
    private static readonly OfficeLimits Limits = new() { MaxXmlDepth = 64, MaxPartSize = 1 << 20, MaxRecursionDepth = 32, MaxAllocationBytes = 1 << 26, MaxTotalUncompressedSize = 16L << 20 };

    private static readonly Lazy<FontLibrary> Fonts = new(FontLibrary.CreateDefault);

    private const string Numbering =
        "<w:numbering xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"hybridMultilevel\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num></w:numbering>";

    private const string Settings =
        "<w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:defaultTabStop w:val=\"720\"/><w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>";

    private const string Body =
        "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>Title</w:t></w:r></w:p><w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>Item one with some words to wrap around the line</w:t></w:r></w:p>" +
        "<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>A</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p><w:r><w:t>Page </w:t></w:r><w:fldSimple w:instr=\" PAGE \"><w:r><w:t>1</w:t></w:r></w:fldSimple></w:p>";

    public static void RunDocx(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
        {
            return;
        }

        string[] names = ["/word/document.xml", "/word/styles.xml", "/word/numbering.xml", "/word/settings.xml"];
        string name = names[data[0] % names.Length];
        try
        {
            OpcPackage p = Replace(TestDocx.Bytes(Body, TestDocx.DefaultStyles, Numbering, Settings), name, data[1..]);
            using var doc = Document.FromPackage(p, new OpenContext(Limits), ownsPackage: true);
            LayoutDocument layout = LayoutEngine.Layout(doc, new LayoutOptions { Fonts = Fonts.Value, MaxPages = 12, UpdateFields = (data[0] & 0x80) != 0, Clock = new FixedClock() });
            for (int i = 0; i < Math.Min(4, layout.Pages.Count); i++)
            {
                LayoutPainter.Paint(layout.Pages[i], new PaintOptions { Tagged = true }).Validate();
            }
        }
        catch (OfficeException)
        {
        }
    }

    public static void RunPptx(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
        {
            return;
        }

        string[] names = ["/ppt/slides/slide1.xml", "/ppt/slideLayouts/slideLayout1.xml", "/ppt/slideMasters/slideMaster1.xml", "/ppt/theme/theme1.xml", "/ppt/presentation.xml"];
        string name = names[data[0] % names.Length];
        try
        {
            var deck = SampleDeck();
            OpcPackage p = Replace(TestPptx.Build(deck), name, data[1..]);
            using var pres = Presentation.FromPackage(p, new OpenContext(Limits), ownsPackage: true);
            using var pdf = new MemoryStream();
            _ = PresentationRenderer.WritePdf(pres, pdf, new PresentationRenderOptions { Fonts = Fonts.Value, Compress = false, Clock = new FixedClock() });
        }
        catch (OfficeException)
        {
        }
    }

    // The deck builder's optional fields are set explicitly: the file is shared with the tests, which use them.
    internal static TestPptx.Deck SampleDeck(string text = "Hello slide text that wraps")
    {
        var deck = new TestPptx.Deck { MasterShapes = null, LayoutBackground = null, NotesMaster = false };
        deck.Slides.Add(new TestPptx.SlideSpec { Shapes = TestPptx.Box(2, 40, 40, 300, 120, TestPptx.P(text)) + TestPptx.Rect(3, 100, 200, 120, 80, "336699"), Background = null, Hidden = false, Notes = null });
        return deck;
    }

    private static OpcPackage Replace(byte[] file, string part, ReadOnlySpan<byte> content)
    {
        OpcPackage p = OpcPackage.Open(new MemoryStream(file), new OpenContext(Limits), leaveOpen: false);
        p.FindPart(part)?.SetBytes(content.ToArray());
        return p;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 3, 9, 14, 5, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public static int WriteDocxSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        Add(dir, "document", 0, "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" + Body + TestDocx.LetterSection + "</w:body></w:document>");
        Add(dir, "styles", 1, TestDocx.DefaultStyles);
        Add(dir, "numbering", 2, Numbering);
        Add(dir, "settings", 3, Settings);
        return 0;
    }

    public static int WritePptxSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        var deck = SampleDeck();
        using OpcPackage p = OpcPackage.Open(new MemoryStream(TestPptx.Build(deck)), leaveOpen: false);
        string[] names = ["/ppt/slides/slide1.xml", "/ppt/slideLayouts/slideLayout1.xml", "/ppt/slideMasters/slideMaster1.xml", "/ppt/theme/theme1.xml", "/ppt/presentation.xml"];
        for (int i = 0; i < names.Length; i++)
        {
            if (p.FindPart(names[i]) is { } part)
            {
                File.WriteAllBytes(Path.Combine(dir, "seed" + i), [(byte)i, .. part.GetBytes().ToArray()]);
            }
        }

        return 0;
    }

    private static void Add(string dir, string name, int mode, string xml) =>
        File.WriteAllBytes(Path.Combine(dir, name), [(byte)mode, .. Encoding.UTF8.GetBytes(xml)]);
}
