using OneFont;
using RecursiveAI.OneDocument.Equations;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering.Equations;

/// <summary>What the equation layout needs from its surroundings.</summary>
public sealed class MathLayoutOptions
{
    /// <summary>Gets or sets the fonts. Required: the math font and the text font of normal-text runs come from it.</summary>
    public required IFontProvider Fonts { get; init; }

    /// <summary>Gets or sets the size in points of text at the equation's own level (the paragraph's text size). Default 11.</summary>
    public double FontSize { get; init; } = 11;

    /// <summary>Gets or sets the colour, 0xRRGGBB, of math without its own colour. Default black.</summary>
    public uint Color { get; init; }

    /// <summary>Gets or sets the family of normal-text runs (<c>m:nor</c>) that name none: the paragraph's font. Default "Cambria".</summary>
    public string TextFamily { get; init; } = "Cambria";

    /// <summary>Gets or sets the document's math settings. Default <see cref="MathSettings.Default"/>.</summary>
    public MathSettings Settings { get; init; } = MathSettings.Default;

    /// <summary>Gets or sets whether the equation is display math (its own line, display style) rather than inline (text style). Default false.</summary>
    public bool Display { get; init; }

    /// <summary>Gets or sets the width in points a display equation may use before it breaks at an operator. Default unlimited.</summary>
    public double AvailableWidth { get; init; } = double.PositiveInfinity;
}

/// <summary>
/// Lays out an equation tree as a box of glyphs and rules: a TeX-style box layout driven by the math font's OpenType <c>MATH</c> table
/// (script levels, shifts, stretchy delimiters, radicals and accents by size variants and assemblies), with spacing between atom
/// classes and display-math breaking at operators as the document's math settings ask.
/// </summary>
public static class MathLayouter
{
    /// <summary>Lays out an equation.</summary>
    /// <param name="node">An <see cref="MathKind.OMath"/> (or any object of one; an <see cref="MathKind.OMathPara"/> lays out its first equation).</param>
    /// <param name="options">The context.</param>
    /// <returns>The box. A font without a <c>MATH</c> table gives fixed ratios and a <c>math.no-math-table</c> note; nothing throws for odd content.</returns>
    public static MathBox Layout(MathNode node, MathLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(options);
        var engine = new MathEngine(options);
        return engine.Run(node);
    }
}

// One laid-out piece while a formula is built: dimensions, what to draw and how it behaves as an atom.
internal sealed class Frag
{
    public double W;
    public double A; // height above the baseline
    public double D; // depth below it
    public double Ic; // italic correction of the last character, not included in W
    public double? AccentX; // where an accent attaches, from the left
    public AtomClass Class;
    public bool IsChar;
    public bool Extended;
    public readonly List<MathItem> Items = [];

    public void Add(Frag f, double dx, double dy)
    {
        foreach (MathItem i in f.Items)
        {
            Items.Add(i.Moved(dx, dy));
        }
    }

    public void Rule(double x, double yTop, double w, double h, uint color) => Items.Add(new MathRule(x, yTop, w, h, color));
}

internal readonly record struct St(int Level, bool Cramped)
{
    public bool Script => Level >= 2;

    public St Sup => new(Level >= 2 ? 3 : 2, Cramped);

    public St Sub => new(Level >= 2 ? 3 : 2, true);

    public St Num => new(Level == 0 ? 1 : Level >= 2 ? 3 : 2, Cramped);

    public St Den => new(Level == 0 ? 1 : Level >= 2 ? 3 : 2, true);

    public St AsCramped => new(Level, true);
}

internal sealed class MathEngine
{
    private readonly MathLayoutOptions _o;
    private readonly List<MathNote> _notes = [];
    private readonly HashSet<string> _noted = [];
    private readonly Dictionary<(FontFace, int), (double MaxY, double MinY)> _bounds = [];
    private FontFace _face = null!;
    private MathTable _table = null!;
    private bool _noGlyphNoted;

    public MathEngine(MathLayoutOptions options)
    {
        _o = options;
        ResolveMathFont();
    }

    private void ResolveMathFont()
    {
        string wanted = _o.Settings.MathFont;
        FontMatch? match = _o.Fonts.Resolve(new FontRequest(wanted));
        MathTable? table = match is null ? null : MathTable.TryCreate(match.Face);
        if (table is null && !string.Equals(wanted, "Noto Sans Math", StringComparison.OrdinalIgnoreCase))
        {
            FontMatch? alt = _o.Fonts.Resolve(new FontRequest("Noto Sans Math"));
            if (alt is not null && MathTable.TryCreate(alt.Face) is { } t2)
            {
                match = alt;
                table = t2;
            }
        }

        if (match is null)
        {
            // No face at all: the layout still needs one to measure with; any fallback face will do.
            FontFace? any = _o.Fonts.FindFallback('x', false, false) ?? _o.Fonts.FindFallback(0x2211, false, false);
            if (any is null)
            {
                throw new InvalidOperationException("The font provider has no face for math.");
            }

            _face = any;
            _table = MathTable.CreateFallback(any.UnitsPerEm);
            Note("math.font-missing", $"No font is available for the math font \"{wanted}\"; fixed ratios and an unrelated face were used.");
            return;
        }

        _face = match.Face;
        if (table is null)
        {
            _table = MathTable.CreateFallback(_face.UnitsPerEm);
            Note("math.no-math-table", $"The font \"{match.ActualFamily}\" has no usable MATH table; fixed ratios were used and delimiters do not stretch.");
        }
        else
        {
            _table = table;
        }
    }

    private void Note(string code, string message)
    {
        if (_noted.Add(code))
        {
            _notes.Add(new MathNote(code, message));
        }
    }

    // ---- Entry ----

    public MathBox Run(MathNode node)
    {
        MathNode omath = node.Kind == MathKind.OMathPara && node.Children.Count > 0 ? node.Children[0] : node;
        var st = new St(_o.Display ? 0 : 1, false);
        string linear = MathLinearizer.Linearize(omath);
        List<Atom> atoms = omath.Kind == MathKind.OMath ? Gather(omath.Children, st, 0) : [new Atom(Layout(omath, st, 0), AtomClass.Ord)];
        Convert(atoms);
        Frag whole;
        int lines = 1;
        double width = _o.AvailableWidth;
        bool manual = atoms.Any(static a => a.Break);
        if (_o.Display && (manual || !double.IsInfinity(width)))
        {
            (whole, lines) = Wrap(atoms, st, width);
        }
        else
        {
            whole = Compose(atoms, 0, atoms.Count, st);
        }

        return new MathBox(whole.W, whole.A, whole.D, whole.Items, linear, lines, _notes);
    }

    private double Size(St st) => _o.FontSize * (st.Level <= 1 ? 1.0 : st.Level == 2 ? Math.Clamp(_table[MathConstant.ScriptPercentScaleDown], 20, 100) / 100.0 : Math.Clamp(_table[MathConstant.ScriptScriptPercentScaleDown], 10, 100) / 100.0);

    private double K(MathConstant c, St st) => _table[c] * Size(st) / _table.UnitsPerEm;

