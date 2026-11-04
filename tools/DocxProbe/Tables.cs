using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Documents;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Tools.DocxProbe;

internal static partial class Program
{
    /// <summary>
    /// <c>DocxProbe tables FILE...</c> prints one JSON line per file with table, merge, section and header/footer counts.
    /// </summary>
    private static int TablesAndSections(string[] files)
    {
        int failures = 0;
        foreach (string file in files)
        {
            var defects = new DefectCollection();
            try
            {
                using InMemoryPackage package = Read(file);
                using Document doc = Document.FromPackage(package, new OpenContext(defects: defects));
                int tables = 0, nested = 0, rows = 0, cells = 0, spans = 0, vRegions = 0, vCont = 0, hCont = 0, orphans = 0, maxCols = 0;
                CountTables(doc, doc.Body.Blocks(), ref tables, ref nested, ref rows, ref cells, ref spans, ref vRegions, ref vCont, ref hCont, ref orphans, ref maxCols);

                IReadOnlyList<Section> sections = doc.Sections;
                int own = 0, linked = 0, blank = 0, landscape = 0, multiCol = 0, titlePg = 0, restarts = 0;
                foreach (Section s in sections)
                {
                    landscape += s.PageSize.Orientation == PageOrientation.Landscape ? 1 : 0;
                    multiCol += s.Columns.Count > 1 ? 1 : 0;
                    titlePg += s.TitlePage ? 1 : 0;
                    restarts += s.PageNumbering.Start is not null ? 1 : 0;
                    _ = s.Margins;
                    _ = s.LineNumbering;
                    _ = s.DocumentGrid;
                    foreach (HeaderFooterKind kind in new[] { HeaderFooterKind.Header, HeaderFooterKind.Footer })
                    {
                        foreach (HeaderFooterType type in new[] { HeaderFooterType.Default, HeaderFooterType.First, HeaderFooterType.Even })
                        {
                            ResolvedHeaderFooter r = s.Resolve(kind, type);
                            if (r.Story is null)
                            {
                                blank++;
                            }
                            else if (r.IsLinked)
                            {
                                linked++;
                            }
                            else
                            {
                                own++;
                            }
                        }
                    }
                }

                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["file"] = Path.GetFileName(file), ["ok"] = true, ["tables"] = tables, ["nested"] = nested, ["rows"] = rows, ["cells"] = cells, ["maxColumns"] = maxCols,
                    ["gridSpans"] = spans, ["vRegions"] = vRegions, ["vContinuations"] = vCont, ["hContinuations"] = hCont, ["orphans"] = orphans,
                    ["sections"] = sections.Count, ["landscape"] = landscape, ["multiColumn"] = multiCol, ["titlePg"] = titlePg, ["pageNumberRestarts"] = restarts,
                    ["evenAndOdd"] = doc.EvenAndOddHeaders, ["storiesOwn"] = own, ["storiesLinked"] = linked, ["storiesBlank"] = blank,
                    ["modified"] = doc.HasUncommittedChanges,
                    ["defects"] = defects.GroupBy(d => d.Code).ToDictionary(g => g.Key, g => g.Count()),
                }));
            }
            catch (Exception ex) when (ex is OfficeException or InvalidDataException or IOException)
            {
                failures++;
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?> { ["file"] = Path.GetFileName(file), ["ok"] = false, ["error"] = ex is OfficeException oe ? oe.Defect.Code : "container", ["message"] = ex.Message }));
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static void CountTables(Document doc, IEnumerable<BlockElement> blocks, ref int tables, ref int nested, ref int rows, ref int cells, ref int spans, ref int vRegions, ref int vCont, ref int hCont, ref int orphans, ref int maxCols)
    {
        foreach (BlockElement b in blocks)
        {
            switch (b)
            {
                case Table t:
                    {
                        tables++;
                        nested += t.IsNested ? 1 : 0;
                        LogicalTable lt = t.Logical();
                        rows += lt.RowCount;
                        cells += lt.Cells.Count;
                        maxCols = Math.Max(maxCols, lt.ColumnCount);
                        spans += lt.Merges.GridSpans;
                        vRegions += lt.Merges.VerticalRegions;
                        vCont += lt.Merges.VerticalContinuations;
                        hCont += lt.Merges.LegacyHorizontalContinuations;
                        orphans += lt.Merges.Orphans;
                        foreach (TableRow row in t.Rows())
                        {
                            foreach (TableCell c in row.Cells())
                            {
                                CountTables(doc, c.Blocks(), ref tables, ref nested, ref rows, ref cells, ref spans, ref vRegions, ref vCont, ref hCont, ref orphans, ref maxCols);
                            }
                        }

                        break;
                    }

                case BlockContentControl sdt:
                    CountTables(doc, sdt.Content.Blocks(), ref tables, ref nested, ref rows, ref cells, ref spans, ref vRegions, ref vCont, ref hCont, ref orphans, ref maxCols);
                    break;
            }
        }
    }

    /// <summary>
    /// <c>DocxProbe edit-tables IN OUT</c> applies one of each table edit to the first table with at least two rows and two columns (insert
    /// and delete a row and a column, set a cell's text) and writes the result as a new zip.
    /// </summary>
    private static Dictionary<string, object?> ApplyTableEdit(Document doc)
    {
        Table? table = doc.Body.Blocks().OfType<Table>().FirstOrDefault(t =>
        {
            LogicalTable l = t.Logical();
            return l.RowCount >= 2 && l.ColumnCount >= 2;
        });
        if (table is null)
        {
            return new Dictionary<string, object?> { ["edited"] = false };
        }

        LogicalTable before = table.Logical();
        table.InsertRow(1);
        table.InsertColumn(1);
        table.SetCellText(1, 1, "OneOffice <edit> & test");
        table.DeleteColumn(0);
        table.DeleteRow(0);
        LogicalTable after = table.Logical();
        return new Dictionary<string, object?>
        {
            ["edited"] = true, ["rowsBefore"] = before.RowCount, ["colsBefore"] = before.ColumnCount, ["rowsAfter"] = after.RowCount, ["colsAfter"] = after.ColumnCount,
            ["orphansAfter"] = after.Merges.Orphans,
        };
    }

    // --- generated section cases, for the page-structure comparison with LibreOffice ---

    private sealed record SectionSpec(int Pages, string? Header, string? First, string? Even, string? Footer, bool TitlePg, string Size);

    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// <c>DocxProbe generate-sections DIR</c> writes small documents whose pages are known (explicit page breaks) with one marker token in
    /// each header or footer, plus <c>NAME.pages</c> with the pages per section.
    /// </summary>
    private static int GenerateSections(string dir)
    {
        Directory.CreateDirectory(dir);
        Case(dir, "sec-orient", false,
            new SectionSpec(2, "H_A1", null, null, null, false, "12240x15840"),
            new SectionSpec(1, null, null, null, null, false, "15840x12240 landscape"),
            new SectionSpec(2, null, null, null, "F_A3", false, "11906x16838"));
        Case(dir, "sec-titlepg", false,
            new SectionSpec(3, "H_DEF1", "H_FIRST1", null, null, true, "12240x15840"),
            new SectionSpec(2, null, null, null, null, false, "12240x15840"),
            new SectionSpec(2, null, null, null, null, true, "12240x15840"));
        Case(dir, "sec-evenodd", true,
            new SectionSpec(4, "H_ODD1", null, "H_EVEN1", "F_ODD1", false, "12240x15840"),
            new SectionSpec(3, "H_ODD2", null, null, null, false, "12240x15840"));
        Case(dir, "sec-all", true,
            new SectionSpec(3, "H_D1", "H_F1", "H_E1", "F_D1", true, "12240x15840"),
            new SectionSpec(2, "H_D2", null, null, null, true, "12240x15840"),
            new SectionSpec(2, null, "H_F3", null, "F_D3", false, "12240x15840 landscape"));
        return 0;
    }

    private static void Case(string dir, string name, bool evenAndOdd, params SectionSpec[] sections)
    {
        var body = new StringBuilder();
        var parts = new List<(string Name, string Xml)>();
        var rels = new StringBuilder();
        int next = 10;
        string Story(string kind, string token)
        {
            string id = "rId" + next++;
            string file = (kind == "header" ? "header" : "footer") + (next - 10) + ".xml";
            string root = kind == "header" ? "hdr" : "ftr";
            parts.Add(("word/" + file, $"<w:{root} xmlns:w=\"{W}\"><w:p><w:r><w:t>{token}</w:t></w:r></w:p></w:{root}>"));
            rels.Append($"<Relationship Id=\"{id}\" Type=\"{R}/{kind}\" Target=\"{file}\"/>");
            return id;
        }

        for (int s = 0; s < sections.Length; s++)
        {
            SectionSpec spec = sections[s];
            var sect = new StringBuilder("<w:sectPr>");
            if (spec.Header is not null) sect.Append($"<w:headerReference w:type=\"default\" r:id=\"{Story("header", spec.Header)}\"/>");
            if (spec.First is not null) sect.Append($"<w:headerReference w:type=\"first\" r:id=\"{Story("header", spec.First)}\"/>");
            if (spec.Even is not null) sect.Append($"<w:headerReference w:type=\"even\" r:id=\"{Story("header", spec.Even)}\"/>");
            if (spec.Footer is not null) sect.Append($"<w:footerReference w:type=\"default\" r:id=\"{Story("footer", spec.Footer)}\"/>");
            string[] size = spec.Size.Split(' ');
            string[] wh = size[0].Split('x');
            sect.Append($"<w:pgSz w:w=\"{wh[0]}\" w:h=\"{wh[1]}\"{(size.Length > 1 ? " w:orient=\"landscape\"" : string.Empty)}/>");
            sect.Append("<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/>");
            if (spec.TitlePg) sect.Append("<w:titlePg/>");
            sect.Append("</w:sectPr>");
            for (int p = 1; p <= spec.Pages; p++)
            {
                string brk = p > 1 ? "<w:r><w:br w:type=\"page\"/></w:r>" : string.Empty;
                bool last = p == spec.Pages;
                string props = last && s < sections.Length - 1 ? "<w:pPr>" + sect + "</w:pPr>" : string.Empty;
                body.Append($"<w:p>{props}{brk}<w:r><w:t>BODY_S{s + 1}P{p}</w:t></w:r></w:p>");
            }

            if (s == sections.Length - 1)
            {
                body.Append(sect);
            }
        }

        string settings = evenAndOdd ? $"<w:settings xmlns:w=\"{W}\"><w:evenAndOddHeaders/></w:settings>" : string.Empty;
        if (evenAndOdd)
        {
            rels.Append($"<Relationship Id=\"rId2\" Type=\"{R}/settings\" Target=\"settings.xml\"/>");
        }

        using FileStream fs = File.Create(Path.Combine(dir, name + ".docx"));
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            void Add(string path, string xml)
            {
                using Stream o = zip.CreateEntry(path).Open();
                o.Write(Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" + xml));
            }

            var ct = new StringBuilder("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            ct.Append("<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>");
            foreach ((string pn, _) in parts)
            {
                string kind = pn.Contains("header", StringComparison.Ordinal) ? "header" : "footer";
                ct.Append($"<Override PartName=\"/{pn}\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.{kind}+xml\"/>");
            }

            if (evenAndOdd)
            {
                ct.Append("<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>");
            }

            Add("[Content_Types].xml", ct + "</Types>");
            Add("_rels/.rels", $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{R}/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add("word/_rels/document.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + rels + "</Relationships>");
            Add("word/document.xml", $"<w:document xmlns:w=\"{W}\" xmlns:r=\"{R}\"><w:body>{body}</w:body></w:document>");
            foreach ((string pn, string xml) in parts)
            {
                Add(pn, xml);
            }

            if (evenAndOdd)
            {
                Add("word/settings.xml", settings);
            }
        }

        File.WriteAllText(Path.Combine(dir, name + ".pages"), string.Join(",", sections.Select(s => s.Pages)));
    }

    /// <summary>
    /// <c>DocxProbe expect FILE PAGES</c> prints, for every page of a document whose sections have the given page counts (comma separated),
    /// the page size in points and the header and footer text the hub resolves for it.
    /// </summary>
    private static int Expect(string file, string pagesCsv)
    {
        using InMemoryPackage package = Read(file);
        using Document doc = Document.FromPackage(package);
        int[] counts = pagesCsv.Split(',').Select(int.Parse).ToArray();
        IReadOnlyList<Section> sections = doc.Sections;
        long pageNumber = 0;
        var pages = new List<Dictionary<string, object?>>();
        for (int s = 0; s < sections.Count && s < counts.Length; s++)
        {
            Section sec = sections[s];
            if (sec.PageNumbering.Start is { } start)
            {
                pageNumber = start - 1;
            }

            PageSize size = sec.PageSize;
            for (int p = 0; p < counts[s]; p++)
            {
                pageNumber++;
                pages.Add(new Dictionary<string, object?>
                {
                    ["w"] = size.Width / 20.0, ["h"] = size.Height / 20.0,
                    ["header"] = sec.ForPage(HeaderFooterKind.Header, p == 0, pageNumber).Story?.Text,
                    ["footer"] = sec.ForPage(HeaderFooterKind.Footer, p == 0, pageNumber).Story?.Text,
                });
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(pages));
        return 0;
    }
}
