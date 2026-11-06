using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Accessibility;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Runs the accessibility check on a tiny generated document of each kind: under NativeAOT and WASM.</summary>
internal static class AccessibilitySmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();

        using (Document doc = Document.Create())
        {
            doc.Body.Container.Node.PrependXml(Encoding.UTF8.GetBytes(
                "<w:p xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:r><w:drawing><wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
                "<wp:docPr id=\"1\" name=\"Picture 1\"/><a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"/></a:graphic></wp:inline></w:drawing></w:r></w:p>"));
            AccessibilityReport r = doc.CheckAccessibility();
            if (!r.Issues.Any(i => i.RuleId == "alt-text.missing" && i.Location.Paragraph == 1) || !r.Issues.Any(i => i.RuleId == "document.title"))
            {
                failures.Add("accessibility document");
            }
        }

        using (Workbook wb = Workbook.Create())
        {
            AccessibilityReport r = wb.CheckAccessibility();
            if (!r.Issues.Any(i => i.RuleId == "sheet.default-name") || !r.Issues.Any(i => i.RuleId == "sheet.blank"))
            {
                failures.Add("accessibility workbook");
            }
        }

        using (Presentation deck = Presentation.Create())
        {
            deck.AddSlide(deck.Masters[0].Layouts.First(l => l.Name == "Blank"));
            AccessibilityReport r = deck.CheckAccessibility();
            if (!r.Issues.Any(i => i.RuleId == "slide.title-missing" && i.Location.Slide == 1))
            {
                failures.Add("accessibility presentation");
            }
        }

        return failures;
    }
}
