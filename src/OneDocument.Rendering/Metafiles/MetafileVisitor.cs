using System.Numerics;
using OneImage.Drawing;
using OneImage.Metafile;
using OneRender.Image;
using OneRender.Painting;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Layout;
using FillRule = OneRender.Painting.FillRule;
using ImageLoader = OneImage.Image;
using Rgba32 = OneImage.Rgba32;
using ImageSampling = OneRender.Painting.ImageSampling;
using Path = OneImage.Drawing.Path;

namespace RecursiveAI.OneDocument.Rendering.Metafiles;

/// <summary>
/// Turns the command stream of one metafile into display-list items. The stream is a canvas: it has a transform and a clip, both set
/// by commands. Every shape is mapped to page pixels here, so the display list only ever holds clips (the base clip of the picture's
/// box, then the metafile's own, re-pushed whenever it changes) and short transforms around text and bitmaps.
/// </summary>
internal sealed class MetafileVisitor : DrawingCommandVisitor
{
    // GDI hatch brushes are 8x8 pixel cells ([MS-EMF] §2.1.24 HatchStyle); OneImage measured the lines at row 3 / column 4 / x+y=7.
    private const float HatchCell = 8f;
    private const int MaxHatchLines = 2000;
    private const int MaxClipEntries = 128;
    private const uint SrcCopy = 0x00CC0020, NotSrcCopy = 0x00330008, PatCopy = 0x00F00021, Blackness = 0x00000042, Whiteness = 0x00FF0062;

    private readonly MetafilePaintContext _ctx;
    private readonly MetafileInfo _info;
    private readonly Matrix3x2 _toPage;
    private readonly RectF _dest;
    private readonly PathBuffer _pb = new();
    private readonly List<(Matrix3x2 M, List<ClipEntry> Clip)> _stack = [];
    private readonly HashSet<string> _said = [];
    private Matrix3x2 _m = Matrix3x2.Identity;
    private List<ClipEntry> _clip = [];
    private int _emitted;
    private bool _dirty;
    private int _record = -1;
    private IFontProvider? _fonts;

    internal MetafileVisitor(MetafilePaintContext ctx, MetafileInfo info, Matrix3x2 toPage, RectF dest)
    {
        _ctx = ctx;
        _info = info;
        _toPage = toPage;
        _dest = dest;
    }

    private sealed record ClipEntry(PathBuffer Path, FillRule Rule);

    private DisplayList L => _ctx.List;

    private ItemId Id() => _ctx.NextId();

    internal void Begin()
    {
        if (_ctx.Tagged)
        {
            L.BeginTag(Id(), TagRole.Figure, _ctx.AltText);
        }

        L.PushClipRect(Id(), _dest);
    }

    // Clips and transforms the painter has open above the destination clip, innermost last (true is a clip). A damaged file can make a
    // drawing call throw part-way through; Abort closes whatever is still open so the display list stays balanced.
    private readonly Stack<bool> _open = new();

    private void PushClipPath(PathBuffer path, FillRule rule)
    {
        L.PushClipPath(Id(), path, rule);
        _open.Push(true);
    }

    private void PushTransform(Matrix3x2 m)
    {
        L.PushTransform(Id(), m);
        _open.Push(false);
    }

    private void PopClip()
    {
        L.PopClip();
        _open.Pop();
    }

    private void PopTransform()
    {
        L.PopTransform();
        _open.Pop();
    }

    /// <summary>Closes every clip and transform still open after a drawing call failed.</summary>
    internal void Abort()
    {
        while (_open.Count > 0)
        {
            if (_open.Pop())
            {
                L.PopClip();
            }
            else
            {
                L.PopTransform();
            }
        }

        _emitted = 0;
    }

    internal void End()
    {
        PopEmitted();
        L.PopClip(); // the destination clip pushed by Begin
        if (_ctx.Tagged)
        {
            L.EndTag();
        }
    }

