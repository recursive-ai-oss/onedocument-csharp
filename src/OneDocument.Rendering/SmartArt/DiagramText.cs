using System.Text;
using OneFont;
using OneRender.Painting;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Rendering.SmartArt;

// Text inside diagram shapes. SmartArt text is DrawingML (a:p / a:r), not WordprocessingML, so it cannot go through the document
// paragraph formatter. This is a small formatter for what diagram text uses: runs with a size, weight, style and colour, paragraphs
// with alignment, margins, line spacing and a bullet, wrapped at the shape's text rectangle. The generator uses the same code to choose
// font sizes that fit, so what it measures is what the painter draws.

/// <summary>A run of text with its character formatting.</summary>
internal sealed class RunSpec
{
    public string Text { get; init; } = string.Empty;

    /// <summary>Size in points.</summary>
    public double Size { get; set; } = 18;

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    /// <summary>The family, a theme token (<c>+mn-lt</c>) or null for the theme's minor font.</summary>
    public string? Family { get; init; }

    public DrawingColor? Color { get; init; }

    /// <summary>The colour as DrawingML XML for the writer (not used when painting).</summary>
    public string? ColorXml { get; init; }

    public bool LineBreak { get; init; }
}

/// <summary>A paragraph of diagram text.</summary>
internal sealed class ParaSpec
{
    public int Level { get; init; }

    /// <summary><c>l</c>, <c>ctr</c>, <c>r</c> or <c>just</c>.</summary>
    public string Align { get; init; } = "l";

    /// <summary>Left margin in points.</summary>
    public double MarginLeft { get; init; }

    /// <summary>First-line indent in points (negative for a hanging bullet).</summary>
    public double Indent { get; init; }

    /// <summary>Line spacing as a fraction of the font's natural line height.</summary>
    public double LineSpacing { get; init; } = 1;

    /// <summary>Space before in points (already converted from a percentage of the size where the source said so).</summary>
    public double SpaceBefore { get; init; }

    public double SpaceAfter { get; init; }

    public string? Bullet { get; init; }

    public List<RunSpec> Runs { get; } = [];

    public double MaxSize => Runs.Count == 0 ? 18 : Runs.Max(r => r.Size);
}

internal sealed class LaidSegment
{
    public required RunSpec Run { get; init; }

    public required string Text { get; init; }

    public required FontFace Face { get; init; }

    public double X { get; init; }

    public double Width { get; init; }
}

internal sealed class LaidLine
{
    public int Paragraph { get; init; }

    public double Top { get; init; }

    public double Baseline { get; init; }

    public double Height { get; init; }

    public double StartX { get; init; }

    public List<LaidSegment> Segments { get; } = [];

    public LaidSegment? Bullet { get; set; }

    public double BulletX { get; set; }
}

internal sealed class LaidText
{
    public List<LaidLine> Lines { get; } = [];

    public double Height { get; set; }

    public double MaxLineWidth { get; set; }

    /// <summary>True when a word was wider than its line and had to be broken inside.</summary>
    public bool SplitWord { get; set; }
}

internal static class DiagramText
{
    private static readonly ShapedRun SharedRun = new();
    private static readonly object Gate = new();

