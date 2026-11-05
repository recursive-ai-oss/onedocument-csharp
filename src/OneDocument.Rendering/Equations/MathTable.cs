using OneFont;

namespace RecursiveAI.OneDocument.Rendering.Equations;

/// <summary>The named constants of an OpenType <c>MATH</c> table (MathConstants), in the order the table stores them.</summary>
public enum MathConstant
{
    /// <summary>Percentage of scaling down for level 1 superscripts and subscripts.</summary>
    ScriptPercentScaleDown = 0,

    /// <summary>Percentage of scaling down for level 2 and deeper scripts.</summary>
    ScriptScriptPercentScaleDown,

    /// <summary>Minimum height of a delimited sub-formula for which delimiters are stretched.</summary>
    DelimitedSubFormulaMinHeight,

    /// <summary>Minimum height of an n-ary operator glyph in display style.</summary>
    DisplayOperatorMinHeight,

    /// <summary>Leading between lines of math.</summary>
    MathLeading,

    /// <summary>Height of the math axis above the baseline.</summary>
    AxisHeight,

    /// <summary>Maximum height of a base for which an accent sits at its natural height.</summary>
    AccentBaseHeight,

    /// <summary>Same, for flattened accents.</summary>
    FlattenedAccentBaseHeight,

    /// <summary>Standard shift down of a subscript.</summary>
    SubscriptShiftDown,

    /// <summary>Maximum height of a subscript's top above the baseline.</summary>
    SubscriptTopMax,

    /// <summary>Minimum drop of the subscript below the base's bottom.</summary>
    SubscriptBaselineDropMin,

    /// <summary>Standard shift up of a superscript.</summary>
    SuperscriptShiftUp,

    /// <summary>Shift up of a superscript in cramped style.</summary>
    SuperscriptShiftUpCramped,

    /// <summary>Minimum height of a superscript's bottom above the baseline.</summary>
    SuperscriptBottomMin,

    /// <summary>Maximum drop of a superscript's baseline below the base's top.</summary>
    SuperscriptBaselineDropMax,

    /// <summary>Minimum gap between a subscript and a superscript.</summary>
    SubSuperscriptGapMin,

    /// <summary>Maximum height of the superscript's bottom when a subscript is present.</summary>
    SuperscriptBottomMaxWithSubscript,

    /// <summary>Space after a script.</summary>
    SpaceAfterScript,

    /// <summary>Minimum gap between an upper limit and the base.</summary>
    UpperLimitGapMin,

    /// <summary>Minimum rise of the upper limit's baseline above the base's top.</summary>
    UpperLimitBaselineRiseMin,

    /// <summary>Minimum gap between a lower limit and the base.</summary>
    LowerLimitGapMin,

    /// <summary>Minimum drop of the lower limit's baseline below the base's bottom.</summary>
    LowerLimitBaselineDropMin,

    /// <summary>Shift up of the top of a stack (fraction without bar).</summary>
    StackTopShiftUp,

    /// <summary>Same, display style.</summary>
    StackTopDisplayStyleShiftUp,

    /// <summary>Shift down of the bottom of a stack.</summary>
    StackBottomShiftDown,

    /// <summary>Same, display style.</summary>
    StackBottomDisplayStyleShiftDown,

    /// <summary>Minimum gap between the two parts of a stack.</summary>
    StackGapMin,

    /// <summary>Same, display style.</summary>
    StackDisplayStyleGapMin,

    /// <summary>Shift up of the top of a stretch stack.</summary>
    StretchStackTopShiftUp,

    /// <summary>Shift down of the bottom of a stretch stack.</summary>
    StretchStackBottomShiftDown,

    /// <summary>Minimum gap above a stretch stack's stretchy element.</summary>
    StretchStackGapAboveMin,

    /// <summary>Minimum gap below it.</summary>
    StretchStackGapBelowMin,

    /// <summary>Shift up of a fraction numerator.</summary>
    FractionNumeratorShiftUp,

