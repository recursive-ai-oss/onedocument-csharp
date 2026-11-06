using System.Linq;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>
/// Removes from a document what its content no longer uses: styles, list definitions, notes, comments and the related parts
/// (pictures, headers, links) nothing refers to. Used by split, so that every piece is a standalone file with only what it needs.
/// </summary>
internal static class DocumentPruner
{
    private static readonly HashSet<string> PrunableRelationships = new(StringComparer.Ordinal)
    {
        "image", "hyperlink", "header", "footer", "oleObject", "chart", "chartUserShapes", "diagramData", "diagramLayout", "diagramQuickStyle",
        "diagramColors", "diagramDrawing", "package", "audio", "video", "control", "themeOverride",
    };

    public static void Prune(Document d, MergeReport report)
    {
        d.CommitChanges();
        var roots = new List<(string Name, MarkupDocument Doc)> { (d.MainPartName, d.MainMarkup) };
        PruneNotes(d, "footnotes", "footnote", "footnoteReference");
        PruneNotes(d, "endnotes", "endnote", "endnoteReference");
        PruneComments(d);
        roots.AddRange(OpsXml.StoryParts(d));

        PruneStylesAndNumbering(d, roots, report);
        PruneRelationships(d, roots);
        int removed = OpsXml.RemoveUnreachableParts(d.Package);
        report.Count("parts-removed", removed);
        d.InvalidateAfterOperation();
    }

    // ---- notes and comments -------------------------------------------------------------------------------------------------------

    private static void PruneNotes(Document d, string relationship, string element, string reference)
    {
        MarkupDocument? doc = d.LoadRelatedByType(d.OfficeRelationship(relationship), out _);
        if (doc is null)
        {
            return;
        }

        var used = new HashSet<long>();
        foreach (MarkupNode n in OpsXml.Elements(d.MainMarkup.Root))
        {
            if (OpsXml.W(n) == reference && Wml.Long(DocumentMerger.WAttr(n, "id")) is { } id)
            {
                used.Add(id);
            }
        }

        foreach (MarkupNode note in OpsXml.Elements(doc.Root).Where(n => OpsXml.W(n) == element && n.Parent == doc.Root).ToList())
        {
            string? type = DocumentMerger.WAttr(note, "type");
            bool normal = type is null or "normal";
            if (normal && Wml.Long(DocumentMerger.WAttr(note, "id")) is { } id && !used.Contains(id))
            {
                note.Remove();
            }
        }
    }

    private static void PruneComments(Document d)
    {
        MarkupDocument? doc = d.LoadRelatedByType(d.OfficeRelationship("comments"), out _);
        if (doc is null)
        {
            return;
        }

        var used = new HashSet<long>();
        foreach (MarkupNode n in OpsXml.Elements(d.MainMarkup.Root))
        {
            if (OpsXml.W(n) is "commentReference" or "commentRangeStart" or "commentRangeEnd" && Wml.Long(DocumentMerger.WAttr(n, "id")) is { } id)
            {
                used.Add(id);
            }
        }

        var keptParaIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int kept = 0;
        foreach (MarkupNode c in OpsXml.Elements(doc.Root).Where(n => OpsXml.W(n) == "comment" && n.Parent == doc.Root).ToList())
        {
            if (Wml.Long(DocumentMerger.WAttr(c, "id")) is { } id && used.Contains(id))
            {
                kept++;
                foreach (MarkupNode p in OpsXml.Elements(c))
                {
                    if (p.GetAttribute(DocumentMerger.W14Ns, "paraId") is { } pid)
                    {
                        keptParaIds.Add(pid);
                    }
                }
            }
            else
            {
                c.Remove();
            }
        }

        // The modern parts list comments by the paragraph id of their last paragraph; entries of removed comments go too.
        foreach (Relationship r in d.Package.GetRelationships(d.MainPartName).ToList())
        {
            bool modern = r.Type.EndsWith("/commentsExtended", StringComparison.Ordinal) || r.Type.EndsWith("/commentsIds", StringComparison.Ordinal);
            if (modern && PartUri.TryResolve(new PartUri(d.MainPartName), r.Target, out PartUri uri) && d.Parts.Load(uri.Value, required: false) is { } ext)
            {
                foreach (MarkupNode e in ext.Root.ChildElements.ToList())
                {
                    string? pid = e.Attributes_ParaId();
                    if (pid is not null && !keptParaIds.Contains(pid))
                    {
                        e.Remove();
                    }
                }
            }
        }

        if (kept == 0)
        {
            RemoveRelated(d, r => r.Type.EndsWith("/comments", StringComparison.Ordinal) || r.Type.EndsWith("/commentsExtended", StringComparison.Ordinal)
                || r.Type.EndsWith("/commentsIds", StringComparison.Ordinal) || r.Type.EndsWith("/commentsExtensible", StringComparison.Ordinal)
                || r.Type.EndsWith("/people", StringComparison.Ordinal));
        }
    }