    // ---- fallback decision -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Finds commands the display list cannot express: it has no raster operations (XOR, AND, invert), no triangle gradients and no
    /// union or exclusive-or of clips. Such a picture is drawn as a bitmap whole, so it never looks half right.
    /// </summary>
    internal static bool NeedsRaster(MetafileDocument doc, out string reason)
    {
        bool drawn = false;
        foreach (DrawingCommand c in doc.Commands)
        {
            if (c is FillPathCommand or StrokePathCommand or BlitCommand or DrawTextCommand or DrawStringCommand or DrawGlyphsCommand or DrawImageCommand)
            {
                drawn = true;
            }

            switch (c)
            {
                case FillPathCommand fb when BrushNeedsRaster(fb.Brush, out reason):
                    return true;
                case ClearCommand { Color.A: not 255 } when drawn || false:
                    reason = "a translucent clear after drawing";
                    return true;
                case DrawStringCommand { Format.Vertical: true }:
                case DrawGlyphsCommand { Vertical: true }:
                    reason = "vertical text";
                    return true;
                case FillPathCommand f when !MixOk(f.Mix):
                    reason = "the raster operation " + f.Mix;
                    return true;
                case StrokePathCommand s when !MixOk(s.Mix):
                    reason = "the raster operation " + s.Mix;
                    return true;
                case StrokePathCommand { Pen.Brush: not null and not SolidBrushSpec }:
                    reason = "pens made from hatch or pattern brushes";
                    return true;
                case ClipCommand { Mode: ClipMode.Union or ClipMode.Xor }:
                    reason = "union or exclusive-or clip regions";
                    return true;
                case GradientFillCommand { Mode: GradientMode.Triangle }:
                    reason = "triangle gradients";
                    return true;
                case BlitCommand b when !BlitOk(b):
                    reason = "the raster operation 0x" + b.Rop.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
                    return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    private static bool BrushNeedsRaster(BrushSpec b, out string reason)
    {
        switch (b)
        {
            case StyleHatchBrushSpec { Style: > 5 }:
                reason = "GDI+ hatch styles beyond the first six";
                return true;
            case PathGradientBrushSpec pg when pg.BoundaryColors.Distinct().Count() > 1:
                reason = "path gradients with several boundary colours";
                return true;
            default:
                reason = string.Empty;
                return false;
        }
    }

    private static bool MixOk(MixMode m) => m is MixMode.CopyPen or MixMode.Nop or MixMode.Black or MixMode.White;

    private static bool BlitOk(BlitCommand b)
    {
        if (b.Kind != BlitKind.RasterOperation)
        {
            return true;
        }

        if (b.Bitmap is null)
        {
            return b.Rop is PatCopy or Blackness or Whiteness or 0x00AA0029 /* DSTCOPY (no-op) */;
        }

        return b.Mask is not null || b.Rop is SrcCopy or NotSrcCopy;
    }

    internal bool PaintRaster(ReadOnlyMemory<byte> data, RectD pr)
    {
        try
        {
            float sx = MathF.Sqrt((_toPage.M11 * _toPage.M11) + (_toPage.M12 * _toPage.M12));
            float sy = MathF.Sqrt((_toPage.M21 * _toPage.M21) + (_toPage.M22 * _toPage.M22));
            // Twice the final size keeps edges clean when the page is rasterised at 144 dpi; capped by the context.
            int w = (int)Math.Clamp(Math.Ceiling(pr.Width * sx * 2), 16, _ctx.MaxRasterSide);
            int h = (int)Math.Clamp(Math.Ceiling(pr.Height * sy * 2), 16, _ctx.MaxRasterSide);
            var config = new OneImage.ImageConfiguration().Add(new MetafileFormat(_ctx.Limits)).Add(new WmfFormat(_ctx.Limits));
            ImageLoader img = ImageLoader.Load(data, new OneImage.LoadOptions { Configuration = config })
                .Resize(width: w, height: h, fit: OneImage.Fit.Stretch);
            Rgba32[] px = img.ToArray<Rgba32>();
            byte[] bytes = new byte[px.Length * 4];
            for (int i = 0; i < px.Length; i++)
            {
                bytes[(i * 4) + 0] = px[i].R;
                bytes[(i * 4) + 1] = px[i].G;
                bytes[(i * 4) + 2] = px[i].B;
                bytes[(i * 4) + 3] = px[i].A;
            }

            ImageHandle handle = _ctx.Images.Add(new RgbaImage(img.Width, img.Height, bytes));
            Matrix3x2 m = Matrix3x2.CreateScale((float)(pr.Width / img.Width), (float)(pr.Height / img.Height))
                * Matrix3x2.CreateTranslation((float)pr.Left, (float)pr.Top) * _toPage;
            var rect = new RectF(0, 0, img.Width, img.Height);
            PushTransform(m);
            L.DrawImage(Id(), handle, rect, rect, ImageSampling.Smooth);
            PopTransform();
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or OneImage.ImageException or InvalidOperationException)
        {
            Note("render.metafile-diagnostic", "raster", "The picture could not be drawn as a bitmap: " + ex.Message, DefectSeverity.Dropped);
            return false;
        }
    }

    // ---- state -------------------------------------------------------------------------------------------------------------

    public override void VisitSaveState(SaveStateCommand command)
    {
        _record = command.RecordIndex;
        _stack.Add((_m, [.. _clip]));
    }

    public override void VisitRestoreState(RestoreStateCommand command)
    {
        if (_stack.Count == 0)
        {
            return;
        }

        (_m, _clip) = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        _dirty = true;
    }

    public override void VisitSetTransform(SetTransformCommand command) => _m = command.Transform;

    public override void VisitClip(ClipCommand command)
    {
        _record = command.RecordIndex;
        if (command.Mode == ClipMode.Reset || command.Path is null)
        {
            _clip = [];
            _dirty = true;
            return;
        }

        var shape = new PathBuffer();
        AddPath(shape, command.Path, _toPage);
        switch (command.Mode)
        {
            case ClipMode.Replace:
                _clip = [new ClipEntry(shape, command.Rule == OneImage.Metafile.FillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero)];
                break;
            case ClipMode.Intersect:
                AddClip(new ClipEntry(shape, Rule(command.Rule)));
                break;
            case ClipMode.Exclude:
            {
                // Everything outside the shape: a huge box with the shape cut out by the even-odd rule.
                var outside = new PathBuffer();
                outside.MoveTo(-1e6f, -1e6f);
                outside.LineTo(1e6f, -1e6f);
                outside.LineTo(1e6f, 1e6f);
                outside.LineTo(-1e6f, 1e6f);
                outside.Close();
                AddPath(outside, command.Path, _toPage);
                AddClip(new ClipEntry(outside, FillRule.EvenOdd));
                break;
            }

            default:
                // Union and exclusive-or normally send the whole picture to the bitmap path; here (fallback disabled) the shape replaces the clip.
                Note("render.metafile-approximated", "clipop", "A union or exclusive-or clip region was drawn as a replacement.", DefectSeverity.Repaired);
                _clip = [new ClipEntry(shape, Rule(command.Rule))];
                break;
        }

        _dirty = true;
    }

    public override void VisitOffsetClip(OffsetClipCommand command)
    {
        // The offset is in device units of the picture; only the scale of the page mapping applies to a movement.
        Vector2 d = Vector2.TransformNormal(command.Delta, _toPage);
        var moved = new List<ClipEntry>(_clip.Count);
        foreach (ClipEntry e in _clip)
        {
            var p = new PathBuffer();
            Offset(p, e.Path, d);
            moved.Add(new ClipEntry(p, e.Rule));
        }

        _clip = moved;
        _dirty = true;
    }

    private static FillRule Rule(OneImage.Metafile.FillRule r) => r == OneImage.Metafile.FillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero;

    private void AddClip(ClipEntry e)
    {
        if (_clip.Count < MaxClipEntries)
        {
            _clip.Add(e);
        }
    }

    private void PopEmitted()
    {
        for (int i = 0; i < _emitted; i++)
        {
            PopClip();
        }

        _emitted = 0;
    }

    private void Sync()
    {
        if (!_dirty)
        {
            return;
        }

        PopEmitted();
        foreach (ClipEntry e in _clip)
        {
            PushClipPath(e.Path, e.Rule);
        }

        _emitted = _clip.Count;
        _dirty = false;
    }

    // ---- shapes ------------------------------------------------------------------------------------------------------------

    private Matrix3x2 Final => _m * _toPage;

    public override void VisitFillPath(FillPathCommand command)
    {
        _record = command.RecordIndex;
        if (command.Path.IsEmpty || command.Mix == MixMode.Nop)
        {
            return;
        }

        Sync();
        BrushSpec brush = command.Mix switch
        {
            MixMode.Black => new SolidBrushSpec(new Rgba32(0, 0, 0, 255)),
            MixMode.White => new SolidBrushSpec(new Rgba32(255, 255, 255, 255)),
            _ => command.Brush,
        };
        _pb.Clear();
        AddPath(_pb, command.Path, Final);
        FillBuffer(_pb, Rule(command.Rule), brush);
    }

    /// <summary>Fills the path in <paramref name="pb"/> (page pixels) with a brush.</summary>
    private void FillBuffer(PathBuffer pb, FillRule rule, BrushSpec brush)
    {
        switch (brush)
        {
            case SolidBrushSpec s:
                L.FillPath(Id(), pb, Color(s.Color), rule);
                break;
            case HatchBrushSpec h:
                FillHatch(pb, rule, h);
                break;
            case PatternBrushSpec p:
                FillPattern(pb, rule, p);
                break;
            case LinearGradientBrushSpec g:
                FillLinear(pb, rule, g);
                break;
            case PathGradientBrushSpec pg:
                FillPathGradient(pb, rule, pg);
                break;
            case TextureBrushSpec t:
                FillTexture(pb, rule, t);
                break;
            case StyleHatchBrushSpec sh:
                FillStyleHatch(pb, rule, sh);
                break;
        }
    }

    // [MS-EMFPLUS] §2.2.2.24 EmfPlusLinearGradientBrushData: the gradient runs from Start to End in the brush's own space.
    private void FillLinear(PathBuffer pb, FillRule rule, LinearGradientBrushSpec g)
    {
        Matrix3x2 m = g.Transform * Final;
        Vector2 p0 = Page(g.Start, m), p1 = Page(g.End, m);
        if (g.Stops.Count == 0)
        {
            return;
        }

        if (g.Stops.Count == 1 || Vector2.DistanceSquared(p0, p1) < 1e-6f)
        {
            L.FillPath(Id(), pb, Color(g.Stops[0].Color), rule);
            return;
        }

        var stops = new GradientStop[g.Stops.Count];
        for (int i = 0; i < stops.Length; i++)
        {
            stops[i] = new GradientStop(g.Stops[i].Position, Color(g.Stops[i].Color));
        }

        // Mirrored tiling has no display-list form; it repeats without the mirror.
        L.FillPath(Id(), pb, L.LinearGradient(p0.X, p0.Y, p1.X, p1.Y, stops, g.Wrap != EdgeWrap.Clamp), rule);
    }

    // [MS-EMFPLUS] §2.2.2.28 EmfPlusPathGradientBrushData: centre colour to boundary colours; the display list has a radial gradient, which
    // matches when the boundary is one colour (several colours are drawn as a bitmap).
    private void FillPathGradient(PathBuffer pb, FillRule rule, PathGradientBrushSpec g)
    {
        Matrix3x2 m = g.Transform * Final;
        Vector2 c = Page(g.Center, m);
        float radius = 0;
        foreach (Vector2 b in g.Boundary)
        {
            radius = MathF.Max(radius, Vector2.Distance(c, Page(b, m)));
        }

        Rgba32 edge = g.BoundaryColors.Count > 0 ? g.BoundaryColors[0] : g.CenterColor;
        if (g.BoundaryColors.Distinct().Count() > 1)
        {
            Note("render.metafile-approximated", "pathgradient", "A path gradient with several boundary colours was drawn with the first one.", DefectSeverity.Repaired);
        }

        if (!(radius > 0))
        {
            L.FillPath(Id(), pb, Color(g.CenterColor), rule);
            return;
        }

        GradientStop[] stops = [new(0, Color(g.CenterColor)), new(1, Color(edge))];
        L.FillPath(Id(), pb, L.RadialGradient(c.X, c.Y, radius, radius, stops), rule);
    }

    private void FillTexture(PathBuffer pb, FillRule rule, TextureBrushSpec t)
    {
        RasterImage img = t.Image;
        if (img.Width <= 0 || img.Height <= 0)
        {
            return;
        }

        Matrix3x2 m = t.Transform * Final;
        if (!Matrix3x2.Invert(m, out Matrix3x2 inv))
        {
            return;
        }

        Bounds(pb, out float x0, out float y0, out float x1, out float y1);
        Vector2[] corners = [Page(new Vector2(x0, y0), inv), Page(new Vector2(x1, y0), inv), Page(new Vector2(x1, y1), inv), Page(new Vector2(x0, y1), inv)];
        float bx0 = corners.Min(p => p.X), by0 = corners.Min(p => p.Y), bx1 = corners.Max(p => p.X), by1 = corners.Max(p => p.Y);
        ImageHandle handle = _ctx.Images.Add(new RgbaImage(img.Width, img.Height, img.Pixels.ToArray()));
        var tile = new RectF(0, 0, img.Width, img.Height);
        PushClipPath(Clone(pb), rule);
        PushTransform(m);
        if (t.Wrap == EdgeWrap.Clamp)
        {
            L.DrawImage(Id(), handle, tile, tile, ImageSampling.Smooth);
        }
        else
        {
            L.DrawImageTiled(Id(), handle, tile, tile, new RectF(bx0, by0, bx1 - bx0, by1 - by0), 0, 0, ImageSampling.Smooth);
        }

        PopTransform();
        PopClip();
    }

    // GDI+ hatch styles 0 to 5 are the six GDI patterns; they are anchored at the corner of the output.
    private void FillStyleHatch(PathBuffer pb, FillRule rule, StyleHatchBrushSpec h)
    {
        HatchKind kind = h.Style switch { 0 => HatchKind.Horizontal, 1 => HatchKind.Vertical, 2 => HatchKind.ForwardDiagonal, 3 => HatchKind.BackwardDiagonal, 4 => HatchKind.Cross, _ => HatchKind.DiagonalCross };
        Matrix3x2.Invert(_toPage, out Matrix3x2 back);
        Vector2 origin = Page(new Vector2(_dest.X, _dest.Y), back);
        FillHatch(pb, rule, new HatchBrushSpec(kind, h.Foreground, h.Background.A == 0 ? null : h.Background, origin));
    }

    private static PaintColor Color(Rgba32 c) => PaintColor.FromBytes(c.R, c.G, c.B, c.A);

    private void FillHatch(PathBuffer pb, FillRule rule, HatchBrushSpec h)
    {
        if (pb.VerbCount == 0)
        {
            return;
        }

        Bounds(pb, out float x0, out float y0, out float x1, out float y1);
        Vector2 o = Page(h.Origin, _toPage);
        // The path is cloned because the shared buffer is reused while the clip is open.
        var clipPath = Clone(pb);
        PushClipPath(clipPath, rule);
        if (h.Background is { } bg)
        {
            L.FillRect(Id(), new RectF(x0, y0, x1 - x0, y1 - y0), Color(bg));
        }

        PaintColor fg = Color(h.Foreground);
        bool horizontal = h.Kind is HatchKind.Horizontal or HatchKind.Cross;
        bool vertical = h.Kind is HatchKind.Vertical or HatchKind.Cross;
        bool forward = h.Kind is HatchKind.ForwardDiagonal or HatchKind.DiagonalCross;
        bool backward = h.Kind is HatchKind.BackwardDiagonal or HatchKind.DiagonalCross;
        int budget = MaxHatchLines;
        if (horizontal)
        {
            for (float y = o.Y + 3 + (HatchCell * MathF.Floor((y0 - o.Y - 3) / HatchCell)); y < y1 && budget-- > 0; y += HatchCell)
            {
                // Lines sit on whole pixels, as GDI draws them, so they stay crisp.
                L.FillRect(Id(), new RectF(x0, MathF.Round(y), x1 - x0, 1), fg);
            }
        }

        if (vertical)
        {
            for (float x = o.X + 4 + (HatchCell * MathF.Floor((x0 - o.X - 4) / HatchCell)); x < x1 && budget-- > 0; x += HatchCell)
            {
                L.FillRect(Id(), new RectF(MathF.Round(x), y0, 1, y1 - y0), fg);
            }
        }

        if (forward || backward)
        {
            if (forward)
            {
                // Lines x - y = c (relative to the origin), c a multiple of the cell.
                float cmin = (x0 - o.X) - (y1 - o.Y), cmax = (x1 - o.X) - (y0 - o.Y);
                for (float c = HatchCell * MathF.Floor(cmin / HatchCell); c <= cmax && budget-- > 0; c += HatchCell)
                {
                    Stripe(o.X - o.Y + c, y0, y1, 1f, fg);
                }
            }

            if (backward)
            {
                // Lines x + y = c + 7.
                float cmin = (x0 - o.X) + (y0 - o.Y) - 7, cmax = (x1 - o.X) + (y1 - o.Y) - 7;
                for (float c = HatchCell * MathF.Floor(cmin / HatchCell); c <= cmax && budget-- > 0; c += HatchCell)
                {
                    Stripe(o.X + o.Y + 7 + c, y0, y1, -1f, fg);
                }
            }

        }

        PopClip();
    }

    // A one-pixel-wide diagonal band between y0 and y1: x = k + dir * y, where k is passed with the origin folded in.
    private void Stripe(float kx, float y0, float y1, float dir, PaintColor color)
    {
        var p = new PathBuffer();
        float xa = kx + (dir * y0), xb = kx + (dir * y1);
        p.MoveTo(xa, y0);
        p.LineTo(xa + 1, y0);
        p.LineTo(xb + 1, y1);
        p.LineTo(xb, y1);
        p.Close();
        L.FillPath(Id(), p, color);
    }

    private void FillPattern(PathBuffer pb, FillRule rule, PatternBrushSpec p)
    {
        DeviceIndependentBitmap bmp = p.Bitmap;
        if (bmp.Width <= 0 || bmp.Height <= 0)
        {
            return;
        }

        byte[] rgba = bmp.ToRgba(false);
        if (p.UseTextColors && bmp.BitCount == 1)
        {
            // A pattern made from a device bitmap: set bits take the text color and clear bits the background color.
            for (int i = 0; i < bmp.Width * bmp.Height; i++)
            {
                bool set = rgba[i * 4] >= 128;
                Rgba32 c = set ? p.Foreground : p.Background;
                rgba[(i * 4) + 0] = c.R;
                rgba[(i * 4) + 1] = c.G;
                rgba[(i * 4) + 2] = c.B;
                rgba[(i * 4) + 3] = c.A;
            }
        }

        ImageHandle handle = _ctx.Images.Add(new RgbaImage(bmp.Width, bmp.Height, rgba));
        Bounds(pb, out float x0, out float y0, out float x1, out float y1);
        Vector2 o = Page(p.Origin, _toPage);
        PushClipPath(Clone(pb), rule);
        var src = new RectF(0, 0, bmp.Width, bmp.Height);
        // Brush bitmaps repeat unscaled in output pixels, anchored at the brush origin.
        L.DrawImageTiled(Id(), handle, src, new RectF(o.X, o.Y, bmp.Width, bmp.Height), new RectF(x0, y0, x1 - x0, y1 - y0), 0, 0, ImageSampling.Pixelated);
        PopClip();
    }

    public override void VisitStrokePath(StrokePathCommand command)
    {
        _record = command.RecordIndex;
        if (command.Path.IsEmpty || command.Mix == MixMode.Nop)
        {
            return;
        }

        Sync();
        PenSpec pen = command.Pen;
        Rgba32 penColor = pen.Brush is SolidBrushSpec sb ? sb.Color : pen.Color;
        Rgba32 color = command.Mix switch
        {
            MixMode.Black => new Rgba32(0, 0, 0, 255),
            MixMode.White => new Rgba32(255, 255, 255, 255),
            _ => penColor,
        };
        Matrix3x2 f = Final;
        float scale = MathF.Sqrt(MathF.Abs((f.M11 * f.M22) - (f.M12 * f.M21)));
        // A cosmetic pen is one pixel wide whatever the scale; its dash lengths are pixels too.
        float width = pen.IsCosmetic ? 1f : MathF.Max((float)pen.Width * scale, 0.25f);
        float[] dash = [];
        if (pen.Dashes is { Count: > 0 } d)
        {
            var list = new List<float>(d.Count * 2);
            for (int pass = 0; pass < (d.Count % 2 == 0 ? 1 : 2); pass++)
            {
                foreach (double v in d)
                {
                    list.Add(MathF.Max((float)v * (pen.IsCosmetic ? 1f : scale), 0.05f));
                }
            }

            dash = [.. list];
        }

        try
        {
            Path path = ToPath(command.Path, f);
            if (dash.Length > 0 && pen.GapColor is { } gap)
            {
                EmitOutline(path.GenerateOutline(width, Join(pen.Join), Cap(pen.Cap), (float)Math.Max(pen.MiterLimit, 1)), Color(gap));
            }

            // GDI+ pens carry a dash cap (the shape of each dash end) and an offset into the pattern, counted in pen widths.
            StrokeCap cap = dash.Length > 0 && pen.Plus is { } plus ? Cap(plus.DashCap) : Cap(pen.Cap);
            float offset = dash.Length > 0 && pen.Plus is { } po ? (float)(po.DashOffset * width) : 0f;
            Path outline = path.GenerateOutline(width, Join(pen.Join), cap, (float)Math.Max(pen.MiterLimit, 1), dash, offset);
            EmitOutline(outline, Color(color));
        }
        catch (Exception ex) when (ex is ArgumentException or OneImage.ImageException or InvalidOperationException)
        {
            Note("render.metafile-diagnostic", "stroke", "A stroke could not be drawn: " + ex.Message, DefectSeverity.Dropped);
        }
    }

    private void EmitOutline(Path outline, PaintColor color)
    {
        _pb.Clear();
        foreach (Contour c in outline.Flatten(0.05f))
        {
            if (c.Points.Length < 2)
            {
                continue;
            }

            _pb.MoveTo(c.Points[0].X, c.Points[0].Y);
            for (int i = 1; i < c.Points.Length; i++)
            {
                _pb.LineTo(c.Points[i].X, c.Points[i].Y);
            }

            _pb.Close();
        }

        if (_pb.VerbCount > 0)
        {
            L.FillPath(Id(), _pb, color, FillRule.NonZero);
        }
    }

    private static StrokeJoin Join(LineJoin j) => j switch { LineJoin.Bevel => StrokeJoin.Bevel, LineJoin.Miter => StrokeJoin.Miter, _ => StrokeJoin.Round };

    private static StrokeCap Cap(LineCap c) => c switch { LineCap.Flat => StrokeCap.Butt, LineCap.Square => StrokeCap.Square, _ => StrokeCap.Round };

    private static Path ToPath(MetafilePath mp, in Matrix3x2 m)
    {
        var b = new PathBuilder();
        int p = 0;
        bool open = false;
        foreach (OneImage.Metafile.PathVerb v in mp.Verbs)
        {
            switch (v)
            {
                case OneImage.Metafile.PathVerb.MoveTo:
                    b.MoveTo(Page(mp.Points[p++], m));
                    open = true;
                    break;
                case OneImage.Metafile.PathVerb.LineTo:
                {
                    Vector2 pt = Page(mp.Points[p++], m);
                    if (open)
                    {
                        b.LineTo(pt);
                    }
                    else
                    {
                        b.MoveTo(pt);
                        open = true;
                    }

                    break;
                }

                case OneImage.Metafile.PathVerb.CubicTo:
                {
                    Vector2 a = Page(mp.Points[p], m), c = Page(mp.Points[p + 1], m), e = Page(mp.Points[p + 2], m);
                    p += 3;
                    if (!open)
                    {
                        b.MoveTo(a);
                        open = true;
                    }

                    b.CubicBezierTo(a.X, a.Y, c.X, c.Y, e.X, e.Y);
                    break;
                }

                default:
                    if (open)
                    {
                        b.Close();
                    }

                    break;
            }
        }

        return b.Build();
    }

    // ---- path buffers ------------------------------------------------------------------------------------------------------

    // The display list refuses coordinates that are not finite or lie beyond a billion pixels. A damaged file can ask for a transform that
    // throws points that far, so every transformed point is brought into range (what lies there is off the page either way).
    private static Vector2 Page(Vector2 v, in Matrix3x2 m)
    {
        Vector2 r = Vector2.Transform(v, m);
        return new Vector2(Bound(r.X), Bound(r.Y));
    }

    private static float Bound(float v) => float.IsFinite(v) ? Math.Clamp(v, -1e8f, 1e8f) : 0f;

    private static void AddPath(PathBuffer pb, MetafilePath mp, in Matrix3x2 m)
    {
        int p = 0;
        bool open = false;
        foreach (OneImage.Metafile.PathVerb v in mp.Verbs)
        {
            switch (v)
            {
                case OneImage.Metafile.PathVerb.MoveTo:
                {
                    Vector2 a = Page(mp.Points[p++], m);
                    pb.MoveTo(a.X, a.Y);
                    open = true;
                    break;
                }

                case OneImage.Metafile.PathVerb.LineTo:
                {
                    Vector2 a = Page(mp.Points[p++], m);
                    if (open)
                    {
                        pb.LineTo(a.X, a.Y);
                    }
                    else
                    {
                        pb.MoveTo(a.X, a.Y);
                        open = true;
                    }

                    break;
                }

                case OneImage.Metafile.PathVerb.CubicTo:
                {
                    Vector2 a = Page(mp.Points[p], m), b = Page(mp.Points[p + 1], m), c = Page(mp.Points[p + 2], m);
                    p += 3;
                    if (!open)
                    {
                        pb.MoveTo(a.X, a.Y);
                        open = true;
                    }

                    pb.CubicTo(a.X, a.Y, b.X, b.Y, c.X, c.Y);
                    break;
                }

                default:
                    if (open)
                    {
                        pb.Close();
                    }

                    break;
            }
        }
    }

    private static void Offset(PathBuffer dst, PathBuffer src, Vector2 d)
    {
        ReadOnlySpan<float> c = src.Coords;
        int k = 0;
        foreach (byte v in src.Verbs)
        {
            switch ((OneRender.Painting.PathVerb)v)
            {
                case OneRender.Painting.PathVerb.Move:
                    dst.MoveTo(c[k] + d.X, c[k + 1] + d.Y);
                    k += 2;
                    break;
                case OneRender.Painting.PathVerb.Line:
                    dst.LineTo(c[k] + d.X, c[k + 1] + d.Y);
                    k += 2;
                    break;
                case OneRender.Painting.PathVerb.Quad:
                    dst.QuadTo(c[k] + d.X, c[k + 1] + d.Y, c[k + 2] + d.X, c[k + 3] + d.Y);
                    k += 4;
                    break;
                case OneRender.Painting.PathVerb.Cubic:
                    dst.CubicTo(c[k] + d.X, c[k + 1] + d.Y, c[k + 2] + d.X, c[k + 3] + d.Y, c[k + 4] + d.X, c[k + 5] + d.Y);
                    k += 6;
                    break;
                default:
                    dst.Close();
                    break;
            }
        }
    }

    private static PathBuffer Clone(PathBuffer src)
    {
        var p = new PathBuffer();
        Offset(p, src, Vector2.Zero);
        return p;
    }

    private static void Bounds(PathBuffer pb, out float x0, out float y0, out float x1, out float y1)
    {
        x0 = y0 = float.MaxValue;
        x1 = y1 = float.MinValue;
        ReadOnlySpan<float> c = pb.Coords;
        for (int i = 0; i + 1 < c.Length; i += 2)
        {
            x0 = MathF.Min(x0, c[i]);
            x1 = MathF.Max(x1, c[i]);
            y0 = MathF.Min(y0, c[i + 1]);
            y1 = MathF.Max(y1, c[i + 1]);
        }

        if (x0 > x1)
        {
            x0 = y0 = x1 = y1 = 0;
        }
    }

    // ---- gradients ---------------------------------------------------------------------------------------------------------

    public override void VisitGradientFill(GradientFillCommand command)
    {
        _record = command.RecordIndex;
        Sync();
        Matrix3x2 f = Final;
        IReadOnlyList<GradientVertex> v = command.Vertices;
        IReadOnlyList<int> idx = command.Indices;
        if (command.Mode == GradientMode.Triangle)
        {
            for (int i = 0; i + 2 < idx.Count; i += 3)
            {
                if (!(Ok(idx[i], v.Count) && Ok(idx[i + 1], v.Count) && Ok(idx[i + 2], v.Count)))
                {
                    continue;
                }

                GradientVertex a = v[idx[i]], b = v[idx[i + 1]], c = v[idx[i + 2]];
                _pb.Clear();
                foreach (GradientVertex g in new[] { a, b, c })
                {
                    Vector2 p = Page(g.Position, f);
                    if (_pb.VerbCount == 0)
                    {
                        _pb.MoveTo(p.X, p.Y);
                    }
                    else
                    {
                        _pb.LineTo(p.X, p.Y);
                    }
                }

                _pb.Close();
                var avg = new Rgba32((byte)((a.Color.R + b.Color.R + c.Color.R) / 3), (byte)((a.Color.G + b.Color.G + c.Color.G) / 3), (byte)((a.Color.B + b.Color.B + c.Color.B) / 3), 255);
                L.FillPath(Id(), _pb, Color(avg), FillRule.NonZero);
            }

            return;
        }

        bool horizontal = command.Mode == GradientMode.Horizontal;
        for (int i = 0; i + 1 < idx.Count; i += 2)
        {
            if (!(Ok(idx[i], v.Count) && Ok(idx[i + 1], v.Count)))
            {
                continue;
            }

            GradientVertex a = v[idx[i]], b = v[idx[i + 1]];
            float lx = MathF.Min(a.Position.X, b.Position.X), rx = MathF.Max(a.Position.X, b.Position.X);
            float ty = MathF.Min(a.Position.Y, b.Position.Y), by = MathF.Max(a.Position.Y, b.Position.Y);
            // GradientFill rectangles run from the upper-left vertex's color to the lower-right vertex's color.
            bool aFirst = horizontal ? a.Position.X <= b.Position.X : a.Position.Y <= b.Position.Y;
            GradientVertex first = aFirst ? a : b;
            GradientVertex second = aFirst ? b : a;
            Vector2 p0 = horizontal ? new Vector2(lx, (ty + by) / 2) : new Vector2((lx + rx) / 2, ty);
            Vector2 p1 = horizontal ? new Vector2(rx, (ty + by) / 2) : new Vector2((lx + rx) / 2, by);
            p0 = Page(p0, f);
            p1 = Page(p1, f);
            _pb.Clear();
            Vector2[] corners = [new(lx, ty), new(rx, ty), new(rx, by), new(lx, by)];
            for (int k = 0; k < 4; k++)
            {
                Vector2 p = Page(corners[k], f);
                if (k == 0)
                {
                    _pb.MoveTo(p.X, p.Y);
                }
                else
                {
                    _pb.LineTo(p.X, p.Y);
                }
            }

            _pb.Close();
            GradientStop[] stops = [new(0, Color(first.Color)), new(1, Color(second.Color))];
            Paint paint = L.LinearGradient(p0.X, p0.Y, p1.X, p1.Y, stops);
            L.FillPath(Id(), _pb, paint, FillRule.NonZero);
        }
    }

    private static bool Ok(int i, int n) => i >= 0 && i < n;

    // ---- bitmaps -----------------------------------------------------------------------------------------------------------

    public override void VisitBlit(BlitCommand command)
    {
        _record = command.RecordIndex;
        Matrix3x2 f = Final;
        if (command.Bitmap is null)
        {
            BlitPattern(command, f);
            return;
        }

        DeviceIndependentBitmap bmp = command.Bitmap;
        if (bmp.Width <= 0 || bmp.Height <= 0)
        {
            return;
        }

        RectD s = command.Source.Normalized;
        double sl = Math.Clamp(s.Left, 0, bmp.Width), st = Math.Clamp(s.Top, 0, bmp.Height);
        double sr = Math.Clamp(s.Right, 0, bmp.Width), sb = Math.Clamp(s.Bottom, 0, bmp.Height);
        double sw = sr - sl, sh = sb - st;
        if (sw <= 0 || sh <= 0)
        {
            return;
        }

        Sync();
        byte[] rgba = bmp.ToRgba(command.Kind == BlitKind.AlphaBlend && command.UsePerPixelAlpha);
        int n = bmp.Width * bmp.Height;
        if (bmp.BitCount == 1 && bmp.Palette.Count == 0)
        {
            // A device bitmap without a color table: set bits are the text color, clear bits the background color.
            for (int i = 0; i < n; i++)
            {
                Rgba32 c = rgba[i * 4] >= 128 ? command.TextColor : command.BackgroundColor;
                Put(rgba, i, c.R, c.G, c.B, c.A);
            }
        }

        if (command.Rop == NotSrcCopy && command.Kind == BlitKind.RasterOperation && command.Mask is null)
        {
            for (int i = 0; i < n; i++)
            {
                Put(rgba, i, (byte)(255 - rgba[i * 4]), (byte)(255 - rgba[(i * 4) + 1]), (byte)(255 - rgba[(i * 4) + 2]), rgba[(i * 4) + 3]);
            }
        }

        if (command.Kind == BlitKind.TransparentColor)
        {
            Rgba32 key = command.TransparentColor;
            for (int i = 0; i < n; i++)
            {
                if (rgba[i * 4] == key.R && rgba[(i * 4) + 1] == key.G && rgba[(i * 4) + 2] == key.B)
                {
                    rgba[(i * 4) + 3] = 0;
                }
            }
        }

        if (command.Mask is { } mask && mask.Width > 0 && mask.Height > 0)
        {
            byte[] m = mask.ToRgba(false);
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    int mx = (int)(x - sl + command.MaskOrigin.X), my = (int)(y - st + command.MaskOrigin.Y);
                    // [MS-EMF] §2.3.1.5 MaskBlt: a set mask bit takes the source, a clear bit keeps the destination.
                    bool keep = mx >= 0 && my >= 0 && mx < mask.Width && my < mask.Height && m[((my * mask.Width) + mx) * 4] >= 128;
                    if (!keep)
                    {
                        rgba[(((y * bmp.Width) + x) * 4) + 3] = 0;
                    }
                }
            }
        }

        // The bitmap's pixel space maps onto the destination parallelogram given by three corners.
        Vector2 tl = command.DestTopLeft, tr = command.DestTopRight, bl = command.DestBottomLeft;
        Vector2 vx = (tr - tl) / (float)(command.Source.Normalized.Width <= 0 ? sw : command.Source.Normalized.Width);
        Vector2 vy = (bl - tl) / (float)(command.Source.Normalized.Height <= 0 ? sh : command.Source.Normalized.Height);
        var a = new Matrix3x2(vx.X, vx.Y, vy.X, vy.Y, tl.X - (float)(s.Left * vx.X) - (float)(s.Top * vy.X), tl.Y - (float)(s.Left * vx.Y) - (float)(s.Top * vy.Y));
        ImageHandle handle = _ctx.Images.Add(new RgbaImage(bmp.Width, bmp.Height, rgba));
        var src = new RectF((float)sl, (float)st, (float)sw, (float)sh);
        bool group = command.Kind == BlitKind.AlphaBlend && command.ConstantAlpha < 255;
        if (group)
        {
            L.PushGroup(Id(), command.ConstantAlpha / 255f);
        }

        PushTransform(a * f);
        L.DrawImage(Id(), handle, src, src, command.Stretch == StretchMode.Halftone ? ImageSampling.Smooth : ImageSampling.Pixelated);
        PopTransform();
        if (group)
        {
            L.PopGroup();
        }
    }

    private static void Put(byte[] px, int i, byte r, byte g, byte b, byte a)
    {
        px[(i * 4) + 0] = r;
        px[(i * 4) + 1] = g;
        px[(i * 4) + 2] = b;
        px[(i * 4) + 3] = a;
    }

    private void BlitPattern(BlitCommand c, in Matrix3x2 f)
    {
        BrushSpec? brush = c.Rop switch
        {
            Blackness => new SolidBrushSpec(new Rgba32(0, 0, 0, 255)),
            Whiteness => new SolidBrushSpec(new Rgba32(255, 255, 255, 255)),
            PatCopy => c.Brush,
            _ => null,
        };
        if (brush is null)
        {
            return;
        }

        Sync();
        // PatBlt names the box by three corners; the fourth completes the parallelogram.
        Vector2 br = c.DestTopRight + (c.DestBottomLeft - c.DestTopLeft);
        _pb.Clear();
        Vector2[] pts = [c.DestTopLeft, c.DestTopRight, br, c.DestBottomLeft];
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = Page(pts[i], f);
            if (i == 0)
            {
                _pb.MoveTo(p.X, p.Y);
            }
            else
            {
                _pb.LineTo(p.X, p.Y);
            }
        }

        _pb.Close();
        FillBuffer(_pb, FillRule.NonZero, brush);
    }

