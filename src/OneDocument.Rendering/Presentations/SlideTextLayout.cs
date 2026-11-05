using OneFont;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Rendering.Presentations;

/// <summary>A stretch of text with one set of character properties.</summary>
internal sealed record SlideTextRun(string Text, SlideRunStyle Style);

/// <summary>A paragraph ready for layout: its properties, runs and the bullet text already worked out.</summary>
internal sealed class SlideTextParagraph
{
    public required SlideParagraphStyle Style;
    public List<SlideTextRun> Runs = [];
    public required SlideRunStyle EndStyle;

    /// <summary>The bullet or number text, or null for none. A picture bullet is drawn as a round bullet.</summary>
    public string? Label;
    public DrawingColor? LabelColor;
    public string? LabelFont;
    public double LabelSizeFactor = 1.0;
    public double? LabelSizePoints;
}

/// <summary>One drawn run of glyphs on a line, positioned relative to the text rectangle.</summary>
internal sealed class PlacedRun
{
    public required FontFace Face;
    public double Size;
    public int[] Glyphs = [];
    public float[] X = [];
    public float[] Y = [];
    public int[] Clusters = [];
    public string Text = string.Empty;
    public bool Rtl;
    public DrawingColor Color;
    public double Left;
    public double Width;
    public double Raise;
    public string? Underline;
    public string? Strike;
    public DrawingColor? Highlight;
    public string? LinkRelationship;
    public string? Language;
    public bool IsLabel;
}

internal sealed class PlacedLine
{
    public List<PlacedRun> Runs = [];
    public double Left;
    public double Top;
    public double Height;
    public double Baseline;
    public double Right;
    public int Column;
}

internal sealed class PlacedParagraph
{
    public required SlideTextParagraph Source;
    public List<PlacedLine> Lines = [];
    public double Top;
    public double Bottom;
}

internal sealed class PlacedText
{
    public List<PlacedParagraph> Paragraphs = [];

    /// <summary>The height of the tallest column in points, spacing included.</summary>
    public double Height;

    /// <summary>The widest line's right edge less the leftmost line's left edge.</summary>
    public double Left;
    public double Right;
}

/// <summary>What the text layout needs besides the paragraphs.</summary>
internal sealed class TextLayoutSettings
{
    public double Width;
    public double Height;
    public bool Wrap = true;
    public int Columns = 1;
    public double ColumnSpacing;
    public double FontScale = 1.0;

    /// <summary>The fraction taken off percentage line spacing (autofit's lnSpcReduction).</summary>
    public double LineReduction;
}

/// <summary>
/// Lays out DrawingML paragraphs: shaping with OneFont, line breaking at Unicode opportunities, bullets, indents, tab stops, alignment
/// and justification, line and paragraph spacing, columns. Positions are points relative to the text rectangle.
/// </summary>
internal sealed class SlideTextLayout(IFontProvider fonts, List<FontSubstitutionNote> fontNotes)
{
    private const double SmallCapsScale = 0.8;
    private const double ScriptScale = 0.65;
    private readonly Dictionary<(string, bool, bool), FontMatch?> _fontCache = [];
    private readonly HashSet<(string, string)> _noted = [];
    private readonly ShapedRun _scratch = new();

    private enum AtomKind
    {
        Glyphs,
        Space,
        Tab,
        LineBreak,
    }

    private sealed class Piece
    {
        public int Start;
        public string Text = string.Empty;
        public string? Shown;
        public SlideRunStyle Style = new();
        public double Size;
        public byte Level;
        public FontMatch? Font;
        public FontFace[] Faces = [];
        public int ClusterCount;
        public int[] CStart = [];
        public int[] CLength = [];
        public int[] CFace = [];
        public int[] CGlyphStart = [];
        public int[] CGlyphCount = [];
        public double[] CAdv = [];
        public int[] GlyphId = [];
        public int[] GXOff = [];
        public int[] GYOff = [];
        public double Ascent;
        public double Descent;
        public bool IsLabel;
        public double Raise;
    }

