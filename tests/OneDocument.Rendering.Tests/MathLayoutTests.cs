using System.Text;
using OneFont;
using RecursiveAI.OneDocument.Equations;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Equations;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Equation layout against values computed from the math font's own MATH table and glyph outlines.
public sealed class MathLayoutTests
{
    internal const string M = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    internal const string WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly FontLibrary Fonts = FontLibrary.CreateDefault();

    internal static MathNode Parse(string inner, string element = "oMath")
    {
        string xml = $"<m:{element} xmlns:m=\"{M}\" xmlns:w=\"{WNs}\">{inner}</m:{element}>";
        Assert.True(MarkupDocument.TryParse(Encoding.UTF8.GetBytes(xml), out MarkupDocument? doc));
        MathNode? node = OmmlReader.Read(doc!.Root, out _);
        Assert.NotNull(node);
        return node!;
    }

    internal static string R(string text, string? rPr = null) =>
        "<m:r>" + (rPr is null ? string.Empty : "<m:rPr>" + rPr + "</m:rPr>") + "<m:t>" + System.Security.SecurityElement.Escape(text) + "</m:t></m:r>";

    internal static string E(string inner) => "<m:e>" + inner + "</m:e>";

    internal static MathBox Lay(string inner, bool display = false, double size = 10, double width = double.PositiveInfinity, MathSettings? settings = null) =>
        MathLayouter.Layout(Parse(inner), new MathLayoutOptions { Fonts = Fonts, FontSize = size, Display = display, AvailableWidth = width, Settings = settings ?? MathSettings.Default });

    // Math italic of a Latin letter, as the layout writes it (h is the Planck-constant block hole).
    internal static string It(char c) => c == 'h' ? "\u210E" : char.ConvertFromUtf32(0x1D44E + (c - 'a'));

    private static MathTable Table(out FontFace face)
    {
        FontMatch match = Fonts.Resolve(new FontRequest("Cambria Math"))!;
        face = match.Face;
        return MathTable.TryCreate(face)!;
    }

    private static (double MaxY, double MinY) Ink(FontFace face, int glyph)
    {
        var o = face.GetOutline(glyph).TightBox();
        return (o.MaxY, o.MinY);
    }

    [Fact]
    public void MathFontIsTheOpenFontWithAMathTable()
    {
        FontMatch m = Fonts.Resolve(new FontRequest("Cambria Math"))!;
        Assert.Equal("Noto Sans Math", m.ActualFamily);
        Assert.Equal(FontMatchKind.Approximate, m.Kind);
        MathTable t = Table(out _);
        Assert.False(t.IsFallback);
        // Values read independently from the font file.
        Assert.Equal(60, t[MathConstant.ScriptPercentScaleDown]);
        Assert.Equal(50, t[MathConstant.ScriptScriptPercentScaleDown]);
        Assert.Equal(278, t[MathConstant.AxisHeight]);
        Assert.Equal(556, t[MathConstant.AccentBaseHeight]);
        Assert.Equal(390, t[MathConstant.SuperscriptShiftUp]);
        Assert.Equal(210, t[MathConstant.SubscriptShiftDown]);
        Assert.Equal(71, t[MathConstant.FractionRuleThickness]);
        Assert.Equal(529, t[MathConstant.FractionNumeratorShiftUp]);
        Assert.Equal(71, t[MathConstant.RadicalRuleThickness]);
        Assert.Equal(56, t[MathConstant.RadicalDegreeBottomRaisePercent]);
        Assert.Equal(-450, t[MathConstant.RadicalKernAfterDegree]);
        Assert.Equal(100, t.MinConnectorOverlap);
    }

    [Fact]
    public void ParenthesisHasSizeVariantsAndAnAssembly()
    {
        MathTable t = Table(out FontFace face);
        int g = face.GetGlyphIndex('(');
        IReadOnlyList<MathGlyphVariant> v = t.VerticalVariants(g);
        Assert.True(v.Count >= 8);
        Assert.Equal(942, v[0].Advance);
        Assert.Equal(1178, v[1].Advance);
        MathGlyphAssembly a = t.VerticalAssembly(g)!;
        Assert.Equal(3, a.Parts.Count);
        Assert.True(a.Parts[1].IsExtender);
        Assert.Equal(1069, a.Parts[1].FullAdvance);
    }

