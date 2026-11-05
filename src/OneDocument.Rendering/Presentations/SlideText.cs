using System.Globalization;
using System.Numerics;
using OneRender.Painting;
using RecursiveAI.OneDocument.Equations;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Drawing;
using RecursiveAI.OneDocument.Rendering.Equations;

namespace RecursiveAI.OneDocument.Rendering.Presentations;

/// <summary>Values text fields take while a slide is rendered.</summary>
internal sealed class FieldContext
{
    /// <summary>The number shown by a slide-number field.</summary>
    public int SlideNumber = 1;

    /// <summary>The clock date and time fields read; null keeps the text the file stored.</summary>
    public TimeProvider? Clock;

    /// <summary>Told about text that was left out; null ignores it.</summary>
    public Action<string, string>? Note;
}

/// <summary>Turns a DrawingML text body into paragraphs ready for layout.</summary>
internal static class TextBodyReader
{
    /// <summary>The most characters laid out in one text body; the rest is left out (a note says so).</summary>
    public const int MaxCharacters = 200_000;

    /// <summary>Reads the paragraphs of a <c>txBody</c> (or <c>a:txBody</c> of a table cell).</summary>
    public static List<SlideTextParagraph> Read(MarkupNode txBody, StyleReader styles, FieldContext fields)
    {
        var result = new List<SlideTextParagraph>();
        int characters = 0;
        // Counters for automatic numbers by level: ECMA-376 Part 1 §21.1.2.4.1 (buAutoNum): a number counts up through consecutive
        // paragraphs of the same scheme at one level; a paragraph at a shallower level, or one without that numbering, starts it again.
        var counter = new int[9];
        var scheme = new string?[9];
        var startAt = new int[9];
        foreach (MarkupNode p in txBody.ChildElements)
        {
            if (p.LocalNameString != "p")
            {
                continue;
            }

            if (characters > MaxCharacters)
            {
                fields.Note?.Invoke("slide.text-truncated", "A text body with more than " + MaxCharacters.ToString(CultureInfo.InvariantCulture) + " characters is cut off there.");
                break;
            }

            MarkupNode pPr = DrawingTheme.Child(p, "pPr");
            int level = Math.Clamp((int)(Number(pPr.IsNull ? null : pPr.GetAttribute("lvl")) ?? 0), 0, 8);
            SlideParagraphStyle ps = styles.Paragraph(pPr, level);
            List<MarkupNode> chain = styles.ParagraphChain(pPr, level);
            SlideRunStyle end = styles.Run(DrawingTheme.Child(p, "endParaRPr"), chain);
            var paragraph = new SlideTextParagraph { Style = ps, EndStyle = end };
            foreach (MarkupNode child in p.ChildElements)
            {
                switch (child.LocalNameString)
                {
                    case "r":
                        {
                            string text = Decode(DrawingTheme.Child(child, "t").InnerText);
                            if (text.Length > 0)
                            {
                                characters += text.Length;
                                paragraph.Runs.Add(new SlideTextRun(text, styles.Run(DrawingTheme.Child(child, "rPr"), chain)));
                            }

                            break;
                        }

                    case "br":
                        paragraph.Runs.Add(new SlideTextRun("\n", styles.Run(DrawingTheme.Child(child, "rPr"), chain)));
                        break;
                    case "AlternateContent":
                        {
                            // An equation (a14:m holding m:oMath inside mc:AlternateContent): laid out as an inline box by the equation layout.
                            MarkupNode om = FindMath(child, 0);
                            if (!om.IsNull && OmmlReader.Read(om, out _) is { } tree)
                            {
                                characters++;
                                paragraph.Runs.Add(new SlideTextRun("\uFFFC", styles.Run(default, chain)) { Math = tree });
                                if (tree.Kind == MathKind.OMathPara)
                                {
                                    fields.Note?.Invoke("slide.math-display-inline", "A display equation in slide text is laid out inline, at the size of the text around it.");
                                }
                            }

                            break;
                        }

                    case "fld":
                        {
                            string text = FieldText(child, fields);
                            if (text.Length > 0)
                            {
                                paragraph.Runs.Add(new SlideTextRun(text, styles.Run(DrawingTheme.Child(child, "rPr"), chain)));
                            }

                            break;
                        }
                }
            }

            bool hasText = false;
            foreach (SlideTextRun r in paragraph.Runs)
            {
                if (r.Text != "\n")
                {
                    hasText = true;
                    break;
                }
            }

            // A paragraph without text shows no bullet and takes no number.
            if (hasText)
            {
                AssignLabel(paragraph, ps, level, counter, scheme, startAt);
            }

            result.Add(paragraph);
        }

        return result;
    }

