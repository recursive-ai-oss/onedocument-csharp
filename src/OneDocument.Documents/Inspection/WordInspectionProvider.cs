using System.Linq;
using RecursiveAI.OneDocument.Inspection;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Inspection;

/// <summary>
/// Inspects and sanitises what only a word-processing document holds: comments and their ranges, tracked changes (accepted through the
/// document model), headers, footers and watermarks, hidden text, collapsed headings, content-control data bindings, document
/// variables, and fields that pull in other files. Edits are made on the markup of the parts, so untouched parts stay byte-identical.
/// </summary>
internal sealed class WordInspectionProvider : IInspectionProvider
{
    internal static readonly WordInspectionProvider Instance = new();

    private static readonly string[] s_revisionNames =
    [
        "ins", "del", "moveFrom", "moveTo", "rPrChange", "pPrChange", "sectPrChange", "tblPrChange", "tblPrExChange", "tblGridChange",
        "trPrChange", "tcPrChange", "cellIns", "cellDel", "cellMerge", "numberingChange",
    ];

    private static readonly string[] s_linkedFields = ["INCLUDEPICTURE", "INCLUDETEXT", "LINK", "DDE", "DDEAUTO", "IMPORT"];

    public void Inspect(InspectionSession session) => Run(session, null);

    public void Sanitize(SanitizeSession session) => Run(session, session);

    private static bool Doing(SanitizeSession? z, InspectionCategory c) => z is not null && z.Selected(c);

    private static bool IsContent(string ct) =>
        InspectionXml.IsWordMarkupType(ct)
        && (ct.EndsWith("main+xml", StringComparison.OrdinalIgnoreCase) || ct.EndsWith("header+xml", StringComparison.OrdinalIgnoreCase)
            || ct.EndsWith("footer+xml", StringComparison.OrdinalIgnoreCase) || ct.EndsWith("footnotes+xml", StringComparison.OrdinalIgnoreCase)
            || ct.EndsWith("endnotes+xml", StringComparison.OrdinalIgnoreCase) || ct.EndsWith("comments+xml", StringComparison.OrdinalIgnoreCase)
            || ct.EndsWith("glossary+xml", StringComparison.OrdinalIgnoreCase));

    private static void Run(InspectionSession s, SanitizeSession? z)
    {
        if (s.MainPartName is null)
        {
            return;
        }

        Comments(s, z);
        Revisions(s, z);
        HeadersFooters(s, z);
        HiddenText(s, z);
        Collapsed(s, z);
        Bindings(s, z);
        DocumentVariables(s, z);
        LinkedFields(s);
    }

