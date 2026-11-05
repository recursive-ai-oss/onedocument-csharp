using System.Text;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Builders for documents with footnotes and endnotes, shared by the note tests and the generated case set.
internal static class NoteDocs
{
    internal const string FootnotesType = "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml";
    internal const string EndnotesType = "application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml";

    // The test styles plus the note styles Word writes: 10 pt note text and superscript reference marks.
    internal static readonly string Styles = TestDocx.DefaultStyles.Replace(
        "</w:styles>",
        "<w:style w:type=\"paragraph\" w:styleId=\"FootnoteText\"><w:name w:val=\"footnote text\"/><w:basedOn w:val=\"Normal\"/><w:rPr><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/></w:rPr></w:style>" +
        "<w:style w:type=\"character\" w:styleId=\"FootnoteReference\"><w:name w:val=\"footnote reference\"/><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"EndnoteText\"><w:name w:val=\"endnote text\"/><w:basedOn w:val=\"Normal\"/><w:rPr><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/></w:rPr></w:style>" +
        "<w:style w:type=\"character\" w:styleId=\"EndnoteReference\"><w:name w:val=\"endnote reference\"/><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr></w:style></w:styles>",
        StringComparison.Ordinal);

    internal static string Settings(string inner) => "<w:settings xmlns:w=\"" + TestDocx.W + "\">" + inner + "</w:settings>";

    // A reference run to footnote `id`.
    internal static string Ref(long id, bool custom = false, string? customText = null) =>
        "<w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteReference " + (custom ? "w:customMarkFollows=\"1\" " : string.Empty) + "w:id=\"" + id + "\"/>" +
        (customText is null ? string.Empty : "<w:t>" + customText + "</w:t>") + "</w:r>";

    internal static string EndRef(long id) =>
        "<w:r><w:rPr><w:rStyle w:val=\"EndnoteReference\"/></w:rPr><w:endnoteReference w:id=\"" + id + "\"/></w:r>";

    // A paragraph of text followed by references.
    internal static string Pn(string text, params long[] refs) =>
        "<w:p><w:r><w:t xml:space=\"preserve\">" + text + "</w:t></w:r>" + string.Concat(refs.Select(id => Ref(id))) + "</w:p>";

    internal static string PEnd(string text, params long[] refs) =>
        "<w:p><w:r><w:t xml:space=\"preserve\">" + text + "</w:t></w:r>" + string.Concat(refs.Select(EndRef)) + "</w:p>";

    // A footnote whose text is one paragraph per entry; the first starts with the automatic mark.
    internal static string Fn(long id, params string[] paragraphs) => Note("footnote", "Footnote", id, paragraphs, mark: true);

    internal static string En(long id, params string[] paragraphs) => Note("endnote", "Endnote", id, paragraphs, mark: true);

    // A footnote that supplies its own mark text.
    internal static string FnCustom(long id, string mark, string text) =>
        "<w:footnote w:id=\"" + id + "\"><w:p><w:pPr><w:pStyle w:val=\"FootnoteText\"/></w:pPr><w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:t>" + mark + "</w:t></w:r><w:r><w:t xml:space=\"preserve\"> " + text + "</w:t></w:r></w:p></w:footnote>";

    private static string Note(string element, string style, long id, string[] paragraphs, bool mark)
    {
        var sb = new StringBuilder("<w:" + element + " w:id=\"" + id + "\">");
        for (int i = 0; i < paragraphs.Length; i++)
        {
            sb.Append("<w:p><w:pPr><w:pStyle w:val=\"" + style + "Text\"/></w:pPr>");
            if (i == 0 && mark)
            {
                sb.Append("<w:r><w:rPr><w:rStyle w:val=\"" + style + "Reference\"/></w:rPr><w:" + element + "Ref/></w:r>");
            }

            sb.Append("<w:r><w:t xml:space=\"preserve\">" + (i == 0 ? " " : string.Empty) + paragraphs[i] + "</w:t></w:r></w:p>");
        }

        return sb.Append("</w:" + element + ">").ToString();
    }

    private static string Separators(string element, string? notice)
    {
        string p(string run) => "<w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r>" + run + "</w:r></w:p>";
        return "<w:" + element + " w:type=\"separator\" w:id=\"-1\">" + p("<w:separator/>") + "</w:" + element + ">" +
            "<w:" + element + " w:type=\"continuationSeparator\" w:id=\"0\">" + p("<w:continuationSeparator/>") + "</w:" + element + ">" +
            (notice is null ? string.Empty : "<w:" + element + " w:type=\"continuationNotice\" w:id=\"900\"><w:p><w:pPr><w:pStyle w:val=\"" + (element == "footnote" ? "Footnote" : "Endnote") + "Text\"/><w:jc w:val=\"right\"/></w:pPr><w:r><w:t>" + notice + "</w:t></w:r></w:p></w:" + element + ">");
    }

    /// <summary>A package with a body, footnotes, optionally endnotes, settings and header or footer parts.</summary>
    internal static byte[] Build(string body, string footnotes = "", string? endnotes = null, string? settings = null, string? sectPr = null, string? notice = null, (string Name, string Xml)[]? parts = null)
    {
        var rels = new StringBuilder();
        var types = new StringBuilder();
        var extra = new List<(string, byte[])>();
        string w = "xmlns:w=\"" + TestDocx.W + "\" xmlns:r=\"" + TestDocx.R + "\"";
        if (footnotes.Length > 0)
        {
            rels.Append("<Relationship Id=\"rIdFn\" Type=\"" + TestDocx.R + "/footnotes\" Target=\"footnotes.xml\"/>");
            types.Append("<Override PartName=\"/word/footnotes.xml\" ContentType=\"" + FootnotesType + "\"/>");
            extra.Add(("word/footnotes.xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:footnotes " + w + ">" + Separators("footnote", notice) + footnotes + "</w:footnotes>")));
        }

        if (endnotes is not null)
        {
            rels.Append("<Relationship Id=\"rIdEn\" Type=\"" + TestDocx.R + "/endnotes\" Target=\"endnotes.xml\"/>");
            types.Append("<Override PartName=\"/word/endnotes.xml\" ContentType=\"" + EndnotesType + "\"/>");
            extra.Add(("word/endnotes.xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:endnotes " + w + ">" + Separators("endnote", null) + endnotes + "</w:endnotes>")));
        }

        foreach ((string name, string xml) in parts ?? [])
        {
            string type = name.StartsWith("header", StringComparison.Ordinal) ? "header" : "footer";
            rels.Append("<Relationship Id=\"rId_" + name.Replace('.', '_') + "\" Type=\"" + TestDocx.R + "/" + type + "\" Target=\"" + name + "\"/>");
            types.Append("<Override PartName=\"/word/" + name + "\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml." + type + "+xml\"/>");
            extra.Add(("word/" + name, Encoding.UTF8.GetBytes(xml)));
        }

        return TestDocx.Bytes(body, styles: Styles, settings: settings, sectPr: sectPr, extraRels: rels.ToString(), extraParts: extra, extraTypes: types.ToString());
    }
}
