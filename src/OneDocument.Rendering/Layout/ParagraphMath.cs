using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Equations;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Equations;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

/// <summary>One equation inside a paragraph: its laid-out box and where it sits in the paragraph text.</summary>
internal sealed class MathSpan(MathBox box, bool display, AlignMode align)
{
    public MathBox Box { get; } = box;

    public bool Display { get; } = display;

    public AlignMode Align { get; } = align;

    /// <summary>The number of characters of the paragraph text the equation takes: its linear form.</summary>
    public int Length => Box.LinearText.Length;
}

// The meeting point of the paragraph core and the equation layout. An equation becomes one unbreakable atom whose text is the
// equation's linear form, so text offsets, extraction and alternative text agree.
internal static class ParagraphMath
{
    /// <summary>Whether the inline element is an <c>m:oMath</c> or <c>m:oMathPara</c>.</summary>
    public static bool IsMath(OtherInline inline)
    {
        MarkupRef m = inline.Markup;
        if (m.Namespace != SchemaNamespace.OfficeMath)
        {
            return false;
        }

        ReadOnlySpan<byte> n = m.LocalName;
        return n.SequenceEqual("oMath"u8) || n.SequenceEqual("oMathPara"u8);
    }

    // ECMA-376 Part 1 §22.1.2.78 (oMathPara) and §22.1.2.57 (jc): an equation paragraph is justified centred as a group unless m:jc or
    // the document's m:defJc says otherwise. A group is laid out as one equation per line here, so centerGroup centres each line.
    private static AlignMode Align(MathJustification? jc) => jc switch
    {
        MathJustification.Left => AlignMode.Start,
        MathJustification.Right => AlignMode.End,
        _ => AlignMode.Center,
    };

    /// <summary>
    /// Lays out the equation in <paramref name="element"/>, appends its linear text to the paragraph text and records the span.
    /// </summary>
    public static void Add(
        LayoutContext ctx, Paragraph p, OtherInline element, (string Family, double Size, bool Bold, bool Italic, uint Color) text, double available,
        System.Text.StringBuilder sb, List<RunInfo> infos, List<int> charRun, Dictionary<int, MathSpan> spans)
    {
        MathNode? node = OmmlReader.Read(element.Markup.Node, out IReadOnlyList<MathNote> readNotes);
        foreach (MathNote n in readNotes)
        {
            ctx.NoteOnce(n.Code, "layout." + n.Code, n.Message, p);
        }

        if (node is null)
        {
            return;
        }

        MathSettings settings = ctx.Compat.EquationSettings;
        bool para = node.Kind == MathKind.OMathPara;
        AlignMode align = Align(para ? node.Properties.Justification ?? settings.DefaultJustification : null);
        IEnumerable<MathNode> equations = para ? node.Children.Where(static c => c.Kind == MathKind.OMath) : [node];
        foreach (MathNode eq in equations)
        {
            var options = new MathLayoutOptions
            {
                Fonts = ctx.Fonts,
                FontSize = text.Size,
                Color = text.Color,
                TextFamily = text.Family,
                Settings = settings,
                Display = para && settings.DisplayDefault,
                AvailableWidth = para ? Math.Max(1, available - settings.LeftMargin - settings.RightMargin) : double.PositiveInfinity,
            };
            MathBox box = MathLayouter.Layout(eq, options);
            foreach (MathNote n in box.Notes)
            {
                ctx.NoteOnce(n.Code, "layout." + n.Code, n.Message, p);
            }

            string linear = box.LinearText;
            if (linear.Length == 0)
            {
                continue;
            }

            var info = new RunInfo { Size = text.Size, SizeCs = text.Size, Bold = text.Bold, Italic = text.Italic, Ascii = text.Family, HAnsi = text.Family, EastAsia = text.Family, Cs = text.Family };
            info.Paint.Color = text.Color;
            infos.Add(info);
            spans[sb.Length] = new MathSpan(box, para, align);
            sb.Append(linear);
            for (int i = 0; i < linear.Length; i++)
            {
                charRun.Add(infos.Count - 1);
            }
        }
    }

    public static Piece MakePiece(MathSpan span, RunInfo info, int start, byte level) => new()
    {
        Kind = AtomKind.Math,
        Run = info,
        Start = start,
        Length = span.Length,
        Level = level,
        Size = info.Size,
        Text = span.Box.LinearText,
        Box = span.Box,
        MathAlign = span.Align,
        Display = span.Display,
    };

    public static Atom MakeAtom(MathSpan span, int pieceIndex, int start, byte level, bool breakBefore) => new()
    {
        Piece = pieceIndex,
        TextStart = start,
        TextLength = span.Length,
        Width = span.Box.Width,
        Level = level,
        BreakBefore = breakBefore,
        Kind = AtomKind.Math,
        Display = span.Display,
    };
}