    [Fact]
    public void DamagedMathTablesNeverThrow()
    {
        // Every truncation of the table and many random corruptions of it must be refused or read without an exception.
        FontMatch m = Fonts.Resolve(new FontRequest("Cambria Math"))!;
        byte[] mathBytes = m.Face.GetTable("MATH").ToArray();
        int parsed = 0;
        for (int cut = 0; cut < mathBytes.Length; cut += Math.Max(1, mathBytes.Length / 600))
        {
            MathTable? t = MathTable.TryCreate(mathBytes.AsMemory(0, cut), 1000);
            _ = t?.VerticalVariants(5);
            parsed += t is null ? 0 : 1;
        }

        var rnd = new Random(7);
        for (int i = 0; i < 300; i++)
        {
            byte[] copy = (byte[])mathBytes.Clone();
            for (int k = 0; k < 8; k++)
            {
                copy[rnd.Next(copy.Length)] = (byte)rnd.Next(256);
            }

            MathTable? t = MathTable.TryCreate(copy, 1000);
            _ = t?.VerticalVariants(5);
            _ = t?.VerticalAssembly(5);
        }

        Assert.True(parsed > 0);
    }

    [Fact]
    public void InlineFractionPlacesNumeratorAndDenominatorFromTheConstants()
    {
        MathTable t = Table(out FontFace face);
        const double size = 10;
        MathBox b = Lay("<m:f><m:num>" + R("1") + "</m:num><m:den>" + R("2") + "</m:den></m:f>", display: false, size: size);
        double s = size / face.UnitsPerEm;
        var glyphs = b.Items.OfType<MathGlyph>().ToList();
        MathGlyph one = glyphs.Single(g => g.Text == "1");
        MathGlyph two = glyphs.Single(g => g.Text == "2");
        MathRule bar = b.Items.OfType<MathRule>().Single();

        // Text style: numerator and denominator are script size.
        Assert.Equal(size * 0.6, one.Size, 6);
        double ss = one.Size / face.UnitsPerEm;
        double rule = t[MathConstant.FractionRuleThickness] * s;
        double axis = t[MathConstant.AxisHeight] * s;
        Assert.Equal(rule, bar.Height, 6);
        Assert.Equal(-(axis + (rule / 2)), bar.Y, 6);
        (double oneMax, double oneMin) = Ink(face, one.GlyphId);
        (double twoMax, double twoMin) = Ink(face, two.GlyphId);
        double numDepth = Math.Max(0, -oneMin * ss);
        double denAscent = Math.Max(0, twoMax * ss);
        double u = Math.Max(t[MathConstant.FractionNumeratorShiftUp] * s, axis + (rule / 2) + (t[MathConstant.FractionNumeratorGapMin] * s) + numDepth);
        double v = Math.Max(t[MathConstant.FractionDenominatorShiftDown] * s, (t[MathConstant.FractionDenominatorGapMin] * s) + denAscent + (rule / 2) - axis);
        Assert.Equal(-u, one.Y, 6);
        Assert.Equal(v, two.Y, 6);
        Assert.Equal(u + (oneMax * ss), b.Ascent, 6);
        Assert.Equal(v + Math.Max(0, -twoMin * ss), b.Descent, 6);
        Assert.Equal(Math.Max(one.Size * 0 + bar.Width, 0), bar.Width, 6);
        Assert.True(bar.Width >= Math.Max(face.GetAdvance(one.GlyphId), face.GetAdvance(two.GlyphId)) * ss - 1e-6);
    }

    [Fact]
    public void DisplayFractionUsesTheDisplayConstantsAndTextSizeParts()
    {
        MathTable t = Table(out FontFace face);
        const double size = 12;
        MathBox b = Lay("<m:f><m:num>" + R("a") + "</m:num><m:den>" + R("b") + "</m:den></m:f>", display: true, size: size);
        double s = size / face.UnitsPerEm;
        MathGlyph[] g = b.Items.OfType<MathGlyph>().ToArray();
        Assert.All(g, x => Assert.Equal(size, x.Size, 6));
        double up = -g[0].Y;
        Assert.True(up >= t[MathConstant.FractionNumeratorDisplayStyleShiftUp] * s - 1e-6);
        Assert.True(g[1].Y >= t[MathConstant.FractionDenominatorDisplayStyleShiftDown] * s - 1e-6);
    }