    // ---- Atoms ----

    private sealed class Atom(Frag frag, AtomClass cls)
    {
        public Frag Frag = frag;
        public AtomClass Class = cls;
        public bool Mark; // an alignment point (equation arrays)
        public bool Break; // a manual line break comes before this atom
        public int BreakAt;
    }

    private List<Atom> Gather(IReadOnlyList<MathNode> nodes, St st, int depth)
    {
        var atoms = new List<Atom>();
        foreach (MathNode n in nodes)
        {
            if (n.Kind == MathKind.Run)
            {
                GatherRun(n, st, atoms);
            }
            else if (n.Kind == MathKind.OMath || n.Kind == MathKind.Argument)
            {
                atoms.AddRange(Gather(n.Children, st, depth + 1));
            }
            else
            {
                Frag f = Layout(n, st, depth + 1);
                atoms.Add(new Atom(f, f.Class));
            }
        }

        return atoms;
    }

    private void GatherRun(MathNode run, St st, List<Atom> atoms)
    {
        MathRunProperties rp = run.Run ?? new MathRunProperties();
        bool first = true;
        if (rp.AlignmentPoint)
        {
            atoms.Add(new Atom(new Frag(), AtomClass.Ord) { Mark = true });
        }

        double size = rp.Size is { } s ? s * (Size(st) / _o.FontSize) : Size(st);
        uint color = rp.Color ?? _o.Color;
        string text = run.Text;
        bool normal = rp.NormalText;
        if (normal)
        {
            FontFace? face = ResolveTextFace(rp);
            for (int i = 0; i < text.Length; i++)
            {
                int cp = char.ConvertToUtf32(text, i);
                if (char.IsHighSurrogate(text[i]))
                {
                    i++;
                }

                Frag g = CharFrag(cp, size, color, face);
                g.Class = AtomClass.Ord;
                atoms.Add(new Atom(g, AtomClass.Ord) { Break = first && rp.Break });
                first = false;
            }

            return;
        }

        bool bold = rp.Style is MathStyle.Bold or MathStyle.BoldItalic;
        MathRunShape shape = new(rp.Script, bold, false);
        for (int i = 0; i < text.Length; i++)
        {
            int cp = char.ConvertToUtf32(text, i);
            int len = char.IsHighSurrogate(text[i]) ? 2 : 1;
            string one = text.Substring(i, len);
            i += len - 1;
            if (one == "&" && !rp.Literal)
            {
                atoms.Add(new Atom(new Frag(), AtomClass.Ord) { Mark = true });
                continue;
            }

            cp = MathText.Normalise(cp);
            bool variable = cp is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= 0x3B1 and <= 0x3C9;
            bool italic = rp.Style switch
            {
                MathStyle.Italic or MathStyle.BoldItalic => true,
                MathStyle.Plain or MathStyle.Bold => false,
                _ => variable,
            };
            string styled = MathText.Styled(char.ConvertFromUtf32(cp), shape with { Italic = italic && rp.Script == MathScript.Roman || italic && rp.Script == MathScript.SansSerif });
            int shown = char.ConvertToUtf32(styled, 0);
            Frag f = CharFrag(shown, size, color, null);
            f.IsChar = true;
            AtomClass cls = MathText.ClassOf(cp);
            if (cp == ',' && i > 0 && i + 1 < text.Length && char.IsDigit(text[i - 1]) && char.IsDigit(text[i + 1]))
            {
                cls = AtomClass.Ord;
            }

            f.Class = cls;
            atoms.Add(new Atom(f, cls) { Break = first && rp.Break, BreakAt = rp.BreakAlignAt });
            first = false;
        }
    }

    private FontFace? ResolveTextFace(MathRunProperties rp)
    {
        string family = rp.FontFamily ?? _o.TextFamily;
        FontMatch? m = _o.Fonts.Resolve(new FontRequest(family, rp.Bold == true, rp.Italic == true));
        return m?.Face;
    }

    // TeX rules 5 and 6 (TeXbook Appendix G): a binary operator with nothing to its left, or after another operator, relation, opening or
    // punctuation, is an ordinary symbol; so is one before a relation, closing or punctuation.
    private static void Convert(List<Atom> atoms)
    {
        AtomClass prev = AtomClass.Open;
        for (int i = 0; i < atoms.Count; i++)
        {
            Atom a = atoms[i];
            if (a.Mark || a.Frag.W == 0 && a.Frag.Items.Count == 0 && a.Class == AtomClass.Ord)
            {
                continue;
            }

            if (a.Class == AtomClass.Bin && prev is AtomClass.Bin or AtomClass.Op or AtomClass.Rel or AtomClass.Open or AtomClass.Punct)
            {
                a.Class = AtomClass.Ord;
            }

            if (a.Class is AtomClass.Rel or AtomClass.Close or AtomClass.Punct && i > 0)
            {
                for (int j = i - 1; j >= 0; j--)
                {
                    if (atoms[j].Mark)
                    {
                        continue;
                    }

                    if (atoms[j].Class == AtomClass.Bin)
                    {
                        atoms[j].Class = AtomClass.Ord;
                    }

                    break;
                }
            }

            prev = a.Class;
        }

        if (atoms.Count > 0)
        {
            Atom last = atoms[^1];
            if (last.Class == AtomClass.Bin)
            {
                last.Class = AtomClass.Ord;
            }
        }
    }

    private Frag Compose(List<Atom> atoms, int from, int to, St st)
    {
        var result = new Frag();
        double em = Size(st);
        double x = 0;
        Atom? prev = null;
        int count = 0;
        for (int i = from; i < to; i++)
        {
            Atom a = atoms[i];
            if (a.Mark)
            {
                continue;
            }

            if (prev is not null)
            {
                x += prev.Frag.Ic;
                x += MathText.SpaceBetween(prev.Class, a.Class, st.Script) * em;
            }

            result.Add(a.Frag, x, 0);
            result.A = Math.Max(result.A, a.Frag.A);
            result.D = Math.Max(result.D, a.Frag.D);
            x += a.Frag.W;
            prev = a;
            count++;
        }

        result.W = x;
        if (prev is not null)
        {
            result.Ic = prev.Frag.Ic;
            if (count == 1)
            {
                result.IsChar = prev.Frag.IsChar;
                result.AccentX = prev.Frag.AccentX;
                result.Extended = prev.Frag.Extended;
            }

            result.Class = count == 1 ? prev.Class : AtomClass.Ord;
        }

        return result;
    }

    private Frag Seq(IReadOnlyList<MathNode> nodes, St st, int depth)
    {
        if (depth > OmmlReader.MaxDepth + 8)
        {
            Note("math.depth", "The equation is nested deeper than the layout allows; the inner part was left out.");
            return new Frag();
        }

        List<Atom> atoms = Gather(nodes, st, depth);
        Convert(atoms);
        return Compose(atoms, 0, atoms.Count, st);
    }

    private Frag Arg(MathNode? arg, St st, int depth) => arg is null ? new Frag() : Seq(arg.Children, st, depth);

    // ---- Glyphs ----

