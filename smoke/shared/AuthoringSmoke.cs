using System.Linq;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>The authoring calls of the three hubs, and sanitising in memory, under NativeAOT and WASM.</summary>
internal static class AuthoringSmoke
{
    // A 1 x 1 pixel PNG.
    private static readonly byte[] s_png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static List<string> Run()
    {
        var failures = new List<string>();
        void Check(string name, bool ok)
        {
            if (!ok)
            {
                failures.Add(name);
            }
        }

        using (Document doc = Document.Create())
        {
            doc.Styles.AddBuiltIn("Heading 1");
            Paragraph h = doc.Body.AddParagraph("Title");
            h.Style = "Heading 1";
            Run r = doc.Body.AddParagraph(string.Empty).AddRun("bold");
            r.Bold = true;
            r.FontSize = 14;
            Table t = doc.Body.AddTable([["a", "b"], ["c", "d"]]);
            doc.Body.AddPicture(s_png, 50);
            doc.Body.Paragraphs().Last().AddHyperlink("link", "https://example.com");
            Check("doc style", h.Style == "Heading 1");
            Check("doc run", r.Bold == true && r.FontSize == 14);
            Check("doc table", t[1, 1].Text == "d");
            Check("doc unknown style", ThrowsStyle(doc));
            Check("doc sanitize", doc.Sanitize().After is not null);
            Check("doc after sanitize", doc.Paragraphs.Any(p => p.Text == "Title"));
        }

        using (Workbook wb = Workbook.Create())
        {
            Worksheet ws = wb.Sheets.Add("S");
            ws.Range("A1:B2").Values = new object?[,] { { "x", 1 }, { 2.5, true } };
            ws.Range("A1:B2").NumberFormat = "0.00";
            ws.AddPicture(s_png, "D2", 20, 20);
            ws.Range("A4:B4").Merge();
            Check("sheet values", ws.Range("A1:B2").Values[0, 0] as string == "x" && ws["A2"].Number == 2.5);
            Check("sheet merge", ws.MergedCells.Count == 1);
        }

        using (Presentation deck = Presentation.Create())
        {
            Slide slide = deck.AddSlide("Title and Content");
            slide.SetPlaceholderText("title", "Hello");
            slide.AddTable([["a", "b"]], 10, 200, 300);
            slide.AddPicture(s_png, 10, 10, 20);
            Check("deck title", slide.Title == "Hello");
            Check("deck shapes", slide.Shapes.Any(s => s.Kind == ShapeKind.Picture) && slide.Shapes.Any(s => s.Table is not null));
            Check("deck layout error", ThrowsLayout(deck));
        }

        return failures;
    }

    private static bool ThrowsStyle(Document doc)
    {
        try
        {
            doc.Body.AddParagraph("x").Style = "No Such Style";
            return false;
        }
        catch (StyleNotFoundException)
        {
            return true;
        }
    }

    private static bool ThrowsLayout(Presentation deck)
    {
        try
        {
            deck.AddSlide("No Such Layout");
            return false;
        }
        catch (PresentationAuthoringException)
        {
            return true;
        }
    }
}