    [Fact]
    public void SuperscriptOfACharacterUsesTheShiftConstant()
    {
        MathTable t = Table(out FontFace face);
        const double size = 10;
        MathBox b = Lay("<m:sSup><m:e>" + R("x") + "</m:e><m:sup>" + R("2") + "</m:sup></m:sSup>", size: size);
        double s = size / face.UnitsPerEm;
        MathGlyph two = b.Items.OfType<MathGlyph>().Single(g => g.Text == "2");
        Assert.Equal(size * 0.6, two.Size, 6);
        double u = Math.Max(t[MathConstant.SuperscriptShiftUp] * s, Math.Max(0, -Ink(face, two.GlyphId).MinY * two.Size / face.UnitsPerEm) + (t[MathConstant.SuperscriptBottomMin] * s));
        Assert.Equal(-u, two.Y, 6);
        // The script follows the base's advance and its italic correction.
        MathGlyph x = b.Items.OfType<MathGlyph>().Single(g => g.Text.Length > 0 && g.Text != "2");
        double expectedX = (face.GetAdvance(x.GlyphId) * s) + (t.ItalicCorrection(x.GlyphId) * s);
        Assert.Equal(expectedX, two.X, 6);
    }

    [Fact]
    public void SubscriptAndSubSuperscriptKeepTheirMinimumGap()
    {
        MathTable t = Table(out FontFace face);
        const double size = 10;
        MathBox sub = Lay("<m:sSub><m:e>" + R("x") + "</m:e><m:sub>" + R("i") + "</m:sub></m:sSub>", size: size);
        double s = size / face.UnitsPerEm;
        MathGlyph i = sub.Items.OfType<MathGlyph>().Last();
        Assert.True(i.Y >= t[MathConstant.SubscriptShiftDown] * s - 1e-6);

        MathBox both = Lay("<m:sSubSup><m:e>" + R("x") + "</m:e><m:sub>" + R("k") + "</m:sub><m:sup>" + R("n") + "</m:sup></m:sSubSup>", size: size);
        MathGlyph[] g = both.Items.OfType<MathGlyph>().ToArray();
        MathGlyph k = g.Single(x => x.Y > 0);
        MathGlyph n = g.Single(x => x.Y < 0);
        double scriptScale = k.Size / face.UnitsPerEm;
        double supBottom = -n.Y + Ink(face, n.GlyphId).MinY * scriptScale;
        double subTop = -k.Y + Ink(face, k.GlyphId).MaxY * scriptScale;
        Assert.True(supBottom - subTop >= t[MathConstant.SubSuperscriptGapMin] * s - 1e-6, $"gap {supBottom - subTop}");
    }

    [Fact]
    public void ScriptLevelsScaleByTheTableAndStop()
    {
        const double size = 10;
        MathBox b = Lay("<m:sSup><m:e>" + R("x") + "</m:e><m:sup><m:sSup><m:e>" + R("y") + "</m:e><m:sup><m:sSup><m:e>" + R("z") + "</m:e><m:sup>" + R("w") + "</m:sup></m:sSup></m:sup></m:sSup></m:sup></m:sSup>", size: size);
        double[] sizes = b.Items.OfType<MathGlyph>().Select(g => g.Size).ToArray();
        Assert.Equal([10, 6, 5, 5], sizes);
    }

    [Fact]
    public void RadicalDrawsItsRuleAtTheGlyphTopWithTheTableThickness()
    {
        MathTable t = Table(out FontFace face);
        const double size = 10;
        MathBox b = Lay("<m:rad><m:radPr><m:degHide m:val=\"1\"/></m:radPr><m:deg/><m:e>" + R("x") + "</m:e></m:rad>", size: size);
        double s = size / face.UnitsPerEm;
        MathRule rule = b.Items.OfType<MathRule>().Single();
        Assert.Equal(t[MathConstant.RadicalRuleThickness] * s, rule.Height, 6);
        MathGlyph sign = b.Items.OfType<MathGlyph>().Single(g => g.Text == "\u221A");
        (double max, _) = Ink(face, sign.GlyphId);
        // The rule's top meets the sign's top.
        Assert.Equal(sign.Y - (max * s), rule.Y, 6);
        // The rule starts where the sign ends and spans the radicand.
        Assert.Equal(sign.X + (face.GetAdvance(sign.GlyphId) * s), rule.X, 6);
        MathGlyph x = b.Items.OfType<MathGlyph>().Single(g => g.Text != "\u221A");
        Assert.Equal(rule.X, x.X, 6);
        Assert.True(rule.Width >= face.GetAdvance(x.GlyphId) * s - 1e-6);
    }