    private static void AssignLabel(SlideTextParagraph paragraph, SlideParagraphStyle ps, int level, int[] counter, string?[] scheme, int[] startAt)
    {
        for (int deeper = level + 1; deeper < 9; deeper++)
        {
            scheme[deeper] = null;
        }

        switch (ps.Bullet)
        {
            case BulletKind.Character:
                scheme[level] = null;
                paragraph.Label = string.IsNullOrEmpty(ps.BulletChar) ? "\u2022" : ps.BulletChar;
                break;
            case BulletKind.Picture:
                scheme[level] = null;
                paragraph.Label = "\u2022";
                break;
            case BulletKind.AutoNumber:
                string type = ps.BulletType ?? "arabicPeriod";
                if (scheme[level] == type && startAt[level] == ps.BulletStart)
                {
                    counter[level]++;
                }
                else
                {
                    scheme[level] = type;
                    startAt[level] = ps.BulletStart;
                    counter[level] = ps.BulletStart;
                }

                paragraph.Label = AutoNumber.Format(type, counter[level]);
                break;
            default:
                scheme[level] = null;
                break;
        }

        if (paragraph.Label is not null)
        {
            paragraph.LabelFont = ps.BulletFont;
            paragraph.LabelColor = ps.BulletColor;
            paragraph.LabelSizeFactor = ps.BulletSizePercent ?? 1.0;
            paragraph.LabelSizePoints = ps.BulletSizePoints;
        }
    }

    // The first m:oMathPara or m:oMath under the first mc:Choice (or a bare math element).
    private static MarkupNode FindMath(MarkupNode n, int depth)
    {
        if (depth > 8)
        {
            return default;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            string name = c.LocalNameString;
            if (name is "oMathPara" or "oMath")
            {
                return c;
            }

            if (name == "Fallback")
            {
                continue;
            }

            MarkupNode found = FindMath(c, depth + 1);
            if (!found.IsNull)
            {
                return found;
            }
        }

        return default;
    }

    private static double? Number(string? s) =>
        s is not null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : null;

    // ECMA-376 Part 1 §21.1.2.2.4 (fld): slidenum shows the slide's number; datetimeN shows the date and time in one of the built-in
    // formats. Without a clock the text the file stored stands.
    private static string FieldText(MarkupNode fld, FieldContext fields)
    {
        string stored = Decode(DrawingTheme.Child(fld, "t").InnerText);
        string type = fld.GetAttribute("type") ?? string.Empty;
        if (type is "slidenum")
        {
            return fields.SlideNumber.ToString(CultureInfo.InvariantCulture);
        }

        if (fields.Clock is { } clock && type.StartsWith("datetime", StringComparison.Ordinal))
        {
            DateTime now = clock.GetLocalNow().DateTime;
            CultureInfo c = CultureInfo.InvariantCulture;
            return type switch
            {
                "datetime" or "datetime1" => now.ToString("M/d/yyyy", c),
                "datetime2" => now.ToString("dddd, MMMM d, yyyy", c),
                "datetime3" => now.ToString("d MMMM yyyy", c),
                "datetime4" => now.ToString("MMMM d, yyyy", c),
                "datetime5" => now.ToString("d-MMM-yy", c),
                "datetime6" => now.ToString("MMMM yy", c),
                "datetime7" => now.ToString("MMM-yy", c),
                "datetime8" => now.ToString("M/d/yyyy h:mm tt", c),
                "datetime9" => now.ToString("M/d/yyyy h:mm:ss tt", c),
                "datetime10" => now.ToString("HH:mm", c),
                "datetime11" => now.ToString("HH:mm:ss", c),
                "datetime12" => now.ToString("h:mm tt", c),
                "datetime13" => now.ToString("h:mm:ss tt", c),
                _ => stored,
            };
        }

        return stored;
    }

    // XML cannot hold some characters; the editing layer writes them as _xHHHH_ and a vertical tab as a line break.
    private static string Decode(string s)
    {
        if (s.IndexOf("_x", StringComparison.Ordinal) < 0)
        {
            return s;
        }

        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '_' && i + 6 < s.Length && s[i + 1] == 'x' && s[i + 6] == '_'
                && int.TryParse(s.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
            {
                sb.Append(code == 0x000B ? '\v' : (char)code);
                i += 6;
            }
            else
            {
                sb.Append(s[i]);
            }
        }

        return sb.ToString();
    }
}