    private static void Comments(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.Comments);
        List<IPackagePart> parts = s.PartsWhere(ct => ct.Contains("wordprocessingml.comments", StringComparison.OrdinalIgnoreCase));
        foreach (IPackagePart p in parts)
        {
            if (!p.ContentType.EndsWith("comments+xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MarkupDocument? doc = s.Markup(p);
            if (doc is null)
            {
                continue;
            }

            var authors = new SortedSet<string>(StringComparer.Ordinal);
            int n = 0;
            foreach (MarkupNode c in InspectionXml.Children(doc.Root))
            {
                if (c.LocalName.SequenceEqual("comment"u8))
                {
                    n++;
                    string? a = InspectionXml.Attr(c, "author"u8);
                    if (!string.IsNullOrEmpty(a))
                    {
                        authors.Add(a);
                    }
                }
            }

            if (n > 0)
            {
                s.Add(InspectionCategory.Comments, p.Name, "comments", n, authors.Count == 0 ? null : "authors: " + string.Join(", ", authors));
            }
        }

        if (!doing)
        {
            return;
        }

        // ECMA-376 Part 1 §17.13.4: a comment is anchored by commentRangeStart/End in the content and a commentReference run; the
        // comment's own text is in the comments part. All three go, then the parts (extended, ids and extensible comments with them).
        foreach (IPackagePart p in s.PartsWhere(IsContent))
        {
            MarkupDocument? doc = z!.Edit(p);
            if (doc is null)
            {
                continue;
            }

            int removed = 0;
            foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
            {
                if (!InspectionXml.IsAttached(e))
                {
                    continue;
                }

                if (InspectionXml.Is(e, "wordprocessingml"u8, "commentRangeStart"u8) || InspectionXml.Is(e, "wordprocessingml"u8, "commentRangeEnd"u8))
                {
                    e.Remove();
                    removed++;
                }
                else if (InspectionXml.Is(e, "wordprocessingml"u8, "commentReference"u8))
                {
                    MarkupNode run = e.Parent;
                    if (run.LocalName.SequenceEqual("r"u8) && OnlyProperties(run, e))
                    {
                        run.Remove();
                    }
                    else
                    {
                        e.Remove();
                    }

                    removed++;
                }
            }

            if (removed > 0)
            {
                z.Removed(InspectionCategory.Comments, p.Name, "comment anchors", removed);
            }
        }

        foreach (IPackagePart p in parts)
        {
            string name = p.Name;
            int n = p.ContentType.EndsWith("comments+xml", StringComparison.OrdinalIgnoreCase) ? Math.Max(1, CountComments(s, p)) : 1;
            z!.RemoveEverywhere(name);
            z.Removed(InspectionCategory.Comments, name, "comments part", n);
        }
    }

    private static int CountComments(InspectionSession s, IPackagePart p) =>
        s.Markup(p) is { } doc ? InspectionXml.Children(doc.Root).Count(c => c.LocalName.SequenceEqual("comment"u8)) : 0;

    private static bool OnlyProperties(MarkupNode run, MarkupNode keep)
    {
        foreach (MarkupNode c in run.ChildElements)
        {
            if (c != keep && !c.LocalName.SequenceEqual("rPr"u8))
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, (int Count, SortedSet<string> Authors)> CountRevisions(InspectionSession s)
    {
        var result = new Dictionary<string, (int, SortedSet<string>)>(StringComparer.OrdinalIgnoreCase);
        foreach (IPackagePart p in s.PartsWhere(IsContent))
        {
            MarkupDocument? doc = s.Markup(p);
            if (doc is null)
            {
                continue;
            }

            int n = 0;
            var authors = new SortedSet<string>(StringComparer.Ordinal);
            foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
            {
                if (e.NamespaceUriBytes.IndexOf("wordprocessingml"u8) < 0 || !IsRevisionName(e))
                {
                    continue;
                }

                n++;
                string? a = InspectionXml.Attr(e, "author"u8);
                if (!string.IsNullOrEmpty(a))
                {
                    authors.Add(a);
                }
            }

            if (n > 0)
            {
                result[p.Name] = (n, authors);
            }
        }

        return result;
    }

    private static bool IsRevisionName(MarkupNode e)
    {
        foreach (string name in s_revisionNames)
        {
            if (e.LocalNameString == name)
            {
                return true;
            }
        }

        return false;
    }

    private static void Revisions(InspectionSession s, SanitizeSession? z)
    {
        var found = CountRevisions(s);
        foreach (KeyValuePair<string, (int Count, SortedSet<string> Authors)> kv in found)
        {
            s.Add(InspectionCategory.Revisions, kv.Key, "tracked changes", kv.Value.Count, kv.Value.Authors.Count == 0 ? null : "authors: " + string.Join(", ", kv.Value.Authors));
        }

        if (!Doing(z, InspectionCategory.Revisions) || found.Count == 0)
        {
            return;
        }

        // Accepting is the safe reading of "remove": the text stays as the author last had it and the history goes. The document model
        // resolves moves, paragraph marks and property changes (docs/revisions.md).
        z!.Flush();
        int accepted;
        using (var doc = Document.FromPackage(z.Package, z.Context))
        {
            accepted = doc.Revisions.AcceptAll();
            doc.CommitChanges();
        }

        foreach (KeyValuePair<string, (int Count, SortedSet<string> Authors)> kv in found)
        {
            z.Removed(InspectionCategory.Revisions, kv.Key, "tracked changes accepted", kv.Value.Count);
        }

        _ = accepted;
        var left = CountRevisions(z);
        foreach (KeyValuePair<string, (int Count, SortedSet<string> Authors)> kv in left)
        {
            z.NotRemoved(InspectionCategory.Revisions, "sanitize.unsupported-construct", kv.Key, kv.Value.Count + " tracked changes could not be accepted (their markup is not what the schema allows).");
        }
    }

    private static bool HasContent(MarkupDocument doc)
    {
        if (doc.Root.InnerText.Trim().Length > 0)
        {
            return true;
        }

        foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
        {
            ReadOnlySpan<byte> n = e.LocalName;
            if (n.SequenceEqual("drawing"u8) || n.SequenceEqual("pict"u8) || n.SequenceEqual("object"u8) || n.SequenceEqual("fldChar"u8) || n.SequenceEqual("fldSimple"u8))
            {
                return true;
            }
        }

        return false;
    }

    private static void HeadersFooters(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.HeadersFooters);
        List<IPackagePart> parts = s.PartsWhere(ct => ct.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase)
            && (ct.EndsWith("header+xml", StringComparison.OrdinalIgnoreCase) || ct.EndsWith("footer+xml", StringComparison.OrdinalIgnoreCase)));
        foreach (IPackagePart p in parts)
        {
            MarkupDocument? doc = s.Markup(p);
            if (doc is null || !HasContent(doc))
            {
                continue;
            }

            bool header = p.ContentType.EndsWith("header+xml", StringComparison.OrdinalIgnoreCase);
            bool watermark = false;
            foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
            {
                string id = InspectionXml.Attr(e, "id"u8) ?? string.Empty;
                if (id.Contains("WaterMark", StringComparison.OrdinalIgnoreCase))
                {
                    watermark = true;
                    break;
                }
            }

            string text = InspectionXml.Brief(doc.Root.InnerText);
            s.Add(InspectionCategory.HeadersFooters, p.Name, (header ? "header" : "footer") + (watermark ? " with a watermark" : string.Empty), 1, text.Length == 0 ? null : text);
        }

        if (!doing || parts.Count == 0)
        {
            return;
        }

        var ids = new List<string>();
        foreach (Relationship r in s.Package.GetRelationships(s.MainPartName))
        {
            IPackagePart? target = s.Target(s.MainPartName, r);
            if (target is not null && parts.Any(p => string.Equals(p.Name, target.Name, StringComparison.OrdinalIgnoreCase)))
            {
                ids.Add(r.Id);
            }
        }

        foreach (IPackagePart p in parts)
        {
            if (s.Markup(p) is { } doc && HasContent(doc))
            {
                z!.Removed(InspectionCategory.HeadersFooters, p.Name, "header or footer");
            }
        }

        z!.Unlink(s.MainPartName, ids);

        // A header or footer that another content part refers to (rare) is removed everywhere, with its references.
        foreach (IPackagePart p in parts)
        {
            if (s.Package.FindPart(p.Name) is not null && z.OwnersOf(p.Name).Count > 0)
            {
                z.RemoveEverywhere(p.Name);
            }
        }
    }

    private static bool IsActive(MarkupNode e)
    {
        string? v = InspectionXml.Attr(e, "val"u8);
        return v is null or "1" or "true" or "on";
    }

    private static void HiddenText(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.HiddenText);
        foreach (IPackagePart p in s.PartsWhere(IsContent))
        {
            if (p.ContentType.EndsWith("comments+xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MarkupDocument? doc = doing ? z!.Edit(p) : s.Markup(p);
            if (doc is null)
            {
                continue;
            }

            var runs = new List<MarkupNode>();
            var marks = new List<MarkupNode>();
            int white = 0;
            foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
            {
                bool vanish = InspectionXml.Is(e, "wordprocessingml"u8, "vanish"u8) || InspectionXml.Is(e, "wordprocessingml"u8, "specVanish"u8);
                if (InspectionXml.Is(e, "wordprocessingml"u8, "color"u8) && string.Equals(InspectionXml.Attr(e, "val"u8), "FFFFFF", StringComparison.OrdinalIgnoreCase))
                {
                    white++;
                }

                if (!vanish || !IsActive(e))
                {
                    continue;
                }

                MarkupNode rPr = e.Parent;
                if (rPr.IsNull || !rPr.LocalName.SequenceEqual("rPr"u8) || !InspectionXml.Ancestor(e, "styles"u8).IsNull)
                {
                    continue;
                }

                MarkupNode host = rPr.Parent;
                if (host.IsNull || host.Kind != MarkupNodeKind.Element)
                {
                    continue;
                }

                if (host.LocalName.SequenceEqual("r"u8) && !runs.Contains(host) && !IsInChange(host))
                {
                    runs.Add(host);
                }
                else if (host.LocalName.SequenceEqual("pPr"u8) && !marks.Contains(e) && !IsInChange(host))
                {
                    marks.Add(e);
                }
            }

            if (white > 0)
            {
                s.Tip(p.Name + ": " + white + " runs have white text; if the page is white they are invisible but are not hidden text, so inspection does not report them.");
            }

            foreach (MarkupNode r in runs)
            {
                s.Add(InspectionCategory.HiddenText, p.Name, "hidden run", 1, InspectionXml.Brief(r.InnerText, 60));
            }

            foreach (MarkupNode m in marks)
            {
                s.Add(InspectionCategory.HiddenText, p.Name, "hidden paragraph mark");
            }

            if (!doing)
            {
                continue;
            }

            int removed = 0;
            foreach (MarkupNode r in runs)
            {
                bool field = false;
                foreach (MarkupNode d in InspectionXml.Descendants(r))
                {
                    ReadOnlySpan<byte> n = d.LocalName;
                    if (n.SequenceEqual("fldChar"u8) || n.SequenceEqual("instrText"u8))
                    {
                        field = true;
                    }
                }

                if (field)
                {
                    z!.NotRemoved(InspectionCategory.HiddenText, "sanitize.unsupported-construct", p.Name, "A hidden run holds a field character; removing it alone would unbalance the field.");
                    continue;
                }

                r.Remove();
                removed++;
            }

            foreach (MarkupNode m in marks)
            {
                MarkupNode rPr = m.Parent;
                m.Remove();
                if (!InspectionXml.HasChildElements(rPr) && InspectionXml.IsAttached(rPr))
                {
                    rPr.Remove();
                }

                removed++;
            }

            if (removed > 0)
            {
                z!.Removed(InspectionCategory.HiddenText, p.Name, "hidden text", removed);
            }
        }
    }

    private static bool IsInChange(MarkupNode e)
    {
        for (MarkupNode a = e.Parent; !a.IsNull; a = a.Parent)
        {
            if (a.Kind == MarkupNodeKind.Element && a.LocalName.EndsWith("Change"u8))
            {
                return true;
            }
        }

        return false;
    }

    // [MS-DOCX] 2.6.2.1 collapsed: a paragraph property that folds the content under a heading.
    private static void Collapsed(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.CollapsedHeadings);
        MarkupDocument? doc = s.Markup(s.MainPartName!);
        if (doc is null)
        {
            return;
        }

        var hits = new List<MarkupNode>();
        foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
        {
            if (e.LocalName.SequenceEqual("collapsed"u8) && e.NamespaceUriBytes.IndexOf("word/2012/wordml"u8) >= 0 && IsActive(e))
            {
                hits.Add(e);
            }
        }

        if (hits.Count == 0)
        {
            return;
        }

        s.Add(InspectionCategory.CollapsedHeadings, s.MainPartName!, "collapsed headings", hits.Count);
        if (doing)
        {
            z!.Edit(s.Package.FindPart(s.MainPartName!)!);
            foreach (MarkupNode h in hits)
            {
                h.Remove();
            }

            z.Removed(InspectionCategory.CollapsedHeadings, s.MainPartName!, "collapsed-heading marks", hits.Count);
        }
    }

    private static void Bindings(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.CustomXml);
        foreach (IPackagePart p in s.PartsWhere(IsContent))
        {
            MarkupDocument? doc = doing ? z!.Edit(p) : s.Markup(p);
            if (doc is null)
            {
                continue;
            }

            var hits = new List<MarkupNode>();
            foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
            {
                if (e.LocalName.SequenceEqual("dataBinding"u8) && e.Parent.LocalName.SequenceEqual("sdtPr"u8))
                {
                    hits.Add(e);
                }
            }

            if (hits.Count == 0)
            {
                continue;
            }

            s.Add(InspectionCategory.CustomXml, p.Name, "content-control data bindings", hits.Count, InspectionXml.Brief(InspectionXml.Attr(hits[0], "xpath"u8) ?? string.Empty));
            if (doing)
            {
                foreach (MarkupNode h in hits)
                {
                    h.Remove();
                }

                z!.Removed(InspectionCategory.CustomXml, p.Name, "content-control data bindings", hits.Count);
            }
        }
    }