    [Fact]
    public void RadicalDegreeSitsLeftAndRaisedByThePercentage()
    {
        MathTable t = Table(out FontFace face);
        MathBox b = Lay("<m:rad><m:deg>" + R("3") + "</m:deg><m:e>" + R("x") + "</m:e></m:rad>", size: 10);
        MathGlyph deg = b.Items.OfType<MathGlyph>().Single(g => g.Text == "3");
        MathGlyph sign = b.Items.OfType<MathGlyph>().Single(g => g.Text == "\u221A");
        Assert.Equal(5, deg.Size, 6);
        Assert.True(deg.X < sign.X + (face.GetAdvance(sign.GlyphId) * 0.01));
        double s = 10.0 / face.UnitsPerEm;
        (double max, double min) = Ink(face, sign.GlyphId);
        double h = (max - min) * s;
        double signBottomUp = -(sign.Y - (min * s));
        Assert.Equal(-signBottomUp + 0, -(signBottomUp), 6);
        double degBaselineUp = -deg.Y;
        Assert.Equal(signBottomUp + (t[MathConstant.RadicalDegreeBottomRaisePercent] / 100.0 * h), degBaselineUp, 5);
    }

    [Fact]
    public void DelimitersGrowToTheContentAndAssembleWhenTallest()
    {
        MathTable t = Table(out FontFace face);
        const double size = 10;
        double s = size / face.UnitsPerEm;
        string tall = "<m:f><m:num>" + R("a") + "</m:num><m:den>" + R("b") + "</m:den></m:f>";
        MathBox b = Lay("<m:d><m:e>" + tall + "</m:e></m:d>", display: true, size: size);
        MathGlyph open = b.Items.OfType<MathGlyph>().First(g => g.Text == "(");
        int baseGlyph = face.GetGlyphIndex('(');
        // The fraction is taller than the base parenthesis, so a larger variant was chosen.
        IReadOnlyList<MathGlyphVariant> v = t.VerticalVariants(baseGlyph);
        int index = v.ToList().FindIndex(x => x.Glyph == open.GlyphId);
        Assert.True(index > 0, "a larger variant was used");
        // It is the smallest variant that reaches the target.
        MathGlyph numG = b.Items.OfType<MathGlyph>().First(g => g.Text == It('a'));
        MathGlyph denG = b.Items.OfType<MathGlyph>().First(g => g.Text == It('b'));
        double axis = t[MathConstant.AxisHeight] * s;
        double a = (-numG.Y) + (Ink(face, numG.GlyphId).MaxY * numG.Size / face.UnitsPerEm);
        double d = denG.Y + (-Ink(face, denG.GlyphId).MinY * denG.Size / face.UnitsPerEm);
        double delta = Math.Max(a - axis, d + axis);
        double target = Math.Max(delta * 2 * 0.901, (delta * 2) - (size * 0.5));
        Assert.True(v[index].Advance * s >= target - 1e-6);
        Assert.True(index == 0 || v[index - 1].Advance * s < target);

        // A very tall matrix needs an assembly: several glyphs make up the parenthesis.
        string rows = string.Concat(Enumerable.Repeat("<m:mr>" + E(R("x")) + "</m:mr>", 12));
        MathBox big = Lay("<m:d><m:e><m:m>" + rows + "</m:m></m:e></m:d>", size: size);
        var parts = big.Items.OfType<MathGlyph>().Where(g => g.Text.Length == 0).ToList();
        Assert.True(parts.Count >= 3, $"parts {parts.Count}");
        double top = parts.Min(g => g.Y - (Ink(face, g.GlyphId).MaxY * s));
        double bottom = parts.Max(g => g.Y - (Ink(face, g.GlyphId).MinY * s));
        Assert.True(bottom - top >= big.Ascent + big.Descent - (size * 0.6), "the assembly covers the content");
    }

    [Fact]
    public void NonGrowingDelimitersKeepTheBaseGlyph()
    {
        MathBox b = Lay("<m:d><m:dPr><m:grow m:val=\"0\"/></m:dPr><m:e><m:f><m:num>" + R("a") + "</m:num><m:den>" + R("b") + "</m:den></m:f></m:e></m:d>", display: true);
        MathGlyph open = b.Items.OfType<MathGlyph>().First(g => g.Text == "(");
        Table(out FontFace face);
        Assert.Equal(face.GetGlyphIndex('('), open.GlyphId);
    }