    private (double MaxY, double MinY) Bounds(FontFace face, int glyph)
    {
        if (_bounds.TryGetValue((face, glyph), out (double, double) b))
        {
            return b;
        }

        GlyphOutline o = face.GetOutline(glyph);
        (double, double) r = o.IsEmpty ? (0, 0) : (o.TightBox().MaxY, o.TightBox().MinY);
        _bounds[(face, glyph)] = r;
        return r;
    }

    private Frag GlyphFrag(FontFace face, int glyph, string text, double size, uint color)
    {
        double s = size / face.UnitsPerEm;
        (double maxY, double minY) = Bounds(face, glyph);
        var f = new Frag { W = face.GetAdvance(glyph) * s, A = Math.Max(0, maxY * s), D = Math.Max(0, -minY * s) };
        if (ReferenceEquals(face, _face))
        {
            f.Ic = _table.ItalicCorrection(glyph) * s;
            if (_table.TopAccentAttachment(glyph) is { } ax)
            {
                f.AccentX = ax * s;
            }

            f.Extended = _table.IsExtendedShape(glyph);
        }

        f.Items.Add(new MathGlyph(face, glyph, text, 0, 0, size, color));
        return f;
    }

    private Frag CharFrag(int cp, double size, uint color, FontFace? preferred)
    {
        string text = char.ConvertFromUtf32(cp);
        if (cp == ' ' || cp == 0xA0)
        {
            FontFace sp = preferred ?? _face;
            return new Frag { W = sp.GetAdvance(sp.GetGlyphIndex(' ')) * size / sp.UnitsPerEm };
        }

        FontFace? face = null;
        if (preferred is not null && preferred.HasGlyph(cp))
        {
            face = preferred;
        }
        else if (_face.HasGlyph(cp))
        {
            face = _face;
        }
        else
        {
            face = _o.Fonts.FindFallback(cp, false, false);
        }

        if (face is null)
        {
            if (!_noGlyphNoted)
            {
                _noGlyphNoted = true;
                Note("math.glyph-missing", $"No font has a glyph for U+{cp:X4}; the math font's missing-glyph shape was used.");
            }

            face = _face;
        }

        return GlyphFrag(face, face.GetGlyphIndex(cp), text, size, color);
    }

    // The glyph of a stretchy character at least `target` points long along the stretch direction: a size variant, else an assembly,
    // else the largest variant, else the glyph itself.
    private Frag Stretchy(int cp, double target, bool vertical, St st, uint color)
    {
        double size = Size(st);
        FontFace face = _face.HasGlyph(cp) ? _face : (_o.Fonts.FindFallback(cp, false, false) ?? _face);
        int g = face.GetGlyphIndex(cp);
        string text = char.ConvertFromUtf32(cp);
        if (!ReferenceEquals(face, _face))
        {
            return GlyphFrag(face, g, text, size, color);
        }

        double s = size / _face.UnitsPerEm;
        IReadOnlyList<MathGlyphVariant> variants = vertical ? _table.VerticalVariants(g) : _table.HorizontalVariants(g);
        MathGlyphVariant? best = null;
        foreach (MathGlyphVariant v in variants)
        {
            best = v;
            if (v.Advance * s >= target)
            {
                return GlyphFrag(face, v.Glyph, text, size, color);
            }
        }

        MathGlyphAssembly? assembly = vertical ? _table.VerticalAssembly(g) : _table.HorizontalAssembly(g);
        if (assembly is not null && assembly.Parts.Count > 0 && target > 0)
        {
            return Assemble(face, assembly, target, vertical, size, color);
        }

        return GlyphFrag(face, best?.Glyph ?? g, text, size, color);
    }

    // OpenType MATH, "GlyphAssembly": parts run from the start (bottom or left) to the end; extender parts repeat. Connectors overlap by
    // at least MinConnectorOverlap; the overlap grows (up to what the connectors allow) until the assembly is exactly `target` long.
    private Frag Assemble(FontFace face, MathGlyphAssembly assembly, double target, bool vertical, double size, uint color)
    {
        double s = size / face.UnitsPerEm;
        double minOverlap = _table.MinConnectorOverlap;
        IReadOnlyList<MathGlyphPart> parts = assembly.Parts;
        int extenders = parts.Count(static p => p.IsExtender);
        int repeat = extenders == 0 ? 1 : 0;
        List<MathGlyphPart> seq;
        while (true)
        {
            seq = [];
            foreach (MathGlyphPart p in parts)
            {
                int times = p.IsExtender ? repeat : 1;
                for (int i = 0; i < times; i++)
                {
                    seq.Add(p);
                }
            }

            double total = seq.Sum(static p => p.FullAdvance) - (Math.Max(seq.Count - 1, 0) * minOverlap);
            if (total * s >= target || repeat >= 64 || extenders == 0)
            {
                break;
            }

            repeat++;
        }

        // The overlap needed for the target, limited by the connector lengths of each joint.
        int joints = Math.Max(seq.Count - 1, 0);
        double sum = seq.Sum(static p => p.FullAdvance);
        double wantOverlap = joints == 0 ? 0 : Math.Max(minOverlap, (sum - (target / s)) / joints);
        var overlaps = new double[joints];
        for (int j = 0; j < joints; j++)
        {
            double maxOverlap = Math.Min(seq[j].EndConnectorLength, seq[j + 1].StartConnectorLength);
            overlaps[j] = Math.Clamp(wantOverlap, Math.Min(minOverlap, maxOverlap), Math.Max(maxOverlap, minOverlap));
        }

        var f = new Frag();
        double pos = 0; // along the stretch direction, from the start, in font units
        double maxCross = 0;
        double top = 0;
        double bottom = 0;
        for (int i = 0; i < seq.Count; i++)
        {
            MathGlyphPart p = seq[i];
            Frag pf = GlyphFrag(face, p.Glyph, string.Empty, size, color);
            if (vertical)
            {
                // The part's ink bottom sits at the current position, going up.
                (double maxY, double minY) = Bounds(face, p.Glyph);
                double originUp = pos - minY;
                f.Add(pf, 0, -originUp * s);
                top = Math.Max(top, (pos + (maxY - minY)) * s);
                maxCross = Math.Max(maxCross, pf.W);
            }
            else
            {
                f.Add(pf, pos * s, 0);
                maxCross = Math.Max(maxCross, pf.A + pf.D);
                top = Math.Max(top, pf.A);
                bottom = Math.Max(bottom, pf.D);
            }

            pos += p.FullAdvance - (i < joints ? overlaps[i] : 0);
        }

        if (vertical)
        {
            f.W = maxCross;
            f.A = top;
            f.D = 0;
        }
        else
        {
            f.W = pos * s;
            f.A = top;
            f.D = bottom;
        }

        return f;
    }

    // ---- Objects ----

