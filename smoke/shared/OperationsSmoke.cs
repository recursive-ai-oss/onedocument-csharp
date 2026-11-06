using System.Linq;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Revisions;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Protection;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Find and replace in a document, a workbook and a presentation, and protection with its hashes, under NativeAOT and WASM.</summary>
internal static class OperationsSmoke
{
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

        var cheap = new ProtectionOptions { SpinCount = 50 };
        var regex = new SearchOptions { UseRegex = true };

        using (Document doc = Document.Create())
        {
            doc.Body.AddParagraph("The quick brown fox, quick again.");
            Check("doc find", doc.Find("quick").Count == 2);
            Check("doc replace", doc.Replace("QUICK", "slow").Count == 2 && doc.Body.Paragraphs().Any(p => p.Text == "The slow brown fox, slow again."));
            // a back-reference needs the backtracking engine; the linear one refuses it
            Check("doc regex", doc.Replace("(o)\\1?w", "X", new DocumentReplaceOptions { Search = regex }).Count == 1);
            Check("doc tracked", doc.Replace("fox", "cat", new DocumentReplaceOptions { TrackedAuthor = "smoke" }).Count == 1 && doc.Revisions.Revisions.Count == 2);
            doc.Revisions.RejectAll();
            Check("doc reject", doc.Body.Paragraphs().Any(p => p.GetText(RevisionView.Accepted).Contains("fox", StringComparison.Ordinal)));
            doc.Protection.Protect(DocumentEditRestriction.ReadOnly, "pw", false, cheap);
            Check("doc protect", doc.Protection.IsEnforced && doc.Protection.Verify("pw") == PasswordCheck.Match && doc.Protection.Verify("no") == PasswordCheck.Mismatch);
        }

        using (Workbook wb = Workbook.Create())
        {
            Worksheet ws = wb.Sheets.Add("S");
            ws["A1"].SetString("alpha beta");
            ws["B1"].SetNumber(2);
            ws["A2"].Formula = "B1*2";
            Check("sheet replace", wb.Replace("beta", "gamma").Count == 1 && ws["A1"].Text == "alpha gamma");
            Check("sheet formula", wb.Replace("B1", "B2", new WorkbookReplaceOptions { Formulas = true, Values = false }).Count == 1 && ws["A2"].Formula == "B2*2");
            Check("sheet formula refused", wb.Replace("B2*2", "B2*(", new WorkbookReplaceOptions { Formulas = true, Values = false }).Issues.Count == 1);
            ws.Protection.Protect(SheetActions.FormatCells, "pw", cheap);
            Check("sheet protect", ws.Protection.IsProtected && ws.Protection.Verify("pw") == PasswordCheck.Match && (ws.Protection.Allowed & SheetActions.FormatCells) != 0);
            wb.Protection.Protect(true, false, "pw", cheap);
            Check("book protect", wb.Protection.LockStructure && wb.Protection.Verify("pw") == PasswordCheck.Match);
            Check("excel hash", PasswordVerifier.FormatExcelHash(PasswordVerifier.ExcelHash("password")) == "83AF");
        }

        using (Presentation deck = Presentation.Create())
        {
            Slide slide = deck.AddSlide(deck.Masters[0].Layouts[1]);
            slide.Shapes[0].TextBody!.SetText("hello world");
            Check("deck replace", deck.Replace("o w", "0_W").Count == 1 && slide.Shapes[0].TextBody!.Text == "hell0_World");
            deck.Protection.Protect("pw", cheap);
            Check("deck protect", deck.Protection.IsProtected && deck.Protection.Verify("pw") == PasswordCheck.Match);
        }

        return failures;
    }
}