    /// <summary>Same, display style.</summary>
    FractionNumeratorDisplayStyleShiftUp,

    /// <summary>Shift down of a fraction denominator.</summary>
    FractionDenominatorShiftDown,

    /// <summary>Same, display style.</summary>
    FractionDenominatorDisplayStyleShiftDown,

    /// <summary>Minimum gap between the numerator and the bar.</summary>
    FractionNumeratorGapMin,

    /// <summary>Same, display style.</summary>
    FractionNumDisplayStyleGapMin,

    /// <summary>Thickness of the fraction bar.</summary>
    FractionRuleThickness,

    /// <summary>Minimum gap between the bar and the denominator.</summary>
    FractionDenominatorGapMin,

    /// <summary>Same, display style.</summary>
    FractionDenomDisplayStyleGapMin,

    /// <summary>Horizontal gap of a skewed fraction's slash.</summary>
    SkewedFractionHorizontalGap,

    /// <summary>Vertical gap of a skewed fraction's slash.</summary>
    SkewedFractionVerticalGap,

    /// <summary>Gap between an overbar and the base.</summary>
    OverbarVerticalGap,

    /// <summary>Thickness of an overbar.</summary>
    OverbarRuleThickness,

    /// <summary>Extra space above an overbar.</summary>
    OverbarExtraAscender,

    /// <summary>Gap between an underbar and the base.</summary>
    UnderbarVerticalGap,

    /// <summary>Thickness of an underbar.</summary>
    UnderbarRuleThickness,

    /// <summary>Extra space below an underbar.</summary>
    UnderbarExtraDescender,

    /// <summary>Gap between the radical's rule and the radicand.</summary>
    RadicalVerticalGap,

    /// <summary>Same, display style.</summary>
    RadicalDisplayStyleVerticalGap,

    /// <summary>Thickness of the radical's rule.</summary>
    RadicalRuleThickness,

    /// <summary>Extra space above the radical's rule.</summary>
    RadicalExtraAscender,

    /// <summary>Kern before the radical's degree.</summary>
    RadicalKernBeforeDegree,

    /// <summary>Kern after the radical's degree.</summary>
    RadicalKernAfterDegree,

    /// <summary>Percentage of the radical sign's height at which the degree's baseline sits.</summary>
    RadicalDegreeBottomRaisePercent,
}

/// <summary>A glyph that can replace another at a larger size, with its extent along the stretch direction.</summary>
/// <param name="Glyph">The glyph id.</param>
/// <param name="Advance">The extent along the stretch direction in font units (height for vertical variants).</param>
public readonly record struct MathGlyphVariant(int Glyph, int Advance);

/// <summary>One part of a glyph assembly.</summary>
/// <param name="Glyph">The glyph id.</param>
/// <param name="StartConnectorLength">Length of the connector at the start, in font units.</param>
/// <param name="EndConnectorLength">Length of the connector at the end.</param>
/// <param name="FullAdvance">The part's full extent along the stretch direction.</param>
/// <param name="IsExtender">Whether the part can be repeated.</param>
public readonly record struct MathGlyphPart(int Glyph, int StartConnectorLength, int EndConnectorLength, int FullAdvance, bool IsExtender);

/// <summary>A glyph assembly: parts listed from the start (bottom or left) to the end.</summary>
/// <param name="Parts">The parts.</param>
/// <param name="ItalicCorrection">The assembly's italic correction in font units.</param>
public sealed record MathGlyphAssembly(IReadOnlyList<MathGlyphPart> Parts, int ItalicCorrection);

