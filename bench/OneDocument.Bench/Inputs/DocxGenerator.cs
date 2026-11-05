using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace RecursiveAI.OneDocument.Bench.Inputs;

/// <summary>
/// What one generated benchmark document looks like. The same spec always yields the same bytes on the same runtime.
/// </summary>
/// <param name="Name">The input's name, used in file names and as the benchmark parameter value.</param>
/// <param name="Paragraphs">Body paragraphs to write (headings and a small table are mixed in at fixed intervals).</param>
/// <param name="ParagraphsPerPage">Insert a hard page break after this many paragraphs; 0 for none.</param>
/// <param name="MediaBytes">Total size of incompressible embedded parts, stored without compression, to reach a target file size.</param>
/// <param name="Seed">Seed of the text and media generator.</param>
public sealed record DocxSpec(string Name, int Paragraphs, int ParagraphsPerPage, int MediaBytes, ulong Seed);

/// <summary>
/// Writes benchmark DOCX files from nothing but a <see cref="DocxSpec"/>: plain WordprocessingML written as text, a fixed list of
/// words, a fixed-seed generator and fixed zip timestamps, so every run on every machine benchmarks the same documents. No
/// comparator library is involved in producing them.
/// </summary>
public static class DocxGenerator
{
    /// <summary>The documents the benchmarks use, from a page or two up to a thousand pages and a 50 MB package.</summary>
    public static IReadOnlyList<DocxSpec> Standard { get; } =
    [
        new("small", 40, 0, 0, 1),
        new("medium", 3_000, 0, 600_000, 2),
        new("large", 60_000, 0, 45_000_000, 3),
        new("pages1000", 35_000, 35, 0, 4),
    ];

