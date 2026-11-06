using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Inspection;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Presentations.Templating;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>
/// An authoring call named something the presentation does not have: a slide layout or a placeholder. Nothing is created in its place.
/// <see cref="Exception.Message"/> lists what exists. The defect code is <c>pptx.layout-not-found</c> or <c>pptx.placeholder-not-found</c>.
/// </summary>
public sealed class PresentationAuthoringException : OfficeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="code">The defect code.</param>
    /// <param name="message">What was asked for and what exists.</param>
    /// <param name="location">The part the lookup was made in, if known.</param>
    public PresentationAuthoringException(string code, string message, DefectLocation location = default)
        : base(new Defect(DefectKind.Reference, code, DefectSeverity.Fatal, message, location))
    {
    }
}

public sealed partial class Presentation
{
    /// <summary>
    /// Adds a slide based on the layout with the given name (the name PowerPoint shows: "Title Slide", "Title and Content"), looked up
    /// ignoring case in every master in order. See <see cref="AddSlide(SlideLayout, int?)"/> for what the new slide holds.
    /// </summary>
    /// <param name="layoutName">The layout's name (<c>p:cSld/@name</c>).</param>
    /// <param name="index">Where the slide goes (0 is first); null appends it.</param>
    /// <returns>The new slide.</returns>
    /// <exception cref="PresentationAuthoringException">No layout has that name (<c>pptx.layout-not-found</c>); the message lists the names there are.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 0 to the number of slides.</exception>
    public Slide AddSlide(string layoutName, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(layoutName);
        var names = new List<string>();
        foreach (SlideMaster master in Masters)
        {
            foreach (SlideLayout layout in master.Layouts)
            {
                string name = layout.Name ?? layout.Type;
                if (string.Equals(name, layoutName, StringComparison.OrdinalIgnoreCase))
                {
                    return AddSlide(layout, index);
                }

                names.Add(name);
            }
        }

        throw new PresentationAuthoringException("pptx.layout-not-found",
            "There is no slide layout named '" + layoutName + "'. The presentation has: " + (names.Count == 0 ? "(none)" : string.Join(", ", names)) + ".", DefectLocation.InPart(MainPartName));
    }

    /// <summary>
    /// Removes hidden data and personal information from the presentation in memory, the categories of <paramref name="options"/> (by
    /// default <see cref="SanitizeOptions.Default"/>), so <c>deck.Sanitize(); deck.SaveAs("clean.pptx");</c> works. Pending edits are
    /// committed first. The same sanitiser as <see cref="Office.Sanitize(string, string, OpenContext?, CancellationToken)"/> runs, including its verification: the
    /// result is inspected again on this call and, if a category reported as removed still holds something,
    /// <see cref="SanitizeVerificationException"/> is thrown and the presentation is left as it was. Only the parts the sanitiser changes
    /// are rewritten; the others stay byte-identical.
    /// </summary>
    /// <param name="options">The categories to remove; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the operation; the presentation is left as it was.</param>
    /// <returns>What was removed, what was kept and why, and the inspection of the result.</returns>
    /// <remarks>
    /// Slides, layouts, shapes and other objects obtained from the presentation before the call no longer belong to it afterwards; read
    /// them again. <see cref="OfficeFile.Format"/> does not change, so when the report's <see cref="SanitizeReport.OutputFormat"/> differs (a
    /// macro project was removed) name the file by that format.
    /// </remarks>
    /// <exception cref="SanitizeVerificationException">The result still holds something in a category reported as removed; nothing was changed.</exception>
    /// <exception cref="ObjectDisposedException">The presentation is disposed.</exception>
    public SanitizeReport Sanitize(SanitizeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CommitChanges();
        SanitizeReport report = PackageSync.SanitizeInPlace(Package, options, Context, cancellationToken);
        lock (Parts.Gate)
        {
            Parts.Clear();
            _partObjects.Clear();
            _reportedSlides.Clear();
            _main = null;
            _odfPristine = false;
        }

        return report;
    }