    private Frag Layout(MathNode n, St st, int depth)
    {
        if (depth > OmmlReader.MaxDepth + 8)
        {
            Note("math.depth", "The equation is nested deeper than the layout allows; the inner part was left out.");
            return new Frag();
        }

        MathProperties p = n.Properties;
        uint color = _o.Color;
        switch (n.Kind)
        {
            case MathKind.Fraction:
                return Fraction(n, p, st, depth, color);
            case MathKind.SuperScript:
                return Scripts(Arg(n.Argument(MathRole.Base), st, depth), null, Arg(n.Argument(MathRole.Superscript), st.Sup, depth), st);
            case MathKind.SubScript:
                return Scripts(Arg(n.Argument(MathRole.Base), st, depth), Arg(n.Argument(MathRole.Subscript), st.Sub, depth), null, st);
            case MathKind.SubSuperScript:
                return Scripts(Arg(n.Argument(MathRole.Base), st, depth), Arg(n.Argument(MathRole.Subscript), st.Sub, depth), Arg(n.Argument(MathRole.Superscript), st.Sup, depth), st);
            case MathKind.PreScript:
                return PreScripts(n, st, depth);
            case MathKind.Radical:
                return Radical(n, p, st, depth, color);
            case MathKind.Delimiter:
                return Delimiter(n, p, st, depth, color);
            case MathKind.NAry:
                return NAry(n, p, st, depth, color);
            case MathKind.Function:
                return Function(n, st, depth);
            case MathKind.Accent:
                return Accent(n, p, st, depth, color);
            case MathKind.Bar:
                return BarObject(n, p, st, depth, color);
            case MathKind.GroupCharacter:
                return GroupCharacter(n, p, st, depth, color);
            case MathKind.LowerLimit:
            case MathKind.UpperLimit:
                return LimitObject(n, st, depth);
            case MathKind.Matrix:
                return Matrix(n, p, st, depth);
            case MathKind.EquationArray:
                return EquationArray(n, p, st, depth);
            case MathKind.Box:
                return Arg(n.Argument(MathRole.Base), st, depth);
            case MathKind.BorderBox:
                return BorderBox(n, p, st, depth, color);
            case MathKind.Phantom:
                return Phantom(n, p, st, depth);
            case MathKind.Run:
                {
                    List<Atom> atoms = [];
                    GatherRun(n, st, atoms);
                    Convert(atoms);
                    return Compose(atoms, 0, atoms.Count, st);
                }

            case MathKind.OMath:
            case MathKind.Argument:
                return Seq(n.Children, st, depth);
            default:
                return new Frag();
        }
    }

    private Frag Fraction(MathNode n, MathProperties p, St st, int depth, uint color)
    {
        bool display = st.Level == 0;
        bool small = _o.Settings.SmallFractions;
        St numSt = small ? new St(2, st.Cramped) : st.Num;
        St denSt = small ? new St(2, true) : st.Den;
        Frag num = Arg(n.Argument(MathRole.Numerator), numSt, depth);
        Frag den = Arg(n.Argument(MathRole.Denominator), denSt, depth);
        double axis = K(MathConstant.AxisHeight, st);
        var f = new Frag { Class = AtomClass.Ord };
        switch (p.FractionStyle)
        {
            case FractionStyle.Linear:
                {
                    Frag slash = CharFrag(0x2215, Size(st), color, null);
                    double x = 0;
                    f.Add(num, x, 0);
                    x += num.W + num.Ic;
                    f.Add(slash, x, 0);
                    x += slash.W;
                    f.Add(den, x, 0);
                    f.W = x + den.W;
                    f.A = Math.Max(Math.Max(num.A, den.A), slash.A);
                    f.D = Math.Max(Math.Max(num.D, den.D), slash.D);
                    return f;
                }

            case FractionStyle.Skewed:
                {
                    double vgap = K(MathConstant.SkewedFractionVerticalGap, st);
                    double hgap = K(MathConstant.SkewedFractionHorizontalGap, st);
                    Frag slash = CharFrag(0x2044, Size(st), color, null);
                    if (slash.Items.Count == 0 || slash.W == 0)
                    {
                        slash = CharFrag('/', Size(st), color, null);
                    }

                    double up = vgap;
                    double x = 0;
                    f.Add(num, x, -up);
                    x += num.W + hgap;
                    f.Add(slash, x, 0);
                    x += slash.W + hgap;
                    f.Add(den, x, up);
                    f.W = x + den.W;
                    f.A = Math.Max(Math.Max(num.A + up, slash.A), den.A - up);
                    f.D = Math.Max(Math.Max(den.D + up, slash.D), num.D - up);
                    return f;
                }
        }

        double w = Math.Max(num.W, den.W);
        double u;
        double v;
        if (p.FractionStyle == FractionStyle.NoBar)
        {
            u = display ? K(MathConstant.StackTopDisplayStyleShiftUp, st) : K(MathConstant.StackTopShiftUp, st);
            v = display ? K(MathConstant.StackBottomDisplayStyleShiftDown, st) : K(MathConstant.StackBottomShiftDown, st);
            double gapMin = display ? K(MathConstant.StackDisplayStyleGapMin, st) : K(MathConstant.StackGapMin, st);
            double gap = (u - num.D) - (den.A - v);
            if (gap < gapMin)
            {
                double delta = (gapMin - gap) / 2;
                u += delta;
                v += delta;
            }
        }
        else
        {
            double rule = K(MathConstant.FractionRuleThickness, st);
            u = display ? K(MathConstant.FractionNumeratorDisplayStyleShiftUp, st) : K(MathConstant.FractionNumeratorShiftUp, st);
            v = display ? K(MathConstant.FractionDenominatorDisplayStyleShiftDown, st) : K(MathConstant.FractionDenominatorShiftDown, st);
            double numGap = display ? K(MathConstant.FractionNumDisplayStyleGapMin, st) : K(MathConstant.FractionNumeratorGapMin, st);
            double denGap = display ? K(MathConstant.FractionDenomDisplayStyleGapMin, st) : K(MathConstant.FractionDenominatorGapMin, st);
            u = Math.Max(u, axis + (rule / 2) + numGap + num.D);
            v = Math.Max(v, denGap + den.A + (rule / 2) - axis);
            f.Rule(0, -(axis + (rule / 2)), w, rule, color);
        }

        f.Add(num, (w - num.W) / 2, -u);
        f.Add(den, (w - den.W) / 2, v);
        f.W = w;
        f.A = u + num.A;
        f.D = v + den.D;
        return f;
    }

    // TeX Appendix G rules 18a-f with OpenType MATH constants (which are those of the current style's size): how far the superscript
    // is raised and the subscript lowered, when present.
    private (double Up, double Down) ScriptShifts(Frag b, Frag? sub, Frag? sup, St st)
    {
        double u = 0;
        double v = 0;
        if (!(b.IsChar && !b.Extended))
        {
            u = b.A - K(MathConstant.SuperscriptBaselineDropMax, st);
            v = b.D + K(MathConstant.SubscriptBaselineDropMin, st);
        }

        double shift = st.Cramped ? K(MathConstant.SuperscriptShiftUpCramped, st) : K(MathConstant.SuperscriptShiftUp, st);
        if (sup is not null)
        {
            u = Math.Max(Math.Max(u, shift), sup.D + K(MathConstant.SuperscriptBottomMin, st));
        }

        if (sub is not null)
        {
            v = Math.Max(v, K(MathConstant.SubscriptShiftDown, st));
            if (sup is null)
            {
                v = Math.Max(v, sub.A - K(MathConstant.SubscriptTopMax, st));
            }
        }

        if (sub is not null && sup is not null)
        {
            double gap = (u - sup.D) - (sub.A - v);
            double gapMin = K(MathConstant.SubSuperscriptGapMin, st);
            if (gap < gapMin)
            {
                v += gapMin - gap;
                double psi = K(MathConstant.SuperscriptBottomMaxWithSubscript, st) - (u - sup.D);
                if (psi > 0)
                {
                    u += psi;
                    v -= psi;
                }
            }
        }

        return (sup is null ? 0 : u, sub is null ? 0 : v);
    }

