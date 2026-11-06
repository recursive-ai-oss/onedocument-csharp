using System.Globalization;
using System.Numerics;
using System.Text;
using OneFont;
using OneRender.Painting;

namespace RecursiveAI.OneDocument.Rendering.Svg;

/// <summary>
/// Writes a OneRender display list as an SVG 1.1 document (with the CSS compositing and masking properties browsers add): one page, one file.
/// </summary>
/// <remarks>
/// <para>
/// Fills, gradients, clips, transforms, opacity groups, blend modes, filters (the CSS filter functions as SVG filter primitives), one-layer
/// masks, images, glyph runs, borders and outer box shadows are written as SVG elements. Semantic tags become <c>role</c>, <c>aria-label</c>,
/// <c>&lt;title&gt;</c> and <c>&lt;a&gt;</c> links. What SVG cannot say (conic gradients, rounded or inset borders and shadows, masks of several
/// layers, 3D transforms) is drawn by the <see cref="SvgOptions.Rasterizer"/> and embedded as an image at the place it belongs; without a
/// rasterizer it is written without the effect. Every such step is reported in <see cref="SvgWriteResult.Notes"/>.
/// </para>
/// <para>
/// The namespace depends only on the display-list types, so it can be lifted out of this package. Output is deterministic: ids come from a
/// counter and numbers are written in the invariant culture with at most <see cref="SvgOptions.Precision"/> decimals.
/// </para>
/// </remarks>
public static class SvgWriter
{
    /// <summary>Writes a display list as an SVG document.</summary>
    /// <param name="list">The finished list, in CSS pixels.</param>
    /// <param name="destination">The stream to write UTF-8 to, without a byte-order mark; it is left open.</param>
    /// <param name="options">Page size and settings; <see cref="SvgOptions.Width"/> and <see cref="SvgOptions.Height"/> are required.</param>
    /// <param name="cancellationToken">Cancels between items.</param>
    /// <returns>What was approximated.</returns>
    /// <exception cref="ArgumentException">The size is not a positive finite number, or the list has an unclosed push.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static SvgWriteResult Write(DisplayList list, Stream destination, SvgOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using var writer = new StreamWriter(destination, new UTF8Encoding(false), 16384, leaveOpen: true) { NewLine = "\n" };
        SvgWriteResult result = Write(list, writer, options, cancellationToken);
        writer.Flush();
        return result;
    }

    /// <summary>Writes a display list as an SVG document.</summary>
    /// <param name="list">The finished list, in CSS pixels.</param>
    /// <param name="destination">The writer to write to; it is not disposed.</param>
    /// <param name="options">Page size and settings.</param>
    /// <param name="cancellationToken">Cancels between items.</param>
    /// <returns>What was approximated.</returns>
    /// <exception cref="ArgumentException">The size is not a positive finite number, or the list has an unclosed push.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static SvgWriteResult Write(DisplayList list, TextWriter destination, SvgOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
        if (!(options.Width > 0) || !(options.Height > 0) || !double.IsFinite(options.Width) || !double.IsFinite(options.Height) || !(options.Scale > 0))
        {
            throw new ArgumentException("The drawing needs a positive finite width, height and scale.", nameof(options));
        }

        if (options.Precision is < 0 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Precision, "Precision is 0 to 6.");
        }

        list.Validate();
        var emitter = new Emitter(list, destination, options, cancellationToken);
        emitter.Run();
        return emitter.Result();
    }

    /// <summary>Writes a display list as an SVG document in a string.</summary>
    /// <param name="list">The finished list, in CSS pixels.</param>
    /// <param name="options">Page size and settings.</param>
    /// <returns>The SVG text.</returns>
    /// <exception cref="ArgumentException">The size is not a positive finite number, or the list has an unclosed push.</exception>
    public static string ToSvg(DisplayList list, SvgOptions options)
    {
        using var sw = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        Write(list, sw, options);
        return sw.ToString();
    }

    private enum FrameKind
    {
        Plain,
        Mask,
        Passthrough,
    }

    private sealed class Frame(FrameKind kind, string? close)
    {
        public FrameKind Kind { get; } = kind;

        public string? Close { get; } = close;

        public Matrix3x2 SavedCtm { get; set; }

        public bool ContentOpen { get; set; }

        public string MaskId { get; set; } = string.Empty;
    }

    private sealed class Emitter
    {
        private readonly DisplayList _list;
        private readonly TextWriter _w;
        private readonly SvgOptions _o;
        private readonly CancellationToken _ct;
        private readonly List<Frame> _stack = [];
        private readonly List<SvgNote> _notes = [];
        private readonly HashSet<string> _noted = new(StringComparer.Ordinal);
        private readonly Dictionary<(int Handle, bool Pixelated), string> _images = [];
        private readonly Dictionary<int, bool> _outlineOk = [];
        private readonly string _numFormat;
        private Matrix3x2 _ctm = Matrix3x2.Identity;
        private int _ids;
        private int _textRuns;
        private int _outlineRuns;
        private int _rasterized;

        public Emitter(DisplayList list, TextWriter w, SvgOptions o, CancellationToken ct)
        {
            _list = list;
            _w = w;
            _o = o;
            _ct = ct;
            _numFormat = o.Precision == 0 ? "0" : "0." + new string('#', o.Precision);
        }

        public SvgWriteResult Result() => new(_notes, _textRuns, _outlineRuns, _rasterized);

        private void Note(string code, string message)
        {
            if (_noted.Add(code))
            {
                _notes.Add(new SvgNote(code, message));
            }
        }

        // ---- text helpers

        private string Id(string prefix) => prefix + (++_ids).ToString(CultureInfo.InvariantCulture);

        private string N(double v)
        {
            double r = Math.Round(v, _o.Precision, MidpointRounding.AwayFromZero);
            if (r == 0 || double.IsNaN(r))
            {
                return "0";
            }

            if (double.IsInfinity(r))
            {
                r = r > 0 ? 1e9 : -1e9;
            }

            return r.ToString(_numFormat, CultureInfo.InvariantCulture);
        }

        private static string Hex(PaintColor c)
        {
            return "#" + PaintColor.ToByte(c.R).ToString("x2", CultureInfo.InvariantCulture)
                + PaintColor.ToByte(c.G).ToString("x2", CultureInfo.InvariantCulture)
                + PaintColor.ToByte(c.B).ToString("x2", CultureInfo.InvariantCulture);
        }

        // fill="#rrggbb" fill-opacity="a" for a colour.
        private string FillAttr(PaintColor c, string name = "fill")
        {
            string s = " " + name + "=\"" + Hex(c) + "\"";
            if (c.A < 1f)
            {
                s += " " + name + "-opacity=\"" + N(c.A) + "\"";
            }

            return s;
        }

        private static string Esc(string s)
        {
            StringBuilder? sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                string? rep = c switch
                {
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '"' => "&quot;",
                    _ => IsXmlChar(s, i) ? null : string.Empty,
                };
                if (rep is not null && sb is null)
                {
                    sb = new StringBuilder(s, 0, i, s.Length + 16);
                }

                if (rep is not null)
                {
                    sb!.Append(rep);
                }
                else
                {
                    sb?.Append(c);
                }
            }

            return sb?.ToString() ?? s;
        }

        // XML 1.0 Char production: surrogates are fine when paired.
        private static bool IsXmlChar(string s, int i)
        {
            char c = s[i];
            if (c is '\t' or '\n' or '\r' || (c >= 0x20 && c < 0xD800) || (c >= 0xE000 && c <= 0xFFFD))
            {
                return true;
            }

            if (char.IsHighSurrogate(c))
            {
                return i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
            }

            return char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(s[i - 1]);
        }

        // ---- main loop

        public void Run()
        {
            _w.Write("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" version=\"1.1\" width=\"");
            _w.Write(N(_o.Width * _o.Scale));
            _w.Write("\" height=\"");
            _w.Write(N(_o.Height * _o.Scale));
            _w.Write("\" viewBox=\"0 0 ");
            _w.Write(N(_o.Width));
            _w.Write(' ');
            _w.Write(N(_o.Height));
            _w.Write('"');
            if (_o.Language is { Length: > 0 } lang)
            {
                _w.Write(" xml:lang=\"" + Esc(lang) + "\"");
            }

            _w.WriteLine('>');
            if (_o.Title is { Length: > 0 } title)
            {
                _w.WriteLine("<title>" + Esc(title) + "</title>");
            }

            if (!_o.TransparentBackground)
            {
                _w.WriteLine("<rect width=\"" + N(_o.Width) + "\" height=\"" + N(_o.Height) + "\" fill=\"#ffffff\"/>");
            }

            for (int i = 0; i < _list.Count; i++)
            {
                _ct.ThrowIfCancellationRequested();
                DisplayItem it = _list[i];
                if (_stack.Count > 0 && _stack[^1] is { Kind: FrameKind.Mask, ContentOpen: false } mf && it.Kind != ItemKind.PushMaskLayer)
                {
                    if (it.Kind != ItemKind.PopMask)
                    {
                        _w.WriteLine("<g mask=\"url(#" + mf.MaskId + ")\">");
                        mf.ContentOpen = true;
                    }
                }

                i = Item(i, it);
            }

            _w.WriteLine("</svg>");
        }

        // Returns the index of the last item consumed.
        private int Item(int i, DisplayItem it)
        {
            switch (it.Kind)
            {
                case ItemKind.FillRect:
                    {
                        RectF r = it.Rect;
                        Paint p = it.Paint;
                        if (p.Kind == PaintKind.Conic && Island(i, i, "svg.conic-gradient", "A conic gradient is embedded as an image: SVG has no conic gradients.", 2))
                        {
                            return i;
                        }

                        string fill = PaintAttr(it, p, r);
                        _w.WriteLine("<rect x=\"" + N(r.X) + "\" y=\"" + N(r.Y) + "\" width=\"" + N(r.Width) + "\" height=\"" + N(r.Height) + "\"" + fill + "/>");
                        return i;
                    }

                case ItemKind.FillRoundedRect:
                    {
                        RectF r = it.Rect;
                        Paint p = it.Paint;
                        if (p.Kind == PaintKind.Conic && Island(i, i, "svg.conic-gradient", "A conic gradient is embedded as an image: SVG has no conic gradients.", 2))
                        {
                            return i;
                        }

                        string fill = PaintAttr(it, p, r);
                        _w.WriteLine("<path d=\"" + RoundedRectPath(r, it.Radii) + "\"" + fill + "/>");
                        return i;
                    }

                case ItemKind.FillPath:
                    {
                        Paint p = it.Paint;
                        if (p.Kind == PaintKind.Conic && Island(i, i, "svg.conic-gradient", "A conic gradient is embedded as an image: SVG has no conic gradients.", 2))
                        {
                            return i;
                        }

                        string d = PathData(it.PathVerbs, it.PathCoords);
                        if (d.Length == 0)
                        {
                            return i;
                        }

                        string fill = PaintAttr(it, p, it.LocalBounds);
                        string rule = it.FillRule == FillRule.EvenOdd ? " fill-rule=\"evenodd\"" : string.Empty;
                        _w.WriteLine("<path d=\"" + d + "\"" + fill + rule + "/>");
                        return i;
                    }

                case ItemKind.Border:
                    Border(i, it);
                    return i;
                case ItemKind.BoxShadow:
                    BoxShadow(i, it);
                    return i;
                case ItemKind.Image:
                    Image(it);
                    return i;
                case ItemKind.GlyphRun:
                    GlyphRun(it);
                    return i;
                case ItemKind.SvgImage:
                    SvgImage(it);
                    return i;
                case ItemKind.BackdropFilter:
                    Note("svg.backdrop-filter", "A backdrop filter is left out: SVG cannot filter what is painted behind an element.");
                    return i;
                case ItemKind.PushClipRect:
                    {
                        RectF r = it.Rect;
                        string id = Id("c");
                        _w.WriteLine("<clipPath id=\"" + id + "\"><rect x=\"" + N(r.X) + "\" y=\"" + N(r.Y) + "\" width=\"" + N(r.Width) + "\" height=\"" + N(r.Height) + "\"/></clipPath>");
                        _w.WriteLine("<g clip-path=\"url(#" + id + ")\">");
                        _stack.Add(new Frame(FrameKind.Plain, "</g>"));
                        return i;
                    }

                case ItemKind.PushClipRoundedRect:
                    {
                        string id = Id("c");
                        _w.WriteLine("<clipPath id=\"" + id + "\"><path d=\"" + RoundedRectPath(it.Rect, it.Radii) + "\"/></clipPath>");
                        _w.WriteLine("<g clip-path=\"url(#" + id + ")\">");
                        _stack.Add(new Frame(FrameKind.Plain, "</g>"));
                        return i;
                    }

                case ItemKind.PushClipPath:
                    {
                        string id = Id("c");
                        string rule = it.FillRule == FillRule.EvenOdd ? " clip-rule=\"evenodd\"" : string.Empty;
                        _w.WriteLine("<clipPath id=\"" + id + "\"><path d=\"" + PathData(it.PathVerbs, it.PathCoords) + "\"" + rule + "/></clipPath>");
                        _w.WriteLine("<g clip-path=\"url(#" + id + ")\">");
                        _stack.Add(new Frame(FrameKind.Plain, "</g>"));
                        return i;
                    }

                case ItemKind.PushTransform:
                    {
                        Matrix3x2 m = it.Matrix;
                        _w.WriteLine("<g transform=\"matrix(" + N(m.M11) + " " + N(m.M12) + " " + N(m.M21) + " " + N(m.M22) + " " + N(m.M31) + " " + N(m.M32) + ")\">");
                        _stack.Add(new Frame(FrameKind.Plain, "</g>") { SavedCtm = _ctm });
                        _ctm = m * _ctm;
                        return i;
                    }

                case ItemKind.PushTransform3D:
                    {
                        if (Island(i, it.MatchIndex, "svg.transform-3d", "A 3D transform is embedded as an image: SVG has only 2D transforms.", 2))
                        {
                            return it.MatchIndex;
                        }

                        Note("svg.transform-3d", "A 3D transform is left out: SVG has only 2D transforms.");
                        _w.WriteLine("<g>");
                        _stack.Add(new Frame(FrameKind.Passthrough, "</g>"));
                        return i;
                    }

                case ItemKind.PushGroup:
                    {
                        string style = "isolation:isolate";
                        if (it.Blend != BlendMode.Normal)
                        {
                            style += ";mix-blend-mode:" + BlendName(it.Blend);
                        }

                        string op = it.Opacity < 1f ? " opacity=\"" + N(it.Opacity) + "\"" : string.Empty;
                        _w.WriteLine("<g" + op + " style=\"" + style + "\">");
                        _stack.Add(new Frame(FrameKind.Plain, "</g>"));
                        return i;
                    }

                case ItemKind.PushFilter:
                    PushFilter(i, it);
                    return i;
                case ItemKind.PushMask:
                    return PushMask(i, it);
                case ItemKind.PushMaskLayer:
                    {
                        // Only reached for the single layer of a mask this writer can express (see PushMask).
                        Frame mf = _stack[^1];
                        RectF b = ScopeBounds(it.ParentIndex + 1, _list[it.ParentIndex].MatchIndex - 1);
                        string region = b.IsEmpty
                            ? string.Empty
                            : " maskUnits=\"userSpaceOnUse\" x=\"" + N(b.X - 4) + "\" y=\"" + N(b.Y - 4) + "\" width=\"" + N(b.Width + 8) + "\" height=\"" + N(b.Height + 8) + "\"";
                        string type = it.MaskMode == MaskLayerMode.Alpha ? " style=\"mask-type:alpha\"" : string.Empty;
                        mf.MaskId = Id("m");
                        _w.WriteLine("<mask id=\"" + mf.MaskId + "\"" + region + type + ">");
                        _stack.Add(new Frame(FrameKind.Plain, "</mask>"));
                        return i;
                    }

                case ItemKind.PushHostScope:
                    _stack.Add(new Frame(FrameKind.Passthrough, null));
                    return i;
                case ItemKind.BeginTag:
                    BeginTag(it);
                    return i;
                case ItemKind.PopTransform:
                    {
                        Frame f = PopFrame();
                        _ctm = f.SavedCtm;
                        return i;
                    }

                case ItemKind.PopMask:
                    {
                        if (_stack[^1].Kind != FrameKind.Mask)
                        {
                            PopFrame();
                            return i;
                        }

                        Frame f = _stack[^1];
                        _stack.RemoveAt(_stack.Count - 1);
                        if (f.ContentOpen)
                        {
                            _w.WriteLine("</g>");
                        }

                        return i;
                    }

                case ItemKind.PopClip:
                case ItemKind.PopGroup:
                case ItemKind.PopFilter:
                case ItemKind.PopMaskLayer:
                case ItemKind.PopHostScope:
                case ItemKind.EndTag:
                    PopFrame();
                    return i;
                default:
                    return i;
            }
        }

        private Frame PopFrame()
        {
            Frame f = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            if (f.Close is { } c)
            {
                _w.WriteLine(c);
            }

            return f;
        }

        private static string BlendName(BlendMode m) => m switch
        {
            BlendMode.Multiply => "multiply",
            BlendMode.Screen => "screen",
            BlendMode.Overlay => "overlay",
            BlendMode.Darken => "darken",
            BlendMode.Lighten => "lighten",
            BlendMode.ColorDodge => "color-dodge",
            BlendMode.ColorBurn => "color-burn",
            BlendMode.HardLight => "hard-light",
            BlendMode.SoftLight => "soft-light",
            BlendMode.Difference => "difference",
            BlendMode.Exclusion => "exclusion",
            BlendMode.Hue => "hue",
            BlendMode.Saturation => "saturation",
            BlendMode.Color => "color",
            BlendMode.Luminosity => "luminosity",
            BlendMode.PlusLighter => "plus-lighter",
            _ => "normal",
        };

        // ---- paint

        // The fill attributes for a paint; a gradient is defined just before the element and referenced by id.
        private string PaintAttr(in DisplayItem it, Paint p, RectF bounds)
        {
            if (p.Kind == PaintKind.Solid)
            {
                return p.Color.IsTransparent ? " fill=\"none\"" : FillAttr(p.Color);
            }

            GradientView g = it.Gradient;
            int n = g.StopCount;
            if (n == 0)
            {
                return " fill=\"none\"";
            }

            string id = Id("g");
            var stops = new GradientStop[n];
            for (int k = 0; k < n; k++)
            {
                stops[k] = g.GetStop(k);
            }

            if (p.Kind == PaintKind.Conic)
            {
                // No rasterizer: the average colour is the nearest honest answer.
                Note("svg.conic-gradient", "A conic gradient is painted in the mean of its stop colours: SVG has no conic gradients and no rasterizer was given.");
                float r = 0, gg = 0, b = 0, a = 0;
                foreach (GradientStop s in stops)
                {
                    r += s.Color.R;
                    gg += s.Color.G;
                    b += s.Color.B;
                    a += s.Color.A;
                }

                return FillAttr(new PaintColor(r / n, gg / n, b / n, a / n));
            }

            // A repeating gradient repeats the span between its first and last stop; SVG repeats its whole vector, so the vector is cut to that span.
            bool rep = g.Repeating && n > 1 && stops[^1].Offset > stops[0].Offset;
            float first = rep && p.Kind == PaintKind.Linear ? stops[0].Offset : 0f;
            float last = rep ? stops[^1].Offset : 1f;
            var sb = new StringBuilder();
            if (p.Kind == PaintKind.Linear)
            {
                double dx = g.X1 - g.X0, dy = g.Y1 - g.Y0;
                sb.Append("<linearGradient id=\"").Append(id).Append("\" gradientUnits=\"userSpaceOnUse\" x1=\"").Append(N(g.X0 + (dx * first)))
                    .Append("\" y1=\"").Append(N(g.Y0 + (dy * first))).Append("\" x2=\"").Append(N(g.X0 + (dx * last))).Append("\" y2=\"").Append(N(g.Y0 + (dy * last))).Append('"');
            }
            else
            {
                // A unit circle scaled to the ellipse; a repeating gradient's span (from the centre) becomes the radius.
                double rx = Math.Max(g.X1, 1e-6) * last, ry = Math.Max(g.Y1, 1e-6) * last;
                sb.Append("<radialGradient id=\"").Append(id).Append("\" gradientUnits=\"userSpaceOnUse\" cx=\"0\" cy=\"0\" r=\"1\" gradientTransform=\"matrix(")
                    .Append(N(rx)).Append(" 0 0 ").Append(N(ry)).Append(' ').Append(N(g.X0)).Append(' ').Append(N(g.Y0)).Append(")\"");
            }

            if (rep)
            {
                sb.Append(" spreadMethod=\"repeat\"");
            }

            sb.Append('>');
            float span = last - first;
            foreach (GradientStop s in stops)
            {
                float off = rep ? (s.Offset - first) / span : s.Offset;
                sb.Append("<stop offset=\"").Append(N(Math.Clamp(off, 0f, 1f))).Append("\" stop-color=\"").Append(Hex(s.Color)).Append('"');
                if (s.Color.A < 1f)
                {
                    sb.Append(" stop-opacity=\"").Append(N(s.Color.A)).Append('"');
                }

                sb.Append("/>");
            }

            sb.Append(p.Kind == PaintKind.Linear ? "</linearGradient>" : "</radialGradient>");
            _w.WriteLine(sb.ToString());
            return " fill=\"url(#" + id + ")\"";
        }

        // ---- shapes

        private string PathData(ReadOnlySpan<byte> verbs, ReadOnlySpan<float> coords)
        {
            var sb = new StringBuilder(verbs.Length * 12);
            int c = 0;
            foreach (byte v in verbs)
            {
                switch ((PathVerb)v)
                {
                    case PathVerb.Move:
                        sb.Append('M').Append(N(coords[c])).Append(' ').Append(N(coords[c + 1]));
                        c += 2;
                        break;
                    case PathVerb.Line:
                        sb.Append('L').Append(N(coords[c])).Append(' ').Append(N(coords[c + 1]));
                        c += 2;
                        break;
                    case PathVerb.Quad:
                        sb.Append('Q').Append(N(coords[c])).Append(' ').Append(N(coords[c + 1])).Append(' ').Append(N(coords[c + 2])).Append(' ').Append(N(coords[c + 3]));
                        c += 4;
                        break;
                    case PathVerb.Cubic:
                        sb.Append('C').Append(N(coords[c])).Append(' ').Append(N(coords[c + 1])).Append(' ').Append(N(coords[c + 2])).Append(' ')
                            .Append(N(coords[c + 3])).Append(' ').Append(N(coords[c + 4])).Append(' ').Append(N(coords[c + 5]));
                        c += 6;
                        break;
                    case PathVerb.Close:
                        sb.Append('Z');
                        break;
                }
            }

            return sb.ToString();
        }

        // A rounded rectangle with elliptical corners (CSS Backgrounds 3 §5.2: radii are scaled down to fit).
        private string RoundedRectPath(RectF r, CornerRadii radii)
        {
            CornerRadii c = radii.Constrained(r.Width, r.Height);
            if (c.IsZero)
            {
                return "M" + N(r.X) + " " + N(r.Y) + "H" + N(r.Right) + "V" + N(r.Bottom) + "H" + N(r.X) + "Z";
            }

            var sb = new StringBuilder();
            sb.Append('M').Append(N(r.X + c.TopLeftX)).Append(' ').Append(N(r.Y));
            sb.Append('H').Append(N(r.Right - c.TopRightX));
            Arc(sb, c.TopRightX, c.TopRightY, r.Right, r.Y + c.TopRightY);
            sb.Append('V').Append(N(r.Bottom - c.BottomRightY));
            Arc(sb, c.BottomRightX, c.BottomRightY, r.Right - c.BottomRightX, r.Bottom);
            sb.Append('H').Append(N(r.X + c.BottomLeftX));
            Arc(sb, c.BottomLeftX, c.BottomLeftY, r.X, r.Bottom - c.BottomLeftY);
            sb.Append('V').Append(N(r.Y + c.TopLeftY));
            Arc(sb, c.TopLeftX, c.TopLeftY, r.X + c.TopLeftX, r.Y);
            sb.Append('Z');
            return sb.ToString();
        }

        private void Arc(StringBuilder sb, float rx, float ry, float x, float y)
        {
            if (rx > 0 && ry > 0)
            {
                sb.Append('A').Append(N(rx)).Append(' ').Append(N(ry)).Append(" 0 0 1 ").Append(N(x)).Append(' ').Append(N(y));
            }
            else
            {
                sb.Append('L').Append(N(x)).Append(' ').Append(N(y));
            }
        }

        // ---- borders (CSS Backgrounds 3 §4)

        private void Border(int index, DisplayItem it)
        {
            RectF r = it.Rect;
            BorderSide t = it.BorderTop, rt = it.BorderRight, b = it.BorderBottom, l = it.BorderLeft;
            if (!it.Radii.IsZero && Island(index, index, "svg.rounded-border", "A rounded border is embedded as an image.", 2))
            {
                return;
            }

            if (!it.Radii.IsZero)
            {
                Note("svg.rounded-border", "A rounded border is drawn with square corners: no rasterizer was given.");
            }

            Side(0, t, r, t, rt, b, l);
            Side(1, rt, r, t, rt, b, l);
            Side(2, b, r, t, rt, b, l);
            Side(3, l, r, t, rt, b, l);
        }

        private void Side(int side, BorderSide s, RectF r, BorderSide t, BorderSide rt, BorderSide b, BorderSide l)
        {
            if (s.Width <= 0 || s.Style is BorderStyle.None or BorderStyle.Hidden || s.Color.IsTransparent)
            {
                return;
            }

            float wT = Math.Max(0, t.Width), wR = Math.Max(0, rt.Width), wB = Math.Max(0, b.Width), wL = Math.Max(0, l.Width);
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            // The side's trapezoid, mitred against its neighbours.
            (float X, float Y)[] poly = side switch
            {
                0 => [(x, y), (x + w, y), (x + w - wR, y + wT), (x + wL, y + wT)],
                1 => [(x + w, y), (x + w, y + h), (x + w - wR, y + h - wB), (x + w - wR, y + wT)],
                2 => [(x + w, y + h), (x, y + h), (x + wL, y + h - wB), (x + w - wR, y + h - wB)],
                _ => [(x, y + h), (x, y), (x + wL, y + wT), (x + wL, y + h - wB)],
            };
            bool topLeft = side is 0 or 3;
            switch (s.Style)
            {
                case BorderStyle.Dashed:
                case BorderStyle.Dotted:
                    {
                        float half = s.Width / 2;
                        (float x1, float y1, float x2, float y2) = side switch
                        {
                            0 => (x, y + half, x + w, y + half),
                            1 => (x + w - half, y, x + w - half, y + h),
                            2 => (x, y + h - half, x + w, y + h - half),
                            _ => (x + half, y, x + half, y + h),
                        };
                        string dash = s.Style == BorderStyle.Dashed
                            ? " stroke-dasharray=\"" + N(2 * s.Width) + " " + N(s.Width) + "\""
                            : " stroke-dasharray=\"0 " + N(2 * s.Width) + "\" stroke-linecap=\"round\"";
                        // The stroke is cut to the side's own trapezoid so it does not run into the corner of the neighbour.
                        string id = Id("c");
                        _w.WriteLine("<clipPath id=\"" + id + "\"><path d=\"" + Polygon(poly) + "\"/></clipPath>");
                        _w.WriteLine("<line x1=\"" + N(x1) + "\" y1=\"" + N(y1) + "\" x2=\"" + N(x2) + "\" y2=\"" + N(y2) + "\" fill=\"none\" stroke=\"" + Hex(s.Color)
                            + "\" stroke-width=\"" + N(s.Width) + "\"" + (s.Color.A < 1f ? " stroke-opacity=\"" + N(s.Color.A) + "\"" : string.Empty) + dash + " clip-path=\"url(#" + id + ")\"/>");
                        break;
                    }

                case BorderStyle.DoubleLine:
                    {
                        // Two strips of a third of the width each, with a gap of a third between.
                        FillPoly(Strip(poly, 0f, 1f / 3), s.Color);
                        FillPoly(Strip(poly, 2f / 3, 1f), s.Color);
                        break;
                    }

                case BorderStyle.Groove:
                case BorderStyle.Ridge:
                    {
                        bool outerDark = (s.Style == BorderStyle.Groove) == topLeft;
                        FillPoly(Strip(poly, 0f, 0.5f), outerDark ? Dark(s.Color) : s.Color);
                        FillPoly(Strip(poly, 0.5f, 1f), outerDark ? s.Color : Dark(s.Color));
                        break;
                    }

                case BorderStyle.Inset:
                case BorderStyle.Outset:
                    FillPoly(poly, (s.Style == BorderStyle.Inset) == topLeft ? Dark(s.Color) : s.Color);
                    break;
                default:
                    FillPoly(poly, s.Color);
                    break;
            }
        }

        // The part of a side's trapezoid between two fractions of its width, measured from the outer edge.
        private static (float X, float Y)[] Strip((float X, float Y)[] p, float from, float to)
        {
            // p[0]-p[1] is the outer edge and p[3]-p[2] the inner edge, so the fractions interpolate between them.
            (float X, float Y) A(float f) => (p[0].X + ((p[3].X - p[0].X) * f), p[0].Y + ((p[3].Y - p[0].Y) * f));
            (float X, float Y) B(float f) => (p[1].X + ((p[2].X - p[1].X) * f), p[1].Y + ((p[2].Y - p[1].Y) * f));
            return [A(from), B(from), B(to), A(to)];
        }

        private static PaintColor Dark(PaintColor c) => new(c.R * 2f / 3f, c.G * 2f / 3f, c.B * 2f / 3f, c.A);

        private string Polygon((float X, float Y)[] p)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < p.Length; i++)
            {
                sb.Append(i == 0 ? 'M' : 'L').Append(N(p[i].X)).Append(' ').Append(N(p[i].Y));
            }

            return sb.Append('Z').ToString();
        }

        private void FillPoly((float X, float Y)[] p, PaintColor c) =>
            _w.WriteLine("<path d=\"" + Polygon(p) + "\"" + FillAttr(c) + "/>");

        // ---- box shadow (CSS Backgrounds 3 §7)

        private void BoxShadow(int index, DisplayItem it)
        {
            if (it.ShadowColor.IsTransparent)
            {
                return;
            }

            if (it.ShadowInset)
            {
                if (!Island(index, index, "svg.inset-shadow", "An inset box shadow is embedded as an image.", 2))
                {
                    Note("svg.inset-shadow", "An inset box shadow is left out: SVG cannot express it and no rasterizer was given.");
                }

                return;
            }

            RectF r = it.Rect;
            float spread = it.ShadowSpread;
            var box = new RectF(r.X, r.Y, r.Width, r.Height);
            var shadow = new RectF(r.X + it.ShadowOffsetX - spread, r.Y + it.ShadowOffsetY - spread, r.Width + (2 * spread), r.Height + (2 * spread));
            if (shadow.Width <= 0 || shadow.Height <= 0)
            {
                return;
            }

            float sigma = it.ShadowBlur / 2f;
            float pad = (3 * sigma) + 2;
            string clip = Id("c");
            // The shadow shows only outside the box: a clip of everything minus the box, by the even-odd rule.
            float cx = Math.Min(box.X, shadow.X) - pad, cy = Math.Min(box.Y, shadow.Y) - pad;
            float cw = Math.Max(box.Right, shadow.Right) + pad - cx, ch = Math.Max(box.Bottom, shadow.Bottom) + pad - cy;
            _w.WriteLine("<clipPath id=\"" + clip + "\"><path clip-rule=\"evenodd\" d=\"M" + N(cx) + " " + N(cy) + "h" + N(cw) + "v" + N(ch) + "h" + N(-cw) + "z" + RoundedRectPath(box, it.Radii) + "\"/></clipPath>");
            string filter = string.Empty;
            if (sigma > 0)
            {
                string fid = Id("f");
                _w.WriteLine("<filter id=\"" + fid + "\" filterUnits=\"userSpaceOnUse\" x=\"" + N(shadow.X - pad) + "\" y=\"" + N(shadow.Y - pad) + "\" width=\"" + N(shadow.Width + (2 * pad))
                    + "\" height=\"" + N(shadow.Height + (2 * pad)) + "\"><feGaussianBlur stdDeviation=\"" + N(sigma) + "\"/></filter>");
                filter = " filter=\"url(#" + fid + ")\"";
            }

            _w.WriteLine("<g clip-path=\"url(#" + clip + ")\"><path d=\"" + RoundedRectPath(shadow, GrowRadii(it.Radii, spread)) + "\"" + FillAttr(it.ShadowColor) + filter + "/></g>");
        }

        private static CornerRadii GrowRadii(CornerRadii r, float by)
        {
            float G(float v) => v > 0 ? Math.Max(0, v + by) : 0;
            return new CornerRadii(G(r.TopLeftX), G(r.TopLeftY), G(r.TopRightX), G(r.TopRightY), G(r.BottomRightX), G(r.BottomRightY), G(r.BottomLeftX), G(r.BottomLeftY));
        }

        // ---- images

        private string? ImageDef(ImageHandle handle, bool pixelated, out int w, out int h)
        {
            w = h = 0;
            if (_o.Images is null || !_o.Images.TryGetImage(handle, out SvgImageData data) || data.Width < 1 || data.Height < 1)
            {
                return null;
            }

            w = data.Width;
            h = data.Height;
            if (_images.TryGetValue((handle.Value, pixelated), out string? id))
            {
                return id;
            }

            id = Id("i");
            _images[(handle.Value, pixelated)] = id;
            string style = pixelated ? " image-rendering=\"optimizeSpeed\" style=\"image-rendering:pixelated\"" : string.Empty;
            _w.WriteLine("<defs><image id=\"" + id + "\" width=\"" + w.ToString(CultureInfo.InvariantCulture) + "\" height=\"" + h.ToString(CultureInfo.InvariantCulture)
                + "\" preserveAspectRatio=\"none\"" + style + " xlink:href=\"data:" + data.MediaType + ";base64," + Convert.ToBase64String(data.Bytes) + "\"/></defs>");
            return id;
        }

        private void Image(DisplayItem it)
        {
            string? id = ImageDef(it.Image, it.Sampling == ImageSampling.Pixelated, out int iw, out int ih);
            if (id is null)
            {
                Note("svg.image-missing", "A picture is left out: its pixels were not available.");
                return;
            }

            RectF src = it.ImageSource.IsEmpty ? new RectF(0, 0, iw, ih) : it.ImageSource;
            if (it.IsTiledImage)
            {
                RectF tile = it.ImageDestination, area = it.ImageTileArea;
                if (area.IsEmpty || tile.IsEmpty)
                {
                    return;
                }

                string pid = Id("p");
                _w.WriteLine("<pattern id=\"" + pid + "\" patternUnits=\"userSpaceOnUse\" x=\"" + N(tile.X) + "\" y=\"" + N(tile.Y) + "\" width=\"" + N(tile.Width + it.ImageTileGapX)
                    + "\" height=\"" + N(tile.Height + it.ImageTileGapY) + "\">" + Viewport(id, 0, 0, tile.Width, tile.Height, src) + "</pattern>");
                _w.WriteLine("<rect x=\"" + N(area.X) + "\" y=\"" + N(area.Y) + "\" width=\"" + N(area.Width) + "\" height=\"" + N(area.Height) + "\" fill=\"url(#" + pid + ")\"/>");
                return;
            }

            RectF dst = it.ImageDestination;
            if (dst.IsEmpty)
            {
                return;
            }

            _w.WriteLine(Viewport(id, dst.X, dst.Y, dst.Width, dst.Height, src));
        }

        // The image placed so that the source rectangle exactly fills the destination: a nested svg is the clip and the scale in one.
        private string Viewport(string id, float x, float y, float w, float h, RectF src) =>
            "<svg x=\"" + N(x) + "\" y=\"" + N(y) + "\" width=\"" + N(w) + "\" height=\"" + N(h) + "\" viewBox=\"" + N(src.X) + " " + N(src.Y) + " " + N(src.Width) + " " + N(src.Height)
            + "\" preserveAspectRatio=\"none\" overflow=\"hidden\"><use xlink:href=\"#" + id + "\"/></svg>";

        // ---- inline SVG pictures

        private void SvgImage(DisplayItem it)
        {
            string? markup = it.SvgMarkup;
            RectF r = it.Rect;
            if (string.IsNullOrEmpty(markup) || r.IsEmpty)
            {
                return;
            }

            int start = markup.IndexOf("<svg", StringComparison.Ordinal);
            if (start < 0)
            {
                return;
            }

            int end = start + 4;
            char quote = '\0';
            while (end < markup.Length)
            {
                char c = markup[end];
                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        quote = '\0';
                    }
                }
                else if (c is '"' or '\'')
                {
                    quote = c;
                }
                else if (c == '>')
                {
                    break;
                }

                end++;
            }

            if (end >= markup.Length)
            {
                return;
            }

            string attrs = markup.Substring(start + 4, end - start - 4);
            bool selfClose = attrs.EndsWith('/');
            if (selfClose)
            {
                attrs = attrs[..^1];
            }

            string? ow = null, oh = null;
            bool hasViewBox = false;
            var kept = new StringBuilder();
            int p = 0;
            while (p < attrs.Length)
            {
                while (p < attrs.Length && char.IsWhiteSpace(attrs[p]))
                {
                    p++;
                }

                int ns = p;
                while (p < attrs.Length && attrs[p] != '=' && !char.IsWhiteSpace(attrs[p]))
                {
                    p++;
                }

                string name = attrs[ns..p];
                while (p < attrs.Length && char.IsWhiteSpace(attrs[p]))
                {
                    p++;
                }

                string value = string.Empty;
                int valueStart = p;
                if (p < attrs.Length && attrs[p] == '=')
                {
                    p++;
                    while (p < attrs.Length && char.IsWhiteSpace(attrs[p]))
                    {
                        p++;
                    }

                    if (p < attrs.Length && attrs[p] is '"' or '\'')
                    {
                        char q = attrs[p++];
                        int vs = p;
                        while (p < attrs.Length && attrs[p] != q)
                        {
                            p++;
                        }

                        value = attrs[vs..p];
                        p = Math.Min(p + 1, attrs.Length);
                    }
                }

                if (name.Length == 0)
                {
                    break;
                }

                switch (name)
                {
                    case "width":
                        ow = value;
                        break;
                    case "height":
                        oh = value;
                        break;
                    case "x" or "y" or "color":
                        break;
                    default:
                        if (name == "viewBox")
                        {
                            hasViewBox = true;
                        }

                        kept.Append(' ').Append(attrs, ns, p - ns);
                        break;
                }

                _ = valueStart;
            }

            string vb = string.Empty;
            if (!hasViewBox && double.TryParse(ow?.Replace("px", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out double vw)
                && double.TryParse(oh?.Replace("px", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out double vh) && vw > 0 && vh > 0)
            {
                vb = " viewBox=\"0 0 " + N(vw) + " " + N(vh) + "\"";
            }

            _w.Write("<svg x=\"" + N(r.X) + "\" y=\"" + N(r.Y) + "\" width=\"" + N(r.Width) + "\" height=\"" + N(r.Height) + "\" color=\"" + Hex(it.SvgColor) + "\"" + vb);
            _w.Write(kept.ToString());
            _w.Write(selfClose ? "/>" : ">");
            _w.WriteLine(markup[(end + 1)..]);
            Note("svg.inline-svg-ids", "Inline SVG pictures keep their own element ids; ids that clash with the page's are not renamed.");
        }

        // ---- glyph runs

        private void GlyphRun(DisplayItem it)
        {
            GlyphRunView run = it.GlyphRun;
            ReadOnlySpan<int> ids = run.GlyphIds;
            if (ids.Length == 0 || run.Color.IsTransparent)
            {
                return;
            }

            FontFace face = run.Face;
            bool canOutline = OutlinesAllowed(run.FaceIndex, face);
            bool bitmapFont = face.HasTable("CBDT") || face.HasTable("sbix");
            bool simple = IsSimpleRun(run);
            bool outline = _o.GlyphMode switch
            {
                SvgGlyphMode.Text => false,
                SvgGlyphMode.Outlines => canOutline && !bitmapFont,
                _ => !simple && canOutline && !bitmapFont,
            };
            string? filterId = null;
            if (run.Blur > 0f)
            {
                RectF b = it.LocalBounds;
                float sigma = run.Blur * 0.5f, pad = (3 * sigma) + 2;
                filterId = Id("f");
                _w.WriteLine("<filter id=\"" + filterId + "\" filterUnits=\"userSpaceOnUse\" x=\"" + N(b.X - pad) + "\" y=\"" + N(b.Y - pad) + "\" width=\"" + N(b.Width + (2 * pad))
                    + "\" height=\"" + N(b.Height + (2 * pad)) + "\"><feGaussianBlur stdDeviation=\"" + N(sigma) + "\"/></filter>");
            }

            string filter = filterId is null ? string.Empty : " filter=\"url(#" + filterId + ")\"";
            if (outline)
            {
                _outlineRuns++;
                string d = RunOutlines(run, face);
                if (d.Length > 0)
                {
                    _w.WriteLine("<path d=\"" + d + "\"" + FillAttr(run.Color) + filter + "/>");
                }

                return;
            }

            if (_o.GlyphMode == SvgGlyphMode.Outlines && !canOutline)
            {
                Note("svg.outlines-refused", "Glyphs of a font whose licence flags forbid embedding are written as text, not as outlines.");
            }

            _textRuns++;
            WriteTextRun(run, face, simple, filter);
        }

        // OpenType OS/2 fsType (offset 8): bit 1 is "restricted licence embedding"; outlines of such fonts are not written.
        private bool OutlinesAllowed(int faceIndex, FontFace face)
        {
            if (_outlineOk.TryGetValue(faceIndex, out bool ok))
            {
                return ok;
            }

            ReadOnlyMemory<byte> os2 = face.GetTable("OS/2");
            ok = os2.Length < 10 || (((os2.Span[8] << 8) | os2.Span[9]) & 0x0002) == 0;
            _outlineOk[faceIndex] = ok;
            return ok;
        }

        // One glyph per BMP character, in logical order, left to right: the SVG text model places exactly these.
        private static bool IsSimpleRun(GlyphRunView run)
        {
            ReadOnlySpan<int> clusters = run.Clusters;
            ReadOnlySpan<char> text = run.Text;
            if (run.RightToLeft || clusters.Length != run.GlyphIds.Length || text.Length != clusters.Length)
            {
                return false;
            }

            for (int i = 0; i < clusters.Length; i++)
            {
                if (clusters[i] != i || char.IsSurrogate(text[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private string RunOutlines(GlyphRunView run, FontFace face)
        {
            var sink = new PathSink(this);
            float scale = run.FontSize / Math.Max(1, face.UnitsPerEm);
            ReadOnlySpan<int> ids = run.GlyphIds;
            ReadOnlySpan<float> xs = run.X, ys = run.Y;
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == 0)
                {
                    continue; // .notdef is not drawn, as in the image and PDF backends
                }

                GlyphOutline outline = face.GetOutline(ids[i]);
                if (outline.IsEmpty)
                {
                    continue;
                }

                // Font units have y up; the page has y down.
                outline.Write(sink, new Matrix3x2(scale, 0, 0, -scale, run.OriginX + xs[i], run.OriginY + (ys.Length > i ? ys[i] : 0)));
            }

            return sink.Text();
        }

        private sealed class PathSink(Emitter e) : IOutlineSink
        {
            private readonly StringBuilder _sb = new();

            public string Text() => _sb.ToString();

            public void MoveTo(float x, float y) => _sb.Append('M').Append(e.N(x)).Append(' ').Append(e.N(y));

            public void LineTo(float x, float y) => _sb.Append('L').Append(e.N(x)).Append(' ').Append(e.N(y));

            public void QuadTo(float cx, float cy, float x, float y) =>
                _sb.Append('Q').Append(e.N(cx)).Append(' ').Append(e.N(cy)).Append(' ').Append(e.N(x)).Append(' ').Append(e.N(y));

            public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y) =>
                _sb.Append('C').Append(e.N(c1x)).Append(' ').Append(e.N(c1y)).Append(' ').Append(e.N(c2x)).Append(' ').Append(e.N(c2y)).Append(' ').Append(e.N(x)).Append(' ').Append(e.N(y));

            public void Close() => _sb.Append('Z');
        }

        private void WriteTextRun(GlyphRunView run, FontFace face, bool simple, string filter)
        {
            ReadOnlySpan<int> ids = run.GlyphIds;
            ReadOnlySpan<float> xs = run.X, ys = run.Y;
            ReadOnlySpan<int> clusters = run.Clusters;
            string text = new(run.Text);
            var head = new StringBuilder();
            head.Append("<text font-family=\"").Append(Esc(face.FamilyName)).Append("\" font-size=\"").Append(N(run.FontSize)).Append('"');
            if (face.Weight != 400 && face.Weight > 0)
            {
                head.Append(" font-weight=\"").Append(face.Weight.ToString(CultureInfo.InvariantCulture)).Append('"');
            }

            if (face.IsItalic)
            {
                head.Append(" font-style=\"italic\"");
            }

            head.Append(FillAttr(run.Color)).Append(filter).Append(" xml:space=\"preserve\"");
            if (simple)
            {
                var xl = new StringBuilder();
                var yl = new StringBuilder();
                var body = new StringBuilder();
                float y0 = ys.Length > 0 ? ys[0] : 0;
                bool sameY = true;
                for (int i = 0; i < ids.Length; i++)
                {
                    if (ids[i] == 0 || !IsXmlChar(text, i))
                    {
                        continue; // a character the face does not have draws nothing
                    }

                    if (xl.Length > 0)
                    {
                        xl.Append(' ');
                        yl.Append(' ');
                    }

                    float gy = ys.Length > i ? ys[i] : 0;
                    sameY &= gy == y0;
                    xl.Append(N(run.OriginX + xs[i]));
                    yl.Append(N(run.OriginY + gy));
                    body.Append(Esc(text[i].ToString()));
                }

                if (body.Length == 0)
                {
                    return;
                }

                string y = sameY ? N(run.OriginY + y0) : yl.ToString();
                _w.WriteLine(head + " x=\"" + xl + "\" y=\"" + y + "\">" + body + "</text>");
                return;
            }

            // Not one glyph per character: each cluster gets its own tspan at its first glyph's position.
            Note("svg.text-approximated", "Some glyph runs (ligatures, marks, right-to-left text) are written as text positioned per cluster; a viewer's own shaping may differ.");
            var sb = new StringBuilder(head.ToString()).Append('>');
            var done = new HashSet<int>();
            for (int i = 0; i < ids.Length; i++)
            {
                int c = clusters.Length > i ? clusters[i] : i;
                if (ids[i] == 0 || !done.Add(c) || c < 0 || c >= text.Length)
                {
                    continue;
                }

                int next = text.Length;
                for (int k = 0; k < clusters.Length; k++)
                {
                    if (clusters[k] > c && clusters[k] < next)
                    {
                        next = clusters[k];
                    }
                }

                float gy = ys.Length > i ? ys[i] : 0;
                sb.Append("<tspan x=\"").Append(N(run.OriginX + xs[i])).Append("\" y=\"").Append(N(run.OriginY + gy)).Append("\">").Append(Esc(text[c..next])).Append("</tspan>");
            }

            _w.WriteLine(sb.Append("</text>").ToString());
        }

        // ---- filters (Filter Effects 1 §5)

        private void PushFilter(int index, DisplayItem it)
        {
            int n = it.FilterOpCount;
            var sb = new StringBuilder();
            string fid = Id("f");
            RectF b = ScopeBounds(index + 1, it.MatchIndex - 1);
            float pad = 4;
            for (int k = 0; k < n; k++)
            {
                FilterOp op = it.GetFilterOp(k);
                if (op.Kind == FilterOpKind.Blur)
                {
                    pad += 3 * op.A;
                }
                else if (op.Kind == FilterOpKind.DropShadow)
                {
                    pad += (3 * op.C) + Math.Max(Math.Abs(op.A), Math.Abs(op.B));
                }
            }

            sb.Append("<filter id=\"").Append(fid).Append("\" color-interpolation-filters=\"sRGB\"");
            if (!b.IsEmpty)
            {
                sb.Append(" filterUnits=\"userSpaceOnUse\" x=\"").Append(N(b.X - pad)).Append("\" y=\"").Append(N(b.Y - pad)).Append("\" width=\"").Append(N(b.Width + (2 * pad)))
                    .Append("\" height=\"").Append(N(b.Height + (2 * pad))).Append('"');
            }
            else
            {
                sb.Append(" x=\"-50%\" y=\"-50%\" width=\"200%\" height=\"200%\"");
            }

            sb.Append('>');
            for (int k = 0; k < n; k++)
            {
                FilterOp op = it.GetFilterOp(k);
                float a = op.A;
                float inv;
                switch (op.Kind)
                {
                    case FilterOpKind.Blur:
                        sb.Append("<feGaussianBlur stdDeviation=\"").Append(N(a)).Append("\"/>");
                        break;
                    case FilterOpKind.Brightness:
                        sb.Append("<feComponentTransfer>").Append(Funcs("type=\"linear\" slope=\"" + N(a) + "\"")).Append("</feComponentTransfer>");
                        break;
                    case FilterOpKind.Contrast:
                        sb.Append("<feComponentTransfer>").Append(Funcs("type=\"linear\" slope=\"" + N(a) + "\" intercept=\"" + N((-0.5 * a) + 0.5) + "\"")).Append("</feComponentTransfer>");
                        break;
                    case FilterOpKind.Grayscale:
                        a = Math.Clamp(a, 0f, 1f);
                        inv = 1 - a;
                        sb.Append(FeMatrix(
                            0.2126f + (0.7874f * inv), 0.7152f - (0.7152f * inv), 0.0722f - (0.0722f * inv),
                            0.2126f - (0.2126f * inv), 0.7152f + (0.2848f * inv), 0.0722f - (0.0722f * inv),
                            0.2126f - (0.2126f * inv), 0.7152f - (0.7152f * inv), 0.0722f + (0.9278f * inv)));
                        break;
                    case FilterOpKind.HueRotate:
                        sb.Append("<feColorMatrix type=\"hueRotate\" values=\"").Append(N(a * 180.0 / Math.PI)).Append("\"/>");
                        break;
                    case FilterOpKind.Invert:
                        a = Math.Clamp(a, 0f, 1f);
                        sb.Append("<feComponentTransfer>").Append(Funcs("type=\"table\" tableValues=\"" + N(a) + " " + N(1 - a) + "\"")).Append("</feComponentTransfer>");
                        break;
                    case FilterOpKind.Opacity:
                        sb.Append("<feComponentTransfer><feFuncA type=\"linear\" slope=\"").Append(N(Math.Clamp(a, 0f, 1f))).Append("\"/></feComponentTransfer>");
                        break;
                    case FilterOpKind.Saturate:
                        sb.Append("<feColorMatrix type=\"saturate\" values=\"").Append(N(Math.Max(0, a))).Append("\"/>");
                        break;
                    case FilterOpKind.Sepia:
                        a = Math.Clamp(a, 0f, 1f);
                        inv = 1 - a;
                        sb.Append(FeMatrix(
                            0.393f + (0.607f * inv), 0.769f - (0.769f * inv), 0.189f - (0.189f * inv),
                            0.349f - (0.349f * inv), 0.686f + (0.314f * inv), 0.168f - (0.168f * inv),
                            0.272f - (0.272f * inv), 0.534f - (0.534f * inv), 0.131f + (0.869f * inv)));
                        break;
                    case FilterOpKind.DropShadow:
                        sb.Append("<feGaussianBlur in=\"SourceAlpha\" stdDeviation=\"").Append(N(op.C)).Append("\" result=\"b\"/><feOffset in=\"b\" dx=\"").Append(N(op.A)).Append("\" dy=\"")
                            .Append(N(op.B)).Append("\" result=\"o\"/><feFlood flood-color=\"").Append(Hex(op.Color)).Append("\" flood-opacity=\"").Append(N(op.Color.A))
                            .Append("\"/><feComposite in2=\"o\" operator=\"in\"/><feMerge><feMergeNode/><feMergeNode in=\"SourceGraphic\"/></feMerge>");
                        break;
                }
            }

            sb.Append("</filter>");
            _w.WriteLine(sb.ToString());
            _w.WriteLine("<g filter=\"url(#" + fid + ")\">");
            _stack.Add(new Frame(FrameKind.Plain, "</g>"));
            _ = index;
        }

        private static string Funcs(string attrs)
        {
            return "<feFuncR " + attrs + "/><feFuncG " + attrs + "/><feFuncB " + attrs + "/>";
        }

        private string FeMatrix(float a, float b, float c, float d, float e, float f, float g, float h, float i) =>
            "<feColorMatrix type=\"matrix\" values=\"" + string.Join(' ', N(a), N(b), N(c), "0 0", N(d), N(e), N(f), "0 0", N(g), N(h), N(i), "0 0 0 0 0 1 0") + "\"/>";

        // ---- masks (Masking 1 §6)

        private int PushMask(int index, DisplayItem it)
        {
            int match = it.MatchIndex;
            int layers = 0;
            bool simple = true;
            for (int j = index + 1; j < match; j++)
            {
                DisplayItem x = _list[j];
                if (x.Kind == ItemKind.PushMaskLayer && x.ParentIndex == index)
                {
                    layers++;
                    simple &= x.MaskComposite == MaskCompositeOp.Add;
                }
            }

            if (layers == 0)
            {
                return match; // a mask with no layers hides everything
            }

            if (layers == 1 && simple)
            {
                _stack.Add(new Frame(FrameKind.Mask, null));
                return index;
            }

            if (Island(index, match, "svg.mask-layers", "A mask of several layers is embedded as an image: SVG masks have one layer.", 2))
            {
                return match;
            }

            Note("svg.mask-layers", "A mask of several layers is left out: SVG masks have one layer and no rasterizer was given.");
            _w.WriteLine("<g>");
            _stack.Add(new Frame(FrameKind.Passthrough, "</g>"));
            // The layers are skipped; the content follows them.
            int j2 = index + 1;
            while (j2 < match && _list[j2].Kind == ItemKind.PushMaskLayer && _list[j2].ParentIndex == index)
            {
                j2 = _list[j2].MatchIndex + 1;
            }

            return j2 - 1;
        }

        // ---- tags (WAI-ARIA roles for the structure types)

        private void BeginTag(DisplayItem it)
        {
            if (!_o.IncludeTags)
            {
                _stack.Add(new Frame(FrameKind.Plain, null));
                return;
            }

            TagRole role = it.Role;
            string? alt = it.AltText;
            string? lang = it.Language;
            string langAttr = lang is { Length: > 0 } ? " xml:lang=\"" + Esc(lang) + "\"" : string.Empty;
            string label = alt is { Length: > 0 } ? " aria-label=\"" + Esc(alt) + "\"" : string.Empty;
            string title = alt is { Length: > 0 } ? "<title>" + Esc(alt) + "</title>" : string.Empty;
            if (role == TagRole.Link)
            {
                string? target = it.LinkTarget;
                if (target is { Length: > 0 })
                {
                    _w.WriteLine("<a xlink:href=\"" + Esc(target) + "\"" + label + langAttr + ">" + title);
                    _stack.Add(new Frame(FrameKind.Plain, "</a>"));
                    return;
                }
            }

            string? aria = role switch
            {
                TagRole.Artifact => " aria-hidden=\"true\"",
                TagRole.Paragraph => " role=\"paragraph\"",
                TagRole.Heading1 => " role=\"heading\" aria-level=\"1\"",
                TagRole.Heading2 => " role=\"heading\" aria-level=\"2\"",
                TagRole.Heading3 => " role=\"heading\" aria-level=\"3\"",
                TagRole.Heading4 => " role=\"heading\" aria-level=\"4\"",
                TagRole.Heading5 => " role=\"heading\" aria-level=\"5\"",
                TagRole.Heading6 => " role=\"heading\" aria-level=\"6\"",
                TagRole.List => " role=\"list\"",
                TagRole.ListItem => " role=\"listitem\"",
                TagRole.Table => " role=\"table\"",
                TagRole.TableRow => " role=\"row\"",
                TagRole.TableHeaderCell => " role=\"columnheader\"" + Spans(it),
                TagRole.TableDataCell => " role=\"cell\"" + Spans(it),
                TagRole.TableHead or TagRole.TableBody or TagRole.TableFoot => " role=\"rowgroup\"",
                TagRole.Figure => alt is { Length: > 0 } ? " role=\"img\"" : " role=\"figure\"",
                TagRole.Caption => " role=\"caption\"",
                TagRole.BlockQuote => " role=\"blockquote\"",
                TagRole.Code => " role=\"code\"",
                TagRole.Form => " role=\"form\"",
                TagRole.Link => " role=\"link\"",
                TagRole.Note => " role=\"doc-footnote\"",
                TagRole.Reference => " role=\"doc-noteref\"",
                TagRole.TOC => " role=\"doc-toc\"",
                TagRole.TOCI => " role=\"listitem\"",
                TagRole.Formula => " role=\"math\"",
                _ => null,
            };
            if (aria is null && label.Length == 0 && langAttr.Length == 0)
            {
                _stack.Add(new Frame(FrameKind.Plain, null));
                return;
            }

            _w.WriteLine("<g" + aria + label + langAttr + ">" + title);
            _stack.Add(new Frame(FrameKind.Plain, "</g>"));
        }

        private static string Spans(DisplayItem it)
        {
            string s = string.Empty;
            if (it.ColumnSpan > 1)
            {
                s += " aria-colspan=\"" + it.ColumnSpan.ToString(CultureInfo.InvariantCulture) + "\"";
            }

            if (it.RowSpan > 1)
            {
                s += " aria-rowspan=\"" + it.RowSpan.ToString(CultureInfo.InvariantCulture) + "\"";
            }

            return s;
        }

        // ---- rasterized regions

        // The area the painting items first..last cover, in the space of the current transform. A push item has no extent of its own,
        // so a scope's area is the union of the layer bounds of what it contains (unclipped, and already widened by the outset of enclosing
        // filters), moved back through the transform.
        private RectF ScopeBounds(int first, int last)
        {
            RectF u = default;
            bool any = false;
            for (int k = first; k <= last && k < _list.Count; k++)
            {
                DisplayItem x = _list[k];
                if (x.Kind is >= ItemKind.PushClipRect and <= ItemKind.PopGroup or ItemKind.BeginTag or ItemKind.EndTag or ItemKind.PushFilter or ItemKind.PopFilter
                    or ItemKind.PushMask or ItemKind.PopMask or ItemKind.PushMaskLayer or ItemKind.PopMaskLayer or ItemKind.PushTransform3D or ItemKind.PushHostScope or ItemKind.PopHostScope)
                {
                    continue;
                }

                RectF r = x.LayerBounds;
                if (r.IsEmpty || !float.IsFinite(r.X) || !float.IsFinite(r.Width) || !float.IsFinite(r.Y) || !float.IsFinite(r.Height))
                {
                    continue;
                }

                if (!any)
                {
                    u = r;
                    any = true;
                    continue;
                }

                float x0 = Math.Min(u.X, r.X), y0 = Math.Min(u.Y, r.Y);
                u = new RectF(x0, y0, Math.Max(u.Right, r.Right) - x0, Math.Max(u.Bottom, r.Bottom) - y0);
            }

            if (!any || !Matrix3x2.Invert(_ctm, out Matrix3x2 inv))
            {
                return default;
            }

            Vector2 a = Vector2.Transform(new Vector2(u.X, u.Y), inv), b = Vector2.Transform(new Vector2(u.Right, u.Y), inv);
            Vector2 c = Vector2.Transform(new Vector2(u.X, u.Bottom), inv), d = Vector2.Transform(new Vector2(u.Right, u.Bottom), inv);
            float minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)), minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
            float maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)), maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
            return new RectF(minX, minY, maxX - minX, maxY - minY);
        }

        // Draws items first..last (a single item, or a push through its pop) with the rasterizer and writes the result as an image at the
        // place the items occupy. Returns false, writing nothing, when there is no rasterizer or the region is empty or too big.
        private bool Island(int first, int last, string code, string message, int margin)
        {
            if (_o.Rasterizer is null)
            {
                return false;
            }

            RectF b = first == last ? _list[first].LocalBounds : ScopeBounds(first + 1, last - 1);
            if (b.IsEmpty || !float.IsFinite(b.Width) || !float.IsFinite(b.Height))
            {
                return false;
            }

            float pad = margin;
            for (int k = first; k <= last; k++)
            {
                DisplayItem x = _list[k];
                if (x.Kind == ItemKind.PushFilter)
                {
                    for (int q = 0; q < x.FilterOpCount; q++)
                    {
                        FilterOp op = x.GetFilterOp(q);
                        pad += op.Kind == FilterOpKind.Blur ? 3 * op.A : op.Kind == FilterOpKind.DropShadow ? (3 * op.C) + Math.Max(Math.Abs(op.A), Math.Abs(op.B)) : 0;
                    }
                }
                else if (x.Kind == ItemKind.BoxShadow)
                {
                    pad += (3 * x.ShadowBlur / 2) + x.ShadowSpread + Math.Max(Math.Abs(x.ShadowOffsetX), Math.Abs(x.ShadowOffsetY));
                }
            }

            double ctmScale = Math.Sqrt(Math.Abs((_ctm.M11 * _ctm.M22) - (_ctm.M12 * _ctm.M21)));
            double ratio = Math.Max(0.25, _o.RasterScale * (ctmScale > 0 ? ctmScale : 1));
            double w = b.Width + (2 * pad), h = b.Height + (2 * pad);
            // About 16 million pixels at most per region.
            double limit = Math.Sqrt(16_000_000.0 / (w * h));
            if (ratio > limit)
            {
                ratio = limit;
            }

            int pw = (int)Math.Ceiling(w * ratio), ph = (int)Math.Ceiling(h * ratio);
            if (pw < 1 || ph < 1)
            {
                return false;
            }

            var region = new DisplayList();
            try
            {
                region.PushTransform(WrapperId(), Matrix3x2.CreateTranslation((float)(pad - b.X), (float)(pad - b.Y)));
                Copy(first, last, region);
                region.PopTransform();
                region.Validate();
                byte[]? png = _o.Rasterizer.RasterizePng(region, pw, ph, (float)ratio);
                if (png is null)
                {
                    return false;
                }

                string id = Id("r");
                _w.WriteLine("<image id=\"" + id + "\" x=\"" + N(b.X - pad) + "\" y=\"" + N(b.Y - pad) + "\" width=\"" + N(w) + "\" height=\"" + N(h)
                    + "\" preserveAspectRatio=\"none\" xlink:href=\"data:image/png;base64," + Convert.ToBase64String(png) + "\"/>");
                _rasterized++;
                Note(code, message);
                return true;
            }
            catch (DisplayListException)
            {
                return false;
            }
        }

        // An id the wrapper can use in the copy: the copied items keep their own ids, and a list refuses two items with one id.
        private ItemId WrapperId()
        {
            uint owner = uint.MaxValue;
            while (_list.TryFind(ItemId.Of(owner), out _))
            {
                owner--;
            }

            return ItemId.Of(owner);
        }

        // Copies items first..last into another list (ids and all), so a backend can draw them alone.
        private void Copy(int first, int last, DisplayList target)
        {
            for (int i = first; i <= last; i++)
            {
                DisplayItem it = _list[i];
                switch (it.Kind)
                {
                    case ItemKind.FillRect:
                        target.FillRect(it.Id, it.Rect, PaintOf(target, it));
                        break;
                    case ItemKind.FillRoundedRect:
                        target.FillRoundedRect(it.Id, it.Rect, it.Radii, PaintOf(target, it));
                        break;
                    case ItemKind.FillPath:
                        target.FillPath(it.Id, it.PathVerbs, it.PathCoords, PaintOf(target, it), it.FillRule);
                        break;
                    case ItemKind.Border:
                        target.DrawBorder(it.Id, it.Rect, it.Radii, it.BorderTop, it.BorderRight, it.BorderBottom, it.BorderLeft);
                        break;
                    case ItemKind.BoxShadow:
                        target.DrawBoxShadow(it.Id, it.Rect, it.Radii, it.ShadowColor, it.ShadowOffsetX, it.ShadowOffsetY, it.ShadowBlur, it.ShadowSpread, it.ShadowInset);
                        break;
                    case ItemKind.Image:
                        if (it.IsTiledImage)
                        {
                            target.DrawImageTiled(it.Id, it.Image, it.ImageSource, it.ImageDestination, it.ImageTileArea, it.ImageTileGapX, it.ImageTileGapY, it.Sampling);
                        }
                        else
                        {
                            target.DrawImage(it.Id, it.Image, it.ImageSource, it.ImageDestination, it.Sampling);
                        }

                        break;
                    case ItemKind.GlyphRun:
                        {
                            GlyphRunView r = it.GlyphRun;
                            target.DrawGlyphRun(it.Id, r.Face, r.FontSize, r.OriginX, r.OriginY, r.Color, r.GlyphIds, r.X, r.Y, r.Clusters, r.Text, r.RightToLeft, r.Blur);
                            break;
                        }

                    case ItemKind.SvgImage:
                        target.SvgImage(it.Id, it.Rect, it.SvgMarkup ?? string.Empty, it.SvgColor);
                        break;
                    case ItemKind.BackdropFilter:
                        target.BackdropFilter(it.Id, it.Rect, it.Radii, Ops(it));
                        break;
                    case ItemKind.PushClipRect:
                        target.PushClipRect(it.Id, it.Rect);
                        break;
                    case ItemKind.PushClipRoundedRect:
                        target.PushClipRoundedRect(it.Id, it.Rect, it.Radii);
                        break;
                    case ItemKind.PushClipPath:
                        target.PushClipPath(it.Id, it.PathVerbs, it.PathCoords, it.FillRule);
                        break;
                    case ItemKind.PopClip:
                        target.PopClip();
                        break;
                    case ItemKind.PushTransform:
                        target.PushTransform(it.Id, it.Matrix);
                        break;
                    case ItemKind.PushTransform3D:
                        target.PushTransform3D(it.Id, it.Matrix4);
                        break;
                    case ItemKind.PopTransform:
                        target.PopTransform();
                        break;
                    case ItemKind.PushGroup:
                        target.PushGroup(it.Id, it.Opacity, it.Blend);
                        break;
                    case ItemKind.PopGroup:
                        target.PopGroup();
                        break;
                    case ItemKind.PushFilter:
                        target.PushFilter(it.Id, Ops(it));
                        break;
                    case ItemKind.PopFilter:
                        target.PopFilter();
                        break;
                    case ItemKind.PushMask:
                        target.PushMask(it.Id);
                        break;
                    case ItemKind.PopMask:
                        target.PopMask();
                        break;
                    case ItemKind.PushMaskLayer:
                        target.PushMaskLayer(it.Id, it.MaskMode, it.MaskComposite);
                        break;
                    case ItemKind.PopMaskLayer:
                        target.PopMaskLayer();
                        break;
                    case ItemKind.PushHostScope:
                        target.PushHostScope(it.Id, it.HostScope);
                        break;
                    case ItemKind.PopHostScope:
                        target.PopHostScope();
                        break;
                    case ItemKind.BeginTag:
                        target.BeginTag(it.Id, it.Role, it.AltText, it.LinkTarget, it.Language, it.ColumnSpan, it.RowSpan);
                        break;
                    case ItemKind.EndTag:
                        target.EndTag();
                        break;
                }
            }
        }

        private static FilterOp[] Ops(DisplayItem it)
        {
            var ops = new FilterOp[it.FilterOpCount];
            for (int i = 0; i < ops.Length; i++)
            {
                ops[i] = it.GetFilterOp(i);
            }

            return ops;
        }

        private static Paint PaintOf(DisplayList target, DisplayItem it)
        {
            Paint p = it.Paint;
            if (p.Kind == PaintKind.Solid)
            {
                return p;
            }

            GradientView g = it.Gradient;
            var stops = new GradientStop[g.StopCount];
            for (int i = 0; i < stops.Length; i++)
            {
                stops[i] = g.GetStop(i);
            }

            return p.Kind switch
            {
                PaintKind.Linear => target.LinearGradient(g.X0, g.Y0, g.X1, g.Y1, stops, g.Repeating),
                PaintKind.Radial => target.RadialGradient(g.X0, g.Y0, g.X1, g.Y1, stops, g.Repeating),
                _ => target.ConicGradient(g.X0, g.Y0, g.FromAngle, stops, g.Repeating),
            };
        }
    }
}
