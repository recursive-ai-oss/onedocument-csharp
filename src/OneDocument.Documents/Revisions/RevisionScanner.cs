using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Documents.Revisions;

/// <summary>Finds the tracked changes of a part in document order.</summary>
internal static class RevisionScanner
{
    /// <summary>Maximum local-name length that can name a revision element.</summary>
    private const int MaxName = 24;

    /// <summary>
    /// Finds the revisions of a part. Revisions inside a branch of <c>mc:AlternateContent</c> that a reader does not take
    /// (ECMA-376 Part 3 §9.3: the first understood <c>mc:Choice</c>, else <c>mc:Fallback</c>) repeat the exposed branch, so they go to
    /// <paramref name="copies"/> and are attached to the revision they copy.
    /// </summary>
    internal static void Scan(IRevisionPart part, List<Revision> into, List<Revision> copies, ref int maxId, IDefectSink? defects)
    {
        MarkupNode root = part.Markup.Root;
        if (root.IsNull)
        {
            return;
        }

        int ordinal = into.Count + copies.Count;
        int firstCopy = copies.Count;
        int firstInto = into.Count;
        var chars = new char[MaxName];
        var skipped = new HashSet<MarkupNode>();
        for (MarkupNode n = root; !n.IsNull; n = WordXml.Next(n, root, true))
        {
            if (n.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            bool inCopy = false;
            for (MarkupNode a = n; !a.IsNull && !inCopy; a = a.Parent)
            {
                inCopy = skipped.Contains(a);
            }

            if (!inCopy && IsUnexposedBranch(n))
            {
                skipped.Add(n);
                inCopy = true;
            }

            if (!WordXml.InMain(n))
            {
                continue;
            }

            ReadOnlySpan<byte> name = n.LocalName;
            if (name.Length > MaxName)
            {
                continue;
            }

            for (int i = 0; i < name.Length; i++)
            {
                chars[i] = (char)name[i];
            }

            ReadOnlySpan<char> local = chars.AsSpan(0, name.Length);
            if (inCopy)
            {
                if (Classify(local, n, out RevisionKind ck, out RevisionScope cs))
                {
                    int cid = int.TryParse(WordXml.Attr(n, "id"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsed) ? parsed : -1;
                    var c = new Revision(part, n, ck, cs, cid, WordXml.Attr(n, "author") ?? string.Empty, Revision.ParseDate(WordXml.Attr(n, "date"))) { IsCopy = true, Ordinal = ordinal++ };
                    if (cid > maxId)
                    {
                        maxId = cid;
                    }

                    if (ck is RevisionKind.MoveFrom or RevisionKind.MoveTo)
                    {
                        c.MoveName = MoveNameOf(n, ck == RevisionKind.MoveFrom ? "moveFromRangeStart" : "moveToRangeStart");
                    }

                    copies.Add(c);
                }
                else if (local is "bookmarkStart" or "commentRangeStart" or "moveFromRangeStart" or "moveToRangeStart" or "permStart")
                {
                    NoteId(n, ref maxId);
                }

                continue;
            }

            if (local is "bookmarkStart" or "commentRangeStart" or "moveFromRangeStart" or "moveToRangeStart" or "permStart")
            {
                NoteId(n, ref maxId);
                continue;
            }

            if (!Classify(local, n, out RevisionKind kind, out RevisionScope scope))
            {
                continue;
            }

            string? idText = WordXml.Attr(n, "id");
            int id = -1;
            if (idText is null || !int.TryParse(idText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out id))
            {
                id = -1;
                Report(defects, part, n, "docx.revision-id-invalid", "A tracked change has no usable w:id.");
            }
            else if (id > maxId)
            {
                maxId = id;
            }

            string? author = WordXml.Attr(n, "author");
            if (author is null)
            {
                author = string.Empty;
                Report(defects, part, n, "docx.revision-author-missing", "A tracked change has no w:author; it is treated as written by nobody.");
            }

            string? dateText = WordXml.Attr(n, "date");
            DateTimeOffset? date = Revision.ParseDate(dateText);
            if (dateText is not null && date is null)
            {
                Report(defects, part, n, "docx.revision-date-invalid", "A tracked change has a w:date that is not a date; it is treated as undated.");
            }

            var r = new Revision(part, n, kind, scope, id, author, date);
            if (kind is RevisionKind.MoveFrom or RevisionKind.MoveTo)
            {
                r.MoveName = MoveNameOf(n, kind == RevisionKind.MoveFrom ? "moveFromRangeStart" : "moveToRangeStart");
            }

            r.Ordinal = ordinal++;
            into.Add(r);
        }

        Pair(into, firstInto, copies, firstCopy);
    }

    /// <summary>True for an <c>mc:Choice</c> or <c>mc:Fallback</c> that is not the branch a reader takes.</summary>
    private static bool IsUnexposedBranch(MarkupNode n)
    {
        if (!MarkupCompatibility.IsChoice(n) && !MarkupCompatibility.IsFallback(n))
        {
            return false;
        }

        MarkupNode ac = n.Parent;
        return !ac.IsNull && MarkupCompatibility.IsAlternateContent(ac) && MarkupCompatibility.SelectAlternateContent(ac, TextBoxScanner.IsUnderstood) != n;
    }

    /// <summary>
    /// Links each copy to the revision it repeats: inside the same <c>mc:AlternateContent</c>, the n-th revision of a kind in the
    /// exposed branch is the one the n-th of that kind in the other branch repeats (the same pairing text boxes use).
    /// </summary>
    private static void Pair(List<Revision> into, int firstInto, List<Revision> copies, int firstCopy)
    {
        if (copies.Count == firstCopy)
        {
            return;
        }

        // Per alternate-content block, the exposed revisions in document order.
        var exposed = new Dictionary<MarkupNode, List<Revision>>();
        for (int i = firstCopy; i < copies.Count; i++)
        {
            Revision c = copies[i];
            MarkupNode branch = default;
            for (MarkupNode a = c.Element; !a.IsNull; a = a.Parent)
            {
                if (IsUnexposedBranch(a))
                {
                    branch = a; // keep climbing: the outermost unexposed branch pairs with the exposed content around it
                }
            }

            if (branch.IsNull)
            {
                continue;
            }

            MarkupNode ac = branch.Parent;
            if (!exposed.TryGetValue(ac, out List<Revision>? list))
            {
                MarkupNode primary = MarkupCompatibility.SelectAlternateContent(ac, TextBoxScanner.IsUnderstood);
                list = [];
                for (int k = firstInto; k < into.Count; k++)
                {
                    for (MarkupNode a = into[k].Element.Parent; !a.IsNull; a = a.Parent)
                    {
                        if (a == primary)
                        {
                            list.Add(into[k]);
                            break;
                        }
                    }
                }

                exposed[ac] = list;
            }

            int index = 0;
            for (int k = firstCopy; k < i; k++)
            {
                if (copies[k].Kind == c.Kind && Under(copies[k].Element, branch))
                {
                    index++;
                }
            }

            int seen = 0;
            foreach (Revision r in list)
            {
                if (r.Kind == c.Kind && seen++ == index)
                {
                    (r.Copies ??= []).Add(c);
                    break;
                }
            }
        }
    }

    private static bool Under(MarkupNode n, MarkupNode ancestor)
    {
        for (MarkupNode a = n; !a.IsNull; a = a.Parent)
        {
            if (a == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private static void NoteId(MarkupNode n, ref int maxId)
    {
        string? t = WordXml.Attr(n, "id");
        if (t is not null && int.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int v) && v > maxId)
        {
            maxId = v;
        }
    }

    private static string? MoveNameOf(MarkupNode n, string startMarker)
    {
        MarkupNode prev = WordXml.PreviousElement(n);
        return !prev.IsNull && WordXml.Is(prev, startMarker) ? WordXml.Attr(prev, "name") : null;
    }

    private static void Report(IDefectSink? sink, IRevisionPart part, MarkupNode n, string code, string message) =>
        sink?.Report(new Defect(DefectKind.Value, code, DefectSeverity.Repaired, message, DefectLocation.InPart(part.Uri, n.SourceStart)));

    /// <summary>Decides whether an element is a tracked change and what kind. Marker elements are told apart by their parent.</summary>
    internal static bool Classify(ReadOnlySpan<char> local, MarkupNode n, out RevisionKind kind, out RevisionScope scope)
    {
        kind = default;
        scope = RevisionScope.Content;
        switch (local)
        {
            case "ins":
            case "del":
            case "moveFrom":
            case "moveTo":
                {
                    bool ins = local is "ins";
                    bool del = local is "del";
                    bool from = local is "moveFrom";
                    MarkupNode parent = n.Parent;
                    if (!parent.IsNull && (WordXml.Is(parent, "rPr") || parent.LocalNameString == "ctrlPr"))
                    {
                        scope = RevisionScope.ParagraphMark;
                        kind = ins ? RevisionKind.ParagraphMarkInserted
                            : del ? RevisionKind.ParagraphMarkDeleted
                            : from ? RevisionKind.ParagraphMarkMovedFrom : RevisionKind.ParagraphMarkMovedTo;
                        return true;
                    }

                    if (!parent.IsNull && WordXml.Is(parent, "trPr") && (ins || del))
                    {
                        scope = RevisionScope.Table;
                        kind = ins ? RevisionKind.TableRowInserted : RevisionKind.TableRowDeleted;
                        return true;
                    }

                    if (!parent.IsNull && WordXml.Is(parent, "numPr") && ins)
                    {
                        scope = RevisionScope.Numbering;
                        kind = RevisionKind.NumberingInserted;
                        return true;
                    }

                    kind = ins ? RevisionKind.Insertion : del ? RevisionKind.Deletion : from ? RevisionKind.MoveFrom : RevisionKind.MoveTo;
                    return true;
                }

            case "rPrChange": return Formatting(RevisionKind.RunPropertiesChange, out kind, out scope);
            case "pPrChange": return Formatting(RevisionKind.ParagraphPropertiesChange, out kind, out scope);
            case "sectPrChange": return Formatting(RevisionKind.SectionPropertiesChange, out kind, out scope);
            case "tblPrChange": return Formatting(RevisionKind.TablePropertiesChange, out kind, out scope);
            case "tblPrExChange": return Formatting(RevisionKind.TablePropertyExceptionsChange, out kind, out scope);
            case "tblGridChange": return Formatting(RevisionKind.TableGridChange, out kind, out scope);
            case "trPrChange": return Formatting(RevisionKind.TableRowPropertiesChange, out kind, out scope);
            case "tcPrChange": return Formatting(RevisionKind.TableCellPropertiesChange, out kind, out scope);
            case "cellIns": return Table(RevisionKind.CellInserted, out kind, out scope);
            case "cellDel": return Table(RevisionKind.CellDeleted, out kind, out scope);
            case "cellMerge": return Table(RevisionKind.CellMerged, out kind, out scope);
            case "numberingChange":
                kind = RevisionKind.NumberingChange;
                scope = RevisionScope.Numbering;
                return true;
            default:
                return false;
        }
    }

    private static bool Formatting(RevisionKind k, out RevisionKind kind, out RevisionScope scope)
    {
        kind = k;
        scope = RevisionScope.Formatting;
        return true;
    }

    private static bool Table(RevisionKind k, out RevisionKind kind, out RevisionScope scope)
    {
        kind = k;
        scope = RevisionScope.Table;
        return true;
    }
}
