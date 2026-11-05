using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Documents;
using RecursiveAI.OneOffice.Documents.Numbering;
using RecursiveAI.OneOffice.Documents.Styles;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Tools.DocxProbe;

/// <summary>
/// Runs the style and numbering resolution over word-processing files and prints JSON lines.
/// <c>DocxProbe probe FILE...</c> resolves every paragraph and run and counts labels and defects (exit 1 when a file fails).
/// <c>DocxProbe labels FILE</c> prints one line per paragraph of the body: the list label, a tab and the text.
/// <c>DocxProbe edit IN OUT</c> adds a style and a list to a document and writes the result as a new zip.
/// <c>DocxProbe census FILE...</c> counts fields, notes and comments.
/// <c>DocxProbe annotate IN OUT</c> adds a comment with a reply and a footnote and rewrites a field result.
/// <c>DocxProbe tables FILE...</c> counts tables, merges, sections and header/footer stories; <c>edit-tables IN OUT</c>, <c>generate-sections DIR</c> and <c>expect FILE PAGES</c> support the table and section checks.
/// <c>DocxProbe lazyopen FILE</c> prints the bytes read from the file by opening it, reading the styles and reading the first paragraph.
/// <c>DocxProbe generate DIR</c> writes the style and numbering cases as small documents.
/// </summary>
internal static partial class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: DocxProbe probe|labels|edit|generate|sdt-census|sdt-edit|sdt-generate|census|annotate ARGS");
            return 2;
        }

        switch (args[0])
        {
            case "probe":
                return Probe(args[1..]);
            case "labels":
                return Labels(args[1]);
            case "edit":
                try
                {
                    return args.Length >= 3 ? Edit(args[1], args[2]) : 2;
                }
                catch (OfficeException ex)
                {
                    Console.Error.WriteLine(ex.Defect.Code + ": " + ex.Message);
                    return 1;
                }
            case "census":
                return Census(args[1..]);
            case "annotate":
                try
                {
                    return args.Length >= 3 ? Annotate(args[1], args[2]) : 2;
                }
                catch (OfficeException ex)
                {
                    Console.Error.WriteLine(ex.Defect.Code + ": " + ex.Message);
                    return 1;
                }
            case "tables":
                return TablesAndSections(args[1..]);
            case "edit-tables":
                return args.Length >= 3 ? EditWith(args[1], args[2], ApplyTableEdit) : 2;
            case "generate-sections":
                return GenerateSections(args[1]);
            case "expect":
                return args.Length >= 3 ? Expect(args[1], args[2]) : 2;
            case "sdt-census":
                return SdtCensus(args[1..]);
            case "sdt-edit":
                return args.Length >= 3 ? SdtEdit(args[1], args[2]) : 2;
            case "sdt-generate":
                SdtGenerate(args[1]);
                return 0;
            case "lazyopen":
                return LazyOpen(args[1]);
            case "generate":
                Cases.Write(args[1]);
                return 0;
            default:
                return 2;
        }
    }

    private static int Probe(string[] files)
    {
        int failures = 0;
        foreach (string file in files)
        {
            var defects = new DefectCollection();
            try
            {
                using InMemoryPackage package = Read(file);
                using Document doc = Document.FromPackage(package, new OpenContext(defects: defects));
                int paragraphs = 0, runs = 0, bold = 0, numbered = 0, labels = 0, needsWord = 0;
                foreach (Paragraph p in doc.Body.Paragraphs())
                {
                    paragraphs++;
                    ResolvedParagraphProperties pp = p.ResolvedProperties;
                    if (p.Numbering is not null)
                    {
                        numbered++;
                    }

                    foreach (Run r in p.Runs())
                    {
                        runs++;
                        if (r.ResolvedProperties.Bold)
                        {
                            bold++;
                        }
                    }

                    _ = pp;
                }

                foreach (ListLabel l in doc.Numbering.LabelBody())
                {
                    labels++;
                    if (l.NeedsWordOutput)
                    {
                        needsWord++;
                    }
                }

                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["file"] = Path.GetFileName(file), ["ok"] = true, ["paragraphs"] = paragraphs, ["runs"] = runs, ["boldRuns"] = bold,
                    ["numbered"] = numbered, ["labels"] = labels, ["labelsNeedingWord"] = needsWord, ["modified"] = doc.HasUncommittedChanges, ["styles"] = doc.Styles.Styles.Count,
                    ["defects"] = defects.GroupBy(d => d.Code).ToDictionary(g => g.Key, g => g.Count()),
                }));
            }
            catch (OfficeException ex)
            {
                failures++;
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?> { ["file"] = Path.GetFileName(file), ["ok"] = false, ["error"] = ex.Defect.Code, ["message"] = ex.Message }));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                failures++;
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?> { ["file"] = Path.GetFileName(file), ["ok"] = false, ["error"] = "container", ["message"] = ex.Message }));
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static int Labels(string file)
    {
        using InMemoryPackage package = Read(file);
        using Document doc = Document.FromPackage(package);
        Dictionary<Paragraph, ListLabel> byParagraph = doc.Numbering.LabelBody().ToDictionary(l => l.Paragraph);
        foreach (Paragraph p in doc.Body.Paragraphs())
        {
            Console.WriteLine((byParagraph.TryGetValue(p, out ListLabel? l) ? l!.Text : string.Empty) + "\t" + p.Text);
        }

        return 0;
    }

    /// <summary>Reads a zip with the BCL into an in-memory package (parts load lazily).</summary>
    internal static InMemoryPackage Read(string path)
    {
        var zip = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read);
        var package = new InMemoryPackage();
        ZipArchiveEntry? ct = zip.GetEntry("[Content_Types].xml");
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ct is not null)
        {
            MarkupDocument types = MarkupDocument.Parse(Bytes(ct));
            foreach (MarkupNode n in types.Root.ChildElements)
            {
                if (n.LocalNameString == "Default")
                {
                    defaults[n.GetAttribute("Extension") ?? string.Empty] = n.GetAttribute("ContentType") ?? string.Empty;
                }
                else if (n.LocalNameString == "Override")
                {
                    overrides[n.GetAttribute("PartName") ?? string.Empty] = n.GetAttribute("ContentType") ?? string.Empty;
                }
            }
        }

        var relParts = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry e in zip.Entries)
        {
            string name = "/" + e.FullName;
            if (e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            {
                relParts[name] = e;
                continue;
            }

            if (e.FullName.StartsWith('[') || e.FullName.EndsWith('/'))
            {
                continue;
            }

            string ext = Path.GetExtension(e.FullName).TrimStart('.');
            string type = overrides.TryGetValue(name, out string? o) ? o : defaults.GetValueOrDefault(ext, string.Empty);
            ZipArchiveEntry entry = e;
            package.AddLoadedPart(name, type, e.Length, () => Bytes(entry));
        }

        foreach ((string relName, ZipArchiveEntry e) in relParts)
        {
            // /_rels/.rels belongs to the package, /word/_rels/document.xml.rels to /word/document.xml.
            string? source = null;
            if (relName != "/_rels/.rels")
            {
                int i = relName.LastIndexOf("/_rels/", StringComparison.Ordinal);
                source = relName[..i] + "/" + relName[(i + 7)..^5];
            }

            var list = new List<Relationship>();
            MarkupDocument doc = MarkupDocument.Parse(Bytes(e));
            foreach (MarkupNode n in doc.Root.ChildElements)
            {
                bool external = string.Equals(n.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase);
                list.Add(new Relationship(n.GetAttribute("Id") ?? string.Empty, n.GetAttribute("Type") ?? string.Empty, n.GetAttribute("Target") ?? string.Empty,
                    external ? RelationshipTargetMode.External : RelationshipTargetMode.Internal));
            }

            package.SetRelationships(source, list);
        }

        return package;
    }

    private static byte[] Bytes(ZipArchiveEntry e)
    {
        using Stream s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}

