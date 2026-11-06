using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Fields;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

// Finds the result runs of cross-reference fields (REF, PAGEREF, NOTEREF) in one paragraph so the painter can tag them as references.
// ECMA-376 Part 1 §17.16.18 (fldChar): begin, separate and end bracket the instruction and the result; the result is the runs between
// separate and end. Nested fields are tracked with a stack; a result run counts when any enclosing field is a cross-reference.
// Simple fields (fldSimple) are not recognised here: their runs are collected without the wrapper.
internal static class CrossReferenceScanner
{
    public static HashSet<Run> ResultRuns(List<(Run? Run, string? Link, OtherInline? Math)> runs)
    {
        var result = new HashSet<Run>();
        var stack = new List<State>();
        foreach ((Run? run, _, _) in runs)
        {
            if (run is null)
            {
                continue;
            }

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
                            stack.Add(new State());
                            break;
                        case "separate" when stack.Count > 0:
                            stack[^1].InResult = true;
                            stack[^1].Reference = IsReference(stack[^1].Code.ToString());
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

            if (structural)
            {
                continue;
            }

            foreach (State s in stack)
            {
                if (s.InResult && s.Reference)
                {
                    result.Add(run);
                    break;
                }
            }
        }

        return result;
    }

    private static bool IsReference(string code) => FieldInstruction.Parse(code).Type is "REF" or "PAGEREF" or "NOTEREF";

    private sealed class State
    {
        public readonly System.Text.StringBuilder Code = new();
        public bool InResult;
        public bool Reference;
    }
}