    // Lays paragraphs out at the given width. Heights follow the font's ascent, descent and line gap scaled by the line spacing.
    public static LaidText Layout(IReadOnlyList<ParaSpec> paragraphs, double width, IFontProvider fonts, string defaultFamily, bool wrap = true)
    {
        var laid = new LaidText();
        double y = 0;
        bool firstPara = true;
        for (int pi = 0; pi < paragraphs.Count; pi++)
        {
            ParaSpec p = paragraphs[pi];
            y += firstPara ? 0 : p.SpaceBefore;
            firstPara = false;
            double avail = Math.Max(1, width - p.MarginLeft);
            // A hanging bullet (negative indent) sits left of the margin and the text starts at the margin.
            double textIndent = p.Bullet is { Length: > 0 } && p.Indent < 0 ? 0 : p.Indent;
            List<List<Piece>> lines = Break(p, avail, textIndent, fonts, defaultFamily, wrap, laid);
            bool firstLine = true;
            foreach (List<Piece> line in lines)
            {
                double asc = 0, desc = 0, w = 0;
                foreach (Piece pc in line)
                {
                    asc = Math.Max(asc, pc.Ascent);
                    desc = Math.Max(desc, pc.Descent);
                    w += pc.Width;
                }

                if (line.Count == 0)
                {
                    (asc, desc) = Metrics(p.MaxSize, p.Runs.Count > 0 ? p.Runs[0] : new RunSpec(), fonts, defaultFamily);
                }

                double lh = (asc + desc) * p.LineSpacing;
                double firstIndent = firstLine ? textIndent : 0;
                double extra = Math.Max(0, avail - firstIndent - w);
                double start = p.MarginLeft + firstIndent + p.Align switch { "ctr" => extra / 2, "r" => extra, _ => 0 };
                var ll = new LaidLine { Paragraph = pi, Top = y, Height = lh, Baseline = y + (asc * p.LineSpacing), StartX = start };
                double x = 0;
                foreach (Piece pc in line)
                {
                    ll.Segments.Add(new LaidSegment { Run = pc.Run, Text = pc.Text, Face = pc.Face, X = x, Width = pc.Width });
                    x += pc.Width;
                }

                if (firstLine && p.Bullet is { Length: > 0 } bullet && p.Runs.Count > 0)
                {
                    RunSpec br = p.Runs[0];
                    FontFace? bf = Resolve(br, fonts, defaultFamily);
                    if (bf is not null)
                    {
                        ll.Bullet = new LaidSegment { Run = br, Text = bullet, Face = bf, X = 0, Width = Advance(bullet, bf, br.Size) };
                        ll.BulletX = Math.Max(0, p.MarginLeft + p.Indent);
                    }
                }

                laid.Lines.Add(ll);
                laid.MaxLineWidth = Math.Max(laid.MaxLineWidth, w + firstIndent + p.MarginLeft);
                y += lh;
                firstLine = false;
            }

            if (pi < paragraphs.Count - 1)
            {
                y += p.SpaceAfter;
            }
        }

        laid.Height = y;
        return laid;
    }

    private readonly record struct Piece(RunSpec Run, string Text, FontFace Face, double Width, double Ascent, double Descent);

    private static List<List<Piece>> Break(ParaSpec p, double width, double firstIndent, IFontProvider fonts, string defaultFamily, bool wrap, LaidText laid)
    {
        var lines = new List<List<Piece>>();
        var cur = new List<Piece>();
        double used = 0;
        double limit = width - firstIndent;
        foreach (RunSpec run in p.Runs)
        {
            if (run.LineBreak)
            {
                lines.Add(cur);
                cur = [];
                used = 0;
                limit = width;
                continue;
            }

            FontFace? face = Resolve(run, fonts, defaultFamily);
            if (face is null || run.Text.Length == 0)
            {
                continue;
            }

            (double asc, double desc) = Metrics(run.Size, run, fonts, defaultFamily);
            foreach (string token in Tokens(run.Text))
            {
                double w = Advance(token, face, run.Size);
                double trimmed = Advance(token.TrimEnd(), face, run.Size);
                if (wrap && used + trimmed > limit + 1e-6 && cur.Count > 0)
                {
                    TrimTrailingSpace(cur, fonts, defaultFamily);
                    lines.Add(cur);
                    cur = [];
                    used = 0;
                    limit = width;
                    if (token.Trim().Length == 0)
                    {
                        continue;
                    }
                }

                if (wrap && trimmed > limit + 1e-6 && cur.Count == 0)
                {
                    // A word wider than the line is split at characters so nothing is lost.
                    laid.SplitWord = true;
                    foreach (string piece in SplitToFit(token, face, run.Size, limit))
                    {
                        double pw = Advance(piece, face, run.Size);
                        if (used + pw > limit + 1e-6 && cur.Count > 0)
                        {
                            lines.Add(cur);
                            cur = [];
                            used = 0;
                            limit = width;
                        }

                        cur.Add(new Piece(run, piece, face, pw, asc, desc));
                        used += pw;
                    }

                    continue;
                }

                cur.Add(new Piece(run, token, face, w, asc, desc));
                used += w;
            }
        }

        TrimTrailingSpace(cur, fonts, defaultFamily);
        lines.Add(cur);
        return lines;
    }

    private static void TrimTrailingSpace(List<Piece> line, IFontProvider fonts, string defaultFamily)
    {
        if (line.Count == 0)
        {
            return;
        }

        Piece last = line[^1];
        string t = last.Text.TrimEnd();
        if (t.Length == last.Text.Length)
        {
            return;
        }

        _ = fonts;
        _ = defaultFamily;
        line[^1] = t.Length == 0 ? last with { Text = string.Empty, Width = 0 } : last with { Text = t, Width = Advance(t, last.Face, last.Run.Size) };
    }