    private static string? Attributes_ParaId(this MarkupNode e)
    {
        foreach (MarkupAttributeRef a in e.Attributes)
        {
            if (!a.IsNamespaceDeclaration && Wml.AsciiEquals(a.LocalName, "paraId"))
            {
                return a.Value;
            }
        }

        return null;
    }

    private static void RemoveRelated(Document d, Func<Relationship, bool> match)
    {
        List<Relationship> rels = [.. d.Package.GetRelationships(d.MainPartName)];
        rels.RemoveAll(r => match(r));
        d.Package.SetRelationships(d.MainPartName, rels);
    }

    // ---- styles and numbering -----------------------------------------------------------------------------------------------------

    private static void PruneStylesAndNumbering(Document d, List<(string Name, MarkupDocument Doc)> roots, MergeReport report)
    {
        if (!d.Styles.Markup.IsValid && !d.Numbering.IsPresent)
        {
            return;
        }

        var styles = new HashSet<string>(StringComparer.Ordinal);
        var nums = new HashSet<string>(StringComparer.Ordinal);
        var instr = new System.Text.StringBuilder();
        foreach ((_, MarkupDocument doc) in roots)
        {
            foreach (MarkupNode n in OpsXml.Elements(doc.Root))
            {
                switch (OpsXml.W(n))
                {
                    case "pStyle" or "rStyle" or "tblStyle":
                        if (DocumentMerger.WAttr(n, "val") is { } s)
                        {
                            styles.Add(s);
                        }

                        break;
                    case "numId":
                        if (DocumentMerger.WAttr(n, "val") is { } nm)
                        {
                            nums.Add(nm);
                        }

                        break;
                    case "instrText":
                        instr.Append(n.InnerText).Append(' ');
                        break;
                    case "fldSimple":
                        instr.Append(DocumentMerger.WAttr(n, "instr")).Append(' ');
                        break;
                }
            }
        }

        string codes = instr.ToString();
        bool hasToc = codes.Contains("TOC", StringComparison.OrdinalIgnoreCase);
        if (d.Styles.Markup.IsValid)
        {
            foreach (Styles.StyleDefinition st in d.Styles.Styles)
            {
                string name = st.Name ?? st.Id;
                if (st.IsDefault || (codes.Length > 0 && codes.Contains(name, StringComparison.OrdinalIgnoreCase))
                    || (hasToc && name.StartsWith("heading ", StringComparison.OrdinalIgnoreCase)))
                {
                    styles.Add(st.Id);
                }
            }
        }

        // Fixpoint: a kept style brings its parent, successor, linked style and list; a kept list brings the styles its levels name.
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (string id in styles.ToList())
            {
                if (d.Styles.Find(id) is not { } st)
                {
                    continue;
                }

                foreach (string? other in new[] { st.BasedOnId, st.NextId, st.LinkId })
                {
                    if (other is not null && styles.Add(other))
                    {
                        grew = true;
                    }
                }

                foreach (MarkupNode n in OpsXml.Elements(st.Markup.Node))
                {
                    if (OpsXml.W(n) == "numId" && DocumentMerger.WAttr(n, "val") is { } nm && nums.Add(nm))
                    {
                        grew = true;
                    }
                }
            }

            foreach (string numId in nums.ToList())
            {
                if (Wml.Long(numId) is not { } id || d.Numbering.FindInstance(id) is not { } inst || d.Numbering.FindAbstract(inst.AbstractNumberingId) is not { } abs)
                {
                    continue;
                }

                foreach (MarkupNode n in OpsXml.Elements(abs.Markup.Node))
                {
                    if (OpsXml.W(n) == "pStyle" && DocumentMerger.WAttr(n, "val") is { } ps && styles.Add(ps))
                    {
                        grew = true;
                    }
                }
            }
        }

