using System.Text;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Builders for documents with sections, headers and footers, shared by the page tests and the generated case set.
internal static class PageDocs
{
    internal const string Letter = "<w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/>";

    internal static string Sect(string inner, string? type = null, string? pgSzMar = null) =>
        "<w:sectPr>" + inner + (type is null ? string.Empty : "<w:type w:val=\"" + type + "\"/>") + (pgSzMar ?? Letter) + "</w:sectPr>";

    // A paragraph that ends a section.
    internal static string End(string sect) => "<w:p><w:pPr>" + sect + "</w:pPr><w:r><w:t>end</w:t></w:r></w:p>";

    internal static string Story(string root, string content) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:" + root + " xmlns:w=\"" + TestDocx.W + "\" xmlns:v=\"urn:schemas-microsoft-com:vml\">" + content + "</w:" + root + ">";

    internal static string PageField(string instr, string cached) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> " + instr + " </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>" + cached + "</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    // A package with the given header and footer parts (named header*.xml and footer*.xml), registered with their content types.
    internal static byte[] Build(string body, string finalSect, (string Name, string Xml)[]? parts = null, string? settings = null)
    {
        var rels = new StringBuilder();
        var types = new StringBuilder();
        var extra = new List<(string, byte[])>();
        foreach ((string name, string xml) in parts ?? [])
        {
            string id = "rId_" + name.Replace('.', '_');
            string type = name.StartsWith("header", StringComparison.Ordinal) ? "header" : "footer";
            rels.Append("<Relationship Id=\"" + id + "\" Type=\"" + TestDocx.R + "/" + type + "\" Target=\"" + name + "\"/>");
            types.Append("<Override PartName=\"/word/" + name + "\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml." + type + "+xml\"/>");
            extra.Add(("word/" + name, Encoding.UTF8.GetBytes(xml)));
        }

        return TestDocx.Bytes(body, settings: settings, sectPr: finalSect, extraRels: rels.ToString(), extraParts: extra, extraTypes: types.ToString());
    }

    internal static string Ref(string kind, string type, string part) => "<w:" + kind + "Reference w:type=\"" + type + "\" r:id=\"rId_" + part.Replace('.', '_') + "\"/>";

    internal static string Paras(int n, string prefix = "Line") => string.Concat(Enumerable.Range(0, n).Select(i => TestDocx.P(prefix + " " + i)));
}
