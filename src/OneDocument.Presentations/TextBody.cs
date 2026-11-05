using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>
/// The text of a shape or table cell (<c>p:txBody</c> or <c>a:txBody</c>): body properties, a list style and paragraphs. A view over
/// the element; edits go to the markup tree and keep everything the model does not touch.
/// </summary>
public sealed class TextBody
{
    internal TextBody(Shape shape, MarkupRef element)
        : this(shape.Part, shape, element)
    {
    }

    internal TextBody(PresentationPart part, Shape? shape, MarkupRef element)
    {
        Part = part;
        Shape = shape;
        Markup = element;
    }

    /// <summary>Gets the part the text is in.</summary>
    public PresentationPart Part { get; }

    /// <summary>Gets the shape the text belongs to, or null for the text of a table cell or a comment.</summary>
    public Shape? Shape { get; }

    /// <summary>Gets the text body element: the hop from the hub to the markup layer.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets <c>a:bodyPr</c> (insets, anchor, wrapping, autofit), or an invalid reference. See <see cref="Shape.ResolveBodyFormat"/> for the inherited values.</summary>
    public MarkupRef BodyProperties => Pml.A(Markup, "bodyPr") ?? default;

    /// <summary>Gets <c>a:lstStyle</c>, the list style of this text body, or an invalid reference.</summary>
    public MarkupRef ListStyle => Pml.A(Markup, "lstStyle") ?? default;

    /// <summary>Gets the paragraphs in order. A text body without any reads as one empty paragraph that is not in the tree yet.</summary>
    public IReadOnlyList<TextParagraph> Paragraphs
    {
        get
        {
            var list = new List<TextParagraph>();
            foreach (MarkupRef p in Pml.Elements(Markup, SchemaNamespace.Drawing, "p"))
            {
                list.Add(new TextParagraph(this, p));
            }

            return list;
        }
    }

    /// <summary>Gets the text: the paragraphs' text joined by a line feed. A line break inside a paragraph reads as a vertical tab (U+000B).</summary>
    public string Text => TextOf(Markup);

    internal static string TextOf(MarkupRef txBody)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (MarkupRef p in Pml.Elements(txBody, SchemaNamespace.Drawing, "p"))
        {
            if (!first)
            {
                sb.Append('\n');
            }

            first = false;
            sb.Append(TextParagraph.TextOfParagraph(p));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Replaces the text. Lines (split at a line feed or carriage return) become paragraphs: the first paragraph is edited in place and
    /// the others copy its paragraph properties and the first run's formatting; paragraphs beyond the new line count are removed. The
    /// body properties and list style are untouched.
    /// </summary>
    /// <param name="text">The new text; empty leaves one empty paragraph. A vertical tab (U+000B) is a line break inside a paragraph.</param>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        IReadOnlyList<TextParagraph> existing = Paragraphs;
        if (existing.Count == 0)
        {
            AppendXmlTo(Markup.Node, "<{a}:p/>");
            existing = Paragraphs;
        }

        existing[0].SetText(lines[0]);
        for (int i = existing.Count - 1; i >= 1; i--)
        {
            existing[i].Markup.Node.Remove();
        }

        if (lines.Length == 1)
        {
            return;
        }

        TextParagraph template = Paragraphs[0];
        MarkupNode after = template.Markup.Node;
        for (int i = 1; i < lines.Length; i++)
        {
            string xml = template.BuildCopy(lines[i]);
            after.InsertXmlAfter(Pml.Utf8(xml));
            after = after.NextSibling;
            while (!after.IsNull && after.Kind != MarkupNodeKind.Element)
            {
                after = after.NextSibling;
            }
        }
    }

    /// <summary>Adds a paragraph at the end, with the paragraph properties of the last paragraph (bullets, level, alignment) and the formatting of its first run.</summary>
    /// <param name="text">The paragraph's text.</param>
    /// <returns>The new paragraph.</returns>
    public TextParagraph AddParagraph(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        IReadOnlyList<TextParagraph> existing = Paragraphs;
        if (existing.Count == 0)
        {
            AppendXmlTo(Markup.Node, "<{a}:p/>");
            TextParagraph only = Paragraphs[0];
            only.SetText(text);
            return only;
        }

        TextParagraph last = existing[^1];
        last.Markup.Node.InsertXmlAfter(Pml.Utf8(last.BuildCopy(text)));
        IReadOnlyList<TextParagraph> after = Paragraphs;
        return after[^1];
    }

    // Appends a fragment whose {a} placeholder stands for the DrawingML prefix in scope at 'node'.
    internal static void AppendXmlTo(MarkupNode node, string fragment)
    {
        var prefixes = new Prefixes(node);
        node.AppendXml(Pml.Utf8(Fill(fragment, prefixes)));
    }

    internal static string Fill(string fragment, Prefixes prefixes)
    {
        string xml = fragment.Replace("{a}", prefixes.A, StringComparison.Ordinal).Replace("{p}", prefixes.P, StringComparison.Ordinal).Replace("{r}", prefixes.R, StringComparison.Ordinal);
        if (prefixes.Declare.Length == 0)
        {
            return xml;
        }

        // Declare what the fragment needs on its first element, so it parses wherever it is inserted.
        int end = xml.IndexOfAny(['>', '/', ' '], 1);
        return end < 0 ? xml : xml.Insert(end, prefixes.Declare);
    }
}

