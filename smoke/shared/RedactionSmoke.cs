using System.IO.Compression;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Redaction;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Redacts a split-run secret from a small document by literal and by pattern, verifies the output: under NativeAOT and WASM (regular expressions, markup edits, the verifier).</summary>
internal static class RedactionSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(zip, "word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Call 555-</w:t></w:r><w:r><w:t>0142 or write to ZEBRA7731.</w:t></w:r></w:p></w:body></w:document>");
        }

        foreach (RedactionMatch match in new[] { RedactionMatch.Literal("ZEBRA7731"), RedactionMatch.Pattern(@"\d{3}-\d{4}") })
        {
            ms.Position = 0;
            using Document doc = Document.Open(ms, leaveOpen: true);
            var plan = new RedactionPlan();
            plan.Matches.Add(match);
            using var output = new MemoryStream();
            RedactionReport report = doc.Redact(plan, output);
            string text = Encoding.UTF8.GetString(output.ToArray());
            output.Position = 0;
            using Document again = Document.Open(output, leaveOpen: true);
            string extracted = again.ExtractText();
            bool literal = !match.IsRegex;
            if (!report.Verified || report.TotalReplacements != 1 || extracted.Contains(literal ? "ZEBRA7731" : "555-0142", StringComparison.Ordinal) || !extracted.Contains((char)0x2588) || text.Length == 0)
            {
                failures.Add("redact " + (literal ? "literal" : "pattern"));
            }
        }

        return failures;
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using Stream s = zip.CreateEntry(name).Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
