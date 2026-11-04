using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Documents.Revisions;

/// <summary>Finds the tracked changes of a part in document order.</summary>
internal static class RevisionScanner
{
    /// <summary>Maximum local-name length that can name a revision element.</summary>
    private const int MaxName = 24;

    internal static void Scan(IRevisionPart part, List<Revision> into, ref int maxId, IDefectSink? defects)
    {
        MarkupNode root = part.Markup.Root;
        if (root.IsNull)
        {
            return;
        }

        var chars = new char[MaxName];
        for (MarkupNode n = root; !n.IsNull; n = WordXml.Next(n, root, true))
        {
            if (n.Kind != MarkupNodeKind.Element || !WordXml.InMain(n))
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

            into.Add(r);
        }
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