    private struct Atom
    {
        public int Piece;
        public int Cluster;
        public int TextStart;
        public double Width;
        public byte Level;
        public bool BreakBefore;
        public AtomKind Kind;
    }

    /// <summary>Lays the paragraphs out.</summary>
    public PlacedText Layout(IReadOnlyList<SlideTextParagraph> paragraphs, TextLayoutSettings s)
    {
        int columns = Math.Clamp(s.Columns, 1, 16);
        double colSpacing = Math.Max(0, s.ColumnSpacing);
        double colWidth = columns == 1 ? s.Width : Math.Max(1, (s.Width - ((columns - 1) * colSpacing)) / columns);
        var result = new PlacedText();
        double y = 0;
        double pendingAfter = 0;
        bool first = true;
        var all = new List<(PlacedLine Line, double SpaceAbove)>();
        foreach (SlideTextParagraph p in paragraphs)
        {
            var placed = new PlacedParagraph { Source = p };
            List<PlacedLine> lines = LayoutParagraph(p, colWidth, s);
            double before = Spacing(p.Style.SpaceBefore, p, lines);
            double after = Spacing(p.Style.SpaceAfter, p, lines);
            // Space before the first paragraph of a body is not applied (to check against PowerPoint).
            double gap = first ? 0 : Math.Max(pendingAfter + before, 0);
            placed.Top = y + gap;
            for (int i = 0; i < lines.Count; i++)
            {
                all.Add((lines[i], i == 0 ? gap : 0));
                placed.Lines.Add(lines[i]);
            }

            result.Paragraphs.Add(placed);
            y = placed.Top;
            foreach (PlacedLine l in lines)
            {
                y += l.Height;
            }

            placed.Bottom = y;
            pendingAfter = after;
            first = false;
        }

        // Flow lines into columns: a column holds lines until the next one would pass the bottom of the box.
        int column = 0;
        double colTop = 0;
        double cursor = 0;
        double tallest = 0;
        double minLeft = double.MaxValue, maxRight = double.MinValue;
        foreach ((PlacedLine line, double spaceAbove) in all)
        {
            double above = spaceAbove;
            double h = line.Height;
            if (columns > 1 && column < columns - 1 && cursor > colTop && cursor + above + h > s.Height + 0.01)
            {
                tallest = Math.Max(tallest, cursor);
                column++;
                cursor = 0;
                colTop = 0;
                above = 0;
            }
            else if (columns == 1)
            {
                // Single column: lines keep the positions the paragraph stack gave them, which is the cursor here.
            }

            cursor += above;
            line.Top = cursor;
            line.Column = column;
            double dx = column * (colWidth + colSpacing);
            line.Left += dx;
            line.Right += dx;
            foreach (PlacedRun r in line.Runs)
            {
                r.Left += dx;
            }

            cursor += h;
            minLeft = Math.Min(minLeft, line.Left);
            maxRight = Math.Max(maxRight, line.Right);
        }

        result.Height = Math.Max(tallest, cursor);
        result.Left = minLeft == double.MaxValue ? 0 : minLeft;
        result.Right = maxRight == double.MinValue ? 0 : maxRight;
        // Paragraph tops follow the final line positions (columns moved lines).
        foreach (PlacedParagraph pp in result.Paragraphs)
        {
            if (pp.Lines.Count > 0)
            {
                pp.Top = pp.Lines[0].Top;
                pp.Bottom = pp.Lines[^1].Top + pp.Lines[^1].Height;
            }
        }

        return result;
    }

    // ECMA-376 Part 1 §21.1.2.2.10 (spcBef) and §21.1.2.2.9 (spcAft): points, or a percentage of the line.
    private static double Spacing(SpacingValue v, SlideTextParagraph p, List<PlacedLine> lines)
    {
        if (!v.IsPercent)
        {
            return v.Value;
        }

        double basis = lines.Count > 0 ? lines[0].Height : p.EndStyle.Size * 1.2;
        return v.Value * basis;
    }