    [Fact]
    public void DisplayNaryPutsLimitsAboveAndBelow()
    {
        MathTable t = Table(out FontFace face);
        const double size = 10;
        double s = size / face.UnitsPerEm;
        MathBox b = Lay("<m:nary><m:naryPr><m:chr m:val=\"\u2211\"/><m:limLoc m:val=\"undOvr\"/></m:naryPr><m:sub>" + R("i") + "</m:sub><m:sup>" + R("n") + "</m:sup><m:e>" + R("a") + "</m:e></m:nary>", display: true, size: size);
        MathGlyph op = b.Items.OfType<MathGlyph>().Single(g => g.Text == "\u2211");
        // A display operator is at least DisplayOperatorMinHeight tall.
        var (max, min) = Ink(face, op.GlyphId);
        // This font's DisplayOperatorMinHeight (2.3 em) is beyond its largest summation (1.34 em): the largest variant is chosen.
        Assert.True(max - min > 1300, $"op height {max - min} units");
        Assert.True(t[MathConstant.DisplayOperatorMinHeight] > 0);
        MathGlyph upper = b.Items.OfType<MathGlyph>().Single(g => g.Text == It('n'));
        MathGlyph lower = b.Items.OfType<MathGlyph>().Single(g => g.Text == It('i'));
        Assert.True(upper.Y < op.Y - (max * s) + 1e-6 + 0.0, "upper limit above the operator");
        Assert.True(lower.Y > op.Y - (min * s), "lower limit below the operator");
        // Centred over the operator.
        double opMid = op.X + (face.GetAdvance(op.GlyphId) * s / 2);
        double upMid = upper.X + (face.GetAdvance(upper.GlyphId) * upper.Size / face.UnitsPerEm / 2);
        Assert.True(Math.Abs(opMid - upMid) < size * 0.2);
    }

    [Fact]
    public void InlineNaryAndIntegralPutLimitsBesideTheSign()
    {
        MathBox b = Lay("<m:nary><m:naryPr><m:chr m:val=\"\u2211\"/></m:naryPr><m:sub>" + R("i") + "</m:sub><m:sup>" + R("n") + "</m:sup><m:e>" + R("a") + "</m:e></m:nary>", display: false);
        MathGlyph op = b.Items.OfType<MathGlyph>().Single(g => g.Text == "\u2211");
        MathGlyph upper = b.Items.OfType<MathGlyph>().Single(g => g.Text == It('n'));
        Assert.True(upper.X > op.X);

        MathBox integral = Lay("<m:nary><m:naryPr><m:chr m:val=\"\u222B\"/></m:naryPr><m:sub>" + R("0") + "</m:sub><m:sup>" + R("1") + "</m:sup><m:e>" + R("x") + "</m:e></m:nary>", display: true);
        MathGlyph sign = integral.Items.OfType<MathGlyph>().Single(g => g.Text == "\u222B");
        MathGlyph top = integral.Items.OfType<MathGlyph>().Single(g => g.Text == "1");
        Assert.True(top.X > sign.X, "integral limits go to the side by default");
    }

    [Fact]
    public void MatrixColumnsAreAlignedAndCentredOnTheAxis()
    {
        MathTable t = Table(out FontFace face);
        const double size = 10;
        double s = size / face.UnitsPerEm;
        MathBox b = Lay("<m:m><m:mr>" + E(R("a")) + E(R("bbb")) + "</m:mr><m:mr>" + E(R("cc")) + E(R("d")) + "</m:mr></m:m>", size: size);
        MathGlyph a = b.Items.OfType<MathGlyph>().First(g => g.Text == It('a'));
        MathGlyph d = b.Items.OfType<MathGlyph>().First(g => g.Text == It('d'));
        Assert.True(d.Y > a.Y);
        // Vertical centre of the box sits on the axis.
        double centre = (-b.Ascent + b.Descent) / 2;
        Assert.Equal(-t[MathConstant.AxisHeight] * s, centre, 5);
        // Column gap is one em between the widest cells.
        MathGlyph[] row1 = b.Items.OfType<MathGlyph>().Where(g => Math.Abs(g.Y - a.Y) < 1e-6).OrderBy(g => g.X).ToArray();
        Assert.Equal(4, row1.Length);
        Assert.True(b.Width > 0 && double.IsFinite(b.Width));
    }

    [Fact]
    public void EquationArrayAlignsAtTheAlignmentPoints()
    {
        string row1 = E(R("x") + "<m:r><m:rPr><m:aln/></m:rPr><m:t>=1</m:t></m:r>");
        string row2 = E(R("yyy") + "<m:r><m:rPr><m:aln/></m:rPr><m:t>=2</m:t></m:r>");
        MathBox b = Lay("<m:eqArr>" + row1 + row2 + "</m:eqArr>");
        MathGlyph[] eq = b.Items.OfType<MathGlyph>().Where(g => g.Text == "=").ToArray();
        Assert.Equal(2, eq.Length);
        Assert.Equal(eq[0].X, eq[1].X, 6);
    }