/// <summary>
/// The parsed <c>MATH</c> table of a font (OpenType specification, "MATH — The mathematical typesetting table"): layout constants,
/// italic corrections, top accent attachment points, extended-shape flags, and size variants and assemblies for stretchy glyphs.
/// All values are in font design units. A table that is damaged anywhere makes <see cref="TryCreate(FontFace)"/> return null; no read goes
/// past the table, so a hostile embedded font cannot cause an exception.
/// </summary>
public sealed class MathTable
{
    private const int ConstantCount = 4 + 51 + 1;
    private readonly int[] _constants;
    private readonly Dictionary<int, int> _italic = [];
    private readonly Dictionary<int, int> _accent = [];
    private readonly HashSet<int> _extended = [];
    private readonly Dictionary<int, List<MathGlyphVariant>> _vertical = [];
    private readonly Dictionary<int, List<MathGlyphVariant>> _horizontal = [];
    private readonly Dictionary<int, MathGlyphAssembly> _verticalAssembly = [];
    private readonly Dictionary<int, MathGlyphAssembly> _horizontalAssembly = [];

    private MathTable(int unitsPerEm, int[] constants, int minConnectorOverlap)
    {
        UnitsPerEm = unitsPerEm;
        _constants = constants;
        MinConnectorOverlap = minConnectorOverlap;
    }

    /// <summary>Gets the font's design units per em.</summary>
    public int UnitsPerEm { get; }

    /// <summary>Gets the minimum overlap of connecting assembly parts, in font units.</summary>
    public int MinConnectorOverlap { get; }

    /// <summary>Gets a constant in font units (percentages for the three <c>*Percent*</c> constants).</summary>
    /// <param name="constant">The constant.</param>
    public int this[MathConstant constant] => _constants[(int)constant];

    /// <summary>Reads the table of a face.</summary>
    /// <param name="face">The face.</param>
    /// <returns>The table, or null when the face has no <c>MATH</c> table or it is unusable.</returns>
    public static MathTable? TryCreate(FontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        return TryCreate(face.GetTable("MATH"), face.UnitsPerEm);
    }

    internal static MathTable? TryCreate(ReadOnlyMemory<byte> mem, int unitsPerEm)
    {
        if (mem.IsEmpty)
        {
            return null;
        }

        ReadOnlySpan<byte> t = mem.Span;
        var r = new Reader(t);
        if (r.U16(0) != 1 || t.Length < 10)
        {
            return null;
        }

        int constantsOffset = r.U16(4);
        int infoOffset = r.U16(6);
        int variantsOffset = r.U16(8);
        if (constantsOffset == 0 || !r.Has(constantsOffset, 8 + (51 * 4) + 2))
        {
            return null;
        }

        var c = new int[ConstantCount];
        c[0] = r.I16(constantsOffset);
        c[1] = r.I16(constantsOffset + 2);
        c[2] = r.U16(constantsOffset + 4);
        c[3] = r.U16(constantsOffset + 6);
        for (int i = 0; i < 51; i++)
        {
            c[4 + i] = r.I16(constantsOffset + 8 + (i * 4));
        }

        c[55] = r.I16(constantsOffset + 8 + (51 * 4));
        int minOverlap = variantsOffset != 0 && r.Has(variantsOffset, 10) ? r.U16(variantsOffset) : 0;
        var table = new MathTable(unitsPerEm, c, minOverlap);
        if (infoOffset != 0 && r.Has(infoOffset, 8))
        {
            table.ReadInfo(r, infoOffset);
        }

        if (variantsOffset != 0 && r.Has(variantsOffset, 10))
        {
            table.ReadVariants(r, variantsOffset);
        }

        return table;
    }