    // ---- text --------------------------------------------------------------------------------------------------------------

    public override void VisitDrawText(DrawTextCommand command)
    {
        _record = command.RecordIndex;
        if (command.Text.Length == 0)
        {
            return;
        }

        Sync();
        Matrix3x2 f = Final;
        if (command.OpaqueRectangle is { } rect)
        {
            _pb.Clear();
            AddRect(_pb, rect, f);
            L.FillPath(Id(), _pb, Color(command.OpaqueColor), FillRule.NonZero);
        }

        FontSpec font = command.Font;
        FontMatch? match = ResolveFont(font);
        if (match is null)
        {
            return;
        }

        OneFont.FontFace face = match.Face;
        OneFont.FontMetrics fm = face.Metrics;
        double upem = fm.UnitsPerEm;
        double cell = fm.Ascender - fm.Descender;
        // [MS-EMF] §2.2.13 LogFont: Height > 0 is the cell height (ascent + descent); < 0 is the em height; 0 asks for a default.
        double emLogical = font.Height < 0 ? -font.Height : font.Height == 0 ? 16 : (cell <= 0 ? font.Height : font.Height * upem / cell);
        float xs = MathF.Sqrt((f.M11 * f.M11) + (f.M12 * f.M12));
        float ys = MathF.Sqrt((f.M21 * f.M21) + (f.M22 * f.M22));
        float emPx = (float)(emLogical * ys);
        if (!(emPx > 0.01f) || !float.IsFinite(emPx))
        {
            return;
        }

        // Glyphs, with fallback faces where the chosen face lacks a character.
        var segments = new List<(OneFont.FontFace Face, List<int> Gids, List<float> X, List<int> Clusters, int TextStart, int TextEnd)>();
        float penX = 0;
        string text = command.Text;
        int spacesLeft = command.BreakCount;
        int charIndex = 0;
        for (int i = 0; i < text.Length; i++, charIndex++)
        {
            int cp = text[i];
            int units = 1;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                units = 2;
            }

            OneFont.FontFace use = face;
            int gid = command.IsGlyphIndices ? cp : face.GetGlyphIndex(cp);
            if (gid == 0 && cp > 32 && !command.IsGlyphIndices && _fonts?.FindFallback(cp, font.Weight >= 600, font.Italic) is { } fb && fb.HasGlyph(cp))
            {
                use = fb;
                gid = fb.GetGlyphIndex(cp);
            }

            double advLogical = command.Advances is { } adv && charIndex < adv.Count ? adv[charIndex] : double.NaN;
            float adv1 = double.IsNaN(advLogical)
                ? use.GetAdvance(gid) * emPx / use.UnitsPerEm
                : (float)(advLogical * xs);
            adv1 += (float)(command.CharacterExtra * xs);
            if (cp == ' ' && spacesLeft > 0)
            {
                adv1 += (float)(command.BreakExtra * xs);
                spacesLeft--;
            }

            if (cp >= 32 || cp == '\t')
            {
                if (segments.Count == 0 || !ReferenceEquals(segments[^1].Face, use))
                {
                    segments.Add((use, [], [], [], i, i));
                }

                (OneFont.FontFace Face, List<int> Gids, List<float> X, List<int> Clusters, int TextStart, int TextEnd) seg = segments[^1];
                seg.Gids.Add(gid);
                seg.X.Add(penX);
                seg.Clusters.Add(i);
                segments[^1] = (seg.Face, seg.Gids, seg.X, seg.Clusters, seg.TextStart, i + units);
            }

            penX += adv1;
            i += units - 1;
        }