    private static readonly DateTimeOffset FixedTime = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] Words =
    [
        "report", "quarter", "budget", "review", "policy", "section", "draft", "client", "north", "region",
        "forecast", "meeting", "action", "owner", "status", "risk", "plan", "update", "market", "sales",
        "annual", "summary", "notes", "figure", "table", "result", "method", "scope", "value", "total",
        "approved", "pending", "revised", "internal", "external", "contract", "invoice", "supplier", "delivery", "schedule",
        "project", "team", "target", "growth", "margin", "volume", "price", "demand", "supply", "chain",
        "quality", "audit", "control", "process", "system", "data", "model", "design", "test", "release",
        "alpha", "bravo", "charlie", "delta",
    ];

    /// <summary>Writes the document for <paramref name="spec"/> to <paramref name="path"/> and returns its SHA-256 as lower-case hex.</summary>
    /// <param name="spec">What to write.</param>
    /// <param name="path">The file to create or replace.</param>
    /// <returns>The hash of the file, recorded in the manifest so a change of runtime or generator is visible.</returns>
    public static string Write(DocxSpec spec, string path)
    {
        ArgumentNullException.ThrowIfNull(spec);
        using (FileStream fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            AddText(zip, "[Content_Types].xml", ContentTypes(spec));
            AddText(zip, "_rels/.rels", RootRels);
            AddText(zip, "docProps/core.xml", Core);
            AddText(zip, "docProps/app.xml", App);
            AddText(zip, "word/_rels/document.xml.rels", DocumentRels(spec));
            AddText(zip, "word/styles.xml", Styles);
            WriteDocument(zip, spec);
            WriteMedia(zip, spec);
        }

        using FileStream read = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(read));
    }

    /// <summary>Generates every standard input into <paramref name="directory"/> that is not already there and returns name → path.</summary>
    /// <param name="directory">Where the files go; created when missing.</param>
    /// <param name="names">Restrict to these input names; null for all.</param>
    /// <returns>The path of each requested input.</returns>
    public static Dictionary<string, string> Ensure(string directory, IReadOnlyCollection<string>? names = null)
    {
        Directory.CreateDirectory(directory);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DocxSpec spec in Standard)
        {
            if (names is not null && !names.Contains(spec.Name))
            {
                continue;
            }

            string path = Path.Combine(directory, spec.Name + ".docx");
            if (!File.Exists(path))
            {
                string tmp = path + ".tmp";
                Write(spec, tmp);
                File.Move(tmp, path, overwrite: true);
            }

            result[spec.Name] = path;
        }

        return result;
    }

    private static void WriteDocument(ZipArchive zip, DocxSpec spec)
    {
        ZipArchiveEntry entry = zip.CreateEntry("word/document.xml", CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTime;
        using Stream s = entry.Open();
        using var w = new StreamWriter(s, new UTF8Encoding(false), 64 * 1024);
        var rng = new Rng(spec.Seed);
        w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        w.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body>");
        for (int i = 0; i < spec.Paragraphs; i++)
        {
            bool pageBreak = spec.ParagraphsPerPage > 0 && i % spec.ParagraphsPerPage == spec.ParagraphsPerPage - 1;
            if (i % 40 == 0)
            {
                w.Write("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>");
                WriteWords(w, rng, 3, 6);
                w.Write("</w:t></w:r></w:p>");
            }
            else if (i % 25 == 7)
            {
                WriteTable(w, rng);
            }
            else
            {
                w.Write("<w:p>");
                int runs = 1 + (int)(rng.Next() % 3);
                for (int r = 0; r < runs; r++)
                {
                    w.Write(r == 1 ? "<w:r><w:rPr><w:b/></w:rPr><w:t xml:space=\"preserve\">" : "<w:r><w:t xml:space=\"preserve\">");
                    WriteWords(w, rng, 4, 20);
                    w.Write(" </w:t></w:r>");
                }

                if (pageBreak)
                {
                    w.Write("<w:r><w:br w:type=\"page\"/></w:r>");
                }

                w.Write("</w:p>");
            }

            if (pageBreak && (i % 40 == 0 || i % 25 == 7))
            {
                w.Write("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
            }
        }

        w.Write("<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>");
    }

    private static void WriteTable(StreamWriter w, Rng rng)
    {
        w.Write("<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/></w:tblGrid>");
        for (int row = 0; row < 3; row++)
        {
            w.Write("<w:tr>");
            for (int col = 0; col < 3; col++)
            {
                w.Write("<w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>");
                WriteWords(w, rng, 1, 4);
                w.Write("</w:t></w:r></w:p></w:tc>");
            }

            w.Write("</w:tr>");
        }

        w.Write("</w:tbl>");
        // ECMA-376 Part 1 §17.4: a table cell must end with a paragraph, and a body should not end on a table, so one follows.
        w.Write("<w:p/>");
    }

    private static void WriteWords(StreamWriter w, Rng rng, int min, int max)
    {
        int n = min + (int)(rng.Next() % (ulong)(max - min + 1));
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                w.Write(' ');
            }

            w.Write(Words[(int)(rng.Next() % (ulong)Words.Length)]);
        }
    }

    private static void WriteMedia(ZipArchive zip, DocxSpec spec)
    {
        // Opaque embedded parts stand in for pictures: stored, not deflated, because random bytes do not compress.
        var rng = new Rng(spec.Seed ^ 0x9E3779B97F4A7C15UL);
        int remaining = spec.MediaBytes;
        int index = 1;
        byte[] buffer = new byte[64 * 1024];
        while (remaining > 0)
        {
            int part = Math.Min(remaining, 5_000_000);
            ZipArchiveEntry entry = zip.CreateEntry($"word/embeddings/oleObject{index}.bin", CompressionLevel.NoCompression);
            entry.LastWriteTime = FixedTime;
            using Stream s = entry.Open();
            for (int left = part; left > 0;)
            {
                int n = Math.Min(left, buffer.Length);
                rng.Fill(buffer.AsSpan(0, n));
                s.Write(buffer, 0, n);
                left -= n;
            }

            remaining -= part;
            index++;
        }
    }

    private static string ContentTypes(DocxSpec spec)
    {
        string bin = spec.MediaBytes > 0 ? "<Default Extension=\"bin\" ContentType=\"application/vnd.openxmlformats-officedocument.oleObject\"/>" : string.Empty;
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" + bin
            + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
            + "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>"
            + "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>"
            + "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/></Types>";
    }

    private static string DocumentRels(DocxSpec spec)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        sb.Append("<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        int index = 1;
        for (int remaining = spec.MediaBytes; remaining > 0; remaining -= 5_000_000, index++)
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"<Relationship Id=\"rId{index + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject\" Target=\"embeddings/oleObject{index}.bin\"/>");
        }

        return sb.Append("</Relationships>").ToString();
    }

    private const string RootRels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>"
        + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>"
        + "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/></Relationships>";

    private const string Core = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
        + "<dc:title>Benchmark input</dc:title><dc:creator>OneDocument benchmark generator</dc:creator><dcterms:created xsi:type=\"dcterms:W3CDTF\">2020-01-01T00:00:00Z</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">2020-01-01T00:00:00Z</dcterms:modified></cp:coreProperties>";

    private const string App = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>OneDocument benchmark generator</Application></Properties>";

    private const string Styles = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
        + "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/><w:sz w:val=\"22\"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"160\" w:line=\"259\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>"
        + "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>"
        + "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/><w:pPr><w:keepNext/><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"32\"/></w:rPr></w:style></w:styles>";

    private static void AddText(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTime;
        using Stream s = entry.Open();
        s.Write(new UTF8Encoding(false).GetBytes(content));
    }

    /// <summary>xorshift64*: a few lines, the same sequence everywhere, independent of the runtime's own generators.</summary>
    private sealed class Rng(ulong seed)
    {
        private ulong _state = seed == 0 ? 0x2545F4914F6CDD1DUL : seed;

        public ulong Next()
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return (_state * 0x2545F4914F6CDD1DUL) >> 11;
        }

        public void Fill(Span<byte> destination)
        {
            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = (byte)(Next() >> 7);
            }
        }
    }
}
