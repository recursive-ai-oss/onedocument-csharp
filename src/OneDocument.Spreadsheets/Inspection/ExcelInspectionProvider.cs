using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RecursiveAI.OneDocument.Inspection;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Inspection;

/// <summary>
/// Inspects and sanitises what only a workbook holds: notes and threaded comments, the change history of a shared workbook, hidden rows,
/// columns, sheets and names, page-setup headers and footers, data connections, external workbook links, macro sheets, file-sharing
/// user names and the stored absolute path. Edits are made on the markup of the parts, so untouched parts stay byte-identical.
/// Removing a sheet or a name only happens when nothing else refers to it (see docs/sanitize.md).
/// </summary>
internal sealed partial class ExcelInspectionProvider : IInspectionProvider
{
    internal static readonly ExcelInspectionProvider Instance = new();

    public void Inspect(InspectionSession session) => Run(session, null);

    public void Sanitize(SanitizeSession session) => Run(session, session);

    [GeneratedRegex(@"\[\d+\]", RegexOptions.CultureInvariant)]
    private static partial Regex ExternalReference();

    private static bool Doing(SanitizeSession? z, InspectionCategory c) => z is not null && z.Selected(c);

    private sealed record SheetEntry(MarkupNode Element, string Name, string State, string? Id, IPackagePart? Part, int Index, bool IsMacro);

    private static void Run(InspectionSession s, SanitizeSession? z)
    {
        if (s.MainPartName is null)
        {
            return;
        }

        if (s.Format == OfficeFormat.Xlsb)
        {
            s.Note("Binary workbook: only the categories shared by every package are inspected.");
            return;
        }

        IPackagePart? main = s.Package.FindPart(s.MainPartName);
        MarkupDocument? wb = main is null ? null : s.Markup(main);
        if (main is null || wb is null)
        {
            return;
        }

        List<SheetEntry> sheets = ReadSheets(s, main, wb);
        Comments(s, z, sheets);
        Revisions(s, z);
        PersonalInformation(s, z, main, wb);
        WorkbookProperties(s, z, main, wb, sheets);
        HeadersFooters(s, z, sheets);
        bool changed = HiddenRowsColumns(s, z, sheets);
        DataConnections(s, z);
        changed |= ExternalLinks(s, z, main, wb, sheets);

        // Sheet and name removal comes last: it needs the other edits flushed so that the reference check sees the final formulas.
        changed |= HiddenSheetsAndNames(s, z, main, sheets);
        if (z is not null && changed)
        {
            TidyAfterRemoval(z);
        }
    }

