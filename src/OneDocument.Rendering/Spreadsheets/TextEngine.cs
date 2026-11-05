using RecursiveAI.OneDocument.Layout;
using OneFont;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering.Spreadsheets;

/// <summary>A character style as the cell text engine uses it.</summary>
internal readonly record struct TextStyle(string Family, double Size, bool Bold, bool Italic, bool Strike, FontUnderline Underline, FontVerticalAlignment Vertical, uint Rgb);

/// <summary>Glyphs of one style on one line, in visual order, ready for a glyph-run item. Positions are points from the chunk's own origin.</summary>
internal sealed class GlyphChunk
{
    public required FontFace Face;
    public required double Size;
    public required int[] GlyphIds;
    public required float[] X;
    public required float[] Y;
    public required int[] Clusters;
    public required string Text;
    public required bool RightToLeft;
    public required TextStyle Style;
    public double Start;   // x of the chunk's origin from the line's left edge
    public double Width;
    public double BaselineShift; // positive raises the text (superscript)
}

internal sealed class TextLine
{
    public List<GlyphChunk> Chunks { get; } = [];
    public double Width;
    public double Ascent;
    public double Descent;
    public int Spaces;
    public bool IsLast;
    public double Height => Ascent + Descent;
}

internal sealed class TextBlock
{
    public List<TextLine> Lines { get; } = [];
    public double Width;
    public double Height;
    public bool IsRightToLeft;
}

/// <summary>
/// Shapes and wraps the text of a cell with OneFont. A cell's text is a few runs with their own fonts; lines break at Unicode line-break
/// opportunities, and a word wider than the line breaks between characters, as Excel does.
/// </summary>
internal sealed class TextEngine(IFontProvider fonts)
{
    private readonly Dictionary<(string, bool, bool), FontMatch?> _matches = [];
    private readonly HashSet<(string, string)> _noted = [];
    private readonly ShapedRun _scratch = new();

    public List<FontSubstitutionNote> Notes { get; } = [];

    public List<string> Missing { get; } = [];

    public FontMatch? Resolve(string family, bool bold, bool italic)
    {
        if (_matches.TryGetValue((family, bold, italic), out FontMatch? m))
        {
            return m;
        }

        m = fonts.Resolve(new FontRequest(family, bold, italic));
        _matches[(family, bold, italic)] = m;
        if (m is null)
        {
            Missing.Add(family);
        }
        else if (!m.IsExact && _noted.Add((m.RequestedFamily, m.ActualFamily)))
        {
            Notes.Add(new FontSubstitutionNote(m.RequestedFamily, m.ActualFamily, m.Kind));
        }

        return m;
    }

    // Ascent, descent and line gap in points from the font's own metrics.
    public (double Ascent, double Descent, double Gap) LineMetrics(in TextStyle style)
    {
        FontMatch? m = Resolve(style.Family, style.Bold, style.Italic);
        if (m is null)
        {
            return (style.Size * 0.8, style.Size * 0.2, 0);
        }

        FontMetrics fm = m.Face.Metrics;
        double k = EffectiveSize(style) / fm.UnitsPerEm;
        return (fm.Ascender * k, Math.Abs(fm.Descender) * k, fm.LineGap * k);
    }

    public static double EffectiveSize(in TextStyle style) => style.Vertical == FontVerticalAlignment.Baseline ? style.Size : style.Size * 2 / 3;

