using System.Text;
using RecursiveAI.OneDocument.Presentations.Templating;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>Builds template presentations from descriptions: run splits, proofing marks and revision ids included, the way Office writes them.</summary>
internal static class Tpl
{
    /// <summary>A run with proofing noise.</summary>
    public static string R(string text, string attrs = "lang=\"en-US\" dirty=\"0\"") =>
        "<a:r><a:rPr " + attrs + "/><a:t>" + text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal) + "</a:t></a:r>";

    /// <summary>A bold run.</summary>
    public static string B(string text) => R(text, "lang=\"en-US\" b=\"1\" dirty=\"0\"");

    public static string P(params string[] runs) => "<a:p>" + string.Concat(runs) + "<a:endParaRPr lang=\"en-US\" dirty=\"0\"/></a:p>";

    public static string Text(string text) => P(R(text));

    public static string Sp(int id, string name, params string[] paragraphs) =>
        "<p:sp><p:nvSpPr><p:cNvPr id=\"" + id + "\" name=\"" + name + "\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"914400\" y=\"" + (id * 100000) + "\"/><a:ext cx=\"4572000\" cy=\"457200\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr>" +
        "<p:txBody><a:bodyPr wrap=\"square\"><a:normAutofit/></a:bodyPr><a:lstStyle/>" + string.Concat(paragraphs) + "</p:txBody></p:sp>";

    public static string Pic(int id, string name, string rid) =>
        "<p:pic><p:nvPicPr><p:cNvPr id=\"" + id + "\" name=\"" + name + "\" descr=\"photo\"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed=\"" + rid + "\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"10\" cy=\"10\"/></a:xfrm></p:spPr></p:pic>";

    /// <summary>A table frame: rows of cells of paragraph XML.</summary>
    public static string Table(int id, string name, params string[][] rows)
    {
        var sb = new StringBuilder("<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"" + id + "\" name=\"" + name + "\"/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp=\"1\"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>");
        sb.Append("<p:xfrm><a:off x=\"914400\" y=\"1000000\"/><a:ext cx=\"6000000\" cy=\"800000\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\"><a:tbl><a:tblPr firstRow=\"1\"/><a:tblGrid>");
        for (int c = 0; c < rows[0].Length; c++)
        {
            sb.Append("<a:gridCol w=\"2000000\"/>");
        }

        sb.Append("</a:tblGrid>");
        int r = 0;
        foreach (string[] row in rows)
        {
            sb.Append("<a:tr h=\"370840\">");
            foreach (string cell in row)
            {
                sb.Append("<a:tc><a:txBody><a:bodyPr/><a:lstStyle/>").Append(cell).Append("</a:txBody><a:tcPr/></a:tc>");
            }

            sb.Append("<a:extLst><a:ext uri=\"{0D108BD9-81ED-4DB2-BD59-A6C34878D82A}\"><a16:rowId xmlns:a16=\"http://schemas.microsoft.com/office/drawing/2014/main\" val=\"").Append(10000 + r++).Append("\"/></a:ext></a:extLst></a:tr>");
        }

        sb.Append("</a:tbl></a:graphicData></a:graphic></p:graphicFrame>");
        return sb.ToString();
    }

    public static string Ph(int id, string name, string type, int idx, params string[] paragraphs) =>
        "<p:sp><p:nvSpPr><p:cNvPr id=\"" + id + "\" name=\"" + name + "\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"" + type + "\"" + (idx > 0 ? " idx=\"" + idx + "\"" : "") + "/></p:nvPr></p:nvSpPr><p:spPr/>" +
        "<p:txBody><a:bodyPr/><a:lstStyle/>" + string.Concat(paragraphs) + "</p:txBody></p:sp>";

    /// <summary>A deck whose slides hold the given shape XML each (blank layout).</summary>
    public static Presentation Deck(params string[][] slides) => DeckOn("Blank", slides);

    /// <summary>A deck whose slides, on the named layout, hold the given shape XML each.</summary>
    public static Presentation DeckOn(string layout, params string[][] slides)
    {
        Presentation deck = Presentation.Create();
        SlideLayout blank = Decks.Layout(deck, layout);
        foreach (string[] shapes in slides)
        {
            Slide slide = deck.AddSlide(blank);
            foreach (Shape s in slide.Shapes.ToList())
            {
                s.Markup.Node.Remove();
            }

            foreach (string xml in shapes)
            {
                Decks.AddShapeXml(slide, xml);
            }
        }

        return deck;
    }

    public static Presentation Deck(params string[] shapesOfOneSlide) => Deck([shapesOfOneSlide]);

    /// <summary>Saves and reopens, so what is read is what the file holds.</summary>
    public static Presentation Reopen(Presentation deck) => Decks.Reopen(Decks.Save(deck));

    public static string[] Texts(Presentation deck) => deck.Slides.Select(s => s.Text).ToArray();

    public static Presentation Render(Presentation deck, object? data, TemplateOptions? options = null)
    {
        PresentationTemplate.Render(deck, data, options);
        return Reopen(deck);
    }

    public static Dictionary<string, object?> Data(params (string Key, object? Value)[] items)
    {
        var d = new Dictionary<string, object?>();
        foreach ((string k, object? v) in items)
        {
            d[k] = v;
        }

        return d;
    }
}