    /// <summary>
    /// Creates constants from fixed ratios of the em, the values of a conventional TeX-style math font, for a face that has no
    /// <c>MATH</c> table. There are no size variants, italic corrections or accent positions, so delimiters and radicals do not stretch.
    /// </summary>
    /// <param name="unitsPerEm">The font's design units per em.</param>
    /// <returns>The fallback table; <see cref="IsFallback"/> is true.</returns>
    public static MathTable CreateFallback(int unitsPerEm)
    {
        int u = Math.Max(unitsPerEm, 1);
        int E(double em) => (int)Math.Round(em * u);
        var c = new int[ConstantCount];
        c[(int)MathConstant.ScriptPercentScaleDown] = 70;
        c[(int)MathConstant.ScriptScriptPercentScaleDown] = 50;
        c[(int)MathConstant.DelimitedSubFormulaMinHeight] = E(1.5);
        c[(int)MathConstant.DisplayOperatorMinHeight] = E(1.3);
        c[(int)MathConstant.AxisHeight] = E(0.25);
        c[(int)MathConstant.AccentBaseHeight] = E(0.45);
        c[(int)MathConstant.FlattenedAccentBaseHeight] = E(0.65);
        c[(int)MathConstant.SubscriptShiftDown] = E(0.15);
        c[(int)MathConstant.SubscriptTopMax] = E(0.35);
        c[(int)MathConstant.SubscriptBaselineDropMin] = E(0.05);
        c[(int)MathConstant.SuperscriptShiftUp] = E(0.413);
        c[(int)MathConstant.SuperscriptShiftUpCramped] = E(0.363);
        c[(int)MathConstant.SuperscriptBottomMin] = E(0.108);
        c[(int)MathConstant.SuperscriptBaselineDropMax] = E(0.386);
        c[(int)MathConstant.SubSuperscriptGapMin] = E(0.16);
        c[(int)MathConstant.SuperscriptBottomMaxWithSubscript] = E(0.344);
        c[(int)MathConstant.SpaceAfterScript] = E(0.05);
        c[(int)MathConstant.UpperLimitGapMin] = E(0.111);
        c[(int)MathConstant.UpperLimitBaselineRiseMin] = E(0.167);
        c[(int)MathConstant.LowerLimitGapMin] = E(0.167);
        c[(int)MathConstant.LowerLimitBaselineDropMin] = E(0.6);
        c[(int)MathConstant.StackTopShiftUp] = E(0.444);
        c[(int)MathConstant.StackTopDisplayStyleShiftUp] = E(0.677);
        c[(int)MathConstant.StackBottomShiftDown] = E(0.345);
        c[(int)MathConstant.StackBottomDisplayStyleShiftDown] = E(0.686);
        c[(int)MathConstant.StackGapMin] = E(0.12);
        c[(int)MathConstant.StackDisplayStyleGapMin] = E(0.28);
        c[(int)MathConstant.FractionNumeratorShiftUp] = E(0.394);
        c[(int)MathConstant.FractionNumeratorDisplayStyleShiftUp] = E(0.677);
        c[(int)MathConstant.FractionDenominatorShiftDown] = E(0.345);
        c[(int)MathConstant.FractionDenominatorDisplayStyleShiftDown] = E(0.686);
        c[(int)MathConstant.FractionNumeratorGapMin] = E(0.04);
        c[(int)MathConstant.FractionNumDisplayStyleGapMin] = E(0.12);
        c[(int)MathConstant.FractionRuleThickness] = E(0.04);
        c[(int)MathConstant.FractionDenominatorGapMin] = E(0.04);
        c[(int)MathConstant.FractionDenomDisplayStyleGapMin] = E(0.12);
        c[(int)MathConstant.SkewedFractionHorizontalGap] = E(0.1);
        c[(int)MathConstant.SkewedFractionVerticalGap] = E(0.2);
        c[(int)MathConstant.OverbarVerticalGap] = E(0.12);
        c[(int)MathConstant.OverbarRuleThickness] = E(0.04);
        c[(int)MathConstant.OverbarExtraAscender] = E(0.04);
        c[(int)MathConstant.UnderbarVerticalGap] = E(0.12);
        c[(int)MathConstant.UnderbarRuleThickness] = E(0.04);
        c[(int)MathConstant.UnderbarExtraDescender] = E(0.04);
        c[(int)MathConstant.RadicalVerticalGap] = E(0.05);
        c[(int)MathConstant.RadicalDisplayStyleVerticalGap] = E(0.148);
        c[(int)MathConstant.RadicalRuleThickness] = E(0.04);
        c[(int)MathConstant.RadicalExtraAscender] = E(0.04);
        c[(int)MathConstant.RadicalKernBeforeDegree] = E(0.28);
        c[(int)MathConstant.RadicalKernAfterDegree] = E(-0.55);
        c[(int)MathConstant.RadicalDegreeBottomRaisePercent] = 60;
        return new MathTable(u, c, 0) { IsFallback = true };
    }