    private Frag Scripts(Frag b, Frag? sub, Frag? sup, St st)
    {
        (double u, double v) = ScriptShifts(b, sub, sup, st);
        double after = K(MathConstant.SpaceAfterScript, st);
        var f = new Frag { Class = b.Class == AtomClass.Op ? AtomClass.Op : AtomClass.Ord };
        f.Add(b, 0, 0);
        double width = b.W;
        double a = b.A;
        double d = b.D;
        if (sup is not null)
        {
            // The superscript follows the base's italic correction; the subscript does not.
            double x = b.W + b.Ic;
            f.Add(sup, x, -u);
            width = Math.Max(width, x + sup.W + after);
            a = Math.Max(a, u + sup.A);
        }

        if (sub is not null)
        {
            f.Add(sub, b.W, v);
            width = Math.Max(width, b.W + sub.W + after);
            d = Math.Max(d, v + sub.D);
        }

        f.W = width;
        f.A = a;
        f.D = d;
        return f;
    }

    // Scripts before the base, right-aligned against it (ECMA-376 Part 1 §22.1.2.99, sPre).
    private Frag PreScripts(MathNode n, St st, int depth)
    {
        Frag b = Arg(n.Argument(MathRole.Base), st, depth);
        Frag sub = Arg(n.Argument(MathRole.Subscript), st.Sub, depth);
        Frag sup = Arg(n.Argument(MathRole.Superscript), st.Sup, depth);
        (double u, double v) = ScriptShifts(b, sub, sup, st);
        double column = Math.Max(sub.W, sup.W) + K(MathConstant.SpaceAfterScript, st);
        var f = new Frag { Class = AtomClass.Ord };
        f.Add(sup, column - sup.W, -u);
        f.Add(sub, column - sub.W, v);
        f.Add(b, column, 0);
        f.W = column + b.W;
        f.Ic = b.Ic;
        f.A = Math.Max(b.A, u + sup.A);
        f.D = Math.Max(b.D, v + sub.D);
        return f;
    }

    private Frag Radical(MathNode n, MathProperties p, St st, int depth, uint color)
    {
        bool display = st.Level == 0;
        Frag content = Arg(n.Argument(MathRole.Base), st.AsCramped, depth);
        double gap = display ? K(MathConstant.RadicalDisplayStyleVerticalGap, st) : K(MathConstant.RadicalVerticalGap, st);
        double rule = K(MathConstant.RadicalRuleThickness, st);
        double extra = K(MathConstant.RadicalExtraAscender, st);
        double target = content.A + content.D + gap + rule;
        Frag sign = Stretchy(0x221A, target, true, st, color);
        double h = sign.A + sign.D;
        double slack = Math.Max(0, h - target);
        double originY = content.D + (slack / 2) - sign.D;
        double inkTop = originY - sign.A;
        var f = new Frag { Class = AtomClass.Ord };
        Frag? deg = p.HideDegree ? null : n.Argument(MathRole.Degree) is { Children.Count: > 0 } d ? Arg(d, new St(3, false), depth) : null;
        double degX = 0;
        double signX = 0;
        if (deg is not null)
        {
            double before = K(MathConstant.RadicalKernBeforeDegree, st);
            double afterKern = K(MathConstant.RadicalKernAfterDegree, st);
            degX = Math.Max(before, 0);
            signX = degX + deg.W + afterKern;
            if (signX < 0)
            {
                degX -= signX;
                signX = 0;
            }

            double raise = _table[MathConstant.RadicalDegreeBottomRaisePercent] / 100.0 * h;
            double baseDown = originY + sign.D - raise;
            f.Add(deg, degX, baseDown);
            _ = baseDown;
        }

        f.Add(sign, signX, originY);
        double cx = signX + sign.W;
        f.Rule(cx, inkTop, content.W, rule, color);
        f.Add(content, cx, 0);
        f.W = cx + content.W;
        f.A = Math.Max(-inkTop + extra, content.A);
        f.D = Math.Max(content.D, originY + sign.D);
        if (deg is not null)
        {
            double raise = _table[MathConstant.RadicalDegreeBottomRaisePercent] / 100.0 * h;
            double baseDown = originY + sign.D - raise;
            f.A = Math.Max(f.A, deg.A - baseDown);
        }

        return f;
    }

    private Frag Delimiter(MathNode n, MathProperties p, St st, int depth, uint color)
    {
        string open = p.OpenCharacter ?? "(";
        string close = p.CloseCharacter ?? ")";
        string sep = p.SeparatorCharacter ?? "|";
        var cells = new List<Frag>();
        foreach (MathNode c in n.Children)
        {
            cells.Add(Arg(c, st, depth));
        }

        if (cells.Count == 0)
        {
            cells.Add(new Frag());
        }

        double a = cells.Max(static c => c.A);
        double d = cells.Max(static c => c.D);
        double axis = K(MathConstant.AxisHeight, st);
        double delta = Math.Max(a - axis, d + axis);
        bool grow = p.Grow ?? true;
        double em = Size(st);
        double target = grow ? Math.Max(delta * 2 * 0.901, (delta * 2) - (em * 0.5)) : 0;
        var f = new Frag { Class = AtomClass.Ord };
        double x = 0;
        void Fence(string s)
        {
            if (s.Length == 0)
            {
                return;
            }

            int cp = char.ConvertToUtf32(s, 0);
            Frag g = grow ? Stretchy(cp, target, true, st, color) : CharFrag(cp, em, color, null);
            // Centre the fence on the math axis.
            double shift = ((g.A - g.D) / 2) - axis;
            f.Add(g, x, shift);
            x += g.W;
            f.A = Math.Max(f.A, g.A - shift);
            f.D = Math.Max(f.D, g.D + shift);
        }

        Fence(open);
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                Fence(sep);
            }

