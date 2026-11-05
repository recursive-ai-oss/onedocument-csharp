using System.Globalization;
using System.Numerics;
using OneRender.Painting;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Rendering.Presentations;

internal sealed partial class RenderSession
{
    // PowerPoint's shrink steps: the font scale falls by 7.5 points of a hundred each time, and line spacing is first cut by 10 then by 20
    // percentage points. The steps and their order are what PowerPoint is seen to store (fontScale 92500 with lnSpcReduction 10000 or
    // 20000, then 85000, 77500 and so on down to 25000 with 20000); which step it picks for a given text is checked against PowerPoint.
    private static readonly (double Scale, double Reduction)[] ShrinkSteps =
    [
        (1.0, 0.0), (1.0, 0.1), (1.0, 0.2), (0.925, 0.1), (0.925, 0.2), (0.85, 0.2), (0.775, 0.2), (0.7, 0.2), (0.625, 0.2),
        (0.55, 0.2), (0.475, 0.2), (0.4, 0.2), (0.325, 0.2), (0.25, 0.2),
    ];

    /// <summary>
    /// Lays the text of a body out in a rectangle and applies the body's fit rule. Returns the laid-out text and the scale used.
    /// </summary>
    private PlacedText LayOutBody(IReadOnlyList<SlideTextParagraph> paragraphs, BodyProperties body, double lineWidth, double avail, bool vertical, out double fontScale)
    {
        var settings = new TextLayoutSettings
        {
            Width = lineWidth,
            Height = avail,
            Wrap = body.Wrap,
            Columns = vertical ? 1 : body.Columns,
            ColumnSpacing = body.ColumnSpacing,
        };
        fontScale = 1.0;
        if (body.Fit == AutofitKind.ShrinkText)
        {
            bool stored = body.FontScale is not null || body.LineReduction is not null;
            bool compute = Options.Autofit switch
            {
                SlideAutofitMode.Computed => true,
                SlideAutofitMode.Stored => false,
                _ => !stored,
            };
            if (!compute)
            {
                settings.FontScale = body.FontScale ?? 1.0;
                settings.LineReduction = body.LineReduction ?? 0.0;
                fontScale = settings.FontScale;
                return Layout.Layout(paragraphs, settings);
            }

            PlacedText? last = null;
            foreach ((double scale, double reduction) in ShrinkSteps)
            {
                settings.FontScale = scale;
                settings.LineReduction = reduction;
                last = Layout.Layout(paragraphs, settings);
                fontScale = scale;
                if (last.Height <= avail + 0.01)
                {
                    break;
                }
            }

            return last!;
        }

        return Layout.Layout(paragraphs, settings);
    }

    private static bool IsVertical(string vert) => vert is "vert" or "vert270" or "eaVert" or "mongolianVert" or "wordArtVert" or "wordArtVertRtl";

    // The text of a shape. ECMA-376 Part 1 §21.1.2.1.1 (bodyPr): the lines are laid out at the width of the text rectangle (its height for
    // vertical text), stacked from the top, the middle or the bottom, and drawn through the shape's transform.
    public void PaintShapeText(PartContext pc, ShapeTextRequest req)
    {
        MarkupNode txBody = DrawingTheme.Child(req.Shape, "txBody");
        if (txBody.IsNull)
        {
            AddNote("slide.text-body-missing", "A shape's text body could not be found; its text is not drawn.");
            return;
        }

        Shape? shape = pc.ShapeFor(req.Shape);
        BodyProperties body = shape is not null ? BodyProperties.Read(shape) : BodyProperties.Read(req.BodyProperties.IsNull ? [] : [req.BodyProperties]);
        StyleReader styles = pc.Styles(shape);
        List<SlideTextParagraph> paragraphs = TextBodyReader.Read(txBody, styles, Fields);
        if (!paragraphs.Any(static p => p.Runs.Count > 0))
        {
            return;
        }

        double rectW = req.RectWidth, rectH = req.RectHeight;
        if (rectW <= 0 || rectH <= 0)
        {
            rectW = req.Width + body.Left + body.Right;
            rectH = req.Height + body.Top + body.Bottom;
        }

        double w = rectW - body.Left - body.Right, h = rectH - body.Top - body.Bottom;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        PaintBody(pc, req.List, req.NextId, paragraphs, body, req.ShapeMatrix, req.RectX + body.Left, req.RectY + body.Top, w, h, shape?.Placeholder);
    }

