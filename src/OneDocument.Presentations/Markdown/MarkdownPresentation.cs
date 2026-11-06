using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.TextExtraction;

namespace RecursiveAI.OneDocument.Presentations.Markdown;

/// <summary>
/// Writes a presentation as GitHub Flavored Markdown: one second-level heading per slide ("Slide 3: Title"), the slide's text in reading order (placeholders of
/// body type as bullet lists by outline level, other text boxes as paragraphs, tables as pipe tables), and the speaker notes under a third-level heading.
/// </summary>
internal static class MarkdownPresentation
{
    public static void Write(Presentation presentation, Stream destination, ILossSink? loss, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        var sb = new StringBuilder();
        long pictures = 0;
        long other = 0;
        long hiddenSlides = 0;
        long links = 0;
        int number = 0;
        foreach (Slide slide in presentation.Slides)
        {
            cancellationToken.ThrowIfCancellationRequested();
            number++;
            if (slide.IsHidden)
            {
                hiddenSlides++;
            }

            string? title = slide.Title;
            string heading = "Slide " + number.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(title))
            {
                heading += ": " + MarkdownWriting.EscapeText(OneLine(title), inTable: false, lineStart: false);
            }

            sb.Append("## ").Append(heading).Append("\n\n");
            foreach (Shape shape in slide.AllShapes())
            {
                if (shape.IsHidden)
                {
                    continue;
                }

                PlaceholderInfo? ph = shape.Placeholder;
                if (ph is { Type: "title" or "ctrTitle" or "dt" or "ftr" or "sldNum" })
                {
                    continue;
                }

                switch (shape.Kind)
                {
                    case ShapeKind.Picture:
                        pictures++;
                        break;
                    case ShapeKind.GraphicFrame:
                        if (shape.Table is { } table)
                        {
                            Table(table, sb);
                        }
                        else
                        {
                            other++;
                        }

                        break;
                    case ShapeKind.Shape:
                        if (shape.TextBody is { } body)
                        {
                            bool bullets = ph is { } info && (info.Type is "body" or "obj" || !info.HasType);
                            Text(body, bullets, sb, ref links);
                        }
                        else
                        {
                            other++;
                        }

                        break;
                    case ShapeKind.Group or ShapeKind.AlternateContent:
                        break;
                    default:
                        other++;
                        break;
                }
            }

            if (slide.Notes is { } notes && notes.NotesText.Trim().Length > 0)
            {
                sb.Append("### Notes\n\n");
                foreach (string para in notes.NotesText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
                {
                    if (para.Trim().Length > 0)
                    {
                        sb.Append(MarkdownWriting.EscapeText(para.Trim(), inTable: false, lineStart: true)).Append("\n\n");
                    }
                }
            }
        }

        if (loss is not null)
        {
            if (pictures > 0)
            {
                loss.Report("loss.pictures", "pictures", pictures, "Pictures are not written.");
            }

            if (other > 0)
            {
                loss.Report("loss.shapes", "shapes, charts, diagrams and media", other, "Shapes without text, charts, diagrams and media have no Markdown form.");
            }

            if (links > 0)
            {
                loss.Report("loss.links", "hyperlinks", links, "Text links are written as their text only.");
            }

            if (hiddenSlides > 0)
            {
                loss.Report("loss.hidden-slides", "hidden slides", hiddenSlides, "Hidden slides are written like the others.");
            }

            loss.Report("loss.layout", "layout, formatting, animations and transitions", 1, "Markdown holds the text and its structure only.");
        }

        destination.Write(new UTF8Encoding(false).GetBytes(sb.ToString().TrimEnd('\n') + "\n"));
    }

    private static string OneLine(string s) => string.Join(' ', s.Split(['\n', '\r', '\v'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static void Text(TextBody body, bool bullets, StringBuilder sb, ref long links)
    {
        bool wroteList = false;
        foreach (TextParagraph p in body.Paragraphs)
        {
            var line = new StringBuilder();
            foreach (TextRun run in p.Runs)
            {
                if (run.Kind == TextRunKind.LineBreak)
                {
                    line.Append(' ');
                    continue;
                }

                string text = run.Text;
                if (text.Length == 0)
                {
                    continue;
                }

                if (run.Properties.IsValid && run.Properties.Node.FirstChild is { IsNull: false })
                {
                    foreach (var child in run.Properties.Node.ChildElements)
                    {
                        if (child.LocalNameString == "hlinkClick")
                        {
                            links++;
                        }
                    }
                }

                string escaped = MarkdownWriting.EscapeText(text.Replace('\v', ' '), inTable: false, lineStart: false);
                bool bold = Pml.AttrBool(run.Properties, "b") == true;
                bool italic = Pml.AttrBool(run.Properties, "i") == true;
                string trimmed = escaped.Trim();
                if (trimmed.Length == 0)
                {
                    line.Append(escaped);
                    continue;
                }

                string lead = escaped[..(escaped.Length - escaped.TrimStart().Length)];
                string trail = escaped[escaped.TrimEnd().Length..];
                string mark = (bold ? "**" : string.Empty) + (italic ? "*" : string.Empty);
                string closing = (italic ? "*" : string.Empty) + (bold ? "**" : string.Empty);
                line.Append(lead).Append(mark).Append(trimmed).Append(closing).Append(trail);
            }

            string content = line.ToString().Trim();
            if (content.Length == 0)
            {
                continue;
            }

            if (bullets)
            {
                sb.Append(' ', 2 * Math.Clamp(p.Level, 0, 8)).Append("- ").Append(content).Append('\n');
                wroteList = true;
            }
            else
            {
                if (wroteList)
                {
                    sb.Append('\n');
                    wroteList = false;
                }

                sb.Append(MarkdownLineStart(content)).Append("\n\n");
            }
        }

        if (wroteList)
        {
            sb.Append('\n');
        }
    }

    // A paragraph that starts like a block marker would turn into one; the text was escaped run by run, so only the start of the line needs care here.
    private static string MarkdownLineStart(string content) =>
        content.Length > 0 && (content[0] == '#' || ((content[0] is '-' or '+') && (content.Length == 1 || content[1] == ' '))) ? "\\" + content : content;

    private static void Table(Table table, StringBuilder sb)
    {
        var rows = new List<List<string>>();
        int columns = 0;
        foreach (TableRow row in table.Rows)
        {
            var cells = new List<string>();
            foreach (TableCell cell in row.Cells)
            {
                if (cell.IsMergedContinuation)
                {
                    cells.Add(string.Empty);
                    continue;
                }

                cells.Add(string.Join("<br>", cell.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Select(l => MarkdownWriting.EscapeText(l.Trim(), inTable: true, lineStart: false))));
            }

            columns = Math.Max(columns, cells.Count);
            rows.Add(cells);
        }

        if (rows.Count == 0 || columns == 0)
        {
            return;
        }

        foreach (List<string> r in rows)
        {
            while (r.Count < columns)
            {
                r.Add(string.Empty);
            }
        }

        MarkdownWriting.WriteTable(sb, rows, new int[columns]);
        sb.Append('\n');
    }
}
