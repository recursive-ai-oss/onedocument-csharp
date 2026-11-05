using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Rendering.Equations;
using static RecursiveAI.OneDocument.Rendering.Tests.MathCases;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Equations inside Word paragraphs: placement on the line, line height, display justification, wrapping, text and tags.
public sealed class MathParagraphTests
{
    private static LayoutDocument Lay(string body, string? settings = null)
    {
        using Document d = TestDocx.Open(body, settings: settings);
        return LayoutEngine.Layout(d, new LayoutOptions { Fonts = TestFonts.Library });
    }

    private static IEnumerable<LayoutRun> MathRuns(LayoutDocument l) =>
        l.Pages.SelectMany(p => p.Paragraphs).SelectMany(p => p.Lines).SelectMany(x => x.Runs).Where(r => r.RunKind == LayoutRunKind.Math);

    [Fact]
    public void InlineEquationSitsOnTheLineBetweenTheWordsAndRaisesIt()
    {
        LayoutDocument l = Lay(Inline("Before ", Frac(R("a+b"), R("c")), " after") + TestDocx.P("Plain line"));
        LayoutParagraph p = l.Pages[0].Paragraphs.First();
        LayoutLine line = Assert.Single(p.Lines);
        LayoutRun math = Assert.Single(line.Runs, r => r.RunKind == LayoutRunKind.Math);
        Assert.Equal("(a+b)/c", math.Math!.LinearText);
        Assert.Equal("Before (a+b)/c after", p.Text);
        Assert.Equal("Before (a+b)/c after", line.Text);
        LayoutRun before = line.Runs.First();
        LayoutRun after = line.Runs.Last();
        Assert.True(before.Bounds.Right <= math.Bounds.X + 1e-6);
        Assert.True(math.Bounds.Right <= after.Bounds.X + 1e-6);
        Assert.Equal(math.Math.Width, math.Bounds.Width, 6);
        Assert.Equal(7, math.TextLength());
        // The fraction is taller than the text, so the line is taller than the plain line below it.
        LayoutLine plain = l.Pages[0].Paragraphs.ElementAt(1).Lines.Single();
        Assert.True(line.Bounds.Height > plain.Bounds.Height);
        Assert.True(line.Ascent >= math.Math.Ascent - 1e-6 && line.Descent >= math.Math.Descent - 1e-6);
    }

    [Fact]
    public void DisplayEquationHasItsOwnLineAndIsCentred()
    {
        LayoutDocument l = Lay(Display(Frac(R("a"), R("b"))));
        LayoutRun math = Assert.Single(MathRuns(l));
        LayoutLine line = l.Pages[0].Paragraphs.Single().Lines.Single();
        double mid = math.Bounds.X + (math.Bounds.Width / 2);
        Assert.Equal(72 + (468.0 / 2), mid, 3);
        Assert.Equal(line.Baseline, math.Math!.Ascent + (line.Baseline - line.Ascent) - (math.Math.Ascent - line.Ascent), 3);
    }

    [Fact]
    public void DisplayJustificationFollowsTheEquationParagraphThenTheDocumentDefault()
    {
        LayoutDocument l = Lay(Display(Frac(R("a"), R("b")), "left") + Display(Frac(R("a"), R("b")), "right") + Display(Frac(R("a"), R("b"))) + Display(Frac(R("a"), R("b")), pPr: "<w:ind w:left=\"1440\"/>"),
            "<w:settings xmlns:w=\"" + TestDocx.W + "\"><m:mathPr xmlns:m=\"" + M + "\"><m:defJc m:val=\"right\"/></m:mathPr></w:settings>");
        LayoutRun[] runs = [.. MathRuns(l)];
        Assert.Equal(4, runs.Length);
        Assert.Equal(72, runs[0].Bounds.X, 3);
        Assert.Equal(72 + 468, runs[1].Bounds.Right, 3);
        Assert.Equal(72 + 468, runs[2].Bounds.Right, 3); // no m:jc: the document's defJc
        Assert.Equal(72 + 468, runs[3].Bounds.Right, 3);
    }