/// <summary>Draws laid-out text into a display list.</summary>
internal static class TextEmitter
{
    private const double K = 96.0 / 72.0;

    internal sealed class Options
    {
        public bool Tagged;
        public TagRole ParagraphRole = TagRole.Paragraph;
        public Func<string, string?>? LinkTarget;
        public required Func<ItemId> NextId;
    }

    /// <summary>Paints the text. <paramref name="frame"/> maps points in the text rectangle (origin top-left) to page pixels.</summary>
    public static void Emit(DisplayList list, PlacedText text, Matrix3x2 frame, double offsetX, double offsetY, Options o)
    {
        list.PushTransform(o.NextId(), Matrix3x2.CreateScale((float)(1 / K)) * Matrix3x2.CreateTranslation((float)offsetX, (float)offsetY) * frame);
        bool inList = false;
        foreach (PlacedParagraph para in text.Paragraphs)
        {
            bool labelled = para.Source.Label is not null && para.Lines.Count > 0;
            if (o.Tagged)
            {
                if (labelled && !inList)
                {
                    list.BeginTag(o.NextId(), TagRole.List);
                    inList = true;
                }
                else if (!labelled && inList)
                {
                    list.EndTag();
                    inList = false;
                }
            }

            if (labelled)
            {
                if (o.Tagged)
                {
                    list.BeginTag(o.NextId(), TagRole.ListItem);
                    list.BeginTag(o.NextId(), TagRole.ListLabel);
                }

                PaintLine(list, para.Lines[0], o, 1);
                if (o.Tagged)
                {
                    list.EndTag();
                    list.BeginTag(o.NextId(), TagRole.ListBody);
                }
            }

            if (o.Tagged)
            {
                list.BeginTag(o.NextId(), para.Lines.Count == 0 ? TagRole.Paragraph : o.ParagraphRole);
            }

            for (int i = 0; i < para.Lines.Count; i++)
            {
                PaintLine(list, para.Lines[i], o, i == 0 && labelled ? 2 : 0);
            }

            if (o.Tagged)
            {
                list.EndTag();
                if (labelled)
                {
                    list.EndTag();
                    list.EndTag();
                }
            }
        }

        if (o.Tagged && inList)
        {
            list.EndTag();
        }

        list.PopTransform();
    }

    // An equation: glyphs, rules and strokes in the box's own coordinates (first baseline, y down), one figure whose alternative text is the
    // equation's linear form.
    private static void PaintMath(DisplayList list, MathBox box, PlacedRun run, double baseline, Options o)
    {
        if (o.Tagged)
        {
            list.BeginTag(o.NextId(), TagRole.Figure, box.LinearText);
        }

        double ox = run.Left, oy = baseline;
        foreach (MathItem item in box.Items)
        {
            PaintColor color = PaintColor.FromBytes((byte)(item.Color >> 16), (byte)(item.Color >> 8), (byte)item.Color);
            switch (item)
            {
                case MathGlyph g:
                    list.DrawGlyphRun(o.NextId(), g.Face, (float)(g.Size * K), (float)((ox + g.X) * K), (float)((oy + g.Y) * K), color, [g.GlyphId], [0f], [0f], [0], g.Text, false);
                    break;
                case MathRule r:
                    list.FillRect(o.NextId(), new RectF((float)((ox + r.X) * K), (float)((oy + r.Y) * K), (float)(r.Width * K), (float)(r.Height * K)), Paint.FromColor(color));
                    break;
                case MathStroke st:
                    {
                        double dx = st.X2 - st.X1, dy = st.Y2 - st.Y1;
                        double len = Math.Sqrt((dx * dx) + (dy * dy));
                        if (len < 1e-9)
                        {
                            break;
                        }

                        double nx = -dy / len * st.Thickness / 2, ny = dx / len * st.Thickness / 2;
                        var path = new PathBuffer();
                        path.MoveTo((float)((ox + st.X1 + nx) * K), (float)((oy + st.Y1 + ny) * K));
                        path.LineTo((float)((ox + st.X2 + nx) * K), (float)((oy + st.Y2 + ny) * K));
                        path.LineTo((float)((ox + st.X2 - nx) * K), (float)((oy + st.Y2 - ny) * K));
                        path.LineTo((float)((ox + st.X1 - nx) * K), (float)((oy + st.Y1 - ny) * K));
                        path.Close();
                        list.FillPath(o.NextId(), path, Paint.FromColor(color));
                        break;
                    }
            }
        }

        if (o.Tagged)
        {
            list.EndTag();
        }
    }

