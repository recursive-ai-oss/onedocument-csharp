using System.Linq;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Appends and splits documents, copies a sheet between workbooks, inserts slides and updates fields: under NativeAOT and WASM.</summary>
internal static class OperationsSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        using (Document dst = Document.Create())
        using (Document src = Document.Create())
        {
            dst.Body.AddParagraph("one");
            src.Body.AddParagraph("two");
            src.Body.AddParagraph("three");
            MergeReport report = dst.Append(src);
            if (report.CountOf("blocks") != 2 || dst.Paragraphs.Select(p => p.Text).Last() != "three")
            {
                failures.Add("document append");
            }

            using SplitResult split = dst.SplitBefore([1, 2]);
            if (split.Pieces.Count != 3 || split.Pieces[1].Document.Paragraphs.Single().Text != "two")
            {
                failures.Add("document split");
            }

            FieldUpdateReport fields = dst.UpdateFields();
            if (fields.LayoutPasses < 1)
            {
                failures.Add("update fields");
            }
        }

        using (var from = Workbook.Create())
        using (var to = Workbook.Create())
        {
            from.Sheets[0]["A1"].SetNumber(4);
            from.Sheets[0]["A2"].Formula = "A1*2";
            from.Sheets.Add("Other")["A1"].SetNumber(1);
            from.Sheets[0]["A3"].Formula = "Other!A1+A1";
            SheetCopyResult r = from.Sheets[0].CopyTo(to, new SheetCopyOptions { Name = "Copy" });
            if (r.Sheet.Name != "Copy" || r.Sheet["A3"].Formula != "[1]Other!A1+A1" || to.ExternalLinks.Count != 1)
            {
                failures.Add("sheet copy");
            }

            using WorkbookSplitResult pieces = from.SplitBySheet();
            if (pieces.Pieces.Count != 2)
            {
                failures.Add("workbook split");
            }
        }

        using (Presentation a = Presentation.Create())
        using (Presentation b = Presentation.Create())
        {
            a.AddSlide(a.Masters[0].Layouts[1]).Shapes[0].TextBody!.SetText("first");
            b.AddSlide(b.Masters[0].Layouts[1]).Shapes[0].TextBody!.SetText("inserted");
            SlideInsertResult r = a.InsertSlides(b.Slides, 0);
            if (a.Slides.Count != 2 || a.Slides[0].Title != "inserted" || r.Report.CountOf("slides") != 1)
            {
                failures.Add("slide insert");
            }

            using Presentation part = a.ExtractSlides(1, 1);
            if (part.Slides.Count != 1 || part.Slides[0].Title != "first")
            {
                failures.Add("slide extract");
            }
        }

        return failures;
    }
}