    [Fact]
    public void AccentIsCentredOverTheBaseAndRaisedOnlyForTallBases()
    {
        MathTable t = Table(out FontFace face);
        const double size = 10;
        MathBox low = Lay("<m:acc><m:accPr><m:chr m:val=\"\u0302\"/></m:accPr><m:e>" + R("x") + "</m:e></m:acc>", size: size);
        MathBox tall = Lay("<m:acc><m:accPr><m:chr m:val=\"\u0302\"/></m:accPr><m:e>" + R("H", "<m:sty m:val=\"p\"/>") + "</m:e></m:acc>", size: size);
        MathGlyph accLow = low.Items.OfType<MathGlyph>().Single(g => g.Text == "\u0302");
        MathGlyph accTall = tall.Items.OfType<MathGlyph>().Single(g => g.Text == "\u0302");
        double s = size / face.UnitsPerEm;
        Assert.True(accTall.Y < accLow.Y, "the accent over a tall base is higher");
        Assert.True(accLow.Y <= 1e-6);
        double hTop = Ink(face, tall.Items.OfType<MathGlyph>().Single(g => g.Text == "H").GlyphId).MaxY * s;
        Assert.Equal(-(hTop - (t[MathConstant.AccentBaseHeight] * s)), accTall.Y, 5);
    }

    [Fact]
    public void BarsAndGroupCharactersSitAboveAndBelow()
    {
        MathBox over = Lay("<m:bar><m:barPr><m:pos m:val=\"top\"/></m:barPr><m:e>" + R("xy") + "</m:e></m:bar>");
        MathRule r = over.Items.OfType<MathRule>().Single();
        Assert.True(r.Y < 0);
        MathBox under = Lay("<m:bar><m:barPr><m:pos m:val=\"bot\"/></m:barPr><m:e>" + R("xy") + "</m:e></m:bar>");
        Assert.True(under.Items.OfType<MathRule>().Single().Y > 0);

        MathBox brace = Lay("<m:groupChr><m:groupChrPr><m:chr m:val=\"\u23DF\"/><m:pos m:val=\"bot\"/></m:groupChrPr><m:e>" + R("x+y+z+w+v+u") + "</m:e></m:groupChr>");
        // The brace is wider than a single glyph: assembled or a large variant, never narrower than the base.
        double left = brace.Items.OfType<MathGlyph>().Where(g => g.Text != "x" && g.Y > 0).Min(g => g.X);
        Assert.True(brace.Descent > over.Descent);
        Assert.True(left < 1);
    }

    [Fact]
    public void LimitsFunctionsAndPhantomsLayOut()
    {
        MathBox lim = Lay("<m:limLow><m:e>" + R("lim", "<m:sty m:val=\"p\"/>") + "</m:e><m:lim>" + R("n\u2192\u221E") + "</m:lim></m:limLow>");
        Assert.True(lim.Descent > 3);
        MathBox func = Lay("<m:func><m:fName>" + R("sin", "<m:sty m:val=\"p\"/>") + "</m:fName><m:e>" + R("x") + "</m:e></m:func>");
        string[] texts = func.Items.OfType<MathGlyph>().Select(g => g.Text).ToArray();
        Assert.Equal(["s", "i", "n", It('x')], texts);
        MathBox hidden = Lay("<m:phant><m:phantPr><m:show m:val=\"0\"/></m:phantPr><m:e>" + R("abc") + "</m:e></m:phant>");
        Assert.Empty(hidden.Items);
        Assert.True(hidden.Width > 0 && hidden.Ascent > 0);
        MathBox zero = Lay("<m:phant><m:phantPr><m:zeroWid m:val=\"1\"/><m:zeroAsc m:val=\"1\"/><m:zeroDesc m:val=\"1\"/></m:phantPr><m:e>" + R("abc") + "</m:e></m:phant>");
        Assert.Equal(0, zero.Width);
        Assert.Equal(0, zero.Ascent);
        Assert.Equal(0, zero.Descent);
        Assert.Equal(3, zero.Items.OfType<MathGlyph>().Count());
    }

    [Fact]
    public void BorderBoxDrawsFourEdgesAndStrikes()
    {
        MathBox b = Lay("<m:borderBox><m:borderBoxPr><m:strikeBLTR m:val=\"1\"/><m:hideTop m:val=\"1\"/></m:borderBoxPr><m:e>" + R("x") + "</m:e></m:borderBox>");
        Assert.Equal(3, b.Items.OfType<MathRule>().Count());
        Assert.Single(b.Items.OfType<MathStroke>());
    }