/// <summary>The inheritance, precedence and numbering cases as small documents, written deterministically.</summary>
internal static class Cases
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static void Write(string dir)
    {
        Directory.CreateDirectory(dir);
        string normal = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";
        Save(dir, "num-multilevel", Body(Item(1, 0, "first") + Item(1, 1, "first.a") + Item(1, 1, "first.b") + Item(1, 2, "deep") + Item(1, 0, "second") + Item(1, 1, "second.a")),
            normal, Abs(0, Lvl(0, "decimal", "%1.") + Lvl(1, "decimal", "%1.%2") + Lvl(2, "lowerLetter", "(%3)")) + Num(1, 0));
        Save(dir, "num-formats", Body(Item(1, 0, "a") + Item(1, 0, "b") + Item(1, 0, "c") + Item(1, 0, "d") + Item(2, 0, "e") + Item(2, 0, "f") + Item(3, 0, "g") + Item(3, 0, "h") + Item(4, 0, "i") + Item(4, 0, "j")),
            normal, Abs(0, Lvl(0, "upperRoman", "%1.")) + Abs(1, Lvl(0, "lowerLetter", "%1)", 25)) + Abs(2, Lvl(0, "ordinal", "%1")) + Abs(3, Lvl(0, "decimalZero", "%1"))
            + Num(1, 0) + Num(2, 1) + Num(3, 2) + Num(4, 3));
        Save(dir, "num-continue-restart", Body(Item(1, 0, "a") + Item(1, 0, "b") + Item(2, 0, "continues") + Item(3, 0, "restarts") + Item(3, 0, "restarts") + Item(1, 0, "back")),
            normal, Abs(0, Lvl(0, "decimal", "%1.")) + Num(1, 0) + Num(2, 0) + Num(3, 0, "<w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"1\"/></w:lvlOverride>"));
        Save(dir, "num-legal", Body(Item(1, 0, "a") + Item(1, 0, "b") + Item(1, 1, "c")),
            normal, Abs(0, Lvl(0, "upperRoman", "%1.") + Lvl(1, "decimal", "%1.%2", 1, "<w:isLgl/>")) + Num(1, 0));
        Save(dir, "num-restart-rules", Body(Item(1, 0, "a") + Item(1, 1, "b") + Item(1, 0, "c") + Item(1, 1, "d")),
            normal, Abs(0, Lvl(0, "decimal", "%1.") + Lvl(1, "decimal", "%2.", 1, "<w:lvlRestart w:val=\"0\"/>")) + Num(1, 0));
        Save(dir, "num-style-linked", Body("<w:p><w:pPr><w:pStyle w:val=\"H1\"/></w:pPr><w:r><w:t>one</w:t></w:r></w:p><w:p><w:pPr><w:pStyle w:val=\"H2\"/></w:pPr><w:r><w:t>one.one</w:t></w:r></w:p><w:p><w:pPr><w:pStyle w:val=\"H2\"/></w:pPr><w:r><w:t>one.two</w:t></w:r></w:p><w:p><w:pPr><w:pStyle w:val=\"H1\"/></w:pPr><w:r><w:t>two</w:t></w:r></w:p>"),
            normal + "<w:style w:type=\"paragraph\" w:styleId=\"H1\"><w:name w:val=\"H1\"/><w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:style><w:style w:type=\"paragraph\" w:styleId=\"H2\"><w:name w:val=\"H2\"/><w:basedOn w:val=\"H1\"/><w:pPr><w:numPr><w:ilvl w:val=\"1\"/></w:numPr></w:pPr></w:style>",
            Abs(0, Lvl(0, "decimal", "%1.", 1, "<w:pStyle w:val=\"H1\"/>") + Lvl(1, "decimal", "%1.%2", 1, "<w:pStyle w:val=\"H2\"/>")) + Num(1, 0));
    }

    private static string Lvl(int i, string fmt, string text, long start = 1, string extra = "") =>
        $"<w:lvl w:ilvl=\"{i}\"><w:start w:val=\"{start}\"/><w:numFmt w:val=\"{fmt}\"/>{extra}<w:lvlText w:val=\"{text}\"/><w:lvlJc w:val=\"left\"/></w:lvl>";

    private static string Abs(int id, string levels) => $"<w:abstractNum w:abstractNumId=\"{id}\"><w:multiLevelType w:val=\"multilevel\"/>{levels}</w:abstractNum>";

    private static string Num(int id, int abs, string extra = "") => $"<w:num w:numId=\"{id}\"><w:abstractNumId w:val=\"{abs}\"/>{extra}</w:num>";

    private static string Item(int num, int lvl, string text) =>
        $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"{lvl}\"/><w:numId w:val=\"{num}\"/></w:numPr></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string Body(string inner) => inner;

    private static void Save(string dir, string name, string body, string styles, string numbering)
    {
        string path = Path.Combine(dir, name + ".docx");
        using FileStream fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/><Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/></Types>");
        Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Add(zip, "word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/></Relationships>");
        Add(zip, "word/document.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"{W}\"><w:body>{body}<w:sectPr/></w:body></w:document>");
        Add(zip, "word/styles.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:styles xmlns:w=\"{W}\">{styles}</w:styles>");
        Add(zip, "word/numbering.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:numbering xmlns:w=\"{W}\">{numbering}</w:numbering>");
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
        e.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using Stream s = e.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