    // ECMA-376 Part 1 §17.15.1.32 docVars: name/value pairs stored in the settings, read by DOCVARIABLE fields.
    private static void DocumentVariables(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.Properties);
        foreach (IPackagePart p in s.PartsWhere(ct => ct.EndsWith("settings+xml", StringComparison.OrdinalIgnoreCase) && ct.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase)))
        {
            MarkupDocument? doc = doing ? z!.Edit(p) : s.Markup(p);
            if (doc is null)
            {
                continue;
            }

            foreach (MarkupNode vars in InspectionXml.Children(doc.Root))
            {
                if (!vars.LocalName.SequenceEqual("docVars"u8))
                {
                    continue;
                }

                int n = InspectionXml.Children(vars).Count;
                if (n == 0)
                {
                    continue;
                }

                s.Add(InspectionCategory.Properties, p.Name + "!docVars", "document variables", n);
                if (doing)
                {
                    vars.Remove();
                    z!.Removed(InspectionCategory.Properties, p.Name + "!docVars", "document variables", n);
                }
            }
        }
    }

    private static void LinkedFields(InspectionSession s)
    {
        foreach (IPackagePart p in s.PartsWhere(IsContent))
        {
            MarkupDocument? doc = s.Markup(p);
            if (doc is null)
            {
                continue;
            }

            foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
            {
                string? instr = null;
                if (e.LocalName.SequenceEqual("instrText"u8))
                {
                    instr = e.InnerText;
                }
                else if (e.LocalName.SequenceEqual("fldSimple"u8))
                {
                    instr = InspectionXml.Attr(e, "instr"u8);
                }

                if (instr is null)
                {
                    continue;
                }

                string word = instr.AsSpan().Trim().ToString();
                int space = word.IndexOf(' ', StringComparison.Ordinal);
                string name = (space < 0 ? word : word[..space]).ToUpperInvariant();
                if (Array.IndexOf(s_linkedFields, name) >= 0)
                {
                    s.Add(InspectionCategory.LinkedContent, p.Name, name + " field", 1, InspectionXml.Brief(instr, 120));
                }
            }
        }
    }
}
