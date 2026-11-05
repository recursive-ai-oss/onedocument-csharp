using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.Documents.Revisions;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Documents.Tests;

/// <summary>
/// Rejecting or accepting everything after several tracked edits to one paragraph leaves no revision behind, and rejecting gives back
/// the text and paragraph properties the paragraph started with. Run in the body, in a table cell and in a text box (both branches).
/// </summary>
public class RevisionRejectGridTests
{
    private const string Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string V = "urn:schemas-microsoft-com:vml";
    private static readonly DateTimeOffset When = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private const string Paras =
        "<w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr><w:r><w:t>Hello world</w:t></w:r></w:p><w:p><w:r><w:t>Other</w:t></w:r></w:p>";

    public enum Place { Body, Cell, DrawingBox, VmlBox }

    public static IEnumerable<object[]> Grid()
    {
        foreach (Place place in Enum.GetValues<Place>())
        {
            foreach (string op in Ops.Keys)
            {
                foreach (bool propertiesFirst in new[] { true, false })
                {
                    yield return [place, op, propertiesFirst];
                }
            }
        }
    }

    private static readonly SortedDictionary<string, Action<RevisionAuthor, Paragraph, Paragraph>> Ops = new(StringComparer.Ordinal)
    {
        ["insert-paragraph"] = (a, p, _) => a.InsertParagraph(p, "added"),
        ["insert-text"] = (a, p, _) => a.InsertText(p, 5, ","),
        ["delete-text"] = (a, p, _) => a.DeleteText(p, 0, 5),
        ["replace-text"] = (a, p, _) => a.ReplaceText(p, 0, 5, "HELLO"),
        ["delete-paragraph"] = (a, p, _) => a.DeleteParagraph(p),
        ["move-paragraph"] = (a, p, q) => a.MoveParagraph(p, q),
        ["move-text"] = (a, p, q) => a.MoveText(p, 0, 5, q, 2),
        ["format-text"] = (a, p, _) => a.FormatText(p, 0, 5, new RunPropertiesDelta { Bold = true }),
    };

    private static string Content() => $"<w:txbxContent>{Paras}</w:txbxContent>";

    private static Document Open(Place place)
    {
        string body = place switch
        {
            Place.Body => Paras,
            Place.Cell => $"<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid><w:tr><w:tc>{Paras}</w:tc></w:tr></w:tbl><w:p/>",
            Place.DrawingBox => $"<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:drawing><wps:wsp><wps:txbx>{Content()}</wps:txbx></wps:wsp></w:drawing></mc:Choice><mc:Fallback><w:pict><v:shape id=\"s1\"><v:textbox>{Content()}</v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>",
            _ => $"<w:p><w:r><w:pict><v:shape id=\"s1\"><v:textbox>{Content()}</v:textbox></v:shape></w:pict></w:r></w:p>",
        };
        InMemoryPackage p = TestDocs.Package("");
        string xml = $"<w:document xmlns:w=\"{TestDocs.WNs}\" xmlns:wps=\"{Wps}\" xmlns:mc=\"{Mc}\" xmlns:v=\"{V}\" xmlns:r=\"{TestDocs.RNs}\"><w:body>{body}</w:body></w:document>";
        p.FindPart("/word/document.xml")!.SetBytes(Encoding.UTF8.GetBytes(xml));
        return Document.FromPackage(p);
    }

    private static BlockContainer Container(Document d, Place place) => place switch
    {
        Place.Body => d.Body,
        Place.Cell => d.Body.Blocks().OfType<Table>().First().Rows().First().Cells().First(),
        _ => d.TextBoxes()[0],
    };

    /// <summary>Text and paragraph-property XML of each paragraph, the part of the state that a reject must restore.</summary>
    private static string[] State(BlockContainer c) =>
        [.. c.Paragraphs().Select(p =>
        {
            MarkupNode pPr = p.Markup.Node.FirstChild;
            while (!pPr.IsNull && pPr.Kind != MarkupNodeKind.Element)
            {
                pPr = pPr.NextSibling;
            }

            string props = !pPr.IsNull && pPr.LocalNameString == "pPr" ? pPr.OuterXml : string.Empty;
            return p.Text + "|" + props;
        })];

    [Theory]
    [MemberData(nameof(Grid))]
    public void RejectAndAcceptLeaveNoRevision(Place place, string op, bool propertiesFirst)
    {
        string[] original;
        using (Document d0 = Open(place))
        {
            original = State(Container(d0, place));
        }

        foreach (bool accept in new[] { false, true })
        {
            using Document d = Open(place);
            BlockContainer c = Container(d, place);
            Paragraph[] ps = [.. c.Paragraphs()];
            RevisionAuthor a = d.Revisions.CreateAuthor("Ann", When);
            var center = new ParagraphPropertiesDelta { Alignment = ParagraphAlignment.Center };
            if (propertiesFirst)
            {
                a.ChangeParagraphProperties(ps[0], center);
            }

            Ops[op](a, ps[0], ps[1]);
            if (!propertiesFirst)
            {
                a.ChangeParagraphProperties(ps[0], center);
            }

            d.CommitChanges();
            d.Revisions.Refresh();
            Assert.NotEmpty(d.Revisions.Revisions);
            if (place is Place.DrawingBox)
            {
                // Each revision is listed once although both branches carry it.
                Assert.Equal(d.Revisions.Revisions.Count, d.Revisions.Revisions.Select(r => (r.Id, r.Kind)).Distinct().Count());
            }

            if (accept)
            {
                d.Revisions.AcceptAll();
            }
            else
            {
                d.Revisions.RejectAll();
            }

            d.CommitChanges();
            Assert.Empty(d.Revisions.Revisions);
            string xml = d.MainMarkup.Root.OuterXml;
            foreach (string leftover in new[] { "<w:ins ", "<w:del ", "<w:moveFrom", "<w:moveTo", "Change " })
            {
                Assert.DoesNotContain(leftover, xml, StringComparison.Ordinal);
            }

            if (!accept)
            {
                Assert.Equal(original, State(Container(d, place)));
            }

            if (place is Place.DrawingBox)
            {
                TextBox box = d.TextBoxes()[0];
                Assert.Equal(box.Container.Node.OuterXml, box.Alternate.Node.OuterXml);
            }
        }
    }
}