    // The width of a string on one line, in points.
    public double Measure(string text, in TextStyle style)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        return Layout([(text, style)], double.PositiveInfinity, false, null).Width;
    }

    private sealed class Segment
    {
        public int Span;
        public int Start;   // in the full text
        public int Length;
        public byte Level;
        public FontMatch? Match;
        public FontFace[] Faces = [];
        public int ClusterCount;
        public int[] CStart = [];
        public int[] CLength = [];
        public int[] CFace = [];
        public int[] CGlyphStart = [];
        public int[] CGlyphCount = [];
        public double[] CAdv = [];
        public int[] GlyphId = [];
        public int[] GXAdv = [];
        public int[] GXOff = [];
        public int[] GYOff = [];
        public double Size;
    }

    private struct Atom
    {
        public int Segment;
        public int Cluster;
        public int TextStart;
        public double Width;
        public bool Space;
        public bool Newline;
        public bool BreakBefore;
        public byte Level;
    }

    /// <summary>Lays out runs of text.</summary>
    /// <param name="spans">The runs: text and style.</param>
    /// <param name="maxWidth">The line width in points; infinity for one line per paragraph.</param>
    /// <param name="forceRightToLeft">True to make the base direction right to left; false lets the first strong character decide.</param>
    /// <param name="language">The language tag for shaping, or null.</param>
    public TextBlock Layout(IReadOnlyList<(string Text, TextStyle Style)> spans, double maxWidth, bool forceRightToLeft, string? language)
    {
        var block = new TextBlock();
        var sb = new System.Text.StringBuilder();
        var spanStart = new int[spans.Count + 1];
        for (int i = 0; i < spans.Count; i++)
        {
            spanStart[i] = sb.Length;
            sb.Append(spans[i].Text);
        }

        spanStart[spans.Count] = sb.Length;
        string text = sb.ToString();
        if (text.Length == 0)
        {
            return block;
        }

        byte[] levels = TextShaping.GetBidiLevels(text, forceRightToLeft ? TextDirection.RightToLeft : TextDirection.Auto, out int paragraphLevel);
        block.IsRightToLeft = (paragraphLevel & 1) == 1;
        LineBreakOpportunity[] opps = TextSegmentation.GetLineBreakOpportunities(text);

        // Segments: one per run and bidi level, and a newline splits too.
        var segments = new List<Segment>();
        for (int s = 0; s < spans.Count; s++)
        {
            int a = spanStart[s];
            int b = spanStart[s + 1];
            int i = a;
            while (i < b)
            {
                int j = i + 1;
                if (text[i] != '\n')
                {
                    while (j < b && text[j] != '\n' && levels[j] == levels[i])
                    {
                        j++;
                    }
                }

                var seg = new Segment { Span = s, Start = i, Length = j - i, Level = levels[i] };
                ShapeSegment(seg, text, spans[s].Style, language);
                segments.Add(seg);
                i = j;
            }
        }

        var atoms = new List<Atom>(text.Length);
        for (int sIdx = 0; sIdx < segments.Count; sIdx++)
        {
            Segment seg = segments[sIdx];
            if (text[seg.Start] == '\n')
            {
                atoms.Add(new Atom { Segment = sIdx, Cluster = -1, TextStart = seg.Start, Newline = true, Level = (byte)paragraphLevel });
                continue;
            }

            if (seg.Match is null)
            {
                continue;
            }

            for (int c = 0; c < seg.ClusterCount; c++)
            {
                int ts = seg.Start + seg.CStart[c];
                atoms.Add(new Atom
                {
                    Segment = sIdx,
                    Cluster = c,
                    TextStart = ts,
                    Width = seg.CAdv[c],
                    Space = text[ts] == ' ',
                    BreakBefore = opps[ts] != LineBreakOpportunity.None,
                    Level = seg.Level,
                });
            }
        }

        // Greedy line breaking. Spaces at the end of a line hang and do not count.
        int start = 0;
        while (true)
        {
            double w = 0;
            double wAtBreak = 0;
            int lastBreak = -1;
            int end = start;
            bool hard = false;
            double trailing = 0;
            for (; end < atoms.Count; end++)
            {
                Atom at = atoms[end];
                if (at.Newline)
                {
                    hard = true;
                    break;
                }

                if (end > start && at.BreakBefore)
                {
                    lastBreak = end;
                    wAtBreak = w - trailing;
                }

                if (!at.Space && w - trailing + at.Width > maxWidth + 1e-6 && end > start)
                {
                    if (lastBreak > start)
                    {
                        end = lastBreak;
                        w = wAtBreak;
                    }

                    break;
                }

                trailing = at.Space ? trailing + at.Width : 0;
                w += at.Width;
            }

            int lineEnd = end;
            int last = lineEnd;
            while (last > start && atoms[last - 1].Space)
            {
                last--;
            }

            TextLine line = BuildLine(atoms, start, last, segments, text, spans, levels, paragraphLevel);
            line.IsLast = hard || lineEnd >= atoms.Count;
            block.Lines.Add(line);
            if (hard)
            {
                start = lineEnd + 1;
                if (start >= atoms.Count)
                {
                    // A trailing newline leaves an empty last line, as the cell editor does.
                    block.Lines.Add(new TextLine { IsLast = true, Ascent = line.Ascent, Descent = line.Descent });
                    break;
                }
            }
            else
            {
                if (lineEnd >= atoms.Count)
                {
                    break;
                }

                start = lineEnd;
                while (start < atoms.Count && atoms[start].Space)
                {
                    start++;
                }

                if (start >= atoms.Count)
                {
                    break;
                }
            }
        }

        foreach (TextLine l in block.Lines)
        {
            block.Width = Math.Max(block.Width, l.Width);
            block.Height += l.Height;
        }

        return block;
    }

    private void ShapeSegment(Segment seg, string text, in TextStyle style, string? language)
    {
        if (text[seg.Start] == '\n')
        {
            return;
        }

        FontMatch? match = Resolve(style.Family, style.Bold, style.Italic);
        seg.Match = match;
        seg.Size = EffectiveSize(style);
        if (match is null)
        {
            return;
        }

        string shown = text.Substring(seg.Start, seg.Length);
        var faces = new List<FontFace> { match.Face };
        for (int k = 0; k < shown.Length; k++)
        {
            int cp = char.ConvertToUtf32(shown, k);
            if (char.IsHighSurrogate(shown[k]))
            {
                k++;
            }

            if (cp <= 0x20 || match.Face.HasGlyph(cp))
            {
                continue;
            }

            FontFace? fb = null;
            foreach (FontFace f in faces)
            {
                if (f.HasGlyph(cp))
                {
                    fb = f;
                    break;
                }
            }

            fb ??= fonts.FindFallback(cp, style.Bold, style.Italic);
            if (fb is not null && !faces.Contains(fb) && faces.Count < FontFallbackChain.MaxFaces)
            {
                faces.Add(fb);
            }
        }

        seg.Faces = [.. faces];
        var options = new ShapingOptions { Kerning = KerningMode.Normal, Language = language };
        options.Features.Add(FontFeature.Off("liga"));
        options.Features.Add(FontFeature.Off("clig"));
        bool rtl = (seg.Level & 1) == 1;
        ShapedRun run = _scratch;
        if (faces.Count == 1)
        {
            TextShaping.ShapeRun(shown, match.Face, run, rtl, options);
        }
        else
        {
            new FontFallbackChain(faces).ShapeRun(shown, run, rtl, options);
        }

        int n = run.ClusterCount;
        seg.ClusterCount = n;
        seg.CStart = new int[n];
        seg.CLength = new int[n];
        seg.CFace = new int[n];
        seg.CGlyphStart = new int[n];
        seg.CGlyphCount = new int[n];
        seg.CAdv = new double[n];
        ReadOnlySpan<ShapedClusterRange> clusters = run.Clusters;
        for (int c = 0; c < n; c++)
        {
            ShapedClusterRange cr = clusters[c];
            seg.CStart[c] = cr.Start;
            seg.CLength[c] = cr.Length;
            seg.CFace[c] = cr.FaceIndex;
            seg.CGlyphStart[c] = cr.GlyphStart;
            seg.CGlyphCount[c] = cr.GlyphCount;
            seg.CAdv[c] = (double)cr.Advance * seg.Size / faces[cr.FaceIndex].UnitsPerEm;
        }

        int g = run.GlyphCount;
        seg.GlyphId = new int[g];
        seg.GXAdv = new int[g];
        seg.GXOff = new int[g];
        seg.GYOff = new int[g];
        ReadOnlySpan<ShapedGlyph> glyphs = run.Glyphs;
        for (int k = 0; k < g; k++)
        {
            seg.GlyphId[k] = glyphs[k].GlyphId;
            seg.GXAdv[k] = glyphs[k].XAdvance;
            seg.GXOff[k] = glyphs[k].XOffset;
            seg.GYOff[k] = glyphs[k].YOffset;
        }
    }

    private TextLine BuildLine(List<Atom> atoms, int from, int to, List<Segment> segments, string text, IReadOnlyList<(string Text, TextStyle Style)> spans, byte[] levels, int paragraphLevel)
    {
        var line = new TextLine();
        int n = to - from;
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = from + i;
        }

        // UAX #9 rule L2: from the highest level down to the lowest odd one, reverse every run at that level or higher.
        int maxLevel = 0;
        int minOdd = int.MaxValue;
        foreach (int idx in order)
        {
            int lv = atoms[idx].Level;
            maxLevel = Math.Max(maxLevel, lv);
            if ((lv & 1) == 1)
            {
                minOdd = Math.Min(minOdd, lv);
            }
        }

        for (int lv = maxLevel; lv >= minOdd && lv > 0; lv--)
        {
            int i = 0;
            while (i < n)
            {
                if (atoms[order[i]].Level < lv)
                {
                    i++;
                    continue;
                }

                int j = i;
                while (j < n && atoms[order[j]].Level >= lv)
                {
                    j++;
                }

                Array.Reverse(order, i, j - i);
                i = j;
            }
        }

        double pen = 0;
        int p = 0;
        while (p < n)
        {
            Atom first = atoms[order[p]];
            Segment seg = segments[first.Segment];
            int q = p + 1;
            while (q < n && atoms[order[q]].Segment == first.Segment)
            {
                q++;
            }

            // order[p..q] are atoms of one segment in visual order: one chunk.
            bool rtl = (seg.Level & 1) == 1;
            var ids = new List<int>();
            var xs = new List<float>();
            var ys = new List<float>();
            var cl = new List<int>();
            // Logical text of the chunk: left to right for even levels, reversed atom order for odd levels.
            var chunkText = new System.Text.StringBuilder();
            var atomOffset = new int[q - p];
            if (rtl)
            {
                for (int k = q - 1; k >= p; k--)
                {
                    atomOffset[k - p] = chunkText.Length;
                    AppendAtomText(chunkText, atoms[order[k]], seg, text);
                }
            }
            else
            {
                for (int k = p; k < q; k++)
                {
                    atomOffset[k - p] = chunkText.Length;
                    AppendAtomText(chunkText, atoms[order[k]], seg, text);
                }
            }

            double chunkStart = pen;
            TextStyle style = spans[seg.Span].Style;
            for (int k = p; k < q; k++)
            {
                Atom at = atoms[order[k]];
                int fi = seg.CFace[at.Cluster];
                double scale = seg.Size / seg.Faces[fi].UnitsPerEm;
                for (int gi = 0; gi < seg.CGlyphCount[at.Cluster]; gi++)
                {
                    int g = seg.CGlyphStart[at.Cluster] + gi;
                    ids.Add(seg.GlyphId[g]);
                    xs.Add((float)(pen - chunkStart + (seg.GXOff[g] * scale)));
                    ys.Add((float)(-seg.GYOff[g] * scale));
                    cl.Add(atomOffset[k - p]);
                    pen += seg.GXAdv[g] * scale;
                }

                // A cluster the font could not draw still takes the width the shaper gave it.
                if (seg.CGlyphCount[at.Cluster] == 0)
                {
                    pen += at.Width;
                }
            }

            double shift = style.Vertical switch { FontVerticalAlignment.Superscript => style.Size * 0.33, FontVerticalAlignment.Subscript => -style.Size * 0.15, _ => 0 };
            var chunk = new GlyphChunk
            {
                Face = seg.Faces[0],
                Size = seg.Size,
                GlyphIds = [.. ids],
                X = [.. xs],
                Y = [.. ys],
                Clusters = [.. cl],
                Text = chunkText.ToString(),
                RightToLeft = rtl,
                Style = style,
                Start = chunkStart,
                Width = pen - chunkStart,
                BaselineShift = shift,
            };

            // Glyphs from a fallback face need their own run: split by face when more than one was used.
            SplitByFace(line, chunk, seg, order, p, q, atoms);
            (double asc, double desc, double _) = LineMetrics(style);
            line.Ascent = Math.Max(line.Ascent, asc + Math.Max(0, shift));
            line.Descent = Math.Max(line.Descent, desc);
            for (int k = p; k < q; k++)
            {
                if (atoms[order[k]].Space)
                {
                    line.Spaces++;
                }
            }

            p = q;
        }

        line.Width = pen;
        if (n == 0 || line.Ascent == 0)
        {
            // An empty line takes its height from the style of the text around it.
            TextStyle st = spans[0].Style;
            (double a, double d, double _) = LineMetrics(st);
            line.Ascent = Math.Max(line.Ascent, a);
            line.Descent = Math.Max(line.Descent, d);
        }

        return line;
    }

    private static void AppendAtomText(System.Text.StringBuilder sb, in Atom at, Segment seg, string text) =>
        sb.Append(text, seg.Start + seg.CStart[at.Cluster], seg.CLength[at.Cluster]);

    // When the shaper used a fallback face for some clusters, the single glyph run would draw them with the wrong face.
    // The chunk is cut into runs of equal face.
    private static void SplitByFace(TextLine line, GlyphChunk chunk, Segment seg, int[] order, int p, int q, List<Atom> atoms)
    {
        bool single = true;
        for (int k = p; k < q; k++)
        {
            if (seg.CFace[atoms[order[k]].Cluster] != 0)
            {
                single = false;
                break;
            }
        }

        if (single)
        {
            line.Chunks.Add(chunk);
            return;
        }

        // Re-walk the glyphs: each atom's glyphs go to the chunk of its face.
        int gi0 = 0;
        int k0 = p;
        while (k0 < q)
        {
            int face = seg.CFace[atoms[order[k0]].Cluster];
            int k1 = k0;
            int glyphs = 0;
            while (k1 < q && seg.CFace[atoms[order[k1]].Cluster] == face)
            {
                glyphs += seg.CGlyphCount[atoms[order[k1]].Cluster];
                k1++;
            }

            int[] ids = chunk.GlyphIds[gi0..(gi0 + glyphs)];
            float[] xs = chunk.X[gi0..(gi0 + glyphs)];
            float[] ys = chunk.Y[gi0..(gi0 + glyphs)];
            int[] cl = chunk.Clusters[gi0..(gi0 + glyphs)];
            float x0 = xs.Length > 0 ? xs[0] : 0;
            for (int i = 0; i < xs.Length; i++)
            {
                xs[i] -= x0;
            }

            double width = (gi0 + glyphs < chunk.X.Length ? chunk.X[gi0 + glyphs] : (float)chunk.Width) - x0;
            line.Chunks.Add(new GlyphChunk
            {
                Face = seg.Faces[face],
                Size = chunk.Size,
                GlyphIds = ids,
                X = xs,
                Y = ys,
                Clusters = cl,
                Text = chunk.Text,
                RightToLeft = chunk.RightToLeft,
                Style = chunk.Style,
                Start = chunk.Start + x0,
                Width = width,
                BaselineShift = chunk.BaselineShift,
            });
            gi0 += glyphs;
            k0 = k1;
        }
    }
}