/// <summary>What a run of a paragraph is.</summary>
public enum TextRunKind
{
    /// <summary>Text (<c>a:r</c>).</summary>
    Text = 0,

    /// <summary>A line break inside the paragraph (<c>a:br</c>).</summary>
    LineBreak = 1,

    /// <summary>A field such as the slide number or date (<c>a:fld</c>), with the text it last showed.</summary>
    Field = 2,
}

/// <summary>A paragraph of a text body (<c>a:p</c>).</summary>
public sealed partial class TextParagraph
{
    internal TextParagraph(TextBody body, MarkupRef element)
    {
        Body = body;
        Markup = element;
    }

    /// <summary>Gets the text body the paragraph is in.</summary>
    public TextBody Body { get; }

    /// <summary>Gets the paragraph element.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets <c>a:pPr</c>, the paragraph's own properties, or an invalid reference. See <see cref="ResolveFormat"/> for the inherited values.</summary>
    public MarkupRef Properties => Pml.A(Markup, "pPr") ?? default;

    /// <summary>Gets or sets the outline level, 0 to 8 (<c>pPr/@lvl</c>, 0 when absent). Setting 0 removes the attribute.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The level is outside 0 to 8.</exception>
    public int Level
    {
        get => Math.Clamp(Pml.AttrInt(Properties, "lvl") ?? 0, 0, 8);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 8);
            MarkupRef pPr = Properties;
            if (!pPr.IsValid)
            {
                if (value == 0)
                {
                    return;
                }

                var prefixes = new Prefixes(Markup.Node);
                Markup.Node.PrependXml(Pml.Utf8(TextBody.Fill("<{a}:pPr/>", prefixes)));
                pPr = Properties;
            }

            if (value == 0)
            {
                pPr.Node.RemoveAttribute("lvl");
            }
            else
            {
                pPr.Node.SetAttribute("lvl", value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>Gets the runs in order: text, line breaks and fields.</summary>
    public IReadOnlyList<TextRun> Runs
    {
        get
        {
            var list = new List<TextRun>();
            foreach (MarkupRef child in Pml.Elements(Markup))
            {
                if (child.Namespace == SchemaNamespace.Drawing && child.LocalNameText is "r" or "br" or "fld")
                {
                    list.Add(new TextRun(this, child));
                }
            }

            return list;
        }
    }

    /// <summary>Gets the paragraph's text: the runs' text joined; a line break reads as a vertical tab (U+000B) and a field as the text it last showed.</summary>
    public string Text => TextOfParagraph(Markup);

    internal static string TextOfParagraph(MarkupRef p)
    {
        var sb = new StringBuilder();
        foreach (MarkupRef run in Pml.Elements(p))
        {
            if (run.Namespace != SchemaNamespace.Drawing)
            {
                continue;
            }

            switch (run.LocalNameText)
            {
                case "r" or "fld":
                    if (Pml.A(run, "t") is { } t)
                    {
                        sb.Append(DecodeText(t.Node.InnerText));
                    }

                    break;
                case "br":
                    sb.Append('\v');
                    break;
            }
        }

        return sb.ToString();
    }

    // ECMA-376 Part 1 §22.9.2.19 (ST_Xstring): _xHHHH_ stands for the UTF-16 code unit HHHH.
    internal static string DecodeText(string s)
    {
        if (!s.Contains("_x", StringComparison.Ordinal))
        {
            return s;
        }

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '_' && i + 6 < s.Length && s[i + 1] == 'x' && s[i + 6] == '_'
                && ushort.TryParse(s.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort unit))
            {
                sb.Append((char)unit);
                i += 6;
            }
            else
            {
                sb.Append(s[i]);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Replaces the paragraph's runs with the text, keeping the paragraph properties, the end-of-paragraph properties and anything else
    /// the model does not read. The new text takes the formatting of the first text run (or of the end-of-paragraph properties when
    /// there was none). A line feed or vertical tab in the text becomes a line break.
    /// </summary>
    /// <param name="text">The new text; empty leaves the paragraph without runs.</param>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string? formatting = FirstRunProperties();
        foreach (TextRun run in Runs)
        {
            run.Markup.Node.Remove();
        }

        string runs = BuildRuns(text, formatting);
        if (runs.Length == 0)
        {
            return;
        }

        var prefixes = new Prefixes(Markup.Node);
        byte[] xml = Pml.Utf8(TextBody.Fill(runs, prefixes));
        MarkupRef? end = Pml.A(Markup, "endParaRPr");
        if (end is not null)
        {
            end.Value.Node.InsertXmlBefore(xml);
        }
        else
        {
            Markup.Node.AppendXml(xml);
        }
    }

    // The formatting a new run takes, as an a:rPr element in the {a} placeholder form; null when the paragraph has none to copy.
    private string? FirstRunProperties()
    {
        foreach (MarkupRef child in Pml.Elements(Markup))
        {
            if (child.Namespace == SchemaNamespace.Drawing && child.LocalNameText is "r" or "fld" && Pml.A(child, "rPr") is { } rPr)
            {
                return ToPlaceholderForm(rPr.Node, "rPr");
            }
        }

        return Pml.A(Markup, "endParaRPr") is { } end ? ToPlaceholderForm(end.Node, "rPr") : null;
    }

    // Re-spells an element as a:<name> with the placeholder prefix so it can be re-inserted wherever the paragraph is.
    private static string ToPlaceholderForm(MarkupNode element, string newLocalName)
    {
        string xml = element.OuterXml.Replace("{", "&#123;", StringComparison.Ordinal);
        string prefix = Encoding.UTF8.GetString(element.Prefix);
        string oldName = (prefix.Length == 0 ? string.Empty : prefix + ":") + element.LocalNameString;
        xml = xml.Replace("<" + oldName, "<{a}:" + newLocalName, StringComparison.Ordinal).Replace("</" + oldName + ">", "</{a}:" + newLocalName + ">", StringComparison.Ordinal);
        return xml.Replace("<" + prefix + ":", "<{a}:", StringComparison.Ordinal).Replace("</" + prefix + ":", "</{a}:", StringComparison.Ordinal);
    }

    private static string BuildRuns(string text, string? formatting)
    {
        var sb = new StringBuilder();
        string props = formatting ?? string.Empty;
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            bool brk = i < text.Length && text[i] is '\n' or '\v';
            if (i < text.Length && !brk)
            {
                continue;
            }

            if (i > start)
            {
                sb.Append("<{a}:r>").Append(props).Append("<{a}:t>").Append(Pml.EscapeForFragment(text[start..i])).Append("</{a}:t></{a}:r>");
            }

            if (brk)
            {
                sb.Append("<{a}:br>").Append(props).Append("</{a}:br>");
            }

            start = i + 1;
        }

        return sb.ToString();
    }

    // A new paragraph like this one: the same paragraph properties and the first run's formatting, holding 'text'.
    internal string BuildCopy(string text)
    {
        string? formatting = FirstRunProperties();
        var sb = new StringBuilder("<{a}:p>");
        if (Properties is { IsValid: true } pPr)
        {
            sb.Append(ToPlaceholderForm(pPr.Node, "pPr"));
        }

        sb.Append(BuildRuns(text, formatting));
        if (Pml.A(Markup, "endParaRPr") is { } end)
        {
            sb.Append(ToPlaceholderForm(end.Node, "endParaRPr"));
        }

        sb.Append("</{a}:p>");
        return TextBody.Fill(sb.ToString(), new Prefixes(Markup.Node));
    }
}

/// <summary>One run of a paragraph: a piece of text with one set of character properties, a line break, or a field.</summary>
public sealed class TextRun
{
    internal TextRun(TextParagraph paragraph, MarkupRef element)
    {
        Paragraph = paragraph;
        Markup = element;
    }

