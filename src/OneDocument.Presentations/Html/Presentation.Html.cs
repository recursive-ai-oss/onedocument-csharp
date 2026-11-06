using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Presentations;

public sealed partial class Presentation
{
    /// <summary>
    /// Gets what the last export to HTML could not carry (pictures, charts, shape geometry and positions, animations, transitions, master graphics,
    /// speaker-note layout and the like), or null until an HTML export has run.
    /// </summary>
    public LossReport? ExportLoss { get; private set; }

    /// <summary>
    /// Writes the presentation as an HTML5 page with one <c>section</c> per slide: the title as a heading, the text of the other shapes as
    /// paragraphs (bullet levels as nested indentation), tables as tables and the speaker notes as an <c>aside</c>. Hidden slides are written
    /// with a <c>hidden</c> attribute. Positions, sizes, pictures, charts, animations and transitions are not written and are listed in
    /// <see cref="ExportLoss"/>.
    /// </summary>
    /// <param name="options">A fragment instead of a page; null for a page.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The HTML.</returns>
    public string ToHtml(HtmlExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        WriteHtml(buffer, options, cancellationToken);
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Writes the presentation as HTML to a stream; see <see cref="ToHtml(HtmlExportOptions?, CancellationToken)"/>.</summary>
    /// <param name="destination">The stream.</param>
    /// <param name="options">A fragment instead of a page; null for a page.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    public void SaveAsHtml(Stream destination, HtmlExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        WriteHtml(destination, options, cancellationToken);
    }

    internal void WriteHtml(Stream destination, HtmlExportOptions? options, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        CommitChanges();
        var loss = new LossReport();
        var sb = new StringBuilder();
        bool fragment = options?.Fragment ?? false;
        if (!fragment)
        {
            sb.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<title></title>\n<style>\nsection.slide{border-bottom:1px solid #ccc;margin:0 0 1.5em}\ntable{border-collapse:collapse}\ntd,th{border:1px solid #bbb;padding:2px 6px}\naside.notes{color:#555;font-size:smaller}\n</style>\n</head>\n<body>\n");
        }

        long pictures = 0;
        long other = 0;
        long animated = 0;
        int number = 0;
        foreach (Slide slide in Slides)
        {
            cancellationToken.ThrowIfCancellationRequested();
            number++;
            sb.Append("<section class=\"slide\" id=\"slide-").Append(number.ToString(CultureInfo.InvariantCulture)).Append('"').Append(slide.IsHidden ? " hidden" : string.Empty).Append(">\n");
            string? title = slide.Title;
            if (!string.IsNullOrWhiteSpace(title))
            {
                sb.Append("<h2>").Append(Esc(title.Trim())).Append("</h2>\n");
            }

            foreach (Shape shape in slide.AllShapes())
            {
                WriteShape(shape, sb, ref pictures, ref other);
            }

            if (slide.HasAnimations || slide.HasTransition)
            {
                animated++;
            }

            if (slide.Notes is { } notes && !string.IsNullOrWhiteSpace(notes.NotesText))
            {
                sb.Append("<aside class=\"notes\">");
                foreach (string line in notes.NotesText.Split('\n'))
                {
                    if (line.Trim().Length > 0)
                    {
                        sb.Append("<p>").Append(Esc(line.TrimEnd('\r'))).Append("</p>");
                    }
                }

                sb.Append("</aside>\n");
            }

            sb.Append("</section>\n");
        }

        if (!fragment)
        {
            sb.Append("</body>\n</html>\n");
        }

        loss.Report("loss.html-slide-layout", "slide layout", Math.Max(1, number), "Shape positions, sizes, fills, lines, masters and layouts are not written; text follows the slide's shape order.");
        if (pictures > 0)
        {
            loss.Report("loss.html-pictures", "pictures", pictures, "Pictures are not written.");
        }

        if (other > 0)
        {
            loss.Report("loss.html-graphic-frames", "charts, diagrams and embedded objects", other, "Charts, SmartArt, media and OLE objects are not written.");
        }

        if (animated > 0)
        {
            loss.Report("loss.html-animations", "animations and transitions", animated, "Animations and transitions are not written.");
        }

        destination.Write(Encoding.UTF8.GetBytes(sb.ToString()));
        ExportLoss = loss;
    }

    private static void WriteShape(Shape shape, StringBuilder sb, ref long pictures, ref long other)
    {
        if (shape.IsHidden)
        {
            return;
        }

        if (shape.Kind == ShapeKind.Group)
        {
            return; // the shapes inside a group are visited on their own
        }

        if (shape.Placeholder is { } ph && ph.Type is "title" or "ctrTitle")
        {
            return; // written as the heading
        }

        if (shape.Placeholder is { } ft && ft.Type is "sldNum" or "dt" or "ftr")
        {
            return;
        }

        if (shape.Table is { } table)
        {
            WriteTable(table, sb);
            return;
        }

        if (shape.Kind == ShapeKind.Picture)
        {
            pictures++;
            return;
        }

        if (shape.Kind == ShapeKind.GraphicFrame)
        {
            other++;
            return;
        }

        if (shape.TextBody is { } body)
        {
            foreach (TextParagraph p in body.Paragraphs)
            {
                string text = p.Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                sb.Append("<p");
                if (p.Level > 0)
                {
                    sb.Append(" style=\"margin-left:").Append((p.Level * 2).ToString(CultureInfo.InvariantCulture)).Append("em\"");
                }

                sb.Append('>').Append(Esc(text)).Append("</p>\n");
            }
        }
    }

    private static void WriteTable(Table table, StringBuilder sb)
    {
        sb.Append("<table>\n");
        bool header = table.HasHeaderRow;
        int r = 0;
        foreach (TableRow row in table.Rows)
        {
            sb.Append("<tr>");
            foreach (TableCell cell in row.Cells)
            {
                if (cell.IsMergedContinuation)
                {
                    continue;
                }

                string el = header && r == 0 ? "th" : "td";
                sb.Append('<').Append(el);
                if (cell.ColumnSpan > 1)
                {
                    sb.Append(" colspan=\"").Append(cell.ColumnSpan.ToString(CultureInfo.InvariantCulture)).Append('"');
                }

                if (cell.RowSpan > 1)
                {
                    sb.Append(" rowspan=\"").Append(cell.RowSpan.ToString(CultureInfo.InvariantCulture)).Append('"');
                }

                sb.Append('>').Append(Esc(cell.Text)).Append("</").Append(el).Append('>');
            }

            sb.Append("</tr>\n");
            r++;
        }

        sb.Append("</table>\n");
    }

    private static string Esc(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default:
                    if (c >= ' ' || c is '\t' or '\n' or '\r')
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }
}
