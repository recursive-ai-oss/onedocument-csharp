using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Tools.AuthoringProbe;

/// <summary>
/// Applies the authoring calls to every OOXML file under a directory and prints one JSON line per file (information only):
///   AuthoringProbe &lt;inputDir&gt; &lt;outputDir&gt; [--limit N]
/// A document gets a paragraph, a formatted run, a link, a picture and a table; a workbook gets a range of values, a number format and a
/// picture; a deck gets a slide with a table and a picture. The result is saved, opened again, and compared part by part with the input:
/// `changed` lists the parts whose bytes differ, `added` and `removed` the others. The last line is a summary. Outputs stay in outputDir so the
/// schema validator and LibreOffice can be run over them.
/// </summary>
internal static class Program
{
    // A 1 x 1 pixel PNG.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: AuthoringProbe <inputDir> <outputDir> [--limit N]");
            return 2;
        }

        DocumentFormats.Register(FormatRegistry.Default);
        SpreadsheetFormats.Register(FormatRegistry.Default);
        PresentationFormats.Register(FormatRegistry.Default);
        int limit = int.MaxValue;
        for (int i = 2; i + 1 < args.Length; i++)
        {
            if (args[i] == "--limit")
            {
                limit = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
            }
        }

        Directory.CreateDirectory(args[1]);
        int files = 0, ok = 0, typed = 0, untyped = 0;
        foreach (string path in Directory.EnumerateFiles(args[0], "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".docx" or ".xlsx" or ".pptx") || files >= limit)
            {
                continue;
            }

            files++;
            string output = Path.Combine(args[1], files.ToString("D4", CultureInfo.InvariantCulture) + ext);
            var watch = Stopwatch.StartNew();
            string status = "ok";
            string? error = null;
            List<string> changed = [], added = [], removed = [];
            try
            {
                Edit(path, output, ext);
                Compare(path, output, changed, added, removed);
                ok++;
            }
            catch (OfficeException ex)
            {
                status = "typed";
                error = ex.Defect.Code + ": " + ex.Message;
                typed++;
            }
            catch (Exception ex)
            {
                status = "untyped";
                error = ex.GetType().Name + ": " + ex.Message;
                untyped++;
            }

            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["file"] = Path.GetRelativePath(args[0], path),
                ["output"] = status == "ok" ? Path.GetFileName(output) : null,
                ["status"] = status,
                ["error"] = error,
                ["changed"] = changed,
                ["added"] = added,
                ["removed"] = removed,
                ["ms"] = watch.ElapsedMilliseconds,
            }));
        }

        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["kind"] = "summary", ["files"] = files, ["ok"] = ok, ["typed"] = typed, ["untyped"] = untyped,
        }));
        return untyped == 0 ? 0 : 1;
    }

    private static void Edit(string input, string output, string ext)
    {
        switch (ext)
        {
            case ".docx":
                using (Document doc = Document.Open(input))
                {
                    Paragraph p = doc.Body.AddParagraph("Authoring probe");
                    Run r = p.AddRun(" bold");
                    r.Bold = true;
                    r.FontSize = 11;
                    p.AddHyperlink("link", "https://example.com");
                    doc.Body.AddTable([["a", "b"], ["c", "d"]]);
                    doc.Body.AddPicture(Png, 30, altText: "probe");
                    doc.SaveAs(output, doc.Format);
                }

                break;
            case ".xlsx":
                using (Workbook wb = Workbook.Open(input))
                {
                    Worksheet sheet = wb.Sheets.Count > 0 ? wb.Sheets[0] : wb.Sheets.Add("Probe");
                    if (sheet.Kind == SheetKind.Worksheet)
                    {
                        sheet.Range("AA1000:AB1001").Values = new object?[,] { { "x", 1 }, { 2.5, true } };
                        sheet.Range("AB1000:AB1001").NumberFormat = "0.00";
                        sheet.AddPicture(Png, "AD1000", 16, 16);
                    }

                    wb.SaveAs(output, wb.Format);
                }

                break;
            default:
                using (Presentation deck = Presentation.Open(input))
                {
                    if (deck.Masters.Count == 0 || deck.Masters[0].Layouts.Count == 0)
                    {
                        deck.SaveAs(output, deck.Format);
                        break;
                    }

                    Slide slide = deck.AddSlide(deck.Masters[0].Layouts[0]);
                    slide.AddTable([["a", "b"]], 10, 10, 200);
                    slide.AddPicture(Png, 10, 100, 20);
                    deck.SaveAs(output, deck.Format);
                }

                break;
        }
    }

    private static void Compare(string input, string output, List<string> changed, List<string> added, List<string> removed)
    {
        using OpcPackage a = OpcPackage.Open(input);
        using OpcPackage b = OpcPackage.Open(output);
        foreach (IPackagePart part in a.Parts)
        {
            if (b.FindPart(part.Name) is not { } now)
            {
                removed.Add(part.Name);
            }
            else if (!now.GetBytes().Span.SequenceEqual(part.GetBytes().Span))
            {
                changed.Add(part.Name);
            }
        }

        foreach (IPackagePart part in b.Parts)
        {
            if (a.FindPart(part.Name) is null)
            {
                added.Add(part.Name);
            }
        }

        // Reopening through the hub proves the output reads.
        string ext = Path.GetExtension(output);
        if (ext == ".docx")
        {
            using Document d = Document.Open(output);
            _ = d.Paragraphs.Count();
        }
        else if (ext == ".xlsx")
        {
            using Workbook w = Workbook.Open(output);
            _ = w.Sheets.Count;
        }
        else
        {
            using Presentation p = Presentation.Open(output);
            _ = p.Slides.Count;
        }
    }
}
