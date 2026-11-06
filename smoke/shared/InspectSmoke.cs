using System.Text;
using RecursiveAI.OneDocument.Inspection;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Inspects and sanitises a small document with a comment, hidden text and personal properties, under NativeAOT and WASM.</summary>
internal static class InspectSmoke
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    internal static List<string> Run()
    {
        var failures = new List<string>();
        RecursiveAI.OneDocument.Documents.DocumentFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        using var source = new MemoryStream();
        using (OpcPackage p = OpcPackage.Create())
        {
            p.SetRelationships(null,
            [
                new Relationship("rId1", Rel + "officeDocument", "word/document.xml"),
                new Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"),
            ]);
            p.AddPart("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", Encoding.UTF8.GetBytes(
                "<w:document xmlns:w=\"" + W + "\"><w:body><w:p><w:commentRangeStart w:id=\"0\"/><w:r><w:t>shown</w:t></w:r><w:commentRangeEnd w:id=\"0\"/></w:p><w:p><w:r><w:rPr><w:vanish/></w:rPr><w:t>secret</w:t></w:r></w:p></w:body></w:document>"));
            p.SetRelationships("/word/document.xml", [new Relationship("rId1", Rel + "comments", "comments.xml")]);
            p.AddPart("/word/comments.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml", Encoding.UTF8.GetBytes(
                "<w:comments xmlns:w=\"" + W + "\"><w:comment w:id=\"0\" w:author=\"A\"><w:p><w:r><w:t>note</w:t></w:r></w:p></w:comment></w:comments>"));
            p.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Encoding.UTF8.GetBytes(
                "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:creator>Someone</dc:creator></cp:coreProperties>"));
            p.WriteTo(source);
        }

        InspectionReport report = Inspector.Inspect(source);
        Check(failures, "inspect comments", report.CountOf(InspectionCategory.Comments) == 1);
        Check(failures, "inspect hidden text", report.CountOf(InspectionCategory.HiddenText) == 1);
        Check(failures, "inspect properties", report.CountOf(InspectionCategory.Properties) == 1);

        using var output = new MemoryStream();
        SanitizeReport sanitized = Sanitizer.Sanitize(source, output);
        Check(failures, "sanitize report", sanitized.StatusOf(InspectionCategory.Comments) == SanitizeStatus.Removed && sanitized.After.IsEmpty);
        output.Position = 0;
        Check(failures, "sanitized file is clean", Inspector.Inspect(output).IsEmpty);
        return failures;
    }

    private static void Check(List<string> failures, string name, bool ok)
    {
        if (!ok)
        {
            failures.Add(name);
        }
    }
}
