using System.IO.Compression;
using System.Text;
#if !ONLY_SHEETS && !ONLY_SLIDES
using RecursiveAI.OneDocument.Documents;
#endif

namespace RecursiveAI.OneDocument.Consumer;

/// <summary>
/// An application that uses only the Office statics and never calls a registration method. With the Spreadsheets package referenced
/// the workbook opens; without it the open raises FormatNotAvailableException naming the format. ONLY_SHEETS and ONLY_SLIDES build
/// the variants that reference a single kind package (and so no Documents code at all).
/// </summary>
internal static class Program
{
    private static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "onedocument-consumer-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        var failures = new List<string>();
        try
        {
#if ONLY_SHEETS
            string xlsx = Path.Combine(dir, "a.xlsx");
            WriteWorkbook(xlsx);
            Check(failures, "xlsx text", () => Office.ExtractText(xlsx).Contains("hello xlsx", StringComparison.Ordinal));
            Check(failures, "xlsx open", () =>
            {
                using OfficeFile f = Office.Open(xlsx);
                return f.Kind == OfficeKind.Workbook && f.Format == OfficeFormat.Xlsx;
            });
#elif ONLY_SLIDES
            string pptx = Path.Combine(dir, "a.pptx");
            WritePresentation(pptx);
            Check(failures, "pptx text", () => Office.ExtractText(pptx).Contains("hello pptx", StringComparison.Ordinal));
            Check(failures, "pptx open", () =>
            {
                using OfficeFile f = Office.Open(pptx);
                return f.Kind == OfficeKind.Presentation && f.Format == OfficeFormat.Pptx;
            });
            Check(failures, "xlsx not available", () =>
            {
                string xlsx = Path.Combine(dir, "a.xlsx");
                WriteWorkbook(xlsx);
                try
                {
                    using OfficeFile f = Office.Open(xlsx);
                    return false;
                }
                catch (FormatNotAvailableException e)
                {
                    return e.Format == OfficeFormat.Xlsx;
                }
            });
#else
            string docx = Path.Combine(dir, "a.docx");
            using (Document d = Document.Create())
            {
                d.Body.AddParagraph("hello docx");
                d.SaveAs(docx);
            }

            string xlsx = Path.Combine(dir, "a.xlsx");
            WriteWorkbook(xlsx);

            Check(failures, "docx text", () => Office.ExtractText(docx).Contains("hello docx", StringComparison.Ordinal));
            Check(failures, "xlsx text", () => Office.ExtractText(xlsx).Contains("hello xlsx", StringComparison.Ordinal));
            Check(failures, "docx open", () =>
            {
                using OfficeFile f = Office.Open(docx);
                return f.Kind == OfficeKind.Document && f.Format == OfficeFormat.Docx;
            });
#if WITH_ALL
            // The metapackage alone brings in the rendering package and its registration: no call names it.
            Check(failures, "docx to pdf", () =>
            {
                string pdf = Path.Combine(dir, "a.pdf");
                Office.Convert(docx, pdf);
                return Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 5) == "%PDF-";
            });
#endif
#if WITH_SPREADSHEETS
            Check(failures, "xlsx open", () =>
            {
                using OfficeFile f = Office.Open(xlsx);
                return f.Kind == OfficeKind.Workbook && f.Format == OfficeFormat.Xlsx;
            });
#else
            Check(failures, "xlsx not available", () =>
            {
                try
                {
                    using OfficeFile f = Office.Open(xlsx);
                    return false;
                }
                catch (FormatNotAvailableException e)
                {
                    Console.WriteLine("expected: " + e.Message);
                    return e.Format == OfficeFormat.Xlsx && e.Message.Contains("Xlsx", StringComparison.Ordinal);
                }
            });
#endif
#endif
        }
        finally
        {
            Directory.Delete(dir, true);
        }

        foreach (string f in failures)
        {
            Console.Error.WriteLine("FAIL " + f);
        }

        Console.WriteLine(failures.Count == 0 ? "consumer: ok" : "consumer: failed");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Check(List<string> failures, string name, Func<bool> check)
    {
        try
        {
            if (!check())
            {
                failures.Add(name);
            }
        }
        catch (Exception e)
        {
            failures.Add(name + ": " + e.GetType().Name + " " + e.Message);
        }
    }

    // A minimal workbook written with the BCL alone, so the check does not depend on the package under test to make its input.
    private static void WriteWorkbook(string path)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
        Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        Add(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        Add(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
        Add(zip, "xl/worksheets/sheet1.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>hello xlsx</t></is></c></row></sheetData></worksheet>");
    }

#if ONLY_SLIDES
    // A minimal presentation written with the BCL alone.
    private static void WritePresentation(string path)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/><Override PartName=\"/ppt/slides/slide1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/></Types>");
        Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"ppt/presentation.xml\"/></Relationships>");
        Add(zip, "ppt/presentation.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><p:presentation xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><p:sldIdLst><p:sldId id=\"256\" r:id=\"rId1\"/></p:sldIdLst><p:sldSz cx=\"9144000\" cy=\"6858000\"/></p:presentation>");
        Add(zip, "ppt/_rels/presentation.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide1.xml\"/></Relationships>");
        Add(zip, "ppt/slides/slide1.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/><p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"T\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:p><a:r><a:t>hello pptx</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>");
    }
#endif

    private static void Add(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry e = zip.CreateEntry(name);
        using Stream s = e.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