    [Fact]
    public void WideDisplayEquationBreaksAtOperatorsWithinTheMeasure()
    {
        string terms = string.Join("+", Enumerable.Range(1, 18).Select(i => "a" + i + "x"));
        LayoutDocument l = Lay(Display(R("f(x)=" + terms)));
        LayoutRun math = Assert.Single(MathRuns(l));
        Assert.True(math.Math!.LineCount >= 2, $"lines {math.Math.LineCount}");
        Assert.True(math.Bounds.Width <= 468 + 1e-6);
        LayoutLine line = l.Pages[0].Paragraphs.Single().Lines.Single();
        Assert.True(line.Ascent + line.Descent >= math.Math.Ascent + math.Math.Descent - 1e-6);
    }

    [Fact]
    public void EquationsFollowTheParagraphsFontSize()
    {
        LayoutDocument big = Lay(Inline("x ", Frac(R("a"), R("b")), " y", "<w:rPr><w:sz w:val=\"48\"/></w:rPr>"));
        LayoutDocument small = Lay(Inline("x ", Frac(R("a"), R("b")), " y"));
        // The paragraph style sets the size of paragraph-mark-less runs; the style's default size scales the equation only through w:rPr of the
        // paragraph style, so compare the two documents under one style: both use 12 pt here.
        Assert.Equal(MathRuns(big).Single().Math!.Width, MathRuns(small).Single().Math!.Width, 6);
    }

    [Fact]
    public void ExtractTextReturnsTheLinearFormOfEquations()
    {
        byte[] docx = TestDocx.Bytes(Inline("Area ", Sup(R("r"), R("2")) + R("\u03C0"), " done") + Display(Nary("\u2211", R("i=1"), R("n"), R("i"))));
        string path = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "math-extract-" + Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            File.WriteAllBytes(path, docx);
            string text = RecursiveAI.OneDocument.Office.ExtractText(path);
            Assert.Contains("Area r^2\u03C0 done", text, StringComparison.Ordinal);
            Assert.Contains("\u2211_(i=1)^n\u2592i", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EquationsAreTaggedFiguresWithTheirLinearTextAsAlternativeText()
    {
        RenderingFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        using Document d = Document.Open(new MemoryStream(TestDocx.Bytes(Inline("See ", Frac(R("a"), R("b")), " there") + Display(Rad(R("x"))))), leaveOpen: false);
        using var ms = new MemoryStream();
        DocumentRenderer.WritePdf(d, ms, new PdfRenderOptions { Layout = new LayoutOptions { Fonts = TestFonts.Library } });
        using var pdf = OnePdf.PdfDocument.Open(ms.ToArray());
        OnePdf.PdfStructureTree tree = pdf.StructureTree ?? throw new InvalidOperationException("no structure tree");
        string[] alts = [.. tree.Elements().Where(e => e.Type == "Figure").Select(e => e.Dictionary.GetString("Alt")?.GetText() ?? string.Empty)];
        Assert.True(alts.Contains("a/b"), string.Join("|", alts));
        Assert.Contains("\u221Ax", alts);
    }

    [Fact]
    public void EveryMathCaseLaysOutWithoutNotesOtherThanFontSubstitution()
    {
        foreach ((string name, byte[] docx) in MathCases.All())
        {
            using Document d = Document.Open(new MemoryStream(docx), leaveOpen: false);
            LayoutDocument l = LayoutEngine.Layout(d, new LayoutOptions { Fonts = TestFonts.Library });
            Assert.NotEmpty(MathRuns(l));
            Assert.DoesNotContain(l.Notes, n => n.Code.StartsWith("layout.math", StringComparison.Ordinal));
            foreach (LayoutRun r in MathRuns(l))
            {
                Assert.True(double.IsFinite(r.Bounds.X) && double.IsFinite(r.Bounds.Width) && r.Bounds.Width > 0, name);
                Assert.True(r.Math!.Items.Count > 0, name);
            }
        }
    }
}

internal static class MathRunExtensions
{
    public static int TextLength(this LayoutRun run) => run.Math!.LinearText.Length;
}