        float width = penX;
        float x0 = command.Horizontal switch { TextHorizontalAlignment.Center => -width / 2, TextHorizontalAlignment.Right => -width, _ => 0 };
        float asc = (float)(fm.Ascender * emPx / upem), desc = (float)(-fm.Descender * emPx / upem);
        float baseline = command.Vertical switch { TextVerticalAlignment.Top => asc, TextVerticalAlignment.Bottom => -desc, _ => 0 };

        // The text lives in a local space whose unit is a page pixel; the escapement turns it about the reference point in device space.
        Vector2 origin = Page(command.Origin, f);
        float theta = (float)(font.Escapement / 10.0 * Math.PI / 180.0);
        Matrix3x2 local = Matrix3x2.CreateRotation(-theta) * Matrix3x2.CreateTranslation(origin);

        bool clipped = false;
        if (command.ClipRectangle is { } cr)
        {
            var pb = new PathBuffer();
            AddRect(pb, cr, f);
            PushClipPath(pb, FillRule.NonZero);
            clipped = true;
        }

        PushTransform(local);
        PaintColor color = Color(command.Color);
        foreach (var seg in segments)
        {
            float[] xs2 = new float[seg.X.Count];
            for (int i = 0; i < xs2.Length; i++)
            {
                xs2[i] = seg.X[i];
            }

            L.DrawGlyphRun(
                Id(),
                seg.Face,
                emPx,
                x0,
                baseline,
                color,
                seg.Gids.ToArray(),
                xs2,
                default,
                seg.Clusters.ToArray(),
                text.AsSpan(0, text.Length));
        }