    private void PaintBody(
        PartContext pc, DisplayList list, Func<ItemId> nextId, List<SlideTextParagraph> paragraphs, BodyProperties body, Matrix3x2 shapeMatrix,
        double x0, double y0, double w, double h, PlaceholderInfo? placeholder)
    {
        bool v90 = body.Vertical is "vert" or "eaVert" or "mongolianVert" or "wordArtVert";
        bool v270 = body.Vertical is "vert270" or "wordArtVertRtl";
        bool vertical = v90 || v270;
        if (body.Vertical is "mongolianVert" or "wordArtVert" or "wordArtVertRtl")
        {
            AddNote("slide.text-vertical-approximated", "Word-art and Mongolian vertical text is drawn as ordinary rotated text.");
        }

        if (body.Vertical == "eaVert")
        {
            AddNote("slide.text-eavert-approximated", "East Asian vertical text keeps Latin characters rotated with the line, as in other vertical text.");
        }

        double lineWidth = vertical ? h : w;
        double avail = vertical ? w : h;
        PlacedText placed = LayOutBody(paragraphs, body, lineWidth, avail, vertical, out _);
        if (placed.Paragraphs.Count == 0)
        {
            return;
        }

        double textHeight = placed.Height;
        // Text taller than its box is not clipped; it overflows from the anchor (up from the bottom, both ways from the middle).
        double shift = body.Anchor switch { "ctr" => (avail - textHeight) / 2, "b" => avail - textHeight, _ => 0 };
        double shiftX = 0;
        if (body.AnchorCenter)
        {
            shiftX = ((lineWidth - (placed.Right - placed.Left)) / 2) - placed.Left;
        }

        Matrix3x2 textToRect = Matrix3x2.Identity;
        if (v90)
        {
            textToRect = new Matrix3x2(0, 1, -1, 0, (float)w, 0);
        }
        else if (v270)
        {
            textToRect = new Matrix3x2(0, -1, 1, 0, 0, (float)h);
        }

        // The rectangle's own space (insets removed, origin at its top-left) to page pixels.
        Matrix3x2 rectToPage = Matrix3x2.CreateTranslation((float)x0, (float)y0) * shapeMatrix;
        double det = ((double)shapeMatrix.M11 * shapeMatrix.M22) - ((double)shapeMatrix.M12 * shapeMatrix.M21);
        if (body.Upright)
        {
            // upright: the text stays level on the page whatever the shape's rotation; it keeps the rectangle's centre.
            Vector2 centre = Vector2.Transform(new Vector2((float)(x0 + (w / 2)), (float)(y0 + (h / 2))), shapeMatrix);
            float scale = (float)Math.Sqrt(Math.Abs(det));
            rectToPage = Matrix3x2.CreateTranslation((float)(-w / 2), (float)(-h / 2)) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(centre);
        }
        else if (det < 0)
        {
            // A flipped shape does not mirror its text; PowerPoint turns the text over for a vertical flip.
            rectToPage = Matrix3x2.CreateTranslation((float)(-w / 2), (float)(-h / 2)) * Matrix3x2.CreateScale(-1f, 1f) * Matrix3x2.CreateTranslation((float)(w / 2), (float)(h / 2)) * rectToPage;
        }

        if (Math.Abs(body.Rotation) > 0.001)
        {
            rectToPage = Matrix3x2.CreateTranslation((float)(-w / 2), (float)(-h / 2)) * Matrix3x2.CreateRotation((float)(body.Rotation * Math.PI / 180)) * Matrix3x2.CreateTranslation((float)(w / 2), (float)(h / 2)) * rectToPage;
        }

        bool artifact = placeholder is { Type: "dt" or "ftr" or "sldNum" or "hdr" };
        TagRole role = placeholder is { Type: "title" or "ctrTitle" } ? TagRole.Heading1 : TagRole.Paragraph;
        if (Tagged && artifact)
        {
            list.BeginTag(nextId(), TagRole.Artifact);
        }

        var options = new TextEmitter.Options { Tagged = Tagged && !artifact, ParagraphRole = role, LinkTarget = pc.LinkTarget, NextId = nextId };
        TextEmitter.Emit(list, placed, textToRect * rectToPage, shiftX, shift, options);
        if (Tagged && artifact)
        {
            list.EndTag();
        }
    }

    // The height in points a shape needs for its text when it grows to fit it (spAutoFit): the laid-out height plus the top and bottom insets.
    public double? MeasureText(PartContext pc, Shape shape, TextBody body, BodyProperties props, ShapeBounds bounds)
    {
        MarkupNode txBody = body.Markup.Node;
        if (txBody.IsNull)
        {
            return null;
        }

        List<SlideTextParagraph> paragraphs = TextBodyReader.Read(txBody, pc.Styles(shape), Fields);
        if (!paragraphs.Any(static p => p.Runs.Count > 0))
        {
            return null;
        }

        bool vertical = IsVertical(props.Vertical);
        if (vertical)
        {
            return null;
        }

        double w = (bounds.Width / 12700.0) - props.Left - props.Right;
        if (w <= 0)
        {
            return null;
        }

        var noFit = new BodyProperties
        {
            Wrap = props.Wrap,
            Columns = props.Columns,
            ColumnSpacing = props.ColumnSpacing,
            Fit = AutofitKind.None,
        };
        PlacedText placed = LayOutBody(paragraphs, noFit, w, double.MaxValue / 4, false, out _);
        return placed.Height + props.Top + props.Bottom;
    }
}
