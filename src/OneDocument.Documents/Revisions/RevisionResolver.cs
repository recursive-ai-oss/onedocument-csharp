using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Documents.Revisions;

/// <summary>
/// Accepts and rejects tracked changes by editing the markup tree. Revisions of one part are resolved from the end of the part
/// towards its start, so an element that is rewritten (the content of a <c>w:ins</c> moves out of it) is never one that a
/// revision still to be resolved points at.
/// </summary>
internal sealed class RevisionResolver
{
    private readonly IDefectSink? _defects;

    internal RevisionResolver(IDefectSink? defects) => _defects = defects;

    /// <summary>Resolves the revisions, which must be in document order, last to first. Returns how many were resolved.</summary>
    internal int ResolveBackwards(IRevisionPart part, IReadOnlyList<Revision> inDocumentOrder, bool accept)
    {
        int done = 0;
        for (int i = inDocumentOrder.Count - 1; i >= 0; i--)
        {
            Revision r = inDocumentOrder[i];
            try
            {
                MarkupNode parent = r.Scope == RevisionScope.Content ? r.Element.Parent : default;
                Resolve(r, accept);
                PruneEmptyWrappers(parent);
                done++;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                _defects?.Report(new Defect(DefectKind.Structure, "docx.revision-unresolvable", DefectSeverity.Dropped,
                    $"A tracked change ({r.Kind}) could not be {(accept ? "accepted" : "rejected")} and was left in place: {ex.Message}",
                    DefectLocation.InPart(part.Uri, r.Offset)));
            }
        }

        return done;
    }

    /// <summary>
    /// Removes the grouping wrappers (<c>w:smartTag</c>, <c>w:customXml</c>, <c>w:bdo</c>, <c>w:dir</c>) that resolving a change left with no
    /// content, and the ones that emptying those leaves empty in turn. Observed in Word 365 (16.0.20527): rejecting the only insertion
    /// in, or accepting the only deletion in, a smart tag or custom XML element removes the element, while an emptied content control
    /// (<c>w:sdt</c>) and an emptied <c>w:hyperlink</c> stay; a simple field is rewritten as a complex field. Those stay here too.
    /// </summary>
    private static void PruneEmptyWrappers(MarkupNode wrapper)
    {
        while (!wrapper.IsNull && WordXml.IsAny(wrapper, "smartTag", "customXml", "bdo", "dir") && !WrapperHasContent(wrapper))
        {
            MarkupNode up = wrapper.Parent;
            wrapper.Remove();
            wrapper = up;
        }
    }