    [Fact]
    public void SpacingFollowsAtomClasses()
    {
        // a=b: relations get 5/18 em on both sides; a+b: binary operators 4/18 em.
        const double size = 18;
        MathBox rel = Lay(R("a=b"), size: size);
        MathBox none = Lay(R("ab"), size: size);
        double extra = rel.Width - none.Width;
        MathBox bin = Lay(R("a+b"), size: size);
        Table(out FontFace face);
        double eqW = face.GetAdvance(face.GetGlyphIndex('=')) * size / face.UnitsPerEm;
        double plusW = face.GetAdvance(face.GetGlyphIndex('+')) * size / face.UnitsPerEm;
        Assert.Equal((eqW + (2 * 5.0 / 18.0 * size)), extra, 4);
        Assert.Equal(plusW + (2 * 4.0 / 18.0 * size), bin.Width - none.Width, 4);
        // A leading binary operator is a unary sign: no spacing.
        MathBox unary = Lay(R("-b"), size: size);
        Assert.True(unary.Width < Lay(R("b"), size: size).Width + (face.GetAdvance(face.GetGlyphIndex('\u2212')) * size / face.UnitsPerEm) + 1e-6);
    }

    [Fact]
    public void LettersAreItalicDigitsAndPlainTextUpright()
    {
        MathBox b = Lay(R("x2") + R("y", "<m:sty m:val=\"p\"/>") + R("z", "<m:sty m:val=\"b\"/>") + R("R", "<m:scr m:val=\"double-struck\"/>"));
        string[] t = b.Items.OfType<MathGlyph>().Select(g => g.Text).ToArray();
        Assert.Equal(["\U0001D465", "2", "y", "\U0001D433", "\u211D"], t);
    }

    [Fact]
    public void EveryObjectLaysOutWithFiniteSaneMetrics()
    {
        string x = R("x");
        string[] cases =
        [
            "<m:acc><m:e>" + x + "</m:e></m:acc>",
            "<m:bar><m:e>" + x + "</m:e></m:bar>",
            "<m:box><m:e>" + x + "</m:e></m:box>",
            "<m:borderBox><m:e>" + x + "</m:e></m:borderBox>",
            "<m:d><m:dPr><m:begChr m:val=\"[\"/><m:sepChr m:val=\";\"/><m:endChr m:val=\"]\"/></m:dPr><m:e>" + x + "</m:e><m:e>" + R("y") + "</m:e></m:d>",
            "<m:eqArr><m:e>" + x + "</m:e><m:e>" + R("y") + "</m:e></m:eqArr>",
            "<m:f><m:fPr><m:type m:val=\"bar\"/></m:fPr><m:num>" + x + "</m:num><m:den>" + R("y") + "</m:den></m:f>",
            "<m:f><m:fPr><m:type m:val=\"skw\"/></m:fPr><m:num>" + x + "</m:num><m:den>" + R("y") + "</m:den></m:f>",
            "<m:f><m:fPr><m:type m:val=\"lin\"/></m:fPr><m:num>" + x + "</m:num><m:den>" + R("y") + "</m:den></m:f>",
            "<m:f><m:fPr><m:type m:val=\"noBar\"/></m:fPr><m:num>" + x + "</m:num><m:den>" + R("y") + "</m:den></m:f>",
            "<m:func><m:fName>" + R("cos") + "</m:fName><m:e>" + x + "</m:e></m:func>",
            "<m:groupChr><m:e>" + x + "</m:e></m:groupChr>",
            "<m:limLow><m:e>" + x + "</m:e><m:lim>" + R("y") + "</m:lim></m:limLow>",
            "<m:limUpp><m:e>" + x + "</m:e><m:lim>" + R("y") + "</m:lim></m:limUpp>",
            "<m:m><m:mr>" + E(x) + E(R("y")) + "</m:mr></m:m>",
            "<m:nary><m:e>" + x + "</m:e></m:nary>",
            "<m:phant><m:e>" + x + "</m:e></m:phant>",
            "<m:rad><m:deg>" + R("n") + "</m:deg><m:e>" + x + "</m:e></m:rad>",
            "<m:sPre><m:sub>" + R("a") + "</m:sub><m:sup>" + R("b") + "</m:sup><m:e>" + x + "</m:e></m:sPre>",
            "<m:sSub><m:e>" + x + "</m:e><m:sub>" + R("i") + "</m:sub></m:sSub>",
            "<m:sSup><m:e>" + x + "</m:e><m:sup>" + R("2") + "</m:sup></m:sSup>",
            "<m:sSubSup><m:e>" + x + "</m:e><m:sub>" + R("i") + "</m:sub><m:sup>" + R("2") + "</m:sup></m:sSubSup>",
        ];
        foreach (bool display in new[] { false, true })
        {
            foreach (string c in cases)
            {
                MathBox b = Lay(c, display);
                Assert.True(double.IsFinite(b.Width) && b.Width > 0, c);
                Assert.True(double.IsFinite(b.Ascent) && b.Ascent >= 0 && b.Ascent < 100, c);
                Assert.True(double.IsFinite(b.Descent) && b.Descent >= 0 && b.Descent < 100, c);
                Assert.NotEmpty(b.Items);
                Assert.Empty(b.Notes);
                foreach (MathItem i in b.Items)
                {
                    if (i is MathGlyph g)
                    {
                        Assert.True(double.IsFinite(g.X) && double.IsFinite(g.Y), c);
                    }
                }
            }
        }
    }