    private FontMatch? Resolve(string family, bool bold, bool italic)
    {
        if (_fontCache.TryGetValue((family, bold, italic), out FontMatch? cached))
        {
            return cached;
        }

        FontMatch? match = fonts.Resolve(new FontRequest(family, bold, italic));
        _fontCache[(family, bold, italic)] = match;
        if (match is { IsExact: false } && _noted.Add((match.RequestedFamily, match.ActualFamily)))
        {
            fontNotes.Add(new FontSubstitutionNote(match.RequestedFamily, match.ActualFamily, match.Kind));
        }

        return match;
    }

    private static string Family(SlideRunStyle st, int cp)
    {
        if (IsEastAsian(cp) && st.EastAsian.Length > 0)
        {
            return st.EastAsian;
        }

        if (IsComplex(cp) && st.ComplexScript.Length > 0)
        {
            return st.ComplexScript;
        }

        return st.Latin;
    }

    private static bool IsEastAsian(int cp) => cp is (>= 0x2E80 and <= 0xA4CF) or (>= 0xAC00 and <= 0xD7AF) or (>= 0xF900 and <= 0xFAFF) or (>= 0xFE30 and <= 0xFE4F) or (>= 0xFF00 and <= 0xFFEF) or (>= 0x20000 and <= 0x2FFFF);

    private static bool IsComplex(int cp) => cp is (>= 0x0590 and <= 0x08FF) or (>= 0x0900 and <= 0x0DFF) or (>= 0x0E00 and <= 0x0EFF) or (>= 0xFB1D and <= 0xFDFF) or (>= 0xFE70 and <= 0xFEFF);

    private static int Category(int cp) => IsEastAsian(cp) ? 2 : IsComplex(cp) ? 3 : 0;

    private static bool IsNeutral(int cp) => cp is ' ' or '.' or ',' or ':' or ';' or '(' or ')' or '-' or '\u00A0' or '"' or '\'' || (cp is >= 0x2000 and <= 0x206F);

    private Piece Shape(string shown, SlideRunStyle st, string family, double size, byte level, int start, bool label, double raise)
    {
        bool bold = st.Bold, italic = st.Italic;
        FontMatch? match = Resolve(family, bold, italic);
        var piece = new Piece { Start = start, Text = shown, Style = st, Size = size, Level = level, Font = match, IsLabel = label, Raise = raise };
        if (match is null || shown.Length == 0)
        {
            piece.Ascent = size * 0.9;
            piece.Descent = size * 0.25;
            return piece;
        }

        (piece.Ascent, piece.Descent) = ParagraphFormatter.Metrics(match.Face, size);
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

            fb ??= fonts.FindFallback(cp, bold, italic);
            if (fb is not null && !faces.Contains(fb) && faces.Count < FontFallbackChain.MaxFaces)
            {
                faces.Add(fb);
            }
        }

        piece.Faces = [.. faces];
        var options = new ShapingOptions { Kerning = st.KerningFrom > 0 && size >= st.KerningFrom ? KerningMode.Normal : KerningMode.None, Language = st.Language };
        options.Features.Add(FontFeature.Off("liga"));
        options.Features.Add(FontFeature.Off("clig"));
        bool rtl = (level & 1) == 1;
        if (faces.Count == 1)
        {
            TextShaping.ShapeRun(shown, match.Face, _scratch, rtl, options);
        }
        else
        {
            new FontFallbackChain(faces).ShapeRun(shown, _scratch, rtl, options);
        }

