using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Documents.Revisions;

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
                Resolve(r, accept);
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
                    RemoveRow(Wml.Ancestor(el, "tr"));
                }

                break;
            case RevisionKind.TableRowDeleted:
                if (accept)
                {
                    RemoveRow(Wml.Ancestor(el, "tr"));
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
                    RemoveCell(Wml.Ancestor(el, "tc"));
                }

                break;
            case RevisionKind.CellDeleted:
                if (accept)
                {
                    RemoveCell(Wml.Ancestor(el, "tc"));
                }
                else
                {
                    RemoveMarker(el);
                }

                break;
            case RevisionKind.CellMerged:
                ResolveCellMerge(el, accept);
                break;

            // §17.13.5.26: the change records only a description of the old numbering, so both directions drop the record.
            case RevisionKind.NumberingChange:
                RemoveMarker(el);
                break;
            case RevisionKind.NumberingInserted:
                if (accept || el.Parent.IsNull)
                {
                    RemoveMarker(el);
                }
                else
                {
                    MarkupNode numPr = el.Parent;
                    MarkupNode pPr = numPr.Parent;
                    numPr.Remove();
                    CleanupEmpty(pPr);
                }

                break;
            default:
                throw new InvalidOperationException("Unknown revision kind.");
        }
    }

    /// <summary>Removes range markers that belong to resolved revisions but have no revision element of their own.</summary>
    internal static void SweepRangeMarkers(MarkupNode root)
    {
        var doomed = new List<MarkupNode>();
        for (MarkupNode n = root; !n.IsNull; n = Wml.Next(n, root, true))
        {
            if (n.Kind != MarkupNodeKind.Element || !Wml.InMain(n))
            {
                continue;
            }

            if (Wml.IsAny(n, "moveFromRangeStart", "moveFromRangeEnd", "moveToRangeStart", "moveToRangeEnd",
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
        MarkupNode start = Wml.PreviousElement(el);
        if (start.IsNull || !Wml.Is(start, prefix + "Start"))
        {
            return;
        }

        string? id = Wml.Attr(start, "id");
        for (MarkupNode s = Wml.NextElement(el); !s.IsNull; s = Wml.NextElement(s))
        {
            if (Wml.Is(s, prefix + "End") && Wml.Attr(s, "id") == id)
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
            for (MarkupNode n = el.FirstChild; !n.IsNull; n = Wml.Next(n, el, true))
            {
                if (Wml.IsAny(n, "delText", "delInstrText"))
                {
                    targets.Add(n);
                }
            }

            foreach (MarkupNode t in targets)
            {
                RenameDeleted(t);
            }
        }

        string inner = Wml.InnerXml(el);
        if (inner.Length == 0)
        {
            el.Remove();
        }
        else
        {
            el.ReplaceWithXml(Wml.Bytes(inner));
        }
    }

    // §17.13.5.12 / §17.3.3.1: w:delText and w:delInstrText are the deleted forms of w:t and w:instrText.
    private static void RenameDeleted(MarkupNode t)
    {
        string oldRaw = t.Name;
        string newLocal = Wml.SpanEquals(t.LocalName, "delText") ? "t" : "instrText";
        string prefix = t.Prefix.Length == 0 ? string.Empty : t.Name[..^t.LocalNameString.Length];
        string newRaw = prefix + newLocal;
        string xml = t.OuterXml;
        string endTag = "</" + oldRaw + ">";
        xml = "<" + newRaw + xml[(1 + oldRaw.Length)..];
        if (xml.EndsWith(endTag, StringComparison.Ordinal))
        {
            xml = xml[..^endTag.Length] + "</" + newRaw + ">";
        }

        t.ReplaceWithXml(Wml.Bytes(xml));
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
        while (!container.IsNull && Wml.IsAny(container, "rPr", "pPr", "trPr", "tcPr") && !HasContent(container))
        {
            MarkupNode parent = container.Parent;
            if (parent.IsNull || !Wml.IsAny(parent, "p", "r", "tr", "tc", "pPr"))
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
        if (pPr.IsNull || p.IsNull || !Wml.Is(p, "p"))
        {
            // A mark outside a paragraph (the math control properties): only the record goes.
            RemoveMarker(marker);
            return;
        }

        bool hasSection = !Wml.Child(pPr, "sectPr").IsNull;
        RemoveMarker(marker);
        if (!markGoes || hasSection)
        {
            return;
        }

        MarkupNode next = Wml.NextElement(p);
        if (!next.IsNull && Wml.Is(next, "p"))
        {
            // §17.3.1.26: the paragraph mark carries the paragraph's properties, so the following paragraph's mark survives and
            // its properties apply to the joined paragraph.
            string content = ParagraphContent(p);
            if (content.Length > 0)
            {
                MarkupNode nextPPr = Wml.Child(next, "pPr");
                if (nextPPr.IsNull)
                {
                    next.PrependXml(Wml.Bytes(content));
                }
                else
                {
                    nextPPr.InsertXmlAfter(Wml.Bytes(content));
                }
            }

            p.Remove();
        }
        else if (inserted && ParagraphContent(p).Length == 0 && Wml.Is(Wml.PreviousElement(p), "p"))
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
            if (Wml.Is(c, "pPr"))
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

            sb.Append(c.OuterXml);
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

        var head = new StringBuilder();
        var tail = new StringBuilder();
        foreach (MarkupNode c in container.ChildElements)
        {
            if (c == change)
            {
                continue;
            }

            if (Wml.IsAny(c, leading))
            {
                head.Append(c.OuterXml);
            }
            else if (Wml.IsAny(c, trailing))
            {
                tail.Append(c.OuterXml);
            }
        }

        string oldXml = old.IsNull ? string.Empty : Wml.ElementsXml(old);
        Wml.SetContent(container, head.ToString() + oldXml + tail);
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
        if (!table.IsNull && Wml.Is(table, "tbl") && Wml.Child(table, "tr").IsNull)
        {
            MarkupNode holder = table.Parent;
            table.Remove();
            if (!holder.IsNull && Wml.Is(holder, "tc") && Wml.Child(holder, "p").IsNull)
            {
                // §17.4.66: a cell must end with a paragraph.
                holder.AppendXml(Wml.Bytes(WmlWriter.For(holder).Empty("p")));
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
        if (!row.IsNull && Wml.Is(row, "tr") && Wml.Child(row, "tc").IsNull)
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
            if (!a.IsNamespaceDeclaration && Wml.SpanEquals(a.LocalName, accept ? "vMerge" : "vMergeOrig"))
            {
                state = a.Value;
            }
        }

        MarkupNode existing = Wml.Child(tcPr, "vMerge");
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
                existing.ReplaceWithXml(Wml.Bytes(fragment));
            }
        }
        else if (fragment is not null)
        {
            el.InsertXmlBefore(Wml.Bytes(fragment));
        }

        el.Remove();
        CleanupEmpty(tcPr);
    }
}