    private static bool WrapperHasContent(MarkupNode container)
    {
        foreach (MarkupNode c in container.Children)
        {
            if (c.Kind == MarkupNodeKind.Element && !WordXml.Is(c, "smartTagPr") && !WordXml.Is(c, "customXmlPr"))
            {
                return true;
            }

            if (c.Kind is MarkupNodeKind.Text or MarkupNodeKind.CData && c.InnerText.AsSpan().Trim().Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void Resolve(Revision r, bool accept)
    {
        MarkupNode el = r.Element;
        switch (r.Kind)
        {
            // ECMA-376 Part 1 §17.13.5.18, §17.13.5.25: accepting an insertion keeps the content and drops the wrapper; rejecting drops both.
            case RevisionKind.Insertion:
            case RevisionKind.MoveTo:
                if (r.Kind == RevisionKind.MoveTo)
                {
                    RemoveMoveMarkers(el, "moveToRange");
                }

                if (accept)
                {
                    Unwrap(el, renameDeleted: false);
                }
                else
                {
                    el.Remove();
                }

                break;

            // §17.13.5.14, §17.13.5.22: accepting a deletion drops the content; rejecting keeps it, and its w:delText becomes w:t again.
            case RevisionKind.Deletion:
            case RevisionKind.MoveFrom:
                if (r.Kind == RevisionKind.MoveFrom)
                {
                    RemoveMoveMarkers(el, "moveFromRange");
                }

                if (accept)
                {
                    el.Remove();
                }
                else
                {
                    Unwrap(el, renameDeleted: true);
                }

                break;

            // §17.13.5.16, §17.13.5.15: a deleted (or rejected-inserted) paragraph mark joins the paragraph to the next one.
            case RevisionKind.ParagraphMarkInserted:
            case RevisionKind.ParagraphMarkMovedTo:
                ResolveParagraphMark(el, markGoes: !accept, inserted: true);
                break;
            case RevisionKind.ParagraphMarkDeleted:
            case RevisionKind.ParagraphMarkMovedFrom:
                ResolveParagraphMark(el, markGoes: accept, inserted: false);
                break;

            case RevisionKind.RunPropertiesChange:
            case RevisionKind.ParagraphPropertiesChange:
            case RevisionKind.SectionPropertiesChange:
            case RevisionKind.TablePropertiesChange:
            case RevisionKind.TablePropertyExceptionsChange:
            case RevisionKind.TableGridChange:
            case RevisionKind.TableRowPropertiesChange:
            case RevisionKind.TableCellPropertiesChange:
                if (accept)
                {
                    RemoveMarker(el);
                }
                else
                {
                    RestoreProperties(el, r.Kind);
                }

                break;

            // §17.13.5.17, §17.13.5.13: row revisions.
            case RevisionKind.TableRowInserted:
                if (accept)
                {
                    RemoveMarker(el);
                }
                else
                {
                    RemoveRow(WordXml.Ancestor(el, "tr"));
                }

                break;
            case RevisionKind.TableRowDeleted:
                if (accept)
                {
                    RemoveRow(WordXml.Ancestor(el, "tr"));
                }
                else
                {
                    RemoveMarker(el);
                }

                break;

            // §17.13.5.4, §17.13.5.2: cell revisions.
            case RevisionKind.CellInserted:
                if (accept)
                {
                    RemoveMarker(el);
                }
                else
                {
                    RemoveCell(WordXml.Ancestor(el, "tc"));
                }

                break;
            case RevisionKind.CellDeleted:
                if (accept)
                {
                    RemoveCell(WordXml.Ancestor(el, "tc"));
                }
                else
                {
                    RemoveMarker(el);
                }

                break;
            case RevisionKind.CellMerged:
                ResolveCellMerge(el, accept);
                break;

            // §17.13.5.26: the change records only a description of the old numbering, so both directions drop the record and keep the numbering.
            case RevisionKind.NumberingChange:
                RemoveMarker(el);
                break;
            // Observed in Word 365 (16.0.20527): rejecting an inserted numbering keeps w:numPr and removes only the marker.
            case RevisionKind.NumberingInserted:
                RemoveMarker(el);
                break;
            default:
                throw new InvalidOperationException("Unknown revision kind.");
        }
    }

    /// <summary>Removes range markers that belong to resolved revisions but have no revision element of their own.</summary>
    internal static void SweepRangeMarkers(MarkupNode root)
    {
        var doomed = new List<MarkupNode>();
        for (MarkupNode n = root; !n.IsNull; n = WordXml.Next(n, root, true))
        {
            if (n.Kind != MarkupNodeKind.Element || !WordXml.InMain(n))
            {
                continue;
            }

            if (WordXml.IsAny(n, "moveFromRangeStart", "moveFromRangeEnd", "moveToRangeStart", "moveToRangeEnd",
                "customXmlInsRangeStart", "customXmlInsRangeEnd", "customXmlDelRangeStart", "customXmlDelRangeEnd",
                "customXmlMoveFromRangeStart", "customXmlMoveFromRangeEnd", "customXmlMoveToRangeStart", "customXmlMoveToRangeEnd"))
            {
                doomed.Add(n);
            }
        }

        foreach (MarkupNode n in doomed)
        {
            n.Remove();
        }
    }

    // §17.13.5.22/25: the range markers bracket the moved content; they go with it.
    private static void RemoveMoveMarkers(MarkupNode el, string prefix)
    {
        MarkupNode start = WordXml.PreviousElement(el);
        if (start.IsNull || !WordXml.Is(start, prefix + "Start"))
        {
            return;
        }

        string? id = WordXml.Attr(start, "id");
        for (MarkupNode s = WordXml.NextElement(el); !s.IsNull; s = WordXml.NextElement(s))
        {
            if (WordXml.Is(s, prefix + "End") && WordXml.Attr(s, "id") == id)
            {
                s.Remove();
                break;
            }
        }

        start.Remove();
    }

    private static void Unwrap(MarkupNode el, bool renameDeleted)
    {
        if (renameDeleted)
        {
            var targets = new List<MarkupNode>();
            for (MarkupNode n = el.FirstChild; !n.IsNull; n = WordXml.Next(n, el, true))
            {
                if (WordXml.IsAny(n, "delText", "delInstrText"))
                {
                    targets.Add(n);
                }
            }

            foreach (MarkupNode t in targets)
            {
                RenameDeleted(t);
            }
        }

        string inner = WordXml.InnerXml(el);
        if (inner.Length == 0)
        {
            el.Remove();
        }
        else
        {
            el.ReplaceWithXml(WordXml.Bytes(inner));
        }
    }

    // §17.13.5.12 / §17.3.3.1: w:delText and w:delInstrText are the deleted forms of w:t and w:instrText.
    private static void RenameDeleted(MarkupNode t)
    {
        string oldRaw = t.Name;
        string newLocal = WordXml.SpanEquals(t.LocalName, "delText") ? "t" : "instrText";
        string prefix = t.Prefix.Length == 0 ? string.Empty : t.Name[..^t.LocalNameString.Length];
        string newRaw = prefix + newLocal;
        string xml = t.OuterXml;
        xml = "<" + newRaw + xml[(1 + oldRaw.Length)..];

        // XML 1.0 §3.1: an end tag may have white space between its name and ">", so find the tag by its name and not by its exact text.
        int end = xml.LastIndexOf("</", StringComparison.Ordinal);
        if (end >= 0 && string.CompareOrdinal(xml, end + 2, oldRaw, 0, oldRaw.Length) == 0)
        {
            xml = xml[..(end + 2)] + newRaw + xml[(end + 2 + oldRaw.Length)..];
        }

        t.ReplaceWithXml(WordXml.Bytes(xml));
    }

    private static void RemoveMarker(MarkupNode el)
    {
        MarkupNode parent = el.Parent;
        el.Remove();
        CleanupEmpty(parent);
    }

    /// <summary>Drops a property container that has nothing left in it, and then the container around it if that is empty too.</summary>
    private static void CleanupEmpty(MarkupNode container)
    {
        while (!container.IsNull && WordXml.IsAny(container, "rPr", "pPr", "trPr", "tcPr") && !HasContent(container))
        {
            MarkupNode parent = container.Parent;
            if (parent.IsNull || !WordXml.IsAny(parent, "p", "r", "tr", "tc", "pPr"))
            {
                return;
            }

            container.Remove();
            container = parent;
        }
    }

    private static bool HasContent(MarkupNode container)
    {
        foreach (MarkupNode c in container.Children)
        {
            if (c.Kind == MarkupNodeKind.Element || (c.Kind is MarkupNodeKind.Text or MarkupNodeKind.CData && c.InnerText.AsSpan().Trim().Length > 0))
            {
                return true;
            }
        }

        return container.Attributes.GetEnumerator().MoveNext();
    }

    private static void ResolveParagraphMark(MarkupNode marker, bool markGoes, bool inserted)
    {
        MarkupNode rPr = marker.Parent;
        MarkupNode pPr = rPr.Parent;
        MarkupNode p = pPr.Parent;
        if (pPr.IsNull || p.IsNull || !WordXml.Is(p, "p"))
        {
            // A mark outside a paragraph (the math control properties): only the record goes.
            RemoveMarker(marker);
            return;
        }

        bool hasSection = !WordXml.Child(pPr, "sectPr").IsNull;
        RemoveMarker(marker);
        if (!markGoes || hasSection)
        {
            return;
        }

        MarkupNode next = WordXml.NextElement(p);
        if (!next.IsNull && WordXml.Is(next, "p"))
        {
            // §17.3.1.26: the paragraph mark carries the paragraph's properties, so the following paragraph's mark survives and
            // its properties apply to the joined paragraph.
            string content = ParagraphContent(p);
            if (content.Length > 0)
            {
                MarkupNode nextPPr = WordXml.Child(next, "pPr");
                if (nextPPr.IsNull)
                {
                    next.PrependXml(WordXml.Bytes(content));
                }
                else
                {
                    nextPPr.InsertXmlAfter(WordXml.Bytes(content));
                }
            }

            p.Remove();
        }
        else if (inserted && ParagraphContent(p).Length == 0 && WordXml.Is(WordXml.PreviousElement(p), "p"))
        {
            // The last paragraph of a container cannot be joined to a following one; an emptied inserted paragraph just goes.
            p.Remove();
        }
    }

    /// <summary>The paragraph's XML after its properties, or empty when it holds only white space.</summary>
    private static string ParagraphContent(MarkupNode p)
    {
        var sb = new StringBuilder();
        bool any = false;
        foreach (MarkupNode c in p.Children)
        {
            if (WordXml.Is(c, "pPr"))
            {
                continue;
            }

            if (c.Kind == MarkupNodeKind.Element)
            {
                any = true;
            }
            else if (c.Kind is MarkupNodeKind.Text or MarkupNodeKind.CData && c.InnerText.AsSpan().Trim().Length > 0)
            {
                any = true;
            }

            sb.Append(WordXml.Portable(c));
        }

        return any ? sb.ToString() : string.Empty;
    }

    private static void RestoreProperties(MarkupNode change, RevisionKind kind)
    {
        MarkupNode container = change.Parent;
        MarkupNode old = default;
        foreach (MarkupNode c in change.ChildElements)
        {
            old = c;
            break;
        }

        string[] leading = kind switch
        {
            RevisionKind.RunPropertiesChange => ["ins", "del", "moveFrom", "moveTo"],
            RevisionKind.SectionPropertiesChange => ["headerReference", "footerReference"],
            _ => [],
        };
        string[] trailing = kind switch
        {
            RevisionKind.ParagraphPropertiesChange => ["rPr", "sectPr"],
            RevisionKind.TableRowPropertiesChange => ["ins", "del"],
            RevisionKind.TableCellPropertiesChange => ["cellIns", "cellDel", "cellMerge"],
            _ => [],
        };

        // The marks that stay (a paragraph mark's w:ins/w:del, a row's w:ins/w:del, ...) are other revisions that may still be waiting
        // to be resolved, so their nodes are kept as they are rather than copied: only the rest of the container is replaced.
        var head = new List<MarkupNode>();
        var tail = new List<MarkupNode>();
        var drop = new List<MarkupNode>();
        for (MarkupNode c = container.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c == change || c.Kind != MarkupNodeKind.Element)
            {
                drop.Add(c);
            }
            else if (WordXml.IsAny(c, leading))
            {
                head.Add(c);
            }
            else if (WordXml.IsAny(c, trailing))
            {
                tail.Add(c);
            }
            else
            {
                drop.Add(c);
            }
        }

        string oldXml = old.IsNull ? string.Empty : WordXml.ElementsXml(old);
        foreach (MarkupNode c in drop)
        {
            c.Remove();
        }

        if (oldXml.Length > 0)
        {
            if (tail.Count > 0)
            {
                tail[0].InsertXmlBefore(WordXml.Bytes(oldXml));
            }
            else if (head.Count > 0)
            {
                head[^1].InsertXmlAfter(WordXml.Bytes(oldXml));
            }
            else
            {
                container.AppendXml(WordXml.Bytes(oldXml));
            }
        }

        CleanupEmpty(container);
    }

    private static void RemoveRow(MarkupNode row)
    {
        if (row.IsNull)
        {
            throw new InvalidOperationException("The row revision is not inside a table row.");
        }

        MarkupNode table = row.Parent;
        row.Remove();
        if (!table.IsNull && WordXml.Is(table, "tbl") && WordXml.Child(table, "tr").IsNull)
        {
            MarkupNode holder = table.Parent;
            table.Remove();
            if (!holder.IsNull && WordXml.Is(holder, "tc") && WordXml.Child(holder, "p").IsNull)
            {
                // §17.4.66: a cell must end with a paragraph.
                holder.AppendXml(WordXml.Bytes(WmlWriter.For(holder).Empty("p")));
            }
        }
    }

    private static void RemoveCell(MarkupNode cell)
    {
        if (cell.IsNull)
        {
            throw new InvalidOperationException("The cell revision is not inside a table cell.");
        }

        MarkupNode row = cell.Parent;
        cell.Remove();
        if (!row.IsNull && WordXml.Is(row, "tr") && WordXml.Child(row, "tc").IsNull)
        {
            RemoveRow(row);
        }
    }

    // §17.13.5.3: w:cellMerge records the vertical merge the cell has now (vMerge) and had before (vMergeOrig).
    private static void ResolveCellMerge(MarkupNode el, bool accept)
    {
        MarkupNode tcPr = el.Parent;
        string? state = null;
        foreach (MarkupAttributeRef a in el.Attributes)
        {
            if (!a.IsNamespaceDeclaration && WordXml.SpanEquals(a.LocalName, accept ? "vMerge" : "vMergeOrig"))
            {
                state = a.Value;
            }
        }

        MarkupNode existing = WordXml.Child(tcPr, "vMerge");
        string? fragment = state switch
        {
            "rest" => WmlWriter.For(tcPr).Empty("vMerge", WmlWriter.For(tcPr).Attr("val", "restart")),
            "cont" => WmlWriter.For(tcPr).Empty("vMerge"),
            _ => null,
        };
        if (!existing.IsNull)
        {
            if (fragment is null)
            {
                existing.Remove();
            }
            else
            {
                existing.ReplaceWithXml(WordXml.Bytes(fragment));
            }
        }
        else if (fragment is not null)
        {
            el.InsertXmlBefore(WordXml.Bytes(fragment));
        }

        el.Remove();
        CleanupEmpty(tcPr);
    }
}
