using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Fields;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

// Finds the fields of one paragraph and decides which of their result runs show a computed value instead of the stored one.
// ECMA-376 Part 1 §17.16 (fldChar, instrText, fldSimple): begin, separate and end bracket the instruction and the result; the result is
// the runs between separate and end. The first result run gets the computed text and the other result runs show nothing, so the
// field keeps the character formatting of its first result run. A value equal to the stored result changes nothing.
internal static class FieldRunReplacer
{
    private sealed class Rec
    {
        public readonly StringBuilder Code = new();
        public readonly StringBuilder Cached = new();
        public readonly List<Run> Runs = [];
        public bool InResult;
        public int Order;
        public MarkupNode Begin;
        public string? Value;
        public bool Closed;
    }

    public static Dictionary<Run, string>? Prepare(LayoutContext ctx, Paragraph p)
    {
        if (ctx.Fields is not { } env)
        {
            return null;
        }

        int index = env.Model.IndexOf(p);
        if (index >= 0 && !env.Model.ParagraphHasField(index))
        {
            return null;
        }

        var recs = new List<Rec>();
        var stack = new List<Rec>();
        var site = new SiteState(ctx, env, p, index);
        Walk(site, p.Inlines(), recs, stack, 0);

        Dictionary<Run, string>? map = null;
        var claimed = new HashSet<Run>();
        foreach (Rec r in recs)
        {
            if (!r.Closed || r.Value is null || r.Runs.Count == 0 || r.Value == r.Cached.ToString())
            {
                continue;
            }

            if (r.Runs.Any(claimed.Contains))
            {
                continue;
            }

            map ??= [];
            for (int i = 0; i < r.Runs.Count; i++)
            {
                map[r.Runs[i]] = i == 0 ? r.Value : string.Empty;
                claimed.Add(r.Runs[i]);
            }
        }

        return map;
    }

    private sealed record SiteState(LayoutContext Ctx, FieldEnv Env, Paragraph Paragraph, int Index);

    private static void Walk(SiteState s, IEnumerable<InlineElement> inlines, List<Rec> recs, List<Rec> stack, int depth)
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
                    OnRun(s, r, recs, stack);
                    break;
                case SimpleField sf:
                    OnSimple(s, sf, recs);
                    break;
                case InlineContainer c:
                    Walk(s, c.Inlines(), recs, stack, depth + 1);
                    break;
            }
        }
    }

    private static void OnSimple(SiteState s, SimpleField sf, List<Rec> recs)
    {
        var rec = new Rec { Order = recs.Count, Begin = sf.Markup.Node, InResult = true };
        rec.Code.Append(sf.Instruction ?? string.Empty);
        foreach (Run r in sf.DescendantRuns())
        {
            rec.Runs.Add(r);
            rec.Cached.Append(r.Text);
        }

        recs.Add(rec);
        Close(s, rec);
    }

    private static void OnRun(SiteState s, Run run, List<Rec> recs, List<Rec> stack)
    {
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
                        var rec = new Rec { Order = recs.Count, Begin = run.Markup.Node };
                        recs.Add(rec);
                        stack.Add(rec);
                        break;
                    case "separate" when stack.Count > 0:
                        stack[^1].InResult = true;
                        break;
                    case "end" when stack.Count > 0:
                        Rec top = stack[^1];
                        stack.RemoveAt(stack.Count - 1);
                        Close(s, top);
                        if (stack.Count > 0 && !stack[^1].InResult)
                        {
                            // A field inside another field's instruction contributes its result to that instruction (IF { PAGEREF x } = 3 …).
                            stack[^1].Code.Append(top.Value ?? top.Cached.ToString());
                        }

                        break;
                }
            }
            else if (Xml.Eq(n.LocalName, "instrText") && stack.Count > 0 && !stack[^1].InResult)
            {
                stack[^1].Code.Append(n.InnerText);
            }
        }

        if (structural || stack.Count == 0 || !stack[^1].InResult)
        {
            return;
        }

        string text = run.Text;
        foreach (Rec r in stack)
        {
            if (r.InResult)
            {
                r.Runs.Add(run);
                r.Cached.Append(text);
            }
        }
    }

    private static void Close(SiteState s, Rec rec)
    {
        rec.Closed = true;
        FieldInstruction f = FieldInstruction.Parse(rec.Code.ToString());
        if (f.Type.Length == 0)
        {
            return;
        }

        var site = new FieldSite
        {
            Ctx = s.Ctx,
            Env = s.Env,
            Paragraph = s.Paragraph,
            ParagraphIndex = s.Index,
            Page = s.Ctx.PageFields,
            Begin = rec.Begin,
            Info = s.Env.Model.FieldAt(rec.Begin),
            Cached = rec.Cached.ToString(),
        };
        rec.Value = FieldEvaluator.Evaluate(site, f);
    }
}