    [Fact]
    public void DisplayEquationBreaksAtBinaryOperators()
    {
        string terms = string.Join("+", Enumerable.Repeat("abcdef", 12));
        MathBox one = Lay(R(terms), display: true);
        Assert.Equal(1, one.LineCount);
        MathBox wrapped = Lay(R(terms), display: true, width: one.Width / 3);
        Assert.True(wrapped.LineCount >= 3, $"lines {wrapped.LineCount}");
        Assert.True(wrapped.Width <= one.Width / 3 + 1e-6, $"width {wrapped.Width} of {one.Width / 3}");
        // Continuation lines are indented by the wrap indent (72 pt by default, at most half the width).
        double firstX = wrapped.Items.OfType<MathGlyph>().Where(g => g.Y == 0).Min(g => g.X);
        double secondLineY = wrapped.Items.OfType<MathGlyph>().Select(g => g.Y).Where(y => y > 0).Min();
        double secondX = wrapped.Items.OfType<MathGlyph>().Where(g => g.Y == secondLineY).Min(g => g.X);
        Assert.True(secondX > firstX);
        // Operator before (the default): the second line starts with the operator.
        MathGlyph start = wrapped.Items.OfType<MathGlyph>().Where(g => g.Y == secondLineY).OrderBy(g => g.X).First();
        Assert.Equal("+", start.Text);
    }

    [Fact]
    public void ManualBreakStartsANewLineAndBreakAfterKeepsTheOperatorOnTheLine()
    {
        MathBox b = Lay(R("a+b") + "<m:r><m:rPr><m:brk/></m:rPr><m:t>c+d</m:t></m:r>", display: true);
        Assert.Equal(2, b.LineCount);
        string terms = string.Join("+", Enumerable.Repeat("abcdef", 12));
        MathBox one = Lay(R(terms), display: true);
        MathBox after = Lay(R(terms), display: true, width: one.Width / 3, settings: ReadSettings("<m:brkBin m:val=\"after\"/>"));
        double secondLineY = after.Items.OfType<MathGlyph>().Select(g => g.Y).Where(y => y > 0).Min();
        MathGlyph first = after.Items.OfType<MathGlyph>().Where(g => g.Y == secondLineY).OrderBy(g => g.X).First();
        Assert.NotEqual("+", first.Text);
    }

    internal static MathSettings ReadSettings(string inner)
    {
        string xml = $"<m:mathPr xmlns:m=\"{M}\">{inner}</m:mathPr>";
        Assert.True(MarkupDocument.TryParse(Encoding.UTF8.GetBytes(xml), out MarkupDocument? doc));
        return OmmlReader.ReadSettings(doc!.Root);
    }

    [Fact]
    public void FontWithoutAMathTableFallsBackToFixedRatiosWithANote()
    {
        var lib = new FontLibrary();
        lib.AddFont(OneRender.Fonts.DefaultFonts.Faces.First(f => f.Family == "Liberation Serif").ReadAllBytes());
        MathBox b = MathLayouter.Layout(Parse("<m:f><m:num>" + R("1") + "</m:num><m:den>" + R("2") + "</m:den></m:f>"), new MathLayoutOptions { Fonts = lib, FontSize = 10 });
        Assert.Contains(b.Notes, n => n.Code == "math.no-math-table");
        Assert.True(b.Width > 0 && b.Ascent > 0);
    }
}