    /// <summary>Gets whether the constants are the fixed ratios of <see cref="CreateFallback"/> and not read from a font.</summary>
    public bool IsFallback { get; private init; }

    /// <summary>Gets the italic correction of a glyph in font units; 0 when none is stored.</summary>
    /// <param name="glyph">The glyph id.</param>
    public int ItalicCorrection(int glyph) => _italic.GetValueOrDefault(glyph);

    /// <summary>Gets the horizontal position where an accent attaches over a glyph, or null when the font gives none.</summary>
    /// <param name="glyph">The glyph id.</param>
    public int? TopAccentAttachment(int glyph) => _accent.TryGetValue(glyph, out int v) ? v : null;

    /// <summary>Gets whether a glyph is an extended shape (taller than usual, so scripts attach to its edges).</summary>
    /// <param name="glyph">The glyph id.</param>
    public bool IsExtendedShape(int glyph) => _extended.Contains(glyph);

    /// <summary>Gets the vertical size variants of a glyph, smallest first (the glyph itself is usually the first).</summary>
    /// <param name="glyph">The glyph id.</param>
    /// <returns>The variants; empty when the glyph does not stretch vertically.</returns>
    public IReadOnlyList<MathGlyphVariant> VerticalVariants(int glyph) => _vertical.TryGetValue(glyph, out List<MathGlyphVariant>? v) ? v : [];

    /// <summary>Gets the horizontal size variants of a glyph, smallest first.</summary>
    /// <param name="glyph">The glyph id.</param>
    /// <returns>The variants; empty when the glyph does not stretch horizontally.</returns>
    public IReadOnlyList<MathGlyphVariant> HorizontalVariants(int glyph) => _horizontal.TryGetValue(glyph, out List<MathGlyphVariant>? v) ? v : [];

    /// <summary>Gets the vertical assembly of a glyph, or null.</summary>
    /// <param name="glyph">The glyph id.</param>
    public MathGlyphAssembly? VerticalAssembly(int glyph) => _verticalAssembly.GetValueOrDefault(glyph);

    /// <summary>Gets the horizontal assembly of a glyph, or null.</summary>
    /// <param name="glyph">The glyph id.</param>
    public MathGlyphAssembly? HorizontalAssembly(int glyph) => _horizontalAssembly.GetValueOrDefault(glyph);

    private void ReadInfo(Reader r, int info)
    {
        int italicOffset = r.U16(info);
        int accentOffset = r.U16(info + 2);
        int extendedOffset = r.U16(info + 4);
        if (italicOffset != 0)
        {
            ReadValueRecords(r, info + italicOffset, _italic);
        }

        if (accentOffset != 0)
        {
            ReadValueRecords(r, info + accentOffset, _accent);
        }

        if (extendedOffset != 0)
        {
            foreach (int g in Coverage(r, info + extendedOffset))
            {
                _extended.Add(g);
            }
        }
    }

    // MathItalicsCorrectionInfo and MathTopAccentAttachment share a layout: coverage offset, count, MathValueRecord[count].
    private static void ReadValueRecords(Reader r, int at, Dictionary<int, int> into)
    {
        if (!r.Has(at, 4))
        {
            return;
        }

        int coverageOffset = r.U16(at);
        int count = r.U16(at + 2);
        if (!r.Has(at + 4, count * 4))
        {
            return;
        }

        int index = 0;
        foreach (int g in Coverage(r, at + coverageOffset))
        {
            if (index >= count)
            {
                break;
            }

            into[g] = r.I16(at + 4 + (index * 4));
            index++;
        }
    }