        float thick = Math.Max(emPx / 16f, 0.5f);
        if (font.Underline)
        {
            L.FillRect(Id(), new RectF(x0, baseline + (emPx * 0.08f), width, thick), color);
        }

        if (font.StrikeOut)
        {
            L.FillRect(Id(), new RectF(x0, baseline - (emPx * 0.3f), width, thick), color);
        }

        PopTransform();
        if (clipped)
        {
            PopClip();
        }
    }

    private FontMatch? ResolveFont(FontSpec font)
    {
        _fonts ??= _ctx.Fonts ?? _ctx.ResolvedFonts ?? (_ctx.ResolvedFonts = FontLibrary.CreateDefault());
        string family = font.FaceName.Length == 0 ? "Arial" : font.FaceName;
        string? familyClass = ((font.PitchAndFamily >> 4) & 0xF) switch { 1 => "roman", 2 => "swiss", 3 => "modern", 4 => "script", 5 => "decorative", _ => null };
        FontMatch? match = _fonts.Resolve(new FontRequest(family, font.Weight >= 600, font.Italic, familyClass));
        if (match is null)
        {
            Note("render.metafile-diagnostic", "font:" + family, $"No font is available for \"{family}\"; its text in the picture is not drawn.", DefectSeverity.Dropped);
            return null;
        }

        if (!match.IsExact)
        {
            Note("render.metafile-font", "font:" + family, $"Font \"{family}\" in the picture is not available; \"{match.ActualFamily}\" was used.", DefectSeverity.Repaired);
        }

        return match;
    }

    private static void AddRect(PathBuffer pb, RectD r, in Matrix3x2 m)
    {
        Vector2[] pts = [new((float)r.Left, (float)r.Top), new((float)r.Right, (float)r.Top), new((float)r.Right, (float)r.Bottom), new((float)r.Left, (float)r.Bottom)];
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = Page(pts[i], m);
            if (i == 0)
            {
                pb.MoveTo(p.X, p.Y);
            }
            else
            {
                pb.LineTo(p.X, p.Y);
            }
        }

        pb.Close();
    }

    // ---- EMF+ commands -----------------------------------------------------------------------------------------------------

    public override void VisitClear(ClearCommand command)
    {
        _record = command.RecordIndex;
        if (command.Color.A == 0)
        {
            return;
        }

        Sync();
        _pb.Clear();
        AddRect(_pb, _info.PictureRectangle, _toPage);
        L.FillPath(Id(), _pb, Color(command.Color), FillRule.NonZero);
    }

    public override void VisitDrawImage(DrawImageCommand command)
    {
        _record = command.RecordIndex;
        RasterImage img = command.Image;
        if (img.Width <= 0 || img.Height <= 0)
        {
            return;
        }

        Sync();
        RectD s = command.Source.Normalized;
        double sl = Math.Clamp(s.Left, 0, img.Width), st = Math.Clamp(s.Top, 0, img.Height), sr = Math.Clamp(s.Right, 0, img.Width), sb = Math.Clamp(s.Bottom, 0, img.Height);
        if (sr - sl <= 0 || sb - st <= 0 || s.Width <= 0 || s.Height <= 0)
        {
            return;
        }

        Vector2 tl = command.DestTopLeft, tr = command.DestTopRight, bl = command.DestBottomLeft;
        Vector2 vx = (tr - tl) / (float)s.Width, vy = (bl - tl) / (float)s.Height;
        var a = new Matrix3x2(vx.X, vx.Y, vy.X, vy.Y, tl.X - (float)(s.Left * vx.X) - (float)(s.Top * vy.X), tl.Y - (float)(s.Left * vx.Y) - (float)(s.Top * vy.Y));
        ImageHandle handle = _ctx.Images.Add(new RgbaImage(img.Width, img.Height, img.Pixels.ToArray()));
        var src = new RectF((float)sl, (float)st, (float)(sr - sl), (float)(sb - st));
        bool group = command.Opacity < 1;
        if (group)
        {
            L.PushGroup(Id(), (float)Math.Clamp(command.Opacity, 0, 1));
        }

        PushTransform(a * Final);
        L.DrawImage(Id(), handle, src, src, command.Interpolation == ImageInterpolation.Nearest ? ImageSampling.Pixelated : ImageSampling.Smooth);
        PopTransform();
        if (group)
        {
            L.PopGroup();
        }
    }

    private static Rgba32 BrushColor(BrushSpec b) => b switch
    {
        SolidBrushSpec s => s.Color,
        LinearGradientBrushSpec { Stops.Count: > 0 } g => g.Stops[0].Color,
        PathGradientBrushSpec p => p.CenterColor,
        StyleHatchBrushSpec h => h.Foreground,
        HatchBrushSpec h => h.Foreground,
        PatternBrushSpec p => p.Foreground,
        _ => new Rgba32(0, 0, 0, 255),
    };

    // [MS-EMFPLUS] §2.3.4.17 DrawDriverString: glyphs at their own positions, or following one another from the origin.
    public override void VisitDrawGlyphs(DrawGlyphsCommand command)
    {
        _record = command.RecordIndex;
        if (command.Text.Length == 0)
        {
            return;
        }

        Matrix3x2 saved = _m;
        if (command.Transform is { } t)
        {
            _m = t * _m;
        }

        Rgba32 color = BrushColor(command.Brush);
        if (command.Brush is not SolidBrushSpec)
        {
            Note("render.metafile-approximated", "textbrush", "Text drawn with a gradient or pattern brush takes the brush's first colour.", DefectSeverity.Repaired);
        }

        if (command.Positions is null)
        {
            VisitDrawText(new DrawTextCommand(command.Text, command.IsGlyphIndices, command.Origin, TextHorizontalAlignment.Left, TextVerticalAlignment.Baseline, command.Font, color, null, null, null, default, null) { RecordIndex = command.RecordIndex });
        }
        else
        {
            for (int i = 0; i < command.Text.Length && i < command.Positions.Count; i++)
            {
                VisitDrawText(new DrawTextCommand(command.Text[i].ToString(), command.IsGlyphIndices, command.Positions[i], TextHorizontalAlignment.Left, TextVerticalAlignment.Baseline, command.Font, color, null, null, null, default, null) { RecordIndex = command.RecordIndex });
            }
        }

        _m = saved;
    }

    // [MS-EMFPLUS] §2.3.4.16 DrawString: the renderer breaks lines in the layout rectangle. Lines wrap at spaces, are aligned as the string
    // format says, and sit at the font's line height; text is not clipped to the rectangle.
    public override void VisitDrawString(DrawStringCommand command)
    {
        _record = command.RecordIndex;
        if (command.Text.Length == 0)
        {
            return;
        }

        FontMatch? match = ResolveFont(command.Font);
        if (match is null)
        {
            return;
        }

        OneFont.FontMetrics fm = match.Face.Metrics;
        double upem = fm.UnitsPerEm;
        double em = command.Font.Height == 0 ? 12 : Math.Abs(command.Font.Height);
        double lineHeight = (fm.Ascender - fm.Descender + fm.LineGap) * em / upem;
        double lead = command.Format.LeadingMargin * em, trail = command.Format.TrailingMargin * em;
        RectD box = command.Layout;
        double usable = box.Width - lead - trail;

        double Width(string text)
        {
            double w = 0;
            foreach (char ch in text)
            {
                w += match.Face.GetAdvance(match.Face.GetGlyphIndex(ch)) * em / upem;
            }

            return w;
        }

        var lines = new List<string>();
        foreach (string para in command.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (command.Format.NoWrap || !(usable > 0))
            {
                lines.Add(para);
                continue;
            }

            var current = new System.Text.StringBuilder();
            foreach (string word in SplitWords(para))
            {
                if (current.Length > 0 && Width(current.ToString() + word.TrimEnd()) > usable)
                {
                    lines.Add(current.ToString().TrimEnd());
                    current.Clear();
                }

                current.Append(word);
            }

            lines.Add(current.ToString().TrimEnd());
        }

        double block = lines.Count * lineHeight;
        double top = command.Format.LineAlignment switch { 1 => box.Top + ((box.Height - block) / 2), 2 => box.Bottom - block, _ => box.Top };
        Rgba32 color = BrushColor(command.Brush);
        if (command.Brush is not SolidBrushSpec)
        {
            Note("render.metafile-approximated", "textbrush", "Text drawn with a gradient or pattern brush takes the brush's first colour.", DefectSeverity.Repaired);
        }

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.Length == 0)
            {
                continue;
            }

            double w = Width(line);
            double x = command.Format.Alignment switch { 1 => box.Left + ((box.Width - w) / 2), 2 => box.Right - trail - w, _ => box.Left + lead };
            VisitDrawText(new DrawTextCommand(line, false, new Vector2((float)x, (float)(top + (i * lineHeight))), TextHorizontalAlignment.Left, TextVerticalAlignment.Top, command.Font, color, null, null, null, default, null) { RecordIndex = command.RecordIndex });
        }
    }

    // Words keep the space that follows them, so a line break leaves the space on the line it ends.
    private static IEnumerable<string> SplitWords(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ' && (i + 1 == text.Length || text[i + 1] != ' '))
            {
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }

    // ---- reporting ---------------------------------------------------------------------------------------------------------

    private void Note(string code, string key, string message, DefectSeverity severity)
    {
        if (_said.Add(code + ":" + key))
        {
            DefectKind kind = code == "render.metafile-diagnostic" ? DefectKind.Record : DefectKind.Unsupported;
            string where = _record >= 0 ? $" (record {_record})" : string.Empty;
            MetafilePainter.Say(_ctx, code, kind, severity, message + where);
        }
    }
}