        int n = _scratch.ClusterCount;
        piece.ClusterCount = n;
        piece.CStart = new int[n];
        piece.CLength = new int[n];
        piece.CFace = new int[n];
        piece.CGlyphStart = new int[n];
        piece.CGlyphCount = new int[n];
        piece.CAdv = new double[n];
        ReadOnlySpan<ShapedClusterRange> clusters = _scratch.Clusters;
        for (int c = 0; c < n; c++)
        {
            ShapedClusterRange cr = clusters[c];
            piece.CStart[c] = cr.Start;
            piece.CLength[c] = cr.Length;
            piece.CFace[c] = cr.FaceIndex;
            piece.CGlyphStart[c] = cr.GlyphStart;
            piece.CGlyphCount[c] = cr.GlyphCount;
            piece.CAdv[c] = (double)cr.Advance * size / faces[cr.FaceIndex].UnitsPerEm;
        }

        int g = _scratch.GlyphCount;
        piece.GlyphId = new int[g];
        piece.GXOff = new int[g];
        piece.GYOff = new int[g];
        ReadOnlySpan<ShapedGlyph> glyphs = _scratch.Glyphs;
        for (int k = 0; k < g; k++)
        {
            piece.GlyphId[k] = glyphs[k].GlyphId;
            piece.GXOff[k] = glyphs[k].XOffset;
            piece.GYOff[k] = glyphs[k].YOffset;
        }