    private void ReadVariants(Reader r, int at)
    {
        int vertCoverage = r.U16(at + 2);
        int horizCoverage = r.U16(at + 4);
        int vertCount = r.U16(at + 6);
        int horizCount = r.U16(at + 8);
        if (!r.Has(at + 10, (vertCount + horizCount) * 2))
        {
            return;
        }

        ReadConstructions(r, at, vertCoverage, vertCount, at + 10, _vertical, _verticalAssembly);
        ReadConstructions(r, at, horizCoverage, horizCount, at + 10 + (vertCount * 2), _horizontal, _horizontalAssembly);
    }

    private static void ReadConstructions(Reader r, int variants, int coverageOffset, int count, int offsets, Dictionary<int, List<MathGlyphVariant>> into, Dictionary<int, MathGlyphAssembly> assemblies)
    {
        if (coverageOffset == 0 || count == 0)
        {
            return;
        }

        int index = 0;
        foreach (int g in Coverage(r, variants + coverageOffset))
        {
            if (index >= count)
            {
                break;
            }

            int construction = variants + r.U16(offsets + (index * 2));
            index++;
            if (!r.Has(construction, 4))
            {
                continue;
            }

            int assemblyOffset = r.U16(construction);
            int variantCount = r.U16(construction + 2);
            if (r.Has(construction + 4, variantCount * 4))
            {
                var list = new List<MathGlyphVariant>(variantCount);
                for (int i = 0; i < variantCount; i++)
                {
                    list.Add(new MathGlyphVariant(r.U16(construction + 4 + (i * 4)), r.U16(construction + 6 + (i * 4))));
                }

                into[g] = list;
            }

            if (assemblyOffset != 0)
            {
                int a = construction + assemblyOffset;
                if (r.Has(a, 6))
                {
                    int italic = r.I16(a);
                    int partCount = r.U16(a + 4);
                    if (r.Has(a + 6, partCount * 10))
                    {
                        var parts = new List<MathGlyphPart>(partCount);
                        for (int i = 0; i < partCount; i++)
                        {
                            int p = a + 6 + (i * 10);
                            parts.Add(new MathGlyphPart(r.U16(p), r.U16(p + 2), r.U16(p + 4), r.U16(p + 6), (r.U16(p + 8) & 1) != 0));
                        }

                        assemblies[g] = new MathGlyphAssembly(parts, italic);
                    }
                }
            }
        }
    }

    // OpenType Coverage table, format 1 (glyph list) and 2 (ranges); glyphs come out in coverage-index order. At most 65,536 glyphs.
    private static List<int> Coverage(Reader r, int at)
    {
        var list = new List<int>();
        if (!r.Has(at, 4))
        {
            return list;
        }

        int format = r.U16(at);
        int count = r.U16(at + 2);
        if (format == 1 && r.Has(at + 4, count * 2))
        {
            for (int i = 0; i < count; i++)
            {
                list.Add(r.U16(at + 4 + (i * 2)));
            }
        }
        else if (format == 2 && r.Has(at + 4, count * 6))
        {
            for (int i = 0; i < count && list.Count <= 65536; i++)
            {
                int start = r.U16(at + 4 + (i * 6));
                int end = r.U16(at + 6 + (i * 6));
                for (int g = start; g <= end && list.Count <= 65536; g++)
                {
                    list.Add(g);
                }
            }
        }

        return list;
    }

    // Bounds-checked big-endian reads over the table.
    private readonly ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _d = data;

        public bool Has(int at, int length) => at >= 0 && length >= 0 && (long)at + length <= _d.Length;

        public int U16(int at) => Has(at, 2) ? (_d[at] << 8) | _d[at + 1] : 0;

        public int I16(int at) => (short)U16(at);
    }
}