    private static PaintColor Color(DrawingColor c) => new((float)c.R, (float)c.G, (float)c.B, (float)c.A);

    // mode 0 draws every run, 1 only the bullet, 2 everything but the bullet.
    private static void PaintLine(DisplayList list, PlacedLine line, Options o, int mode)
    {
        double baseline = line.Top + line.Baseline;
        // Backgrounds first, then the glyphs, then the rules, so text is never covered.
        foreach (PlacedRun r in line.Runs)
        {
            if ((mode == 1 && !r.IsLabel) || (mode == 2 && r.IsLabel))
            {
                continue;
            }

            if (r.Highlight is { } hl && r.Width > 0)
            {
                list.FillRect(o.NextId(), new RectF((float)(r.Left * K), (float)(line.Top * K), (float)(r.Width * K), (float)(line.Height * K)), Paint.FromColor(Color(hl)));
            }
        }

        foreach (PlacedRun r in line.Runs)
        {
            if (r.Math is { } math)
            {
                if (mode != 1)
                {
                    PaintMath(list, math, r, baseline, o);
                }

                continue;
            }

            if (r.Glyphs.Length == 0 || (mode == 1 && !r.IsLabel) || (mode == 2 && r.IsLabel))
            {
                continue;
            }

            string? target = r.LinkRelationship is { } rel ? o.LinkTarget?.Invoke(rel) : null;
            bool link = o.Tagged && target is not null;
            if (link)
            {
                list.BeginTag(o.NextId(), TagRole.Link, linkTarget: target);
            }

            var xs = new float[r.X.Length];
            var ys = new float[r.Y.Length];
            for (int i = 0; i < xs.Length; i++)
            {
                xs[i] = (float)(r.X[i] * K);
            }

            for (int i = 0; i < ys.Length; i++)
            {
                ys[i] = (float)(r.Y[i] * K);
            }

            list.DrawGlyphRun(o.NextId(), r.Face, (float)(r.Size * K), (float)(r.Left * K), (float)((baseline - r.Raise) * K), Color(r.Color), r.Glyphs, xs, ys, r.Clusters, r.Text, r.Rtl);
            if (link)
            {
                list.EndTag();
            }

        }

        foreach (PlacedRun r in line.Runs)
        {
            if (r.Width <= 0 || (mode == 1 && !r.IsLabel) || (mode == 2 && r.IsLabel) || (r.Underline is null && r.Strike is null))
            {
                continue;
            }

            double upem = r.Face.UnitsPerEm;
            double thickness = Math.Max(0.5, r.Face.Metrics.UnderlineThickness / upem * r.Size);
            Paint paint = Paint.FromColor(Color(r.Color));
            double y = baseline - r.Raise;
            if (r.Underline is not null)
            {
                double pos = r.Face.Metrics.UnderlinePosition is var up && up != 0 ? -up / upem * r.Size : r.Size * 0.1;
                double w = r.Underline.StartsWith("heavy", StringComparison.Ordinal) ? thickness * 2 : thickness;
                list.FillRect(o.NextId(), new RectF((float)(r.Left * K), (float)((y + pos) * K), (float)(r.Width * K), (float)(w * K)), paint);
                if (r.Underline == "dbl")
                {
                    list.FillRect(o.NextId(), new RectF((float)(r.Left * K), (float)((y + pos + (thickness * 2.5)) * K), (float)(r.Width * K), (float)(thickness * K)), paint);
                }
            }

            if (r.Strike is not null)
            {
                double mid = y - (r.Size * 0.3);
                list.FillRect(o.NextId(), new RectF((float)(r.Left * K), (float)(mid * K), (float)(r.Width * K), (float)(thickness * K)), paint);
                if (r.Strike == "dblStrike")
                {
                    list.FillRect(o.NextId(), new RectF((float)(r.Left * K), (float)((mid - (thickness * 2.5)) * K), (float)(r.Width * K), (float)(thickness * K)), paint);
                }
            }
        }

    }
}