        long removedStyles = 0;
        if (d.Styles.Markup.IsValid)
        {
            foreach (Styles.StyleDefinition st in d.Styles.Styles.ToList())
            {
                if (!styles.Contains(st.Id))
                {
                    st.Markup.Node.Remove();
                    removedStyles++;
                }
            }

            d.Styles.Invalidate();
        }

        report.Count("styles-removed", removedStyles);
        PruneNumbering(d, nums, report);
    }

    private static void PruneNumbering(Document d, HashSet<string> nums, MergeReport report)
    {
        if (!d.Numbering.IsPresent)
        {
            return;
        }

        var keepAbstract = new HashSet<long>();
        foreach (Numbering.NumberingInstance inst in d.Numbering.Instances)
        {
            if (nums.Contains(DocumentMerger.Num(inst.Id)))
            {
                keepAbstract.Add(inst.AbstractNumberingId);
            }
        }

        // A list that takes its levels from a numbering style (w:numStyleLink) needs the definition that style names (w:styleLink).
        foreach (Numbering.AbstractNumberingDefinition a in d.Numbering.AbstractDefinitions)
        {
            if (keepAbstract.Contains(a.Id) && a.NumberingStyleLink is { } link)
            {
                foreach (Numbering.AbstractNumberingDefinition other in d.Numbering.AbstractDefinitions)
                {
                    if (other.StyleLink == link)
                    {
                        keepAbstract.Add(other.Id);
                    }
                }
            }
        }

        long removed = 0;
        foreach (Numbering.NumberingInstance inst in d.Numbering.Instances.ToList())
        {
            if (!nums.Contains(DocumentMerger.Num(inst.Id)))
            {
                inst.Markup.Node.Remove();
                removed++;
            }
        }

        foreach (Numbering.AbstractNumberingDefinition a in d.Numbering.AbstractDefinitions.ToList())
        {
            if (!keepAbstract.Contains(a.Id))
            {
                a.Markup.Node.Remove();
                removed++;
            }
        }

        report.Count("lists-removed", removed);
        if (d.Numbering.Instances.Count == 0 && d.Numbering.AbstractDefinitions.Count == 0)
        {
            RemoveRelated(d, r => RelationshipTypes.Is(r.Type, "numbering"));
        }
    }

    // ---- relationships --------------------------------------------------------------------------------------------------------

    private static void PruneRelationships(Document d, List<(string Name, MarkupDocument Doc)> roots)
    {
        foreach ((string name, MarkupDocument doc) in roots)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (MarkupNode n in OpsXml.Elements(doc.Root))
            {
                foreach (MarkupAttributeRef a in n.Attributes)
                {
                    if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0 && a.NamespaceUri is "http://schemas.openxmlformats.org/officeDocument/2006/relationships" or "http://purl.oclc.org/ooxml/officeDocument/relationships")
                    {
                        used.Add(a.Value);
                    }
                }
            }

            List<Relationship> rels = [.. d.Package.GetRelationships(name)];
            int before = rels.Count;
            rels.RemoveAll(r => !used.Contains(r.Id) && PrunableRelationships.Contains(r.Type[(r.Type.LastIndexOf('/') + 1)..]));
            if (rels.Count != before)
            {
                d.Package.SetRelationships(name, rels);
            }
        }
    }
}