            f.Add(cells[i], x, 0);
            x += cells[i].W + cells[i].Ic;
            f.A = Math.Max(f.A, cells[i].A);
            f.D = Math.Max(f.D, cells[i].D);
        }

        Fence(close);
        f.W = x;
        return f;
    }

    private Frag NAry(MathNode n, MathProperties p, St st, int depth, uint color)
    {
        string chr = p.Character ?? "\u222B";
        int cp = chr.Length > 0 ? char.ConvertToUtf32(chr, 0) : 0;
        bool display = st.Level == 0;
        double axis = K(MathConstant.AxisHeight, st);
        Frag op = new();
        if (cp != 0)
        {
            double size = Size(st);
            if (display && MathText.IsLargeOperator(cp))
            {
                double min = K(MathConstant.DisplayOperatorMinHeight, st);
                op = Stretchy(cp, min, true, st, color);
            }
            else
            {
                op = CharFrag(cp, size, color, null);
            }

            double shift = ((op.A - op.D) / 2) - axis;
            var centred = new Frag { W = op.W, A = op.A - shift, D = op.D + shift, Ic = op.Ic, Class = AtomClass.Op };
            centred.Add(op, 0, shift);
            op = centred;
        }

        LimitLocation loc = p.LimitLocation;
        if (loc == LimitLocation.Default)
        {
            loc = MathText.IsIntegral(cp) ? _o.Settings.IntegralLimits : _o.Settings.NaryLimits;
        }

        if (!display)
        {
            loc = LimitLocation.SubSup;
        }

        Frag? sub = p.HideSubscript || n.Argument(MathRole.Subscript) is not { Children.Count: > 0 } sa ? null : Arg(sa, st.Sub, depth);
        Frag? sup = p.HideSuperscript || n.Argument(MathRole.Superscript) is not { Children.Count: > 0 } pa ? null : Arg(pa, st.Sup, depth);
        Frag head;
        if (loc == LimitLocation.UnderOver && (sub is not null || sup is not null))
        {
            head = Limits(op, sub, sup, st);
        }
        else if (sub is not null || sup is not null)
        {
            head = Scripts(op, sub, sup, st);
        }
        else
        {
            head = op;
        }

        Frag body = Arg(n.Argument(MathRole.Base), st, depth);
        var f = new Frag { Class = AtomClass.Op };
        f.Add(head, 0, 0);
        double thin = 3.0 / 18.0 * Size(st);
        double x = head.W + thin;
        f.Add(body, x, 0);
        f.W = x + body.W;
        f.A = Math.Max(head.A, body.A);
        f.D = Math.Max(head.D, body.D);
        f.Ic = body.Ic;
        return f;
    }

    // Limits above and below a base, centred (TeX rule 13a; OpenType MATH UpperLimit/LowerLimit constants).
    private Frag Limits(Frag b, Frag? lower, Frag? upper, St st)
    {
        var f = new Frag { Class = b.Class };
        double w = Math.Max(b.W, Math.Max(lower?.W ?? 0, upper?.W ?? 0));
        double bx = (w - b.W) / 2;
        f.Add(b, bx, 0);
        double a = b.A;
        double d = b.D;
        if (upper is not null)
        {
            double up = Math.Max(K(MathConstant.UpperLimitBaselineRiseMin, st) + b.A, b.A + K(MathConstant.UpperLimitGapMin, st) + upper.D);
            f.Add(upper, ((w - upper.W) / 2) + (b.Ic / 2), -up);
            a = up + upper.A;
        }

        if (lower is not null)
        {
            double down = Math.Max(K(MathConstant.LowerLimitBaselineDropMin, st) + b.D, b.D + K(MathConstant.LowerLimitGapMin, st) + lower.A);
            f.Add(lower, ((w - lower.W) / 2) - (b.Ic / 2), down);
            d = down + lower.D;
        }

        f.W = w;
        f.A = a;
        f.D = d;
        return f;
    }

    private Frag LimitObject(MathNode n, St st, int depth)
    {
        Frag b = Arg(n.Argument(MathRole.Base), st, depth);
        Frag lim = Arg(n.Argument(MathRole.Limit), st.Sub, depth);
        Frag f = n.Kind == MathKind.LowerLimit ? Limits(b, lim, null, st) : Limits(b, null, lim, st);
        f.Class = AtomClass.Op;
        return f;
    }

    private Frag Function(MathNode n, St st, int depth)
    {
        Frag name = Arg(n.Argument(MathRole.FunctionName), st, depth);
        Frag arg = Arg(n.Argument(MathRole.Base), st, depth);
        var f = new Frag { Class = AtomClass.Ord };
        f.Add(name, 0, 0);
        double x = name.W + name.Ic;
        // A function name is an operator-class atom: a thin space separates it from its argument (TeXbook, Op followed by Ord).
        if (arg.W > 0)
        {
            x += 3.0 / 18.0 * Size(st);
        }

        f.Add(arg, x, 0);
        f.W = x + arg.W;
        f.A = Math.Max(name.A, arg.A);
        f.D = Math.Max(name.D, arg.D);
        f.Ic = arg.Ic;
        return f;
    }

    // Combining accents map to the spacing characters math fonts carry glyphs for (TeX's accent set).
    private static int SpacingAccent(int cp) => cp switch
    {
        0x0300 => 0x0060,
        0x0301 => 0x00B4,
        0x0302 => 0x02C6,
        0x0303 => 0x02DC,
        0x0304 => 0x00AF,
        0x0305 => 0x00AF,
        0x0306 => 0x02D8,
        0x0307 => 0x02D9,
        0x0308 => 0x00A8,
        0x030A => 0x02DA,
        0x030C => 0x02C7,
        0x20D6 => 0x2190,
        0x20D7 => 0x2192,
        _ => cp,
    };

    private Frag Accent(MathNode n, MathProperties p, St st, int depth, uint color)
    {
        Frag b = Arg(n.Argument(MathRole.Base), st.AsCramped, depth);
        string chr = p.Character ?? "\u0302";
        if (chr.Length == 0)
        {
            return b;
        }

        int cp = SpacingAccent(char.ConvertToUtf32(chr, 0));
        Frag acc = CharFrag(cp, Size(st), color, null);
        bool single = b.IsChar;
        if (!single && b.W > acc.W * 1.05)
        {
            acc = Stretchy(cp, b.W, false, st, color);
        }

        double baseAttach = b.AccentX ?? (b.W / 2);
        double accAttach = acc.AccentX ?? (acc.W / 2);
        double dx = baseAttach - accAttach;
        double accentBase = K(MathConstant.AccentBaseHeight, st);
        double up = Math.Max(0, b.A - accentBase);
        var f = new Frag { Class = AtomClass.Ord };
        double shiftBase = Math.Max(0, -dx);
        f.Add(b, shiftBase, 0);
        f.Add(acc, dx + shiftBase, -up);
        f.W = Math.Max(b.W + shiftBase, dx + shiftBase + acc.W);
        f.A = Math.Max(b.A, up + acc.A);
        f.D = b.D;
        f.Ic = 0;
        return f;
    }

    private Frag BarObject(MathNode n, MathProperties p, St st, int depth, uint color)
    {
        Frag b = Arg(n.Argument(MathRole.Base), st, depth);
        var f = new Frag { Class = AtomClass.Ord };
        f.Add(b, 0, 0);
        f.W = b.W;
        f.A = b.A;
        f.D = b.D;
        if (p.Placement == MathPlacement.Top)
        {
            double gap = K(MathConstant.OverbarVerticalGap, st);
            double t = K(MathConstant.OverbarRuleThickness, st);
            f.Rule(0, -(b.A + gap + t), b.W, t, color);
            f.A = b.A + gap + t + K(MathConstant.OverbarExtraAscender, st);
        }
        else
        {
            double gap = K(MathConstant.UnderbarVerticalGap, st);
            double t = K(MathConstant.UnderbarRuleThickness, st);
            f.Rule(0, b.D + gap, b.W, t, color);
            f.D = b.D + gap + t + K(MathConstant.UnderbarExtraDescender, st);
        }

        return f;
    }

    private Frag GroupCharacter(MathNode n, MathProperties p, St st, int depth, uint color)
    {
        Frag b = Arg(n.Argument(MathRole.Base), st, depth);
        bool top = p.Placement == MathPlacement.Top;
        string chr = p.Character ?? (top ? "\u23DE" : "\u23DF");
        var f = new Frag { Class = AtomClass.Ord };
        f.Add(b, 0, 0);
        f.W = b.W;
        f.A = b.A;
        f.D = b.D;
        if (chr.Length == 0)
        {
            return f;
        }

        int cp = char.ConvertToUtf32(chr, 0);
        Frag g = Stretchy(cp, b.W, false, st, color);
        double gx = (b.W - g.W) / 2;
        if (top)
        {
            double gap = K(MathConstant.OverbarVerticalGap, st);
            double baseline = -(b.A + gap + g.D);
            f.Add(g, gx, baseline);
            f.A = b.A + gap + g.A + g.D;
        }
        else
        {
            double gap = K(MathConstant.UnderbarVerticalGap, st);
            double baseline = b.D + gap + g.A;
            f.Add(g, gx, baseline);
            f.D = b.D + gap + g.A + g.D;
        }

        f.W = Math.Max(b.W, g.W);
        return f;
    }

    private double RowPitch(MathProperties p, St st)
    {
        double em = Size(st);
        return p.RowSpacingRule switch
        {
            MathSpacingRule.OneAndHalf => em * 1.2 * 1.5,
            MathSpacingRule.DoubleLine => em * 1.2 * 2,
            MathSpacingRule.Exactly => p.RowSpacing / 20.0,
            MathSpacingRule.Multiple => em * 1.2 * Math.Max(p.RowSpacing, 1) / 2.0,
            _ => em * 1.2,
        };
    }

    private double ColumnGap(MathProperties p, St st)
    {
        double em = Size(st);
        return p.ColumnGapRule switch
        {
            MathSpacingRule.OneAndHalf => em * 1.5,
            MathSpacingRule.DoubleLine => em * 2,
            MathSpacingRule.Exactly => (p.ColumnSpacing > 0 ? p.ColumnSpacing : p.ColumnGap) / 20.0,
            MathSpacingRule.Multiple => em * Math.Max(p.ColumnGap, 1) / 2.0,
            _ => em,
        };
    }

    private Frag Matrix(MathNode n, MathProperties p, St st, int depth)
    {
        St cell = st.Level == 0 ? new St(1, st.Cramped) : st;
        var rows = new List<List<Frag>>();
        foreach (MathNode r in n.Children)
        {
            var row = new List<Frag>();
            foreach (MathNode c in r.Children)
            {
                row.Add(Arg(c, cell, depth));
            }

            rows.Add(row);
        }

        int cols = rows.Count == 0 ? 0 : rows.Max(static r => r.Count);
        var just = new MathJustification[cols];
        int k = 0;
        foreach (MatrixColumnSpec spec in p.Columns)
        {
            for (int i = 0; i < spec.Count && k < cols; i++)
            {
                just[k++] = spec.Justification;
            }
        }

        var widths = new double[cols];
        foreach (List<Frag> r in rows)
        {
            for (int c = 0; c < r.Count; c++)
            {
                widths[c] = Math.Max(widths[c], r[c].W);
            }
        }

        double gap = ColumnGap(p, cell);
        double pitch = RowPitch(p, cell);
        var f = new Frag { Class = AtomClass.Ord };
        var colX = new double[cols];
        double x = 0;
        for (int c = 0; c < cols; c++)
        {
            colX[c] = x;
            x += widths[c] + (c < cols - 1 ? gap : 0);
        }

        double y = 0;
        double firstA = 0;
        double lastBaseline = 0;
        for (int ri = 0; ri < rows.Count; ri++)
        {
            List<Frag> r = rows[ri];
            double a = Math.Max(r.Count == 0 ? 0 : r.Max(static c => c.A), 0.7 * pitch);
            double d = Math.Max(r.Count == 0 ? 0 : r.Max(static c => c.D), 0.3 * pitch);
            if (ri == 0)
            {
                firstA = a;
            }
            else
            {
                y += a;
            }

            double rowY = y;
            lastBaseline = rowY;
            for (int c = 0; c < r.Count; c++)
            {
                double dx = just[c] switch
                {
                    MathJustification.Left => 0,
                    MathJustification.Right => widths[c] - r[c].W,
                    _ => (widths[c] - r[c].W) / 2,
                };
                f.Add(r[c], colX[c] + dx, rowY);
            }

            y += d;
        }

        double total = firstA + y;
        double delta = (p.BaseJustification ?? MathVerticalAlignment.Center) switch
        {
            MathVerticalAlignment.Top => 0,
            MathVerticalAlignment.Bottom => -lastBaseline,
            _ => firstA - (total / 2) - K(MathConstant.AxisHeight, st),
        };
        var shifted = new Frag { Class = AtomClass.Ord, W = x };
        shifted.Add(f, 0, delta);
        shifted.A = firstA - delta;
        shifted.D = total - firstA + delta;
        return shifted;
    }

    private Frag EquationArray(MathNode n, MathProperties p, St st, int depth)
    {
        var rows = new List<List<Frag>>();
        bool anyMarks = false;
        foreach (MathNode r in n.Children)
        {
            List<Atom> atoms = Gather(r.Children, st, depth);
            Convert(atoms);
            var segs = new List<Frag>();
            int start = 0;
            for (int i = 0; i <= atoms.Count; i++)
            {
                if (i == atoms.Count || atoms[i].Mark)
                {
                    if (i < atoms.Count || start > 0 || atoms.Any(static a => a.Mark))
                    {
                        segs.Add(Compose(atoms, start, i, st));
                    }
                    else
                    {
                        segs.Add(Compose(atoms, start, i, st));
                    }

                    start = i + 1;
                    if (i < atoms.Count)
                    {
                        anyMarks = true;
                    }
                }
            }

            rows.Add(segs);
        }

        int cols = rows.Count == 0 ? 0 : rows.Max(static r => r.Count);
        var widths = new double[cols];
        foreach (List<Frag> r in rows)
        {
            for (int c = 0; c < r.Count; c++)
            {
                widths[c] = Math.Max(widths[c], r[c].W);
            }
        }

        double pitch = RowPitch(p, st);
        var f = new Frag { Class = AtomClass.Ord };
        double total = widths.Sum();
        double y = 0;
        double firstA = 0;
        double lastBaseline = 0;
        for (int ri = 0; ri < rows.Count; ri++)
        {
            List<Frag> r = rows[ri];
            double a = Math.Max(r.Count == 0 ? 0 : r.Max(static c => c.A), 0.7 * pitch);
            double d = Math.Max(r.Count == 0 ? 0 : r.Max(static c => c.D), 0.3 * pitch);
            if (ri == 0)
            {
                firstA = a;
            }
            else
            {
                y += a;
            }

            lastBaseline = y;
            if (!anyMarks)
            {
                f.Add(r[0], (total - r[0].W) / 2, y);
            }
            else
            {
                double x = 0;
                for (int c = 0; c < cols; c++)
                {
                    if (c < r.Count)
                    {
                        // Even segments end at the alignment point, odd ones start at it (AMS-style alignment).
                        double dx = c % 2 == 0 ? widths[c] - r[c].W : 0;
                        f.Add(r[c], x + dx, y);
                    }

                    x += widths[c];
                }
            }

            y += d;
        }

        double height = firstA + y;
        double delta = (p.BaseJustification ?? MathVerticalAlignment.Center) switch
        {
            MathVerticalAlignment.Top => 0,
            MathVerticalAlignment.Bottom => -lastBaseline,
            _ => firstA - (height / 2) - K(MathConstant.AxisHeight, st),
        };
        var shifted = new Frag { Class = AtomClass.Ord, W = total };
        shifted.Add(f, 0, delta);
        shifted.A = firstA - delta;
        shifted.D = height - firstA + delta;
        return shifted;
    }

    private Frag BorderBox(MathNode n, MathProperties p, St st, int depth, uint color)
    {
        Frag b = Arg(n.Argument(MathRole.Base), st, depth);
        double t = Math.Max(K(MathConstant.UnderbarRuleThickness, st), 0.4);
        double pad = Size(st) * 0.1;
        var f = new Frag { Class = AtomClass.Ord };
        double w = b.W + (2 * pad);
        double top = -(b.A + pad + t);
        double bottom = b.D + pad;
        f.Add(b, pad, 0);
        if (!p.HideTop)
        {
            f.Rule(0, top, w, t, color);
        }

        if (!p.HideBottom)
        {
            f.Rule(0, bottom, w, t, color);
        }

        if (!p.HideLeft)
        {
            f.Rule(0, top, t, bottom + t - top, color);
        }

        if (!p.HideRight)
        {
            f.Rule(w - t, top, t, bottom + t - top, color);
        }

        double midY = (top + bottom + t) / 2;
        if (p.StrikeHorizontal)
        {
            f.Rule(0, midY - (t / 2), w, t, color);
        }

        if (p.StrikeVertical)
        {
            f.Rule((w - t) / 2, top, t, bottom + t - top, color);
        }

        if (p.StrikeBottomLeftToTopRight)
        {
            f.Items.Add(new MathStroke(0, bottom + t, w, top, t, color));
        }

        if (p.StrikeTopLeftToBottomRight)
        {
            f.Items.Add(new MathStroke(0, top, w, bottom + t, t, color));
        }

        f.W = w;
        f.A = b.A + pad + t;
        f.D = bottom + t;
        return f;
    }

    private Frag Phantom(MathNode n, MathProperties p, St st, int depth)
    {
        Frag b = Arg(n.Argument(MathRole.Base), st, depth);
        var f = new Frag { Class = b.Class, W = p.ZeroWidth ? 0 : b.W, A = p.ZeroAscent ? 0 : b.A, D = p.ZeroDescent ? 0 : b.D };
        if (p.Show)
        {
            f.Add(b, 0, 0);
        }

        return f;
    }

    // ---- Display breaking ----

    // Greedy breaking at binary operators and relations (and manual breaks) per m:brkBin; continuation lines are indented by
    // m:wrapIndent (ECMA-376 Part 1 §22.1.2.12, §22.1.2.123).
    private (Frag Whole, int Lines) Wrap(List<Atom> atoms, St st, double avail)
    {
        MathSettings s = _o.Settings;
        var lines = new List<(Frag Frag, double Indent)>();
        int start = 0;
        int n = atoms.Count;
        bool repeatNext = false;
        Atom? repeated = null;
        while (start < n || lines.Count == 0)
        {
            double indent = lines.Count == 0 ? 0 : Math.Min(s.WrapIndent, Math.Max(avail * 0.5, 0));
            double limit = double.IsInfinity(avail) ? double.PositiveInfinity : avail - indent;
            int end = start;
            int lastBreak = -1;
            double x = 0;
            Atom? prev = null;
            if (repeatNext && repeated is not null)
            {
                x = repeated.Frag.W;
                prev = repeated;
            }

            while (end < n)
            {
                Atom a = atoms[end];
                if (a.Break && end > start)
                {
                    break;
                }

                if (a.Mark)
                {
                    end++;
                    continue;
                }

                double add = a.Frag.W + (prev is null ? 0 : prev.Frag.Ic + (MathText.SpaceBetween(prev.Class, a.Class, st.Script) * Size(st)));
                if (x + add > limit && end > start)
                {
                    break;
                }

                bool opportunity = a.Class is AtomClass.Bin or AtomClass.Rel;
                if (end > start && opportunity && s.BreakBinary == BinaryBreak.Before)
                {
                    lastBreak = end;
                }
                else if (end > start && prev is { Class: AtomClass.Bin or AtomClass.Rel } && s.BreakBinary != BinaryBreak.Before)
                {
                    lastBreak = end;
                }

                x += add;
                prev = a;
                end++;
            }

            if (end < n && !(atoms[end].Break))
            {
                // Overflowed: break at the last opportunity if there was one, else here.
                if (lastBreak > start)
                {
                    end = lastBreak;
                }
            }

            if (end <= start)
            {
                end = Math.Min(start + 1, n);
            }

            var part = new List<Atom>();
            if (repeatNext && repeated is not null)
            {
                part.Add(repeated);
            }

            part.AddRange(atoms.GetRange(start, end - start));
            lines.Add((Compose(part, 0, part.Count, st), indent));
            repeatNext = false;
            if (s.BreakBinary == BinaryBreak.Repeat && end < n && end > start)
            {
                Atom lastAtom = atoms[end - 1];
                if (lastAtom.Class is AtomClass.Bin or AtomClass.Rel)
                {
                    repeatNext = true;
                    repeated = lastAtom;
                }
            }

            start = end;
            if (start >= n)
            {
                break;
            }
        }

        var whole = new Frag();
        double y = 0;
        double width = 0;
        double lineGap = Math.Max(K(MathConstant.MathLeading, st), Size(st) * 0.1);
        for (int i = 0; i < lines.Count; i++)
        {
            (Frag lf, double indent) = lines[i];
            if (i > 0)
            {
                y += lines[i - 1].Frag.D + lineGap + lf.A;
            }

            double x = s.WrapRight && i > 0 && !double.IsInfinity(avail) ? Math.Max(indent, avail - lf.W) : indent;
            whole.Add(lf, x, y);
            width = Math.Max(width, x + lf.W);
        }

        whole.W = width;
        whole.A = lines[0].Frag.A;
        whole.D = y + lines[^1].Frag.D;
        return (whole, lines.Count);
    }
}
