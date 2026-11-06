using System.Linq;
using System.Security;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.TextExtraction;

namespace RecursiveAI.OneDocument.Redaction;

// Cell-range regions of a workbook: the cells are emptied (styles stay), and the strings, comments, hyperlinks, pictures and chart caches
// that came from them go too.
internal static class SpreadsheetRegions
{
    public static void Apply(OpcPackage package, RedactionPlan plan, RedactionReport report, OfficeLimits limits)
    {
        var ranges = new List<CellRangeRegion>();
        foreach (RedactionRegion region in plan.Regions)
        {
            if (region is CellRangeRegion r)
            {
                ranges.Add(r);
            }
            else
            {
                report.Gap("redaction.region-not-applicable", region.GetType().Name, "Only cell-range regions apply to a workbook.");
            }
        }

        if (ranges.Count == 0)
        {
            return;
        }

        string? workbookPart = RedactionSupport.MainPart(package);
        MarkupDocument? workbook = workbookPart is null ? null : RedactionSupport.Load(package, workbookPart, limits);
        if (workbookPart is null || workbook is null)
        {
            report.Gap("redaction.region-failed", "/", "The workbook part could not be read.");
            return;
        }

        HashSet<string> before = RedactionSupport.Reachable(package);
        var cleared = new HashSet<int>(); // shared-string indices referenced by emptied cells
        var clearedBySheet = new Dictionary<string, List<CellRangeRegion>>(StringComparer.OrdinalIgnoreCase);

        foreach (MarkupNode sheet in RedactionSupport.Descendants(workbook.Root).Where(static e => RedactionSupport.Is(e, NsKind.Sml, "sheet")))
        {
            string? name = sheet.GetAttribute("name");
            List<CellRangeRegion> mine = ranges.Where(r => string.Equals(r.Sheet, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (mine.Count == 0)
            {
                continue;
            }

            string? rid = sheet.GetAttribute("http://schemas.openxmlformats.org/officeDocument/2006/relationships", "id")
                ?? sheet.GetAttribute("http://purl.oclc.org/ooxml/officeDocument/relationships", "id");
            string? part = rid is null ? null : RedactionSupport.RelatedPart(package, workbookPart, rid);
            if (part is null)
            {
                report.Gap("redaction.region-failed", name ?? "?", "The sheet's part could not be found.");
                continue;
            }

            clearedBySheet[name!] = mine;
            Sheet(package, part, mine, cleared, report, limits);
        }

        foreach (CellRangeRegion r in ranges)
        {
            if (!clearedBySheet.ContainsKey(r.Sheet))
            {
                report.Gap("redaction.region-failed", r.Sheet, "No sheet has this name.");
            }
        }

        if (cleared.Count > 0)
        {
            BlankSharedStrings(package, workbookPart, cleared, report, limits);
        }

        Charts(package, clearedBySheet, report, limits);
        PivotNotice(package, clearedBySheet, report, limits);
        _ = RedactionSupport.RemoveNewlyUnreachable(package, before, report);
    }

    private static void Sheet(OpcPackage package, string part, List<CellRangeRegion> ranges, HashSet<int> cleared, RedactionReport report, OfficeLimits limits)
    {
        MarkupDocument? doc = RedactionSupport.Load(package, part, limits);
        if (doc is null || doc.Root.IsNull)
        {
            report.Gap("redaction.region-failed", part, "The sheet part could not be read.");
            return;
        }

        bool In(int row, int col) => ranges.Any(r => r.Contains(row, col));
        int count = 0;
        foreach (MarkupNode c in RedactionSupport.Descendants(doc.Root).Where(static e => RedactionSupport.Is(e, NsKind.Sml, "c")).ToList())
        {
            if (c.GetAttribute("r") is not { } address || !CellRangeRegion.TryParseCell(address, out int row, out int col) || !In(row, col))
            {
                continue;
            }

            if (c.GetAttribute("t") == "s")
            {
                foreach (MarkupNode v in RedactionSupport.ChildrenNamed(c, "v"))
                {
                    if (int.TryParse(v.InnerText, out int index))
                    {
                        cleared.Add(index);
                    }
                }
            }

            foreach (MarkupNode f in RedactionSupport.ChildrenNamed(c, "f"))
            {
                if (f.GetAttribute("ref") is { } extent && CellRangeRegion.TryParse(extent, out int r1, out int c1, out int r2, out int c2) && !(In(r1, c1) && In(r2, c2)))
                {
                    report.Gap("redaction.shared-formula-extent", part + "!" + address, "An emptied cell is the master of a shared or array formula that extends outside the region; the dependents may need repair.");
                }
            }

            foreach (MarkupNode child in c.ChildElements.ToList())
            {
                child.Remove();
            }

            c.RemoveAttribute("t");
            count++;
        }

        foreach (MarkupNode link in RedactionSupport.Descendants(doc.Root).Where(static e => RedactionSupport.Is(e, NsKind.Sml, "hyperlink")).ToList())
        {
            if (link.GetAttribute("ref") is { } sqref && Intersects(sqref, ranges))
            {
                MarkupNode container = link.Parent;
                link.Remove();
                count++;
                if (!container.ChildElements.Any())
                {
                    container.Remove(); // an empty hyperlinks element is not valid
                }
            }
        }

        RedactionSupport.Save(package, part, doc);
        report.Add("region", part, count);

        foreach (Relationship rel in package.GetRelationships(part).ToList())
        {
            string? target = RedactionSupport.Resolve(package, part, rel);
            if (target is null)
            {
                continue;
            }

            if (rel.Type.EndsWith("/comments", StringComparison.Ordinal))
            {
                Comments(package, target, ranges, report, limits);
            }
            else if (rel.Type.EndsWith("/vmlDrawing", StringComparison.Ordinal))
            {
                VmlNotes(package, target, ranges, report, limits);
            }
            else if (rel.Type.EndsWith("/drawing", StringComparison.Ordinal))
            {
                Drawing(package, target, ranges, report, limits);
            }
            else if (rel.Type.EndsWith("/threadedComment", StringComparison.Ordinal))
            {
                Threaded(package, target, ranges, report, limits);
            }
        }

        // Hyperlinks removed above leave their external targets in the sheet's relationships until they are dropped.
        MarkupDocument? after = RedactionSupport.Load(package, part, limits);
        if (after is not null)
        {
            RedactionSupport.DropUnusedRelationships(package, part, after, package.GetRelationships(part).Where(static r => r.TargetMode == RelationshipTargetMode.External).Select(static r => r.Id));
        }
    }

    private static bool Intersects(string sqref, List<CellRangeRegion> ranges)
    {
        foreach (string piece in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (CellRangeRegion.TryParse(piece, out int r1, out int c1, out int r2, out int c2) && ranges.Any(r => r.Intersects(r1, c1, r2, c2)))
            {
                return true;
            }
        }

        return false;
    }

    private static void Comments(OpcPackage package, string part, List<CellRangeRegion> ranges, RedactionReport report, OfficeLimits limits)
    {
        MarkupDocument? doc = RedactionSupport.Load(package, part, limits);
        if (doc is null || doc.Root.IsNull)
        {
            return;
        }

        int n = 0;
        foreach (MarkupNode c in RedactionSupport.Descendants(doc.Root).Where(static e => RedactionSupport.Is(e, NsKind.Sml, "comment")).ToList())
        {
            if (c.GetAttribute("ref") is { } address && Intersects(address, ranges))
            {
                c.Remove();
                n++;
            }
        }

        RedactionSupport.Save(package, part, doc);
        report.Add("region-comment", part, n);
    }

    private static void Threaded(OpcPackage package, string part, List<CellRangeRegion> ranges, RedactionReport report, OfficeLimits limits)
    {
        MarkupDocument? doc = RedactionSupport.Load(package, part, limits);
        if (doc is null || doc.Root.IsNull)
        {
            return;
        }

        int n = 0;
        foreach (MarkupNode c in doc.Root.ChildElements.ToList())
        {
            if (c.LocalNameString == "threadedComment" && c.GetAttribute("ref") is { } address && Intersects(address, ranges))
            {
                c.Remove();
                n++;
            }
        }

        RedactionSupport.Save(package, part, doc);
        report.Add("region-comment", part, n);
    }

    // The legacy drawing draws one shape per note; ClientData names the note's cell (zero-based row and column).
    private static void VmlNotes(OpcPackage package, string part, List<CellRangeRegion> ranges, RedactionReport report, OfficeLimits limits)
    {
        MarkupDocument? doc = RedactionSupport.Load(package, part, limits);
        if (doc is null || doc.Root.IsNull)
        {
            return;
        }

        int n = 0;
        foreach (MarkupNode shape in RedactionSupport.Descendants(doc.Root).Where(static e => e.LocalNameString == "shape").ToList())
        {
            MarkupNode data = RedactionSupport.ChildrenNamed(shape, "ClientData").FirstOrDefault();
            if (data.IsNull)
            {
                continue;
            }

            string? row = RedactionSupport.ChildrenNamed(data, "Row").Select(static e => e.InnerText).FirstOrDefault();
            string? col = RedactionSupport.ChildrenNamed(data, "Column").Select(static e => e.InnerText).FirstOrDefault();
            if (int.TryParse(row, out int r) && int.TryParse(col, out int c) && ranges.Any(x => x.Contains(r + 1, c + 1)))
            {
                shape.Remove();
                n++;
            }
        }

        RedactionSupport.Save(package, part, doc);
        report.Add("region-comment", part, n);
    }

    private static void Drawing(OpcPackage package, string part, List<CellRangeRegion> ranges, RedactionReport report, OfficeLimits limits)
    {
        MarkupDocument? doc = RedactionSupport.Load(package, part, limits);
        if (doc is null || doc.Root.IsNull)
        {
            return;
        }

        var known = new HashSet<string>(package.GetRelationships(part).Select(static r => r.Id), StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        int n = 0;
        foreach (MarkupNode anchor in doc.Root.ChildElements.ToList())
        {
            if (anchor.LocalNameString is not ("twoCellAnchor" or "oneCellAnchor"))
            {
                continue;
            }

            MarkupNode from = RedactionSupport.ChildrenNamed(anchor, "from").FirstOrDefault();
            if (from.IsNull)
            {
                continue;
            }

            string? col = RedactionSupport.ChildrenNamed(from, "col").Select(static e => e.InnerText).FirstOrDefault();
            string? row = RedactionSupport.ChildrenNamed(from, "row").Select(static e => e.InnerText).FirstOrDefault();
            if (int.TryParse(row, out int r) && int.TryParse(col, out int c) && ranges.Any(x => x.Contains(r + 1, c + 1)))
            {
                used.UnionWith(RedactionSupport.RelationshipIdsUsed(anchor, known));
                anchor.Remove();
                n++;
            }
        }

        RedactionSupport.DropUnusedRelationships(package, part, doc, used);
        RedactionSupport.Save(package, part, doc);
        report.Add("region-drawing", part, n);
    }

    private static void BlankSharedStrings(OpcPackage package, string workbookPart, HashSet<int> cleared, RedactionReport report, OfficeLimits limits)
    {
        // An entry still used by a remaining cell must stay.
        var stillUsed = new HashSet<int>();
        foreach (string sheetPart in package.Parts.Where(static p => p.ContentType.Contains("worksheet+xml", StringComparison.Ordinal)).Select(static p => p.Name).ToList())
        {
            MarkupDocument? doc = RedactionSupport.Load(package, sheetPart, limits);
            if (doc is null || doc.Root.IsNull)
            {
                continue;
            }

            foreach (MarkupNode c in RedactionSupport.Descendants(doc.Root).Where(static e => RedactionSupport.Is(e, NsKind.Sml, "c")))
            {
                if (c.GetAttribute("t") == "s")
                {
                    foreach (MarkupNode v in RedactionSupport.ChildrenNamed(c, "v"))
                    {
                        if (int.TryParse(v.InnerText, out int i))
                        {
                            stillUsed.Add(i);
                        }
                    }
                }
            }
        }

        foreach (string part in RedactionSupport.RelatedParts(package, workbookPart, "/sharedStrings"))
        {
            MarkupDocument? doc = RedactionSupport.Load(package, part, limits);
            if (doc is null || doc.Root.IsNull)
            {
                continue;
            }

            int index = 0;
            int n = 0;
            foreach (MarkupNode si in doc.Root.ChildElements.ToList())
            {
                if (si.LocalNameString != "si")
                {
                    continue;
                }

                if (cleared.Contains(index) && !stillUsed.Contains(index))
                {
                    foreach (MarkupNode c in si.ChildElements.ToList())
                    {
                        c.Remove();
                    }

                    string p = RedactionSupport.Prefix(si);
                    si.AppendXml(Encoding.UTF8.GetBytes($"<{p}t/>"));
                    n++;
                }

                index++;
            }

            RedactionSupport.Save(package, part, doc);
            report.Add("region-shared-string", part, n);
        }
    }

    // A chart that draws from an emptied range keeps no cached values and no embedded copy of its data.
    private static void Charts(OpcPackage package, Dictionary<string, List<CellRangeRegion>> bySheet, RedactionReport report, OfficeLimits limits)
    {
        foreach (string part in package.Parts.Where(static p => p.ContentType.Contains("drawingml.chart+xml", StringComparison.Ordinal)).Select(static p => p.Name).ToList())
        {
            MarkupDocument? doc = RedactionSupport.Load(package, part, limits);
            if (doc is null || doc.Root.IsNull)
            {
                continue;
            }

            int n = 0;
            foreach (MarkupNode reference in RedactionSupport.Descendants(doc.Root).Where(static e => e.LocalNameString is "strRef" or "numRef" or "multiLvlStrRef").ToList())
            {
                string? formula = RedactionSupport.ChildrenNamed(reference, "f").Select(static e => e.InnerText).FirstOrDefault();
                if (formula is null || !RefersTo(formula, bySheet))
                {
                    continue;
                }

                foreach (MarkupNode cache in reference.ChildElements.Where(static e => e.LocalNameString is "strCache" or "numCache" or "multiLvlStrCache").ToList())
                {
                    cache.Remove();
                    n++;
                }
            }

            if (n > 0)
            {
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (MarkupNode ext in RedactionSupport.Descendants(doc.Root).Where(static e => e.LocalNameString == "externalData").ToList())
                {
                    used.UnionWith(RedactionSupport.RelationshipIdsUsed(ext, new HashSet<string>(package.GetRelationships(part).Select(static r => r.Id), StringComparer.Ordinal)));
                    ext.Remove();
                }

                RedactionSupport.DropUnusedRelationships(package, part, doc, used);
                RedactionSupport.Save(package, part, doc);
                report.Add("region-chart-cache", part, n);
            }
        }
    }

    private static bool RefersTo(string formula, Dictionary<string, List<CellRangeRegion>> bySheet)
    {
        int bang = formula.LastIndexOf('!');
        if (bang < 0)
        {
            return false;
        }

        string sheet = formula[..bang].Trim('\'').Replace("''", "'", StringComparison.Ordinal);
        string range = formula[(bang + 1)..].Replace("$", string.Empty, StringComparison.Ordinal);
        if (!bySheet.TryGetValue(sheet, out List<CellRangeRegion>? ranges))
        {
            return false;
        }

        return !CellRangeRegion.TryParse(range, out int r1, out int c1, out int r2, out int c2) || ranges.Any(r => r.Intersects(r1, c1, r2, c2));
    }

    private static void PivotNotice(OpcPackage package, Dictionary<string, List<CellRangeRegion>> bySheet, RedactionReport report, OfficeLimits limits)
    {
        foreach (string part in package.Parts.Where(static p => p.ContentType.Contains("pivotCacheDefinition+xml", StringComparison.Ordinal)).Select(static p => p.Name).ToList())
        {
            MarkupDocument? doc = RedactionSupport.Load(package, part, limits);
            if (doc is null || doc.Root.IsNull)
            {
                continue;
            }

            foreach (MarkupNode source in RedactionSupport.Descendants(doc.Root).Where(static e => e.LocalNameString == "worksheetSource"))
            {
                string? sheet = source.GetAttribute("sheet");
                string? range = source.GetAttribute("ref");
                if (sheet is not null && bySheet.TryGetValue(sheet, out List<CellRangeRegion>? ranges)
                    && (range is null || !CellRangeRegion.TryParse(range.Replace("$", string.Empty, StringComparison.Ordinal), out int r1, out int c1, out int r2, out int c2) || ranges.Any(r => r.Intersects(r1, c1, r2, c2))))
                {
                    report.Gap("redaction.pivot-cache-source", part, "A pivot cache is built from an emptied range and still holds its values; redact it by match or remove the pivot table.");
                }
            }
        }
    }
}