    /// <summary>Stores a picture as a media part; returns its part name.</summary>
    internal string AddMedia(byte[] bytes, PptxImage info)
    {
        string name = NextPartName("/ppt/media/image{0}" + info.Extension);
        Package.AddPart(name, info.ContentType, bytes);
        return name;
    }

    /// <summary>Adds a relationship from a part and returns its id.</summary>
    internal string AddPartRelationship(string source, string type, string target, RelationshipTargetMode mode = RelationshipTargetMode.Internal)
    {
        var rels = new List<Relationship>(Package.GetRelationships(source));
        int n = 1;
        while (rels.Exists(r => r.Id == "rId" + n.ToString(CultureInfo.InvariantCulture)))
        {
            n++;
        }

        string id = "rId" + n.ToString(CultureInfo.InvariantCulture);
        rels.Add(new Relationship(id, type, target, mode));
        Package.SetRelationships(source, rels);
        return id;
    }

    internal string ImageRelationshipType => RelationshipType("image");
}

public sealed partial class Slide
{
    private const double EmuPerPoint = 12700;

    /// <summary>
    /// Sets the text of a placeholder of the slide. <paramref name="placeholder"/> is the placeholder type as PowerPoint writes it:
    /// <c>title</c> (also finds <c>ctrTitle</c>), <c>subTitle</c>, <c>body</c>, <c>obj</c> (the content placeholder), <c>dt</c>, <c>ftr</c>… The
    /// first placeholder of that type wins; use the <c>index</c> overload to pick a particular one. Lines of the text become paragraphs
    /// (see <see cref="TextBody.SetText(string)"/>).
    /// </summary>
    /// <param name="placeholder">The placeholder type, ignoring case.</param>
    /// <param name="text">The text.</param>
    /// <returns>The shape that holds the text.</returns>
    /// <exception cref="PresentationAuthoringException">The slide has no such placeholder, or it holds no text (<c>pptx.placeholder-not-found</c>); the message lists the placeholders there are.</exception>
    public Shape SetPlaceholderText(string placeholder, string text)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        ArgumentNullException.ThrowIfNull(text);
        foreach (Shape shape in AllShapes())
        {
            if (shape.Placeholder is { } ph && (string.Equals(ph.Type, placeholder, StringComparison.OrdinalIgnoreCase)
                || (string.Equals(placeholder, "title", StringComparison.OrdinalIgnoreCase) && ph.Type == "ctrTitle")) && shape.TextBody is { } body)
            {
                body.SetText(text);
                return shape;
            }
        }

