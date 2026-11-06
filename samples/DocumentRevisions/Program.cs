// Open a document, list the paragraphs of one style, accept every tracked change and save.
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Samples;

string dir = SampleSupport.WorkDirectory(args);
string path = Path.Combine(dir, "contract.docx");

// A contract with two headings and one tracked insertion by a reviewer.
using (Document created = Document.Create())
{
    created.Styles.AddBuiltIn("Heading 1");
    created.Body.AddParagraph("Scope").Style = "Heading 1";
    Paragraph body = created.Body.AddParagraph("The supplier delivers the goods.");
    created.Body.AddParagraph("Payment").Style = "Heading 1";
    created.Revisions.CreateAuthor("Reviewer", null).InsertText(body, 0, "Within 30 days, ");
    created.SaveAs(path);
}

// The task.
using Document doc = Document.Open(path);
List<string> headings = doc.Paragraphs.Where(p => p.Style == "Heading 1").Select(p => p.Text).ToList();
int accepted = doc.Revisions.AcceptAll();
doc.SaveAs(Path.Combine(dir, "clean.docx"));

if (headings.Count != 2 || accepted == 0 || doc.Revisions.Revisions.Count != 0)
{
    throw new InvalidOperationException("Unexpected result: " + headings.Count + " headings, " + accepted + " accepted.");
}

Console.WriteLine(string.Join(", ", headings) + "; accepted " + accepted + " change(s)");
