using System.Text;
using RecursiveAI.OneDocument.Documents.Revisions;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Documents.Tests;

internal static class Fixture
{
    internal const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    internal static RevisionPart Part(string bodyXml, string uri = "/word/document.xml") =>
        new(uri, MarkupDocument.Parse(Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"" + W + "\"><w:body>" + bodyXml + "</w:body></w:document>")));

    internal static RevisionSet Set(string bodyXml) => RevisionSet.Scan([Part(bodyXml)]);

    internal static string Xml(RevisionSet set) => set.Parts[0].Markup.Root.OuterXml;

    internal static string Text(RevisionSet set, RevisionView view) => RevisionSet.GetText(set.Parts[0], view);

    internal static MarkupNode Paragraph(RevisionSet set, int index)
    {
        int i = 0;
        MarkupNode root = set.Parts[0].Markup.Root;
        for (MarkupNode n = root; !n.IsNull; n = Next(n, root))
        {
            if (n.Kind == MarkupNodeKind.Element && n.LocalNameString == "p" && i++ == index)
            {
                return n;
            }
        }

        throw new InvalidOperationException("No such paragraph.");
    }

    private static MarkupNode Next(MarkupNode n, MarkupNode root)
    {
        if (!n.FirstChild.IsNull)
        {
            return n.FirstChild;
        }

        for (MarkupNode c = n; !c.IsNull && c != root; c = c.Parent)
        {
            if (!c.NextSibling.IsNull)
            {
                return c.NextSibling;
            }
        }

        return default;
    }

    internal static string R(string text) => "<w:r><w:t xml:space=\"preserve\">" + text + "</w:t></w:r>";

    internal static string Ins(int id, string author, string inner) =>
        "<w:ins w:id=\"" + id + "\" w:author=\"" + author + "\" w:date=\"2024-03-01T10:00:00Z\">" + inner + "</w:ins>";

    internal static string Del(int id, string author, string text) =>
        "<w:del w:id=\"" + id + "\" w:author=\"" + author + "\" w:date=\"2024-03-01T10:00:00Z\"><w:r><w:delText xml:space=\"preserve\">" + text + "</w:delText></w:r></w:del>";
}
