using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Fields;
using RecursiveAI.OneDocument.Documents.Numbering;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

// What a header or footer paragraph needs to know about the page it is printed on.
internal sealed class PageFieldState
{
    public long Printed; // the number the page prints
    public string? NumberFormat; // w:pgNumType w:fmt of the page's section
    public int NumPages; // total pages from the previous pass; 0 on the first pass
    public int SectionPages; // pages of the page's section from the previous pass
    public int SectionNumber; // 1-based
}

// Page-number fields in headers and footers (ECMA-376 Part 1 §17.16.5.45 PAGE, §17.16.5.43 NUMPAGES, §17.16.5.58 SECTIONPAGES, §17.16.5.59 SECTION).
// Layout knows these numbers only while it places pages, so it replaces the cached result text of these fields when a header or
// footer paragraph is formatted. The general field update (dates, references, TOC) is a later stage and replaces this hook: the
// formatter asks `Prepare` for run replacements and nothing else.
internal static class PageFieldScanner
{
    private sealed class Open
    {
        public readonly System.Text.StringBuilder Code = new();
        public bool InResult;
        public string? Value;
        public bool ResultStarted;
    }

    public static void Prepare(LayoutContext ctx, Paragraph p)
    {
        ctx.RunText = null;
        if (ctx.PageFields is not { } state)
        {
            return;
        }

        var map = new Dictionary<Run, string>();
        var stack = new List<Open>();
        Walk(ctx, state, p.Inlines(), map, stack, 0);
        ctx.RunText = map.Count == 0 ? null : map;
    }

    private static void Walk(LayoutContext ctx, PageFieldState state, IEnumerable<InlineElement> inlines, Dictionary<Run, string> map, List<Open> stack, int depth)
    {
        if (depth > 32)
        {
            return;
        }

        foreach (InlineElement e in inlines)
        {
            switch (e)
            {
                case Run r:
                    OnRun(ctx, state, r, map, stack);
                    break;
                case SimpleField sf:
                    string? value = Evaluate(ctx, state, sf.Instruction ?? string.Empty);
                    if (value is null)
                    {
                        Walk(ctx, state, sf.Inlines(), map, stack, depth + 1);
                        break;
                    }

                    bool first = true;
                    foreach (Run r in Runs(sf, 0))
                    {
                        map[r] = first ? value : string.Empty;
                        first = false;
                    }

                    break;
                case InlineContainer c:
                    Walk(ctx, state, c.Inlines(), map, stack, depth + 1);
                    break;
            }
        }
    }

    private static IEnumerable<Run> Runs(InlineContainer c, int depth)
    {
        if (depth > 8)
        {
            yield break;
        }

        foreach (InlineElement e in c.Inlines())
        {
            if (e is Run r)
            {
                yield return r;
            }
            else if (e is InlineContainer inner)
            {
                foreach (Run x in Runs(inner, depth + 1))
                {
                    yield return x;
                }
            }
        }
    }

    // ECMA-376 Part 1 §17.16.18 (fldChar): begin, separate and end bracket the instruction text (§17.16.13 instrText) and the result.
    private static void OnRun(LayoutContext ctx, PageFieldState state, Run run, Dictionary<Run, string> map, List<Open> stack)
    {
        bool replacing = stack.Count == 1 && stack[0].InResult && stack[0].Value is not null;
        bool structural = false;
        for (MarkupNode n = run.Markup.Node.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind != MarkupNodeKind.Element || new MarkupRef(n).Namespace != SchemaNamespace.Wordprocessing)
            {
                continue;
            }

            if (Xml.Eq(n.LocalName, "fldChar"))
            {
                structural = true;
                switch (Xml.Attr(new MarkupRef(n), "fldCharType"))
                {
                    case "begin":
                        stack.Add(new Open());
                        break;
                    case "separate" when stack.Count > 0:
                        Open top = stack[^1];
                        top.InResult = true;
                        top.Value = Evaluate(ctx, state, top.Code.ToString());
                        break;
                    case "end" when stack.Count > 0:
                        stack.RemoveAt(stack.Count - 1);
                        break;
                }
            }
            else if (Xml.Eq(n.LocalName, "instrText") && stack.Count > 0 && !stack[^1].InResult)
            {
                stack[^1].Code.Append(n.InnerText);
            }
        }

        if (replacing && !structural)
        {
            Open top = stack[0];
            map[run] = top.ResultStarted ? string.Empty : (top.Value ?? string.Empty);
            top.ResultStarted = true;
        }
    }

    // Returns the text a field of this type shows on the page, or null when the field is not one layout knows.
    private static string? Evaluate(LayoutContext ctx, PageFieldState state, string instruction)
    {
        FieldInstruction f = FieldInstruction.Parse(instruction);
        string? format = null;
        foreach (FieldSwitch s in f.Switches)
        {
            if (s.Name == "*" && s.Value is { } v)
            {
                format = v switch
                {
                    "Roman" or "ROMAN" => "upperRoman",
                    "roman" => "lowerRoman",
                    "ALPHABETIC" => "upperLetter",
                    "alphabetic" => "lowerLetter",
                    "Arabic" or "ARABIC" or "arabic" => "decimal",
                    _ => format,
                };
            }
        }

        if (string.Equals(f.Type, "PAGE", StringComparison.OrdinalIgnoreCase))
        {
            return NumberFormatter.Format(format ?? state.NumberFormat, state.Printed).Text;
        }

        if (string.Equals(f.Type, "NUMPAGES", StringComparison.OrdinalIgnoreCase))
        {
            ctx.UsesPageTotals = true;
            return NumberFormatter.Format(format, Math.Max(1, state.NumPages)).Text;
        }

        if (string.Equals(f.Type, "SECTIONPAGES", StringComparison.OrdinalIgnoreCase))
        {
            ctx.UsesPageTotals = true;
            return NumberFormatter.Format(format, Math.Max(1, state.SectionPages)).Text;
        }

        if (string.Equals(f.Type, "SECTION", StringComparison.OrdinalIgnoreCase))
        {
            return NumberFormatter.Format(format, state.SectionNumber).Text;
        }

        return null;
    }
}