    // Words with their trailing spaces; each CJK character is its own token so it can break anywhere.
    private static IEnumerable<string> Tokens(string text)
    {
        var sb = new StringBuilder();
        bool inSpace = false;
        foreach (char c in text)
        {
            bool space = c == ' ' || c == '\t';
            if (c >= 0x2E80 && !char.IsSurrogate(c))
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }

                yield return c.ToString();
                inSpace = false;
                continue;
            }

            if (inSpace && !space)
            {
                yield return sb.ToString();
                sb.Clear();
            }

            sb.Append(c);
            inSpace = space;
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    private static IEnumerable<string> SplitToFit(string token, FontFace face, double size, double limit)
    {
        var sb = new StringBuilder();
        foreach (char c in token)
        {
            sb.Append(c);
            if (sb.Length > 1 && Advance(sb.ToString(), face, size) > limit)
            {
                sb.Length--;
                yield return sb.ToString();
                sb.Clear();
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    internal static FontFace? Resolve(RunSpec run, IFontProvider fonts, string defaultFamily)
    {
        string family = run.Family is null or "+mn-lt" or "+mn-ea" or "+mn-cs" ? defaultFamily : run.Family;
        return fonts.Resolve(new FontRequest(family, run.Bold, run.Italic))?.Face;
    }

    private static (double Ascent, double Descent) Metrics(double size, RunSpec run, IFontProvider fonts, string defaultFamily)
    {
        FontFace? f = Resolve(run, fonts, defaultFamily);
        if (f is null)
        {
            return (size * 0.9, size * 0.25);
        }

        FontMetrics m = f.Metrics;
        double upm = f.UnitsPerEm;
        return ((m.Ascender + (m.LineGap / 2.0)) * size / upm, -m.Descender * size / upm);
    }

    internal static double Advance(string text, FontFace face, double size)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        lock (Gate)
        {
            TextShaping.ShapeRun(text, face, SharedRun, false, null);
            double adv = 0;
            foreach (ShapedGlyph g in SharedRun.Glyphs)
            {
                adv += g.XAdvance;
            }

            return adv * size / face.UnitsPerEm;
        }
    }

    /// <summary>Draws laid-out text into a list. The origin is the top-left of the text rectangle in the list's current coordinate space.</summary>
    public static void Draw(DisplayList list, Func<ItemId> nextId, LaidText text, double originX, double originY, bool tagged)
    {
        int currentPara = -1;
        foreach (LaidLine line in text.Lines)
        {
            if (tagged && line.Paragraph != currentPara)
            {
                if (currentPara >= 0)
                {
                    list.EndTag();
                }

                list.BeginTag(nextId(), TagRole.Paragraph);
                currentPara = line.Paragraph;
            }

            if (line.Bullet is { } b)
            {
                DrawSegment(list, nextId, b, originX + line.BulletX, originY + line.Baseline);
            }

            foreach (LaidSegment s in line.Segments)
            {
                if (s.Text.Length > 0)
                {
                    DrawSegment(list, nextId, s, originX + line.StartX + s.X, originY + line.Baseline);
                }
            }
        }

        if (tagged && currentPara >= 0)
        {
            list.EndTag();
        }
    }

    private static void DrawSegment(DisplayList list, Func<ItemId> nextId, LaidSegment s, double x, double baseline)
    {
        PaintColor color = s.Run.Color is { } c ? new PaintColor((float)c.R, (float)c.G, (float)c.B, (float)c.A) : new PaintColor(0, 0, 0, 1);
        lock (Gate)
        {
            TextShaping.ShapeRun(s.Text, s.Face, SharedRun, false, null);
            int n = SharedRun.GlyphCount;
            if (n == 0)
            {
                return;
            }

            var ids = new int[n];
            var xs = new float[n];
            var ys = new float[n];
            var cl = new int[n];
            double upm = s.Face.UnitsPerEm;
            double pen = 0;
            ReadOnlySpan<ShapedGlyph> glyphs = SharedRun.Glyphs;
            ReadOnlySpan<ShapedGlyphOrigin> origins = SharedRun.Origins;
            for (int i = 0; i < n; i++)
            {
                ids[i] = glyphs[i].GlyphId;
                xs[i] = (float)((pen + glyphs[i].XOffset) * s.Run.Size / upm);
                ys[i] = (float)(-glyphs[i].YOffset * s.Run.Size / upm);
                cl[i] = Math.Clamp(origins[i].Cluster, 0, Math.Max(0, s.Text.Length - 1));
                pen += glyphs[i].XAdvance;
            }

            list.DrawGlyphRun(nextId(), s.Face, (float)s.Run.Size, (float)x, (float)baseline, color, ids, xs, ys, cl, s.Text);
        }
    }

    /// <summary>
    /// Reads the paragraphs of a <c>txBody</c> (ECMA-376 Part 1 §21.1.2.1.1) for painting. Run colour is the run's own, else
    /// <paramref name="defaultColor"/> (the shape style's font reference).
    /// </summary>
    public static List<ParaSpec> Parse(MarkupNode txBody, ColorResolver colors, DrawingColor? defaultColor, double defaultSize)
    {
        var result = new List<ParaSpec>();
        foreach (MarkupNode p in Dx.Children(txBody, "p"))
        {
            MarkupNode pPr = Dx.Child(p, "pPr");
            double lineSpacing = 1;
            double before = 0, after = 0;
            double paraSize = defaultSize;
            foreach (MarkupNode r in p.ChildElements)
            {
                if (r.LocalNameString == "r" && Dx.Child(r, "rPr").GetAttribute("sz") is { } s0)
                {
                    paraSize = Dx.Num(s0, defaultSize * 100) / 100;
                    break;
                }
            }

            MarkupNode ln = Dx.Child(pPr, "lnSpc");
            if (Dx.Child(ln, "spcPct") is { IsNull: false } lp)
            {
                lineSpacing = Dx.Num(lp.GetAttribute("val"), 100000) / 100000;
            }
            else if (Dx.Child(ln, "spcPts") is { IsNull: false } lpts)
            {
                lineSpacing = Math.Max(0.1, Dx.Num(lpts.GetAttribute("val"), 0) / 100 / (paraSize * 1.2));
            }

            before = Spacing(Dx.Child(pPr, "spcBef"), paraSize);
            after = Spacing(Dx.Child(pPr, "spcAft"), paraSize);
            string? bullet = Dx.Child(pPr, "buChar").GetAttribute("char");
            var para = new ParaSpec
            {
                Level = Dx.Int(pPr.IsNull ? null : pPr.GetAttribute("lvl"), 0),
                Align = pPr.IsNull ? "l" : pPr.GetAttribute("algn") ?? "l",
                MarginLeft = pPr.IsNull ? 0 : Dx.Num(pPr.GetAttribute("marL"), 0) / 12700,
                Indent = pPr.IsNull ? 0 : Dx.Num(pPr.GetAttribute("indent"), 0) / 12700,
                LineSpacing = lineSpacing,
                SpaceBefore = before,
                SpaceAfter = after,
                Bullet = bullet,
            };
            foreach (MarkupNode r in p.ChildElements)
            {
                switch (r.LocalNameString)
                {
                    case "r" or "fld":
                        {
                            MarkupNode rPr = Dx.Child(r, "rPr");
                            double size = rPr.IsNull ? defaultSize : Dx.Num(rPr.GetAttribute("sz"), defaultSize * 100) / 100;
                            DrawingColor? color = defaultColor;
                            if (!rPr.IsNull && Dx.Child(rPr, "solidFill") is { IsNull: false } sf && colors.ResolveChild(sf) is { } rc)
                            {
                                color = rc;
                            }

                            string? family = Dx.Child(rPr, "latin").GetAttribute("typeface");
                            para.Runs.Add(new RunSpec
                            {
                                Text = Dx.Child(r, "t").InnerText,
                                Size = Math.Clamp(size, 1, 400),
                                Bold = !rPr.IsNull && Dx.Bool(rPr.GetAttribute("b"), false),
                                Italic = !rPr.IsNull && Dx.Bool(rPr.GetAttribute("i"), false),
                                Family = family,
                                Color = color,
                            });
                            break;
                        }

                    case "br":
                        para.Runs.Add(new RunSpec { LineBreak = true, Size = paraSize });
                        break;
                }
            }

            result.Add(para);
        }

        return result;
    }

    // spcPct is a percentage of the line (about 1.2 times the size); spcPts is in hundredths of a point.
    private static double Spacing(MarkupNode e, double size)
    {
        if (e.IsNull)
        {
            return 0;
        }

        if (Dx.Child(e, "spcPct") is { IsNull: false } pct)
        {
            return Dx.Num(pct.GetAttribute("val"), 0) / 100000 * size * 1.2;
        }

        return Dx.Child(e, "spcPts") is { IsNull: false } pts ? Dx.Num(pts.GetAttribute("val"), 0) / 100 : 0;
    }
}