    // Removing cells, formulas or sheets leaves two things behind that still describe them: the calculation chain lists cells that no
    // longer hold a formula (Excel repairs that on open and rebuilds the chain when it is absent), and the shared string table keeps
    // every string it ever held. Strings no cell uses any more are emptied in place, so that the indexes of the others stay valid.
    private static void TidyAfterRemoval(SanitizeSession z)
    {
        z.Flush();
        foreach (IPackagePart chain in z.PartsWhere(ct => ct.EndsWith("calcChain+xml", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            z.RemoveEverywhere(chain.Name);
        }

        var used = new HashSet<int>();
        foreach (IPackagePart p in z.Package.Parts.ToList())
        {
            if (!(p.ContentType.Contains("worksheet+xml", StringComparison.OrdinalIgnoreCase) || p.ContentType.Contains("macrosheet", StringComparison.OrdinalIgnoreCase)
                || p.ContentType.Contains("dialogsheet", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (z.Markup(p) is not { } doc)
            {
                return; // a sheet that cannot be read may use any string, so none is emptied
            }

            foreach (MarkupNode c in InspectionXml.Descendants(doc.DocumentNode))
            {
                if (c.LocalName.SequenceEqual("c"u8) && InspectionXml.Attr(c, "t"u8) == "s")
                {
                    foreach (MarkupNode v in InspectionXml.Children(c))
                    {
                        if (v.LocalName.SequenceEqual("v"u8) && int.TryParse(v.InnerText, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                        {
                            used.Add(index);
                        }
                    }
                }
            }
        }

        foreach (IPackagePart sst in z.PartsWhere(ct => ct.EndsWith("spreadsheetml.sharedStrings+xml", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (z.Edit(sst) is not { } doc)
            {
                continue;
            }

            int i = 0;
            int emptied = 0;
            foreach (MarkupNode si in InspectionXml.Children(doc.Root))
            {
                if (si.LocalName.SequenceEqual("si"u8))
                {
                    if (!used.Contains(i) && (InspectionXml.HasChildElements(si) || si.InnerText.Length > 0))
                    {
                        si.SetText(string.Empty);
                        emptied++;
                    }

                    i++;
                }
            }

            if (emptied > 0)
            {
                z.Removed(InspectionCategory.HiddenRowsColumns, sst.Name, "strings that no cell uses any more emptied in the shared string table", emptied);
            }
        }

        z.Flush();
    }

    private static List<SheetEntry> ReadSheets(InspectionSession s, IPackagePart main, MarkupDocument wb)
    {
        var list = new List<SheetEntry>();
        foreach (MarkupNode sheets in InspectionXml.Children(wb.Root))
        {
            if (!sheets.LocalName.SequenceEqual("sheets"u8))
            {
                continue;
            }

            int i = 0;
            foreach (MarkupNode e in InspectionXml.Children(sheets))
            {
                string name = InspectionXml.Attr(e, "name"u8) ?? string.Empty;
                string state = InspectionXml.Attr(e, "state"u8) ?? "visible";
                string? rid = InspectionXml.Attr(e, "id"u8);
                IPackagePart? part = null;
                foreach (Relationship r in s.Package.GetRelationships(main.Name))
                {
                    if (r.Id == rid)
                    {
                        part = s.Target(main.Name, r);
                    }
                }

                bool macro = part?.ContentType.Contains("macrosheet", StringComparison.OrdinalIgnoreCase) == true;
                list.Add(new SheetEntry(e, name, state, rid, part, i++, macro));
            }
        }

        return list;
    }

    private static bool IsWorksheet(IPackagePart? p) =>
        p is not null && p.ContentType.Contains("worksheet+xml", StringComparison.OrdinalIgnoreCase);

    private static void Comments(InspectionSession s, SanitizeSession? z, List<SheetEntry> sheets)
    {
        bool doing = Doing(z, InspectionCategory.Comments);
        foreach (SheetEntry sh in sheets)
        {
            if (sh.Part is null)
            {
                continue;
            }

            var ids = new List<string>();
            foreach (Relationship r in s.Package.GetRelationships(sh.Part.Name))
            {
                IPackagePart? t = s.Target(sh.Part.Name, r);
                if (t is null)
                {
                    continue;
                }

                if (t.ContentType.EndsWith("spreadsheetml.comments+xml", StringComparison.OrdinalIgnoreCase))
                {
                    MarkupDocument? doc = s.Markup(t);
                    int n = 0;
                    var authors = new SortedSet<string>(StringComparer.Ordinal);
                    if (doc is not null)
                    {
                        foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
                        {
                            if (e.LocalName.SequenceEqual("comment"u8))
                            {
                                n++;
                            }
                            else if (e.LocalName.SequenceEqual("author"u8) && e.Parent.LocalName.SequenceEqual("authors"u8) && e.InnerText.Length > 0)
                            {
                                authors.Add(e.InnerText);
                            }
                        }
                    }

                    s.Add(InspectionCategory.Comments, sh.Name + "!" + t.Name, "notes", Math.Max(1, n), authors.Count == 0 ? null : "authors: " + string.Join(", ", authors));
                    ids.Add(r.Id);
                    if (doing)
                    {
                        z!.Removed(InspectionCategory.Comments, sh.Name + "!" + t.Name, "notes", Math.Max(1, n));
                    }
                }
                else if (t.ContentType.Contains("threadedcomments", StringComparison.OrdinalIgnoreCase))
                {
                    MarkupDocument? doc = s.Markup(t);
                    int n = doc is null ? 1 : Math.Max(1, InspectionXml.Children(doc.Root).Count);
                    s.Add(InspectionCategory.Comments, sh.Name + "!" + t.Name, "threaded comments", n);
                    ids.Add(r.Id);
                    if (doing)
                    {
                        z!.Removed(InspectionCategory.Comments, sh.Name + "!" + t.Name, "threaded comments", n);
                    }
                }
            }

            if (doing && ids.Count > 0)
            {
                z!.Unlink(sh.Part.Name, ids);
                RemoveNoteShapes(s, z, sh);
            }
        }
    }

    // The legacy drawing of a sheet draws the note boxes; it may also hold form controls. Note shapes (ClientData ObjectType="Note") go;
    // the drawing goes with the last of them.
    private static void RemoveNoteShapes(InspectionSession s, SanitizeSession z, SheetEntry sh)
    {
        foreach (Relationship r in z.Package.GetRelationships(sh.Part!.Name).Where(r => r.Type.EndsWith("/vmlDrawing", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            IPackagePart? vml = s.Target(sh.Part.Name, r);
            if (vml is null)
            {
                continue;
            }

            MarkupDocument? doc = z.Edit(vml);
            if (doc is null)
            {
                z.NotRemoved(InspectionCategory.Comments, "sanitize.unsupported-construct", vml.Name, "The drawing that holds the note boxes is not well-formed XML, so the boxes could not be taken out.");
                continue;
            }

            int notes = 0;
            int others = 0;
            foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
            {
                if (!e.LocalName.SequenceEqual("shape"u8))
                {
                    continue;
                }

                bool isNote = false;
                foreach (MarkupNode c in InspectionXml.Children(e))
                {
                    if (c.LocalName.SequenceEqual("ClientData"u8) && InspectionXml.Attr(c, "ObjectType"u8) == "Note")
                    {
                        isNote = true;
                    }
                }

                if (isNote)
                {
                    e.Remove();
                    notes++;
                }
                else
                {
                    others++;
                }
            }

            if (others == 0)
            {
                z.Unlink(sh.Part.Name, [r.Id]);
            }
        }
    }

    private static void Revisions(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.Revisions);
        foreach (IPackagePart p in s.PartsWhere(ct => ct.Contains("spreadsheetml.revision", StringComparison.OrdinalIgnoreCase) || ct.EndsWith("spreadsheetml.userNames+xml", StringComparison.OrdinalIgnoreCase)))
        {
            MarkupDocument? doc = s.Markup(p);
            int n = doc is null ? 1 : Math.Max(1, InspectionXml.Children(doc.Root).Count);
            s.Add(InspectionCategory.Revisions, p.Name, p.ContentType.Contains("userNames", StringComparison.OrdinalIgnoreCase) ? "shared-workbook user names" : "shared-workbook change history", n);
        }

        if (!doing)
        {
            return;
        }

        foreach (string name in s.PartsWhere(ct => ct.Contains("spreadsheetml.revision", StringComparison.OrdinalIgnoreCase) || ct.EndsWith("spreadsheetml.userNames+xml", StringComparison.OrdinalIgnoreCase)).Select(p => p.Name).ToList())
        {
            z!.RemoveEverywhere(name);
            z.Removed(InspectionCategory.Revisions, name, "shared-workbook change history");
        }
    }

    private static void PersonalInformation(InspectionSession s, SanitizeSession? z, IPackagePart main, MarkupDocument wb)
    {
        bool doing = Doing(z, InspectionCategory.PersonalInformation);
        if (doing)
        {
            z!.Edit(main);
        }

        foreach (MarkupNode e in InspectionXml.Descendants(wb.DocumentNode))
        {
            if (e.LocalName.SequenceEqual("fileSharing"u8) && !string.IsNullOrEmpty(InspectionXml.Attr(e, "userName"u8)))
            {
                s.Add(InspectionCategory.PersonalInformation, main.Name + "!fileSharing", "file-sharing user name", 1, InspectionXml.Attr(e, "userName"u8));
                if (doing)
                {
                    e.RemoveAttribute("userName");
                    z!.Removed(InspectionCategory.PersonalInformation, main.Name + "!fileSharing", "file-sharing user name");
                }
            }
            else if (e.LocalName.SequenceEqual("absPath"u8) && InspectionXml.IsAttached(e))
            {
                s.Add(InspectionCategory.PersonalInformation, main.Name + "!absPath", "absolute path of the last save", 1, InspectionXml.Brief(InspectionXml.Attr(e, "url"u8) ?? string.Empty, 120));
                if (doing)
                {
                    InspectionXml.RemoveWithWrappers(e);
                    z!.Removed(InspectionCategory.PersonalInformation, main.Name + "!absPath", "absolute path of the last save");
                }
            }
        }
    }

    // The Properties group of Excel's inspector also covers comments on defined names and tables, scenario authors and comments, and
    // the file paths used to publish pages.
    private static void WorkbookProperties(InspectionSession s, SanitizeSession? z, IPackagePart main, MarkupDocument wb, List<SheetEntry> sheets)
    {
        bool doing = Doing(z, InspectionCategory.Properties);
        if (doing)
        {
            z!.Edit(main);
        }

        foreach (MarkupNode e in InspectionXml.Descendants(wb.DocumentNode))
        {
            if (e.LocalName.SequenceEqual("definedName"u8) && !string.IsNullOrEmpty(InspectionXml.Attr(e, "comment"u8)))
            {
                s.Add(InspectionCategory.Properties, main.Name + "!" + (InspectionXml.Attr(e, "name"u8) ?? "name"), "comment on a defined name", 1, InspectionXml.Brief(InspectionXml.Attr(e, "comment"u8)!));
                if (doing)
                {
                    e.RemoveAttribute("comment");
                    z!.Removed(InspectionCategory.Properties, main.Name, "comment on a defined name");
                }
            }
            else if (e.LocalName.SequenceEqual("webPublishObjects"u8) && InspectionXml.IsAttached(e))
            {
                s.Add(InspectionCategory.Properties, main.Name + "!webPublishObjects", "file paths for publishing web pages", Math.Max(1, InspectionXml.Children(e).Count));
                if (doing)
                {
                    e.Remove();
                    z!.Removed(InspectionCategory.Properties, main.Name, "file paths for publishing web pages");
                }
            }
        }

        foreach (SheetEntry sh in sheets)
        {
            if (sh.Part is null)
            {
                continue;
            }

            if (IsWorksheet(sh.Part) && (doing ? z!.Edit(sh.Part) : s.Markup(sh.Part)) is { } doc)
            {
                foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
                {
                    if (e.LocalName.SequenceEqual("scenario"u8) && (InspectionXml.Attr(e, "user"u8) is not null || InspectionXml.Attr(e, "comment"u8) is not null))
                    {
                        s.Add(InspectionCategory.Properties, sh.Name + "!scenario", "scenario author and comment", 1, InspectionXml.Brief((InspectionXml.Attr(e, "user"u8) ?? string.Empty) + " " + (InspectionXml.Attr(e, "comment"u8) ?? string.Empty)));
                        if (doing)
                        {
                            e.RemoveAttribute("user");
                            e.RemoveAttribute("comment");
                            z!.Removed(InspectionCategory.Properties, sh.Name, "scenario author and comment");
                        }
                    }
                }
            }

            foreach (IPackagePart t in s.TargetsOf(sh.Part.Name, "/table"))
            {
                if ((doing ? z!.Edit(t) : s.Markup(t)) is { } td && !string.IsNullOrEmpty(InspectionXml.Attr(td.Root, "comment"u8)))
                {
                    s.Add(InspectionCategory.Properties, t.Name, "comment on a table", 1, InspectionXml.Brief(InspectionXml.Attr(td.Root, "comment"u8)!));
                    if (doing)
                    {
                        td.Root.RemoveAttribute("comment");
                        z!.Removed(InspectionCategory.Properties, t.Name, "comment on a table");
                    }
                }
            }
        }
    }

    private static void HeadersFooters(InspectionSession s, SanitizeSession? z, List<SheetEntry> sheets)
    {
        bool doing = Doing(z, InspectionCategory.HeadersFooters);
        foreach (SheetEntry sh in sheets)
        {
            if (sh.Part is null || !(IsWorksheet(sh.Part) || sh.Part.ContentType.Contains("chartsheet", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            MarkupDocument? doc = doing ? z!.Edit(sh.Part) : s.Markup(sh.Part);
            if (doc is null)
            {
                continue;
            }

            foreach (MarkupNode e in InspectionXml.Children(doc.Root))
            {
                if (e.LocalName.SequenceEqual("headerFooter"u8) && e.InnerText.Trim().Length > 0)
                {
                    s.Add(InspectionCategory.HeadersFooters, sh.Name + "!headerFooter", "page-setup header or footer", 1, InspectionXml.Brief(e.InnerText));
                    if (doing)
                    {
                        e.Remove();
                        z!.Removed(InspectionCategory.HeadersFooters, sh.Name + "!headerFooter", "page-setup header or footer");
                    }
                }
                else if (e.LocalName.SequenceEqual("legacyDrawingHF"u8))
                {
                    s.Add(InspectionCategory.HeadersFooters, sh.Name + "!legacyDrawingHF", "pictures in the header or footer");
                    if (doing)
                    {
                        string? rid = InspectionXml.Attr(e, "id"u8);
                        if (rid is not null)
                        {
                            z!.Unlink(sh.Part.Name, [rid]);
                        }

                        z!.Removed(InspectionCategory.HeadersFooters, sh.Name + "!legacyDrawingHF", "pictures in the header or footer");
                    }
                }
            }
        }
    }

    private static int ColumnIndex(string letters)
    {
        int n = 0;
        foreach (char c in letters)
        {
            if (c is >= 'A' and <= 'Z')
            {
                n = (n * 26) + (c - 'A' + 1);
            }
            else if (c is >= 'a' and <= 'z')
            {
                n = (n * 26) + (c - 'a' + 1);
            }
            else
            {
                break;
            }
        }

        return n;
    }

    private static string ColumnLetters(int index)
    {
        var sb = new StringBuilder();
        for (int n = index; n > 0; n = (n - 1) / 26)
        {
            sb.Insert(0, (char)('A' + ((n - 1) % 26)));
        }

        return sb.ToString();
    }

    private static bool HiddenRowsColumns(InspectionSession s, SanitizeSession? z, List<SheetEntry> sheets)
    {
        bool doing = Doing(z, InspectionCategory.HiddenRowsColumns);
        bool changed = false;
        foreach (SheetEntry sh in sheets)
        {
            if (!IsWorksheet(sh.Part))
            {
                continue;
            }

            MarkupDocument? doc = doing ? z!.Edit(sh.Part!) : s.Markup(sh.Part!);
            if (doc is null)
            {
                continue;
            }

            var rowEls = new List<MarkupNode>();
            var colEls = new List<MarkupNode>();
            var colRanges = new List<(int Min, int Max, MarkupNode Element)>();
            MarkupNode data = default;
            foreach (MarkupNode e in InspectionXml.Children(doc.Root))
            {
                if (e.LocalName.SequenceEqual("sheetData"u8))
                {
                    data = e;
                }
                else if (e.LocalName.SequenceEqual("cols"u8))
                {
                    foreach (MarkupNode c in InspectionXml.Children(e))
                    {
                        if (c.LocalName.SequenceEqual("col"u8) && InspectionXml.AttrTrue(c, "hidden"u8)
                            && int.TryParse(InspectionXml.Attr(c, "min"u8), NumberStyles.None, CultureInfo.InvariantCulture, out int mn)
                            && int.TryParse(InspectionXml.Attr(c, "max"u8), NumberStyles.None, CultureInfo.InvariantCulture, out int mx))
                        {
                            colEls.Add(c);
                            colRanges.Add((mn, Math.Min(mx, 16384), c));
                        }
                    }
                }
            }

            var rowNumbers = new List<int>();
            if (!data.IsNull)
            {
                foreach (MarkupNode r in InspectionXml.Children(data))
                {
                    if (r.LocalName.SequenceEqual("row"u8) && InspectionXml.AttrTrue(r, "hidden"u8))
                    {
                        rowEls.Add(r);
                        _ = int.TryParse(InspectionXml.Attr(r, "r"u8), NumberStyles.None, CultureInfo.InvariantCulture, out int rn);
                        rowNumbers.Add(rn);
                    }
                }
            }

            if (rowEls.Count > 0)
            {
                s.Add(InspectionCategory.HiddenRowsColumns, sh.Name + "!rows", "hidden rows", rowEls.Count, Ranges(rowNumbers, n => n.ToString(CultureInfo.InvariantCulture)));
            }

            if (colEls.Count > 0)
            {
                int total = colRanges.Sum(c => c.Max - c.Min + 1);
                s.Add(InspectionCategory.HiddenRowsColumns, sh.Name + "!columns", "hidden columns", total, string.Join(", ", colRanges.Take(10).Select(c => ColumnLetters(c.Min) + (c.Max > c.Min ? ":" + ColumnLetters(c.Max) : string.Empty))));
            }

            if (!doing || (rowEls.Count == 0 && colEls.Count == 0))
            {
                continue;
            }

            // Office shows, and does not delete, hidden rows and columns that belong to a table or a PivotTable. The same here.
            List<(int C1, int R1, int C2, int R2)> guarded = GuardedRanges(z!, sh.Part!);
            var clearRows = new HashSet<int>();
            var showOnlyRows = new List<MarkupNode>();
            for (int i = 0; i < rowEls.Count; i++)
            {
                if (guarded.Any(g => rowNumbers[i] >= g.R1 && rowNumbers[i] <= g.R2))
                {
                    showOnlyRows.Add(rowEls[i]);
                }
                else
                {
                    clearRows.Add(rowNumbers[i]);
                }
            }

            var clearCols = new List<(int Min, int Max)>();
            var showOnlyCols = new List<MarkupNode>();
            foreach ((int mn, int mx, MarkupNode el) in colRanges)
            {
                if (guarded.Any(g => mn <= g.C2 && mx >= g.C1))
                {
                    showOnlyCols.Add(el);
                }
                else
                {
                    clearCols.Add((mn, mx));
                }
            }

            // ECMA-376 Part 1 §18.3.1.40 (f): a shared formula's master cell carries ref and the text; dependents carry only si. A master
            // that is cleared with dependents that stay would leave them without a formula, so such a sheet is left alone.
            if (HasSharedConflict(data, clearRows, clearCols))
            {
                z!.NotRemoved(InspectionCategory.HiddenRowsColumns, "sanitize.unsupported-construct", sh.Name, "A shared formula starts in a hidden row or column and continues in visible cells.");
                continue;
            }

            ClearCompanions(z!, sh.Part!, doc, clearRows, clearCols);

            if (!data.IsNull)
            {
                foreach (MarkupNode r in InspectionXml.Children(data))
                {
                    if (!r.LocalName.SequenceEqual("row"u8))
                    {
                        continue;
                    }

                    _ = int.TryParse(InspectionXml.Attr(r, "r"u8), NumberStyles.None, CultureInfo.InvariantCulture, out int rn);
                    bool clearRow = clearRows.Contains(rn) && InspectionXml.AttrTrue(r, "hidden"u8);
                    foreach (MarkupNode c in InspectionXml.Children(r))
                    {
                        if (c.LocalName.SequenceEqual("c"u8) && (clearRow || InColumns(c, clearCols)))
                        {
                            c.Remove();
                        }
                    }

                    if (InspectionXml.AttrTrue(r, "hidden"u8))
                    {
                        r.RemoveAttribute("hidden");
                    }
                }
            }

            foreach ((int _, int _, MarkupNode el) in colRanges)
            {
                el.RemoveAttribute("hidden");
            }

            int cleared = clearRows.Count + clearCols.Sum(c => c.Max - c.Min + 1);
            int shown = showOnlyRows.Count + showOnlyCols.Count;
            changed |= cleared > 0;
            if (cleared > 0)
            {
                z!.Removed(InspectionCategory.HiddenRowsColumns, sh.Name, "hidden rows and columns cleared and shown (contents removed; rows and columns keep their positions so references stay valid)", cleared);
            }

            if (shown > 0)
            {
                z!.Removed(InspectionCategory.HiddenRowsColumns, sh.Name, "hidden rows and columns of a table or PivotTable shown (contents kept)", shown);
            }
        }

        return changed;
    }

    // The text of a cleared cell can also be stored beside the cell: in a hyperlink, a data-validation list or a conditional format that
    // applies only to cleared cells. Those go with the cell.
    private static void ClearCompanions(SanitizeSession z, IPackagePart sheet, MarkupDocument doc, HashSet<int> clearRows, List<(int Min, int Max)> clearCols)
    {
        bool allCleared(string? sqref)
        {
            if (string.IsNullOrWhiteSpace(sqref))
            {
                return false;
            }

            foreach (string token in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryRange(token, out (int C1, int R1, int C2, int R2) r))
                {
                    return false;
                }

                bool inCols = clearCols.Any(c => r.C1 >= c.Min && r.C2 <= c.Max);
                bool inRows = r.R2 - r.R1 < 100000;
                for (int row = r.R1; inRows && row <= r.R2; row++)
                {
                    inRows = clearRows.Contains(row);
                }

                if (!inCols && !inRows)
                {
                    return false;
                }
            }

            return true;
        }

        foreach (MarkupNode group in InspectionXml.Children(doc.Root))
        {
            ReadOnlySpan<byte> n = group.LocalName;
            if (n.SequenceEqual("hyperlinks"u8) || n.SequenceEqual("dataValidations"u8))
            {
                bool links = n.SequenceEqual("hyperlinks"u8);
                foreach (MarkupNode item in InspectionXml.Children(group))
                {
                    if (allCleared(InspectionXml.Attr(item, links ? "ref"u8 : "sqref"u8)))
                    {
                        z.RemoveSubtree(sheet.Name, item);
                    }
                }

                int left = InspectionXml.Children(group).Count;
                if (left == 0)
                {
                    group.Remove();
                }
                else if (!links)
                {
                    group.SetAttribute("count", left.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        foreach (MarkupNode cf in InspectionXml.Children(doc.Root))
        {
            if (cf.LocalName.SequenceEqual("conditionalFormatting"u8) && allCleared(InspectionXml.Attr(cf, "sqref"u8)))
            {
                cf.Remove();
            }
        }
    }

    // The cell ranges of tables and PivotTables on a sheet, as (first column, first row, last column, last row).
    private static List<(int C1, int R1, int C2, int R2)> GuardedRanges(SanitizeSession z, IPackagePart sheet)
    {
        var list = new List<(int, int, int, int)>();
        foreach (Relationship r in z.Package.GetRelationships(sheet.Name))
        {
            IPackagePart? t = z.Target(sheet.Name, r);
            if (t is null || !(r.Type.EndsWith("/table", StringComparison.OrdinalIgnoreCase) || r.Type.EndsWith("/pivotTable", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            MarkupDocument? doc = z.Markup(t);
            if (doc is null)
            {
                continue;
            }

            string? reference = r.Type.EndsWith("/table", StringComparison.OrdinalIgnoreCase)
                ? InspectionXml.Attr(doc.Root, "ref"u8)
                : InspectionXml.Children(doc.Root).Where(c => c.LocalName.SequenceEqual("location"u8)).Select(c => InspectionXml.Attr(c, "ref"u8)).FirstOrDefault();
            if (reference is not null && TryRange(reference, out (int, int, int, int) range))
            {
                list.Add(range);
            }
        }

        return list;
    }

    private static bool TryRange(string text, out (int C1, int R1, int C2, int R2) range)
    {
        range = default;
        string[] ends = text.Split(':');
        if (ends.Length is < 1 or > 2 || !TryCell(ends[0], out int c1, out int r1) || !TryCell(ends[^1], out int c2, out int r2))
        {
            return false;
        }

        range = (Math.Min(c1, c2), Math.Min(r1, r2), Math.Max(c1, c2), Math.Max(r1, r2));
        return true;
    }

    private static bool TryCell(string text, out int col, out int row)
    {
        string t = text.Replace("$", string.Empty, StringComparison.Ordinal);
        int i = 0;
        while (i < t.Length && char.IsLetter(t[i]))
        {
            i++;
        }

        col = ColumnIndex(t[..i]);
        return int.TryParse(t.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out row) && col > 0 && row > 0;
    }

    private static bool InColumns(MarkupNode cell, List<(int Min, int Max)> ranges)
    {
        if (ranges.Count == 0)
        {
            return false;
        }

        string r = InspectionXml.Attr(cell, "r"u8) ?? string.Empty;
        int col = ColumnIndex(r);
        foreach ((int min, int max) in ranges)
        {
            if (col >= min && col <= max)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSharedConflict(MarkupNode data, HashSet<int> clearRows, List<(int Min, int Max)> cols)
    {
        if (data.IsNull)
        {
            return false;
        }

        var removedMasters = new HashSet<string>(StringComparer.Ordinal);
        var keptDependents = new HashSet<string>(StringComparer.Ordinal);
        foreach (MarkupNode r in InspectionXml.Children(data))
        {
            _ = int.TryParse(InspectionXml.Attr(r, "r"u8), NumberStyles.None, CultureInfo.InvariantCulture, out int rn);
            bool hiddenRow = clearRows.Contains(rn) && InspectionXml.AttrTrue(r, "hidden"u8);
            foreach (MarkupNode c in InspectionXml.Children(r))
            {
                bool removed = hiddenRow || InColumns(c, cols);
                foreach (MarkupNode f in InspectionXml.Children(c))
                {
                    if (!f.LocalName.SequenceEqual("f"u8) || InspectionXml.Attr(f, "t"u8) != "shared")
                    {
                        continue;
                    }

                    string si = InspectionXml.Attr(f, "si"u8) ?? string.Empty;
                    bool master = InspectionXml.Attr(f, "ref"u8) is not null;
                    if (removed && master)
                    {
                        removedMasters.Add(si);
                    }
                    else if (!removed && !master)
                    {
                        keptDependents.Add(si);
                    }
                }
            }
        }

        return removedMasters.Overlaps(keptDependents);
    }

    private static string Ranges(List<int> numbers, Func<int, string> format)
    {
        var parts = new List<string>();
        int i = 0;
        while (i < numbers.Count && parts.Count < 10)
        {
            int j = i;
            while (j + 1 < numbers.Count && numbers[j + 1] == numbers[j] + 1)
            {
                j++;
            }

            parts.Add(j > i ? format(numbers[i]) + ":" + format(numbers[j]) : format(numbers[i]));
            i = j + 1;
        }

        return string.Join(", ", parts) + (i < numbers.Count ? ", ..." : string.Empty);
    }

    private static void DataConnections(InspectionSession s, SanitizeSession? z)
    {
        bool doing = Doing(z, InspectionCategory.DataConnections);
        List<IPackagePart> connections = s.PartsWhere(ct => ct.EndsWith("spreadsheetml.connections+xml", StringComparison.OrdinalIgnoreCase));
        List<IPackagePart> queryTables = s.PartsWhere(ct => ct.EndsWith("spreadsheetml.queryTable+xml", StringComparison.OrdinalIgnoreCase));
        foreach (IPackagePart p in connections)
        {
            MarkupDocument? doc = s.Markup(p);
            var names = new List<string>();
            if (doc is not null)
            {
                foreach (MarkupNode c in InspectionXml.Children(doc.Root))
                {
                    names.Add(InspectionXml.Attr(c, "name"u8) ?? "connection");
                }
            }

            s.Add(InspectionCategory.DataConnections, p.Name, "data connections", Math.Max(1, names.Count), string.Join(", ", names.Take(5)));
        }

        foreach (IPackagePart q in queryTables)
        {
            s.Add(InspectionCategory.DataConnections, q.Name, "query table");
        }

        if (!doing)
        {
            return;
        }

        bool pivotUses = s.PartsWhere(ct => ct.Contains("pivotCacheDefinition", StringComparison.OrdinalIgnoreCase))
            .Any(p => s.Markup(p) is { } d && d.Root.OuterXml.Contains("connectionId", StringComparison.Ordinal));
        if (queryTables.Count > 0 || pivotUses)
        {
            foreach (IPackagePart p in connections)
            {
                z!.NotRemoved(InspectionCategory.DataConnections, "sanitize.referenced", p.Name, "A query table or a PivotTable cache is bound to the connections.");
            }

            foreach (IPackagePart q in queryTables)
            {
                z!.NotRemoved(InspectionCategory.DataConnections, "sanitize.referenced", q.Name, "A query table is bound to a table of cells.");
            }

            return;
        }

        foreach (IPackagePart p in connections.ToList())
        {
            string name = p.Name;
            z!.RemoveEverywhere(name);
            z.Removed(InspectionCategory.DataConnections, name, "data connections");
        }
    }

    private static bool ExternalLinks(InspectionSession s, SanitizeSession? z, IPackagePart main, MarkupDocument wb, List<SheetEntry> sheets)
    {
        bool doing = Doing(z, InspectionCategory.ExternalLinks);
        List<IPackagePart> links = s.PartsWhere(ct => ct.Contains("spreadsheetml.externalLink", StringComparison.OrdinalIgnoreCase));
        foreach (IPackagePart p in links)
        {
            string target = string.Join(", ", s.Package.GetRelationships(p.Name).Select(r => r.Target).Take(2));
            s.Add(InspectionCategory.ExternalLinks, p.Name, "external workbook link", 1, InspectionXml.Brief(target, 120));
        }

        if (!doing || links.Count == 0)
        {
            return false;
        }

        // Breaking links keeps the values Excel last cached and drops the formulas that read the other workbook. If any other place
        // (validation, conditional formats, charts) still reads it, nothing is changed: a half-broken link would be worse.
        var blockers = new List<string>();
        foreach (IPackagePart p in s.Package.Parts)
        {
            bool sheet = p.ContentType.Contains("worksheet+xml", StringComparison.OrdinalIgnoreCase);
            bool chart = p.ContentType.Contains("drawingml.chart+xml", StringComparison.OrdinalIgnoreCase);
            if (!sheet && !chart)
            {
                continue;
            }

            MarkupDocument? doc = s.Markup(p);
            if (doc is null)
            {
                continue;
            }

            foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
            {
                ReadOnlySpan<byte> n = e.LocalName;
                bool site = chart ? n.SequenceEqual("f"u8)
                    : (n.SequenceEqual("formula1"u8) || n.SequenceEqual("formula2"u8) || (n.SequenceEqual("formula"u8) && e.Parent.LocalName.SequenceEqual("cfRule"u8)));
                if (site && ExternalReference().IsMatch(e.InnerText))
                {
                    blockers.Add(p.Name);
                    break;
                }
            }
        }

        if (blockers.Count > 0)
        {
            foreach (IPackagePart p in links)
            {
                z!.NotRemoved(InspectionCategory.ExternalLinks, "sanitize.referenced", p.Name, "Validation, conditional formatting or a chart still reads the other workbook (" + string.Join(", ", blockers.Distinct().Take(3)) + ").");
            }

            return false;
        }

        int formulas = 0;
        foreach (SheetEntry sh in sheets)
        {
            if (!IsWorksheet(sh.Part))
            {
                continue;
            }

            MarkupDocument? doc = z!.Edit(sh.Part!);
            if (doc is null)
            {
                continue;
            }

            var shared = new HashSet<string>(StringComparer.Ordinal);
            var cells = new List<MarkupNode>();
            foreach (MarkupNode f in InspectionXml.Descendants(doc.DocumentNode))
            {
                if (f.LocalName.SequenceEqual("f"u8) && f.Parent.LocalName.SequenceEqual("c"u8))
                {
                    cells.Add(f);
                    if (InspectionXml.Attr(f, "t"u8) == "shared" && InspectionXml.Attr(f, "ref"u8) is not null && ExternalReference().IsMatch(f.InnerText))
                    {
                        shared.Add(InspectionXml.Attr(f, "si"u8) ?? string.Empty);
                    }
                }
            }

            foreach (MarkupNode f in cells)
            {
                bool dependent = InspectionXml.Attr(f, "t"u8) == "shared" && shared.Contains(InspectionXml.Attr(f, "si"u8) ?? string.Empty);
                if (dependent || ExternalReference().IsMatch(f.InnerText))
                {
                    f.Remove();
                    formulas++;
                }
            }
        }

        z!.Edit(main);
        foreach (MarkupNode dn in InspectionXml.Descendants(wb.DocumentNode))
        {
            if (dn.LocalName.SequenceEqual("definedName"u8) && ExternalReference().IsMatch(dn.InnerText) && InspectionXml.IsAttached(dn))
            {
                dn.Remove();
                z.Removed(InspectionCategory.ExternalLinks, main.Name, "defined name that read the other workbook");
            }
        }

        foreach (MarkupNode dn in InspectionXml.Descendants(wb.DocumentNode))
        {
            if (dn.LocalName.SequenceEqual("definedNames"u8) && !InspectionXml.HasChildElements(dn) && InspectionXml.IsAttached(dn))
            {
                dn.Remove();
            }
            else if (dn.LocalName.SequenceEqual("externalReferences"u8) && InspectionXml.IsAttached(dn))
            {
                dn.Remove();
            }
        }

        var ids = s.Package.GetRelationships(main.Name).Where(r => r.Type.EndsWith("/externalLink", StringComparison.OrdinalIgnoreCase)).Select(r => r.Id).ToList();
        z.Unlink(main.Name, ids);
        z.Removed(InspectionCategory.ExternalLinks, main.Name, "external workbook links broken (cells keep their last values)", Math.Max(1, links.Count));
        if (formulas > 0)
        {
            z.Removed(InspectionCategory.ExternalLinks, main.Name, "formulas replaced by their last values", formulas);
        }

        return true;
    }

    // ---- hidden sheets, macro sheets and hidden names ----

    private static List<string> FormulaTexts(InspectionSession s, string? excludePart, HashSet<MarkupNode> skipNames)
    {
        var texts = new List<string>();
        foreach (IPackagePart p in s.Package.Parts)
        {
            if (excludePart is not null && string.Equals(p.Name, excludePart, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool sheet = p.ContentType.Contains("sheet+xml", StringComparison.OrdinalIgnoreCase) || p.ContentType.Contains("macrosheet", StringComparison.OrdinalIgnoreCase);
            bool chart = p.ContentType.Contains("drawingml.chart", StringComparison.OrdinalIgnoreCase);
            bool workbook = string.Equals(p.Name, s.MainPartName, StringComparison.OrdinalIgnoreCase);
            bool pivot = p.ContentType.Contains("pivotCacheDefinition", StringComparison.OrdinalIgnoreCase) || p.ContentType.Contains("slicerCache", StringComparison.OrdinalIgnoreCase)
                || p.ContentType.Contains("table+xml", StringComparison.OrdinalIgnoreCase) || p.ContentType.Contains("connections", StringComparison.OrdinalIgnoreCase)
                || p.ContentType.Contains("queryTable", StringComparison.OrdinalIgnoreCase);
            if (!sheet && !chart && !workbook && !pivot)
            {
                continue;
            }

            MarkupDocument? doc = s.Markup(p);
            if (doc is null)
            {
                continue;
            }

            foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
            {
                ReadOnlySpan<byte> n = e.LocalName;
                if (n.SequenceEqual("f"u8) || n.SequenceEqual("formula"u8) || n.SequenceEqual("formula1"u8) || n.SequenceEqual("formula2"u8) || n.SequenceEqual("calculatedColumnFormula"u8)
                    || (n.SequenceEqual("definedName"u8) && !skipNames.Contains(e)))
                {
                    texts.Add(e.InnerText);
                }

                foreach (MarkupAttributeRef a in e.Attributes)
                {
                    if (!a.IsNamespaceDeclaration && (a.LocalName.SequenceEqual("location"u8) || a.LocalName.SequenceEqual("sheet"u8) || a.LocalName.SequenceEqual("sourceFile"u8) || a.LocalName.SequenceEqual("command"u8)))
                    {
                        texts.Add(a.Value);
                    }
                }
            }
        }

        return texts;
    }

    private static bool MentionsSheet(List<string> texts, string sheetName)
    {
        string quoted = sheetName.Replace("'", "''", StringComparison.Ordinal);
        foreach (string t in texts)
        {
            if (t.Contains(sheetName + "!", StringComparison.OrdinalIgnoreCase) || t.Contains(quoted + "'!", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, sheetName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MentionsName(List<string> texts, string name)
    {
        foreach (string t in texts)
        {
            int at = 0;
            while ((at = t.IndexOf(name, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                bool before = at == 0 || !(char.IsLetterOrDigit(t[at - 1]) || t[at - 1] is '_' or '.');
                int end = at + name.Length;
                bool after = end >= t.Length || !(char.IsLetterOrDigit(t[end]) || t[end] is '_' or '.');
                if (before && after)
                {
                    return true;
                }

                at = end;
            }
        }

        return false;
    }

    private static bool HiddenSheetsAndNames(InspectionSession s, SanitizeSession? z, IPackagePart main, List<SheetEntry> sheets)
    {
        bool doingSheets = Doing(z, InspectionCategory.HiddenSheets);
        bool doingMacros = Doing(z, InspectionCategory.Macros);
        bool doingNames = Doing(z, InspectionCategory.HiddenNames);
        MarkupDocument wb = s.Markup(main)!;
        var hiddenSheets = sheets.Where(x => x.State is "hidden" or "veryHidden").ToList();
        foreach (SheetEntry h in hiddenSheets)
        {
            s.Add(InspectionCategory.HiddenSheets, "workbook!" + h.Name, h.State == "veryHidden" ? "very hidden sheet" : "hidden sheet", 1, h.Name);
        }

        foreach (SheetEntry m in sheets.Where(x => x.IsMacro))
        {
            s.Add(InspectionCategory.Macros, m.Part!.Name, "XLM macro sheet", 1, m.Name);
        }

        var hiddenNames = new List<MarkupNode>();
        foreach (MarkupNode dns in InspectionXml.Children(wb.Root))
        {
            if (!dns.LocalName.SequenceEqual("definedNames"u8))
            {
                continue;
            }

            foreach (MarkupNode dn in InspectionXml.Children(dns))
            {
                string name = InspectionXml.Attr(dn, "name"u8) ?? string.Empty;
                if (InspectionXml.AttrTrue(dn, "hidden"u8) && !name.StartsWith("_xl", StringComparison.OrdinalIgnoreCase))
                {
                    hiddenNames.Add(dn);
                    s.Add(InspectionCategory.HiddenNames, "workbook!" + name, "hidden defined name", 1, InspectionXml.Brief(dn.InnerText, 100));
                }
            }
        }

        if (z is null)
        {
            return false;
        }

        var toRemove = new List<SheetEntry>();
        if (doingSheets)
        {
            toRemove.AddRange(hiddenSheets);
        }

        if (doingMacros)
        {
            foreach (SheetEntry m in sheets.Where(x => x.IsMacro && !toRemove.Contains(x)))
            {
                toRemove.Add(m);
            }
        }

        z.Flush();
        wb = z.Markup(main)!;
        sheets = ReadSheets(z, main, wb);
        var current = new List<SheetEntry>();
        foreach (SheetEntry t in toRemove)
        {
            SheetEntry? match = sheets.FirstOrDefault(x => x.Name == t.Name);
            if (match is not null)
            {
                current.Add(match);
            }
        }

        toRemove = current;
        hiddenNames = [];
        foreach (MarkupNode dns in InspectionXml.Children(wb.Root))
        {
            if (dns.LocalName.SequenceEqual("definedNames"u8))
            {
                foreach (MarkupNode dn in InspectionXml.Children(dns))
                {
                    string name = InspectionXml.Attr(dn, "name"u8) ?? string.Empty;
                    if (InspectionXml.AttrTrue(dn, "hidden"u8) && !name.StartsWith("_xl", StringComparison.OrdinalIgnoreCase))
                    {
                        hiddenNames.Add(dn);
                    }
                }
            }
        }

        // Names first: a hidden name stays when any formula still uses it.
        if (doingNames && hiddenNames.Count > 0)
        {
            z.Edit(main);
            List<string> texts = FormulaTexts(z, null, [.. hiddenNames]);
            foreach (MarkupNode dn in hiddenNames)
            {
                string name = InspectionXml.Attr(dn, "name"u8) ?? string.Empty;
                if (MentionsName(texts, name))
                {
                    z.NotRemoved(InspectionCategory.HiddenNames, "sanitize.referenced", "workbook!" + name, "A formula, rule or chart uses the name.");
                    continue;
                }

                dn.Remove();
                z.Removed(InspectionCategory.HiddenNames, "workbook!" + name, "hidden defined name");
            }

            RemoveEmptyDefinedNames(wb);
        }

        bool changed = false;
        foreach (SheetEntry sh in toRemove)
        {
            InspectionCategory cat = sh.IsMacro && !(sh.State is "hidden" or "veryHidden" && doingSheets) ? InspectionCategory.Macros : InspectionCategory.HiddenSheets;
            if (sh.IsMacro && doingMacros)
            {
                cat = InspectionCategory.Macros;
            }

            changed |= RemoveSheet(z, main, sh, cat);
        }

        return changed;
    }

    private static void RemoveEmptyDefinedNames(MarkupDocument wb)
    {
        foreach (MarkupNode dns in InspectionXml.Children(wb.Root))
        {
            if (dns.LocalName.SequenceEqual("definedNames"u8) && !InspectionXml.HasChildElements(dns))
            {
                dns.Remove();
            }
        }
    }

    private static bool RemoveSheet(SanitizeSession z, IPackagePart main, SheetEntry sh, InspectionCategory cat)
    {
        string where = "workbook!" + sh.Name;
        if (sh.Part is null)
        {
            z.NotRemoved(cat, "sanitize.unsupported-construct", where, "The sheet's part is missing.");
            return false;
        }

        MarkupDocument wb = z.Edit(main)!;
        MarkupNode current = default;
        MarkupNode sheetsEl = default;
        foreach (MarkupNode e in InspectionXml.Children(wb.Root))
        {
            if (e.LocalName.SequenceEqual("sheets"u8))
            {
                sheetsEl = e;
            }
        }

        int idx = 0;
        int index = -1;
        foreach (MarkupNode e in InspectionXml.Children(sheetsEl))
        {
            if ((InspectionXml.Attr(e, "name"u8) ?? string.Empty) == sh.Name)
            {
                current = e;
                index = idx;
            }

            idx++;
        }

        if (current.IsNull)
        {
            return false;
        }

        var scoped = new HashSet<MarkupNode>();
        foreach (MarkupNode dns in InspectionXml.Children(wb.Root))
        {
            if (!dns.LocalName.SequenceEqual("definedNames"u8))
            {
                continue;
            }

            foreach (MarkupNode dn in InspectionXml.Children(dns))
            {
                if (InspectionXml.Attr(dn, "localSheetId"u8) == index.ToString(CultureInfo.InvariantCulture))
                {
                    scoped.Add(dn);
                }
            }
        }

        // Tables on the sheet are referred to by name from other formulas, so their names count as mentions of the sheet.
        var tableNames = new List<string>();
        foreach (IPackagePart t in z.TargetsOf(sh.Part.Name, "/table"))
        {
            if (z.Markup(t) is { } td)
            {
                tableNames.Add(InspectionXml.Attr(td.Root, "displayName"u8) ?? InspectionXml.Attr(td.Root, "name"u8) ?? string.Empty);
            }
        }

        List<string> texts = FormulaTexts(z, sh.Part.Name, scoped);

        string? blocker = null;
        if (MentionsSheet(texts, sh.Name))
        {
            blocker = "A formula, name, chart or pivot source in another place refers to the sheet.";
        }
        else if (tableNames.Any(n => n.Length > 0 && MentionsName(texts, n)))
        {
            blocker = "A formula refers to a table on the sheet.";
        }

        if (blocker is not null)
        {
            z.NotRemoved(cat, "sanitize.referenced", where, blocker);
            return false;
        }

        // ECMA-376 Part 1 §18.2.5 definedName: localSheetId is the position of the sheet in the sheets list, so names after the removed
        // sheet move up by one and names scoped to it go.
        foreach (MarkupNode dns in InspectionXml.Children(wb.Root))
        {
            if (!dns.LocalName.SequenceEqual("definedNames"u8))
            {
                continue;
            }

            foreach (MarkupNode dn in InspectionXml.Children(dns))
            {
                if (int.TryParse(InspectionXml.Attr(dn, "localSheetId"u8), NumberStyles.None, CultureInfo.InvariantCulture, out int local))
                {
                    if (local == index)
                    {
                        dn.Remove();
                    }
                    else if (local > index)
                    {
                        dn.SetAttribute("localSheetId", (local - 1).ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        RemoveEmptyDefinedNames(wb);
        foreach (MarkupNode e in InspectionXml.Descendants(wb.DocumentNode))
        {
            if (!e.LocalName.SequenceEqual("workbookView"u8))
            {
                continue;
            }

            foreach (string attr in new[] { "activeTab", "firstSheet" })
            {
                if (int.TryParse(e.GetAttribute(attr), NumberStyles.None, CultureInfo.InvariantCulture, out int v))
                {
                    if (v == index)
                    {
                        e.SetAttribute(attr, "0");
                    }
                    else if (v > index)
                    {
                        e.SetAttribute(attr, (v - 1).ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        string partName = sh.Part.Name;
        current.Remove();
        foreach (string? owner in z.OwnersOf(partName))
        {
            z.UnlinkTarget(owner, partName);
        }

        z.RemovePart(partName);
        foreach (IPackagePart chain in z.PartsWhere(ct => ct.EndsWith("calcChain+xml", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            z.RemoveEverywhere(chain.Name);
        }

        // docProps/app.xml lists every sheet by name; it would still name the removed one.
        foreach (IPackagePart app in z.TargetsOf(null, "/extended-properties"))
        {
            if (z.Edit(app) is { } ad)
            {
                foreach (MarkupNode c in InspectionXml.Children(ad.Root))
                {
                    if (c.LocalName.SequenceEqual("TitlesOfParts"u8) || c.LocalName.SequenceEqual("HeadingPairs"u8))
                    {
                        c.Remove();
                    }
                }
            }
        }

        z.Removed(cat, where, (sh.IsMacro ? "macro sheet" : "hidden sheet") + " '" + sh.Name + "' removed with its drawings, comments and tables");
        return true;
    }
}