    /// <summary>Gets the paragraph the run is in.</summary>
    public TextParagraph Paragraph { get; }

    /// <summary>Gets the run element (<c>a:r</c>, <c>a:br</c> or <c>a:fld</c>).</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets what the run is.</summary>
    public TextRunKind Kind => Markup.LocalNameText switch
    {
        "br" => TextRunKind.LineBreak,
        "fld" => TextRunKind.Field,
        _ => TextRunKind.Text,
    };

    /// <summary>Gets <c>a:rPr</c>, the run's own character properties, or an invalid reference. See <see cref="TextParagraph.ResolveRunFormat"/> for inherited values.</summary>
    public MarkupRef Properties => Pml.A(Markup, "rPr") ?? default;

    /// <summary>Gets the language tag (<c>rPr/@lang</c>), or null.</summary>
    public string? Language => Pml.Attr(Properties, "lang");

    /// <summary>Gets the field type (<c>a:fld/@type</c>: <c>slidenum</c>, <c>datetime1</c>…) of a field, or null.</summary>
    public string? FieldType => Kind == TextRunKind.Field ? Pml.Attr(Markup, "type") : null;

    /// <summary>Gets or sets the run's text. A line break reads as a vertical tab and ignores setting. Setting a field's text changes what it last showed, not the field.</summary>
    public string Text
    {
        get => Kind == TextRunKind.LineBreak ? "\v" : Pml.A(Markup, "t") is { } t ? TextParagraph.DecodeText(t.Node.InnerText) : string.Empty;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Kind == TextRunKind.LineBreak)
            {
                return;
            }

            if (Pml.A(Markup, "t") is { } t)
            {
                t.Node.ReplaceWithXml(Pml.Utf8(TextBody.Fill("<{a}:t>" + Pml.EscapeForFragment(value) + "</{a}:t>", new Prefixes(t.Node))));
            }
            else
            {
                TextBody.AppendXmlTo(Markup.Node, "<{a}:t>" + Pml.EscapeForFragment(value) + "</{a}:t>");
            }
        }
    }
}