        throw NoPlaceholder("type '" + placeholder + "'");
    }

    /// <summary>Sets the text of the placeholder with the given <c>idx</c> (ECMA-376 Part 1 §19.3.1.36); see <see cref="SetPlaceholderText(string, string)"/>.</summary>
    /// <param name="index">The placeholder's index; the title is 0, the first content placeholder usually 1.</param>
    /// <param name="text">The text.</param>
    /// <returns>The shape that holds the text.</returns>
    /// <exception cref="PresentationAuthoringException">The slide has no such placeholder, or it holds no text (<c>pptx.placeholder-not-found</c>).</exception>
    public Shape SetPlaceholderText(int index, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (Shape shape in AllShapes())
        {
            if (shape.Placeholder is { } ph && ph.Index == index && shape.TextBody is { } body)
            {
                body.SetText(text);
                return shape;
            }
        }

        throw NoPlaceholder("index " + index.ToString(CultureInfo.InvariantCulture));
    }

    private PresentationAuthoringException NoPlaceholder(string what)
    {
        var found = new List<string>();
        foreach (Shape shape in AllShapes())
        {
            if (shape.Placeholder is { } ph)
            {
                found.Add(ph.Type + " (idx " + ph.Index.ToString(CultureInfo.InvariantCulture) + (shape.TextBody is null ? ", no text" : string.Empty) + ")");
            }
        }

        return new PresentationAuthoringException("pptx.placeholder-not-found",
            "The slide has no text placeholder with " + what + ". It has: " + (found.Count == 0 ? "(none)" : string.Join(", ", found)) + ".", DefectLocation.InPart(PartName));
    }

    /// <summary>
    /// Adds a picture to the slide, on top of everything else. The picture is stored as a media part.
    /// </summary>
    /// <param name="image">A PNG, JPEG, GIF or BMP picture.</param>
    /// <param name="left">The distance of the picture's left edge from the slide's, in points (1/72 inch).</param>
    /// <param name="top">The distance of the top edge from the slide's, in points.</param>
    /// <param name="width">The width in points.</param>
    /// <param name="height">The height in points; null keeps the picture's proportions.</param>
    /// <param name="altText">The alternative text for readers who cannot see the picture; recommended (the accessibility check asks for it).</param>
    /// <returns>The new picture shape.</returns>
    /// <exception cref="ArgumentException">The bytes are not a supported picture, a size is not positive or a number is not finite.</exception>
    public Shape AddPicture(byte[] image, double left, double top, double width, double? height = null, string? altText = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!PptxImage.TryRead(image, out PptxImage info))
        {
            throw new ArgumentException("The bytes are not a PNG, JPEG, GIF or BMP picture.", nameof(image));
        }

        if (altText is not null)
        {
            RequireXmlText(altText, nameof(altText));
        }

        double h = height ?? width * (info.Height > 0 ? info.Height : 96) / (info.Width > 0 ? info.Width : 96);
        (long x, long y, long cx, long cy) = Geometry(left, top, width, h);
        MarkupRef tree = RequireShapeTree();
        string media = Owner.AddMedia(image, info);
        string rid = Owner.AddPartRelationship(PartName, Owner.ImageRelationshipType, Presentation.RelativeTarget(PartName, media));
        long id = NextShapeId(tree);
        (string p, string a, string r) = Namespaces();
        string xml = "<p:pic xmlns:p=\"" + p + "\" xmlns:a=\"" + a + "\" xmlns:r=\"" + r + "\"><p:nvPicPr><p:cNvPr id=\"" + id.ToString(CultureInfo.InvariantCulture)
            + "\" name=\"Picture " + id.ToString(CultureInfo.InvariantCulture) + "\"" + (altText is null ? string.Empty : " descr=\"" + Pml.Escape(altText) + "\"")
            + "/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed=\"" + rid
            + "\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr><a:xfrm><a:off x=\"" + x.ToString(CultureInfo.InvariantCulture) + "\" y=\"" + y.ToString(CultureInfo.InvariantCulture)
            + "\"/><a:ext cx=\"" + cx.ToString(CultureInfo.InvariantCulture) + "\" cy=\"" + cy.ToString(CultureInfo.InvariantCulture)
            + "\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>";
        return InsertShape(tree, xml, id);
    }

    /// <summary>
    /// Adds an empty table of <paramref name="rows"/> by <paramref name="columns"/> cells; see <see cref="AddTable(IEnumerable{IEnumerable{string}}, double, double, double, double)"/>.
    /// </summary>
    /// <param name="rows">The number of rows, 1 to 1,000.</param>
    /// <param name="columns">The number of columns, 1 to 63.</param>
    /// <param name="left">The left edge in points.</param>
    /// <param name="top">The top edge in points.</param>
    /// <param name="width">The table's width in points; the columns share it equally.</param>
    /// <param name="rowHeight">The height of each row in points (default 28.8, 0.4 inch).</param>
    /// <returns>The new table.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A count is outside its range.</exception>
    /// <exception cref="ArgumentException">A size is not positive or a number is not finite.</exception>
    public Table AddTable(int rows, int columns, double left, double top, double width, double rowHeight = 28.8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, 1000);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, 63);
        return BuildTable(rows, columns, null, left, top, width, rowHeight);
    }

    /// <summary>
    /// Adds a table filled from rows of text, using PowerPoint's default table style with a header row. The table has as many columns
    /// as the longest row (shorter rows get empty cells). A line feed in a cell text starts a new paragraph.
    /// </summary>
    /// <param name="rows">The rows, each a sequence of cell texts; null texts are empty.</param>
    /// <param name="left">The left edge in points.</param>
    /// <param name="top">The top edge in points.</param>
    /// <param name="width">The table's width in points; the columns share it equally.</param>
    /// <param name="rowHeight">The height of each row in points (default 28.8, 0.4 inch).</param>
    /// <returns>The new table; reach a cell with <c>table[row, column]</c>.</returns>
    /// <exception cref="ArgumentException">There is no row or cell, there are more than 63 columns or 1,000 rows, a size is not positive, or a text holds a character XML cannot carry; the slide is unchanged.</exception>
    public Table AddTable(IEnumerable<IEnumerable<string>> rows, double left, double top, double width, double rowHeight = 28.8)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var all = new List<List<string>>();
        int columns = 0;
        foreach (IEnumerable<string> row in rows)
        {
            ArgumentNullException.ThrowIfNull(row);
            var cells = new List<string>();
            foreach (string cell in row)
            {
                cells.Add(cell ?? string.Empty);
            }

            columns = Math.Max(columns, cells.Count);
            all.Add(cells);
        }

        if (all.Count is 0 or > 1000 || columns is 0 or > 63)
        {
            throw new ArgumentException("A table has 1 to 1,000 rows and 1 to 63 columns.", nameof(rows));
        }

        return BuildTable(all.Count, columns, all, left, top, width, rowHeight);
    }

    private Table BuildTable(int rowCount, int columnCount, List<List<string>>? cells, double left, double top, double width, double rowHeight)
    {
        (long x, long y, long cx, long cy) = Geometry(left, top, width, rowHeight * rowCount);
        long rowEmu = Math.Max(1, (long)Math.Round(rowHeight * EmuPerPoint));
        if (cells is not null)
        {
            foreach (List<string> row in cells)
            {
                foreach (string t in row)
                {
                    RequireXmlText(t, nameof(cells));
                }
            }
        }

        MarkupRef tree = RequireShapeTree();
        long id = NextShapeId(tree);
        (string p, string a, _) = Namespaces();
        bool strict = Owner.IsStrict;
        long colEmu = Math.Max(1, cx / columnCount);
        var sb = new StringBuilder();
        // ECMA-376 Part 1 §20.1.4.2.27 (tbl): tblPr, tblGrid, then rows; each cell holds a txBody and tcPr. The table style id is PowerPoint's
        // built-in "Medium Style 2 - Accent 1", which PowerPoint knows without a definition in tableStyles.xml.
        sb.Append("<p:graphicFrame xmlns:p=\"").Append(p).Append("\" xmlns:a=\"").Append(a).Append("\"><p:nvGraphicFramePr><p:cNvPr id=\"").Append(id.ToString(CultureInfo.InvariantCulture))
            .Append("\" name=\"Table ").Append(id.ToString(CultureInfo.InvariantCulture)).Append("\"/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp=\"1\"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>")
            .Append("<p:xfrm><a:off x=\"").Append(x.ToString(CultureInfo.InvariantCulture)).Append("\" y=\"").Append(y.ToString(CultureInfo.InvariantCulture)).Append("\"/><a:ext cx=\"")
            .Append((colEmu * columnCount).ToString(CultureInfo.InvariantCulture)).Append("\" cy=\"").Append((rowEmu * rowCount).ToString(CultureInfo.InvariantCulture)).Append("\"/></p:xfrm>")
            .Append("<a:graphic><a:graphicData uri=\"").Append(strict ? "http://purl.oclc.org/ooxml/drawingml/table" : "http://schemas.openxmlformats.org/drawingml/2006/table")
            .Append("\"><a:tbl><a:tblPr firstRow=\"1\" bandRow=\"1\"><a:tableStyleId>{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}</a:tableStyleId></a:tblPr><a:tblGrid>");
        for (int c = 0; c < columnCount; c++)
        {
            sb.Append("<a:gridCol w=\"").Append(colEmu.ToString(CultureInfo.InvariantCulture)).Append("\"/>");
        }

        sb.Append("</a:tblGrid>");
        for (int r = 0; r < rowCount; r++)
        {
            sb.Append("<a:tr h=\"").Append(rowEmu.ToString(CultureInfo.InvariantCulture)).Append("\">");
            for (int c = 0; c < columnCount; c++)
            {
                string text = cells is not null && c < cells[r].Count ? cells[r][c] : string.Empty;
                sb.Append("<a:tc><a:txBody><a:bodyPr/><a:lstStyle/>");
                foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
                {
                    sb.Append(line.Length == 0
                        ? "<a:p><a:endParaRPr lang=\"en-US\"/></a:p>"
                        : "<a:p><a:r><a:rPr lang=\"en-US\"/><a:t>" + Pml.Escape(line) + "</a:t></a:r></a:p>");
                }

                sb.Append("</a:txBody><a:tcPr/></a:tc>");
            }

            sb.Append("</a:tr>");
        }

        sb.Append("</a:tbl></a:graphicData></a:graphic></p:graphicFrame>");
        return InsertShape(tree, sb.ToString(), id).Table!;
    }

    private static (long X, long Y, long Cx, long Cy) Geometry(double left, double top, double width, double height)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            throw new ArgumentException("A position must be finite and a size a positive number of points.");
        }

        // ECMA-376 Part 1 §20.1.10.16 (ST_Coordinate): 64-bit EMU; Office keeps shapes within about 51 million EMU of the origin.
        const double Limit = 51_206_400;
        long x = (long)Math.Round(left * EmuPerPoint);
        long y = (long)Math.Round(top * EmuPerPoint);
        long cx = Math.Max(1, (long)Math.Round(width * EmuPerPoint));
        long cy = Math.Max(1, (long)Math.Round(height * EmuPerPoint));
        if (Math.Abs(x) > Limit || Math.Abs(y) > Limit || cx > Limit || cy > Limit)
        {
            throw new ArgumentException("The position or size is outside what a slide can hold.");
        }

        return (x, y, cx, cy);
    }

    private (string P, string A, string R) Namespaces() => Owner.IsStrict ? (Pml.PStrict, Pml.AStrict, Pml.RStrict) : (Pml.PTransitional, Pml.ATransitional, Pml.RTransitional);

    private MarkupRef RequireShapeTree()
    {
        MarkupRef tree = ShapeTreeMarkup;
        return tree.IsValid ? tree : throw new InvalidOperationException("The slide has no shape tree to add to.");
    }

    // ECMA-376 Part 1 §19.3.1.12 (cNvPr): ids are unique within the slide.
    private static long NextShapeId(MarkupRef tree)
    {
        long max = 1;
        var stack = new Stack<MarkupNode>();
        stack.Push(tree.Node);
        while (stack.Count > 0)
        {
            foreach (MarkupNode child in stack.Pop().ChildElements)
            {
                if (child.LocalNameString == "cNvPr" && long.TryParse(child.GetAttribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                {
                    max = Math.Max(max, id);
                }

                stack.Push(child);
            }
        }

        return max + 1;
    }

    // The new shape goes at the end of the tree, before a trailing extLst (the schema puts it last).
    private Shape InsertShape(MarkupRef tree, string xml, long id)
    {
        MarkupNode? ext = null;
        foreach (MarkupNode child in tree.Node.ChildElements)
        {
            if (child.LocalNameString == "extLst")
            {
                ext = child;
            }
        }

        byte[] bytes = Encoding.UTF8.GetBytes(xml);
        if (ext is { } e)
        {
            e.InsertXmlBefore(bytes);
        }
        else
        {
            tree.Node.AppendXml(bytes);
        }

        foreach (Shape shape in Shapes)
        {
            if (shape.Id == id)
            {
                return shape;
            }
        }

        throw new InvalidOperationException("The new shape was not found in the shape tree.");
    }

    private static void RequireXmlText(string s, string paramName)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool ok = c is '\t' or '\n' or '\r' || (c >= 0x20 && c < 0xD800) || (c >= 0xE000 && c <= 0xFFFD);
            if (!ok && char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
                ok = true;
            }

            if (!ok)
            {
                throw new ArgumentException("The text holds a character that XML cannot carry (U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) + ").", paramName);
            }
        }
    }
}