        return piece;
    }

    private static string UpperKeepLength(string s)
    {
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = char.ToUpperInvariant(chars[i]);
        }

        return new string(chars);
    }

    private static void AddAtoms(List<Piece> pieces, int index, List<Atom> atoms, LineBreakOpportunity[]? opps, double extraPerCluster)
    {
        Piece pc = pieces[index];
        for (int c = 0; c < pc.ClusterCount; c++)
        {
            int textStart = pc.IsLabel ? -1 : pc.Start + pc.CStart[c];
            bool space = pc.Text.Length > pc.CStart[c] && pc.Text[pc.CStart[c]] == ' ';
            atoms.Add(new Atom
            {
                Piece = index,
                Cluster = c,
                TextStart = textStart,
                Width = pc.CAdv[c] + extraPerCluster,
                Level = pc.Level,
                BreakBefore = textStart >= 0 && opps is not null && opps[textStart] != LineBreakOpportunity.None,
                Kind = space ? AtomKind.Space : AtomKind.Glyphs,
            });
        }
    }

    private List<PlacedLine> LayoutParagraph(SlideTextParagraph p, double width, TextLayoutSettings s)
    {
        SlideParagraphStyle st = p.Style;
        bool rtl = st.Rtl;
        double scale = s.FontScale;

        var text = new System.Text.StringBuilder();
        var charRun = new List<int>();
        for (int r = 0; r < p.Runs.Count; r++)
        {
            int before = text.Length;
            text.Append(p.Runs[r].Text);
            for (int i = before; i < text.Length; i++)
            {
                charRun.Add(r);
            }
        }

        string paragraph = text.ToString();
        var pieces = new List<Piece>();
        var atoms = new List<Atom>();
        byte baseLevel = (byte)(rtl ? 1 : 0);
        if (paragraph.Length > 0)
        {
            byte[] levels = TextShaping.GetBidiLevels(paragraph, rtl ? TextDirection.RightToLeft : TextDirection.LeftToRight, out _);
            LineBreakOpportunity[] opps = TextSegmentation.GetLineBreakOpportunities(paragraph);
            int i = 0;
            while (i < paragraph.Length)
            {
                int runIndex = charRun[i];
                SlideRunStyle rs = p.Runs[runIndex].Style;
                char ch = paragraph[i];
                if (ch is '\t' or '\n' or '\v')
                {
                    AtomKind kind = ch == '\t' ? AtomKind.Tab : AtomKind.LineBreak;
                    Piece special = Shape(" ", rs, rs.Latin, rs.Size * scale, baseLevel, i, false, 0);
                    special.CStart = [];
                    special.ClusterCount = 0;
                    special.Text = ch.ToString();
                    pieces.Add(special);
                    atoms.Add(new Atom { Piece = pieces.Count - 1, TextStart = i, Kind = kind, Level = baseLevel, BreakBefore = opps[i] != LineBreakOpportunity.None });
                    i++;
                    continue;
                }

                int cp = char.ConvertToUtf32(paragraph, i);
                int cat = Category(cp);
                byte level = levels[i];
                bool lowered = rs.Cap == "small" && char.IsLower(ch);
                int j = i + char.ConvertFromUtf32(cp).Length;
                while (j < paragraph.Length && charRun[j] == runIndex && levels[j] == level)
                {
                    char cj = paragraph[j];
                    if (cj is '\t' or '\n' or '\v')
                    {
                        break;
                    }

                    int cpj = char.ConvertToUtf32(paragraph, j);
                    bool lowJ = rs.Cap == "small" && char.IsLower(cj);
                    if ((Category(cpj) != cat && !IsNeutral(cpj)) || lowJ != lowered)
                    {
                        break;
                    }

                    j += char.IsHighSurrogate(cj) ? 2 : 1;
                }

                string segment = paragraph[i..j];
                double size = rs.Size * scale;
                double raise = 0;
                if (rs.Baseline != 0)
                {
                    // ECMA-376 Part 1 §21.1.2.3.9: raised or lowered text is set smaller; PowerPoint uses about two thirds.
                    raise = rs.Baseline * size;
                    size *= ScriptScale + 0.017;
                }

                if (lowered)
                {
                    size *= SmallCapsScale;
                }

                string shown = rs.Cap == "all" || lowered ? UpperKeepLength(segment) : segment;
                Piece piece = Shape(shown, rs, Family(rs, cp), size, level, i, false, raise);
                piece.Text = segment;
                piece.Shown = ReferenceEquals(shown, segment) ? null : shown;
                pieces.Add(piece);
                AddAtoms(pieces, pieces.Count - 1, atoms, opps, rs.Spacing);
                i = j;
            }
        }

        // The bullet or number: shaped on its own, drawn at the hanging position before the first line.
        Piece? label = null;
        if (p.Label is { Length: > 0 } lbl)
        {
            SlideRunStyle basis = p.Runs.Count > 0 ? p.Runs[0].Style : p.EndStyle;
            SlideRunStyle ls = new()
            {
                Bold = p.LabelFont is null && basis.Bold,
                Italic = false,
                Color = p.LabelColor ?? basis.Color,
                Latin = p.LabelFont ?? basis.Latin,
                Language = basis.Language,
                KerningFrom = 0,
            };
            double lsize = (p.LabelSizePoints ?? (basis.Size * p.LabelSizeFactor)) * (p.LabelSizePoints is null ? scale : 1);
            string family = ls.Latin;
            string mapped = SymbolMap.Map(family, lbl, out string? replacement);
            label = Shape(mapped, ls, replacement ?? family, lsize, baseLevel, -1, true, 0);
        }

        // Paragraph geometry: ECMA-376 Part 1 §21.1.2.2.7 (pPr): marL is the left margin of every line and indent moves the first line from it.
        // A bullet sits at marL + indent; with a hanging indent the text starts at marL, and a bullet that is wider than the hanging space
        // pushes the first line's text after it.
        double bulletX = Math.Max(0, st.MarginLeft + st.Indent);
        double firstStart = Math.Max(0, st.MarginLeft + st.Indent);
        double restStart = Math.Max(0, st.MarginLeft);
        if (label is not null)
        {
            double labelWidth = 0;
            for (int c = 0; c < label.ClusterCount; c++)
            {
                labelWidth += label.CAdv[c];
            }

            // A bullet that fits in the hanging space leaves the text at the margin; a wider one is followed by the next tab stop.
            double labelEnd = bulletX + labelWidth;
            firstStart = st.Indent < 0 && labelEnd <= restStart + 0.01 ? restStart : labelEnd + TabAdvance(labelEnd, st);
        }

        double limit = s.Wrap ? Math.Max(1, width - st.MarginRight) : double.MaxValue / 4;
        var lines = new List<PlacedLine>();
        int pos = 0;
        bool firstLine = true;
        while (pos < atoms.Count || firstLine)
        {
            double lineStart = firstLine ? firstStart : restStart;
            int end = FindLineEnd(atoms, pieces, pos, lineStart, limit, st, s.Wrap, out bool hardBreak);
            bool last = end >= atoms.Count;
            PlacedLine line = BuildLine(atoms, pieces, pos, end, lineStart, limit, width, st, p, hardBreak, last, firstLine ? label : null, bulletX, s);
            lines.Add(line);
            pos = end;
            firstLine = false;
            if (last)
            {
                break;
            }
        }

        return lines;
    }

    // Greedy line breaking: take atoms until the next one would pass the limit, then back up to the last break opportunity. Spaces at the
    // end of a line hang past the limit. A hard line break (a:br) ends the line and is consumed.
    private static int FindLineEnd(List<Atom> atoms, List<Piece> pieces, int start, double lineStart, double limit, SlideParagraphStyle st, bool wrap, out bool hardBreak)
    {
        hardBreak = false;
        double x = lineStart;
        int lastBreak = -1;
        for (int i = start; i < atoms.Count; i++)
        {
            Atom a = atoms[i];
            if (a.Kind == AtomKind.LineBreak)
            {
                hardBreak = true;
                return i + 1;
            }

            double w = a.Kind == AtomKind.Tab ? TabAdvance(x, st) : a.Width;
            if (i > start && a.BreakBefore)
            {
                lastBreak = i;
            }

            if (wrap && i > start && x + w > limit + 0.01 && a.Kind != AtomKind.Space)
            {
                if (lastBreak > start)
                {
                    return lastBreak;
                }

                return i;
            }

            x += w;
        }

        return atoms.Count;
    }

    // ECMA-376 Part 1 §21.1.2.2.13 (tabLst) and §21.1.2.2.7 (defTabSz): tab stops count from the text rectangle's left edge; past the last
    // stop the default interval repeats. Every stop is treated as left aligned (centre, right and decimal stops are not implemented).
    private static double TabAdvance(double x, SlideParagraphStyle st)
    {
        foreach ((double position, _) in st.Tabs)
        {
            if (position > x + 0.01)
            {
                return position - x;
            }
        }

        double interval = Math.Max(1, st.DefaultTab);
        double next = (Math.Floor((x + 0.01) / interval) + 1) * interval;
        return next - x;
    }

    private PlacedLine BuildLine(
        List<Atom> atoms, List<Piece> pieces, int start, int end, double lineStart, double limit, double width, SlideParagraphStyle st, SlideTextParagraph p,
        bool hardBreak, bool lastOfParagraph, Piece? label, double labelX, TextLayoutSettings s)
    {
        int count = end - start;
        var widths = new double[count];
        double x = lineStart;
        int lastVisible = -1;
        for (int k = 0; k < count; k++)
        {
            Atom a = atoms[start + k];
            double w = a.Kind switch
            {
                AtomKind.Tab => TabAdvance(x, st),
                AtomKind.LineBreak => 0,
                _ => a.Width,
            };
            widths[k] = w;
            x += w;
            if (a.Kind is AtomKind.Glyphs or AtomKind.Tab)
            {
                lastVisible = k;
            }
        }

        double trailing = 0;
        for (int k = count - 1; k > lastVisible; k--)
        {
            trailing += widths[k];
        }

        double contentWidth = x - trailing - lineStart;
        double avail = Math.Max(0, (s.Wrap ? limit : width) - lineStart);
        double shift = 0;
        double extraPerGap = 0;
        bool justify = st.Align is "just" or "justLow" or "thaiDist" && !lastOfParagraph && !hardBreak;
        bool distribute = st.Align == "dist";
        switch (st.Align)
        {
            case "ctr":
                shift = (avail - contentWidth) / 2;
                break;
            case "r":
                shift = avail - contentWidth;
                break;
        }

        if (!s.Wrap && st.Align is "ctr" or "r")
        {
            // Without wrapping the line is placed against the box's edges even when it is wider than the box.
            avail = Math.Max(0, width - lineStart);
            shift = st.Align == "ctr" ? (avail - contentWidth) / 2 : avail - contentWidth;
        }

        if ((justify || distribute) && s.Wrap && contentWidth < avail)
        {
            int gaps = 0;
            for (int k = 0; k <= lastVisible; k++)
            {
                if (atoms[start + k].Kind == AtomKind.Space || (distribute && k < lastVisible))
                {
                    gaps++;
                }
            }

            if (gaps > 0)
            {
                extraPerGap = (avail - contentWidth) / gaps;
                for (int k = 0; k <= lastVisible; k++)
                {
                    if (atoms[start + k].Kind == AtomKind.Space || (distribute && k < lastVisible))
                    {
                        widths[k] += extraPerGap;
                    }
                }
            }

            shift = 0;
        }

        // Visual order: reverse runs of atoms from the highest bidi level down to the lowest odd level (Unicode UAX #9 rule L2).
        var order = new int[count];
        for (int k = 0; k < count; k++)
        {
            order[k] = k;
        }

        int maxLevel = 0, minOdd = int.MaxValue;
        for (int k = 0; k < count; k++)
        {
            int lv = atoms[start + k].Level;
            maxLevel = Math.Max(maxLevel, lv);
            if ((lv & 1) == 1)
            {
                minOdd = Math.Min(minOdd, lv);
            }
        }

        for (int lv = maxLevel; lv >= minOdd && lv > 0; lv--)
        {
            int k = 0;
            while (k < count)
            {
                if (atoms[start + order[k]].Level >= lv)
                {
                    int j = k;
                    while (j < count && atoms[start + order[j]].Level >= lv)
                    {
                        j++;
                    }

                    Array.Reverse(order, k, j - k);
                    k = j;
                }
                else
                {
                    k++;
                }
            }
        }

        var line = new PlacedLine { Left = lineStart + shift };
        double ascent = 0, descent = 0;
        // Group visual neighbours from the same piece and face into one drawn run.
        double pen = lineStart + shift;
        int v = 0;
        while (v < count)
        {
            Atom first = atoms[start + order[v]];
            Piece pc = pieces[first.Piece];
            if (first.Kind is AtomKind.Tab or AtomKind.LineBreak)
            {
                ascent = Math.Max(ascent, pc.Ascent);
                descent = Math.Max(descent, pc.Descent);
                pen += widths[order[v]];
                v++;
                continue;
            }

            int faceIndex = pc.CFace.Length > first.Cluster ? pc.CFace[first.Cluster] : 0;
            int v1 = v + 1;
            while (v1 < count)
            {
                Atom b = atoms[start + order[v1]];
                if (b.Piece != first.Piece || b.Kind is AtomKind.Tab or AtomKind.LineBreak || (pc.CFace.Length > b.Cluster ? pc.CFace[b.Cluster] : 0) != faceIndex)
                {
                    break;
                }

                v1++;
            }

            PlacedRun run = MakeRun(pc, atoms, start, order, v, v1, widths, pen, faceIndex, false);
            line.Runs.Add(run);
            pen += run.Width;
            ascent = Math.Max(ascent, pc.Ascent + Math.Max(0, pc.Raise));
            descent = Math.Max(descent, pc.Descent + Math.Max(0, -pc.Raise));

            v = v1;
        }

        if (label is not null && label.ClusterCount > 0)
        {
            var labelOrder = new int[label.ClusterCount];
            var labelAtoms = new List<Atom>();
            for (int c = 0; c < label.ClusterCount; c++)
            {
                labelAtoms.Add(new Atom { Piece = 0, Cluster = c, TextStart = -1, Width = label.CAdv[c], Level = label.Level });
                labelOrder[c] = c;
            }

            var labelWidths = new double[label.ClusterCount];
            for (int c = 0; c < labelWidths.Length; c++)
            {
                labelWidths[c] = label.CAdv[c];
            }

            PlacedRun lr = MakeRun(label, labelAtoms, 0, labelOrder, 0, labelOrder.Length, labelWidths, labelX, label.CFace.Length > 0 ? label.CFace[0] : 0, true);
            line.Runs.Add(lr);
            ascent = Math.Max(ascent, label.Ascent);
            descent = Math.Max(descent, label.Descent);
        }

        if (line.Runs.Count == 0 || ascent == 0)
        {
            // An empty line is as tall as the paragraph's end mark.
            SlideRunStyle es = p.EndStyle;
            Piece mark = Shape(" ", es, es.Latin, es.Size * s.FontScale, 0, 0, false, 0);
            ascent = Math.Max(ascent, mark.Ascent);
            descent = Math.Max(descent, mark.Descent);
        }

        double raw = ascent + descent;
        double lineHeight = raw;
        SpacingValue ls = st.LineSpacing;
        if (ls.IsPercent)
        {
            double pct = Math.Max(0.05, ls.Value - s.LineReduction);
            lineHeight = raw * pct;
        }
        else
        {
            lineHeight = ls.Value;
        }

        line.Height = lineHeight;
        line.Baseline = raw > 0 ? lineHeight * ascent / raw : lineHeight * 0.8;
        line.Right = pen;
        if (line.Runs.Count == 0)
        {
            line.Right = line.Left;
        }

        return line;
    }

    private static PlacedRun MakeRun(Piece pc, List<Atom> atoms, int start, int[] order, int v0, int v1, double[] widths, double x, int faceIndex, bool isLabel)
    {
        bool rtl = (pc.Level & 1) == 1;
        FontFace face = pc.Faces.Length > faceIndex ? pc.Faces[faceIndex] : pc.Font!.Face;
        double unit = pc.Size / face.UnitsPerEm;
        int minText = int.MaxValue, maxText = 0;
        for (int v = v0; v < v1; v++)
        {
            int c = atoms[start + order[v]].Cluster;
            minText = Math.Min(minText, pc.CStart[c]);
            maxText = Math.Max(maxText, pc.CStart[c] + pc.CLength[c]);
        }

        string shown = pc.Shown ?? pc.Text;
        if (minText > maxText || minText == int.MaxValue)
        {
            minText = 0;
            maxText = 0;
        }

        maxText = Math.Min(maxText, shown.Length);
        minText = Math.Min(minText, maxText);
        string runText = shown[minText..maxText];
        var ids = new List<int>();
        var gx = new List<float>();
        var gy = new List<float>();
        var cl = new List<int>();
        double pen = 0;
        for (int v = v0; v < v1; v++)
        {
            int c = atoms[start + order[v]].Cluster;
            double clusterPen = pen;
            for (int g = 0; g < pc.CGlyphCount[c]; g++)
            {
                int gi = pc.CGlyphStart[c] + (rtl ? pc.CGlyphCount[c] - 1 - g : g);
                ids.Add(pc.GlyphId[gi]);
                gx.Add((float)(clusterPen + (pc.GXOff[gi] * unit)));
                gy.Add((float)(-pc.GYOff[gi] * unit));
                cl.Add(Math.Clamp(pc.CStart[c] - minText, 0, Math.Max(0, runText.Length - 1)));
            }

            pen += widths[order[v]];
        }

        SlideRunStyle st = pc.Style;
        return new PlacedRun
        {
            Face = face,
            Size = pc.Size,
            Glyphs = [.. ids],
            X = [.. gx],
            Y = [.. gy],
            Clusters = [.. cl],
            Text = runText,
            Rtl = rtl,
            Color = st.Color,
            Left = x,
            Width = pen,
            Raise = pc.Raise,
            Underline = isLabel ? null : st.Underline,
            Strike = isLabel ? null : st.Strike,
            Highlight = isLabel ? null : st.Highlight,
            LinkRelationship = isLabel ? null : st.LinkRelationship,
            Language = st.Language,
            IsLabel = isLabel,
        };
    }
}
