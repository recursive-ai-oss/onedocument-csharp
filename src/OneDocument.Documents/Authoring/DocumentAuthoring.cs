using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Documents.Styles;
using RecursiveAI.OneDocument.Schema;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Templating;
using RecursiveAI.OneDocument.Inspection;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Documents.Revisions;
using W = RecursiveAI.OneDocument.Schema.Wordprocessing;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    /// <summary>
    /// Removes hidden data and personal information from the document in memory, the categories of <paramref name="options"/> (by
    /// default <see cref="SanitizeOptions.Default"/>), so <c>doc.Sanitize(); doc.SaveAs("clean.docx");</c> works. Pending edits are
    /// committed first. The same sanitiser as <see cref="Office.Sanitize(string, string, OpenContext?, CancellationToken)"/> runs, including its verification: the result is inspected again
    /// on this call and, if a category reported as removed still holds something, <see cref="SanitizeVerificationException"/> is thrown
    /// and the document is left as it was. Only the parts the sanitiser changes are rewritten; the others stay byte-identical.
    /// </summary>
    /// <param name="options">The categories to remove; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the operation; the document is left as it was.</param>
    /// <returns>What was removed, what was kept and why, and the inspection of the result.</returns>
    /// <remarks>
    /// Objects obtained from the document before the call (<see cref="Paragraph"/>, <see cref="Table"/>, <see cref="StyleSheet"/>, …)
    /// no longer belong to it afterwards; read them again from the document. <see cref="OfficeFile.Format"/> does not change, so when the
    /// report's <see cref="SanitizeReport.OutputFormat"/> differs (a macro project was removed) name the file by that format.
    /// </remarks>
    /// <exception cref="SanitizeVerificationException">The result still holds something in a category reported as removed; nothing was changed.</exception>
    /// <exception cref="ObjectDisposedException">The document is disposed.</exception>
    public SanitizeReport Sanitize(SanitizeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CommitChanges();
        SanitizeReport report = PackageSync.SanitizeInPlace(Package, options, Context, cancellationToken);
        ForgetParsedParts();
        return report;
    }

    // Everything the hub parsed from the package is dropped, so the next use reads the sanitised parts.
    private void ForgetParsedParts()
    {
        lock (Parts.Gate)
        {
            Parts.Clear();
            _body = null;
            _main = null;
            _styles = null;
            _numbering = null;
            _theme = null;
            _comments = null;
            _footnotes = null;
            _endnotes = null;
            _revisions = null;
            _properties = null;
            _customXmlParts = null;
            _coreProperties = null;
            _coreResolved = false;
            _textBoxes.Clear();
            _mdPristine = false;
            _odfPristine = false;
            _rtfPristine = false;
        }
    }

    /// <summary>Adds an external or internal relationship to the main part and returns its id.</summary>
    internal string AddMainRelationship(string type, string target, RelationshipTargetMode mode)
    {
        var rels = new List<Relationship>(Package.GetRelationships(MainPartName));
        int n = 1;
        while (rels.Exists(r => r.Id == "rId" + n.ToString(CultureInfo.InvariantCulture)))
        {
            n++;
        }

        string id = "rId" + n.ToString(CultureInfo.InvariantCulture);
        rels.Add(new Relationship(id, type, target, mode));
        Package.SetRelationships(MainPartName, rels);
        return id;
    }

    /// <summary>Stores a picture as a media part related to the main part; returns the relationship id.</summary>
    internal string AddPictureRelationship(byte[] bytes, ImageInfo info)
    {
        int n = 1;
        string partName;
        do
        {
            partName = "/word/media/image" + n.ToString(CultureInfo.InvariantCulture) + "." + info.Extension;
            n++;
        }
        while (Package.FindPart(partName) is not null);

        Package.AddPart(partName, info.ContentType, bytes);
        return AddMainRelationship(OfficeRelationship("image"), partName["/word/".Length..], RelationshipTargetMode.Internal);
    }

    // ECMA-376 Part 1 §20.4.2.5 (docPr): ids identify drawing objects and must be unique in the document.
    internal string NextDrawingId()
    {
        long max = 0;
        var stack = new Stack<MarkupNode>();
        stack.Push(MainMarkup.Root);
        while (stack.Count > 0)
        {
            MarkupNode node = stack.Pop();
            foreach (MarkupNode child in node.ChildElements)
            {
                if (child.LocalNameString == "docPr" && long.TryParse(child.GetAttribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                {
                    max = Math.Max(max, id);
                }

                stack.Push(child);
            }
        }

        return (max + 1).ToString(CultureInfo.InvariantCulture);
    }
}

public sealed partial class Run
{
    /// <summary>
    /// Gets or sets direct bold (<c>w:b</c>): true turns it on, false turns it off explicitly (overriding the style), null removes the
    /// direct setting so the style decides. Reading gives the direct setting only, never the style's.
    /// </summary>
    public bool? Bold
    {
        get => Properties?.Bold is { } b ? b.Val ?? true : null;
        set => SetOnOff(value, static p => p.GetOrAddBold(), static p => p.RemoveBold());
    }

    /// <summary>Gets or sets direct italic (<c>w:i</c>); true, false or null as for <see cref="Bold"/>.</summary>
    public bool? Italic
    {
        get => Properties?.Italic is { } b ? b.Val ?? true : null;
        set => SetOnOff(value, static p => p.GetOrAddItalic(), static p => p.RemoveItalic());
    }

    /// <summary>
    /// Gets or sets direct single underline (<c>w:u</c>): true underlines, false writes an explicit "none", null removes the direct
    /// setting. Other underline styles read as true; use <see cref="Properties"/> for them.
    /// </summary>
    public bool? Underline
    {
        get => Properties?.Underline is { } u ? !string.Equals(u.ValText, "none", StringComparison.Ordinal) : null;
        set
        {
            if (value is null)
            {
                Properties?.RemoveUnderline();
                return;
            }

            View.GetOrAddRunProperties().GetOrAddUnderline().ValText = value.Value ? "single" : "none";
        }
    }

    /// <summary>
    /// Gets or sets the direct font size in points (<c>w:sz</c>, stored in half-points; the complex-script size is set with it). Sizes
    /// are rounded to the nearest half point. Null removes the setting.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">When setting: the size is not between 0.5 and 1638 points (ECMA-376 Part 1 §17.3.2.38 allows 1 to 3276 half-points).</exception>
    public double? FontSize
    {
        get => Properties?.Size?.Val is { } v ? v / 2.0 : null;
        set
        {
            if (value is null)
            {
                Properties?.RemoveSize();
                Properties?.RemoveSizeComplexScript();
                return;
            }

            if (double.IsNaN(value.Value) || value.Value < 0.5 || value.Value > 1638)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A font size is 0.5 to 1638 points.");
            }

            long half = (long)Math.Round(value.Value * 2, MidpointRounding.AwayFromZero);
            W.RunProperties props = View.GetOrAddRunProperties();
            props.GetOrAddSize().Val = half;
            props.GetOrAddSizeComplexScript().Val = half;
        }
    }

    /// <summary>
    /// Gets or sets the direct text colour (<c>w:color</c>) as six hex digits ("C00000", any case; written upper case) or "auto".
    /// Null removes the setting.
    /// </summary>
    /// <exception cref="ArgumentException">When setting: the text is neither six hex digits nor "auto".</exception>
    public string? Color
    {
        get => Properties?.Color?.Val;
        set
        {
            if (value is null)
            {
                Properties?.RemoveColor();
                return;
            }

            string v = value.TrimStart('#');
            bool hex = v.Length == 6 && v.All(Uri.IsHexDigit);
            if (!hex && !string.Equals(v, "auto", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("A colour is six hex digits such as C00000, or auto.", nameof(value));
            }

            View.GetOrAddRunProperties().GetOrAddColor().Val = hex ? v.ToUpperInvariant() : "auto";
        }
    }

    /// <summary>
    /// Gets or sets the direct font name (<c>w:rFonts</c>); setting names the font for Latin, East Asian and complex-script text alike.
    /// Reading gives the Latin name. Null removes the setting.
    /// </summary>
    public string? FontName
    {
        get => Properties?.RunFonts?.Ascii;
        set
        {
            if (value is null)
            {
                Properties?.RemoveRunFonts();
                return;
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            WordXml.RequireXmlText(value, nameof(value));
            W.RunFonts fonts = View.GetOrAddRunProperties().GetOrAddRunFonts();
            fonts.Ascii = value;
            fonts.HAnsi = value;
            fonts.EastAsia = value;
            fonts.Cs = value;
        }
    }

    private void SetOnOff(bool? value, Func<W.RunProperties, W.OnOff> add, Func<W.RunProperties, bool> remove)
    {
        if (value is null)
        {
            if (Properties is { } existing)
            {
                remove(existing);
            }

            return;
        }

        W.OnOff element = add(View.GetOrAddRunProperties());
        element.Val = value.Value ? null : false;
    }
}

public sealed partial class Paragraph
{
    private string QualifiedFor(out string? declare)
    {
        string ns = Markup.NamespaceUriText;
        string prefix = Owner_Prefix(Markup.Node, ns, out bool needsDeclaration);
        declare = needsDeclaration ? ns : null;
        return prefix;
    }

    private MarkupNode AppendChildXml(string xml)
    {
        Markup.Node.AppendXml(Encoding.UTF8.GetBytes(xml));
        MarkupNode last = Markup.Node.LastChild;
        while (!last.IsNull && last.Kind != MarkupNodeKind.Element)
        {
            last = last.PreviousSibling;
        }

        return last;
    }

    /// <summary>
    /// Adds a run with the text at the end of the paragraph.
    /// </summary>
    /// <param name="text">The text; a tab character becomes <c>w:tab</c> and a line feed <c>w:br</c> (see <see cref="SetText(string)"/>).</param>
    /// <returns>The new run; set its <see cref="Run.Bold"/>, <see cref="Run.FontSize"/> and the other direct formatting properties.</returns>
    /// <exception cref="ArgumentException">The text holds a character XML cannot carry.</exception>
    public Run AddRun(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        WordXml.RequireXmlText(text, nameof(text));
        string prefix = QualifiedFor(out string? declare);
        string decl = declare is null ? string.Empty : " xmlns:" + prefix + "=\"" + declare + "\"";
        MarkupNode node = AppendChildXml("<" + prefix + ":r" + decl + ">" + RunContent(text, prefix) + "</" + prefix + ":r>");
        return new Run(Owner, new MarkupRef(node));
    }

    /// <summary>
    /// Adds a hyperlink with the given text at the end of the paragraph. An address starting with <c>#</c> links to a bookmark of the
    /// document; any other address becomes an external relationship of the main part (it is stored, never fetched). The text takes the
    /// <c>Hyperlink</c> character style, which is added when the document lacks it.
    /// </summary>
    /// <param name="text">The link text.</param>
    /// <param name="address">A URL, or <c>#bookmark</c>.</param>
    /// <returns>The new hyperlink.</returns>
    /// <exception cref="ArgumentException">The address is empty or the text holds a character XML cannot carry.</exception>
    public Hyperlink AddHyperlink(string text, string address)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        WordXml.RequireXmlText(text, nameof(text));
        WordXml.RequireXmlText(address, nameof(address));
        StyleDefinition link = Owner.Styles.AddBuiltIn("Hyperlink");
        string prefix = QualifiedFor(out string? declare);
        string decl = declare is null ? string.Empty : " xmlns:" + prefix + "=\"" + declare + "\"";
        string target;
        if (address.StartsWith('#'))
        {
            target = " " + prefix + ":anchor=\"" + WmlEdit.Escape(address[1..]) + "\"";
        }
        else
        {
            string id = Owner.AddMainRelationship(Owner.OfficeRelationship("hyperlink"), address, RelationshipTargetMode.External);
            string r = Owner.IsStrict ? "http://purl.oclc.org/ooxml/officeDocument/relationships" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            target = " xmlns:r=\"" + r + "\" r:id=\"" + id + "\"";
        }

        string run = "<" + prefix + ":r><" + prefix + ":rPr><" + prefix + ":rStyle " + prefix + ":val=\"" + WmlEdit.Escape(link.Id) + "\"/></" + prefix + ":rPr>" + RunContent(text, prefix) + "</" + prefix + ":r>";
        MarkupNode node = AppendChildXml("<" + prefix + ":hyperlink" + decl + target + " " + prefix + ":history=\"1\">" + run + "</" + prefix + ":hyperlink>");
        return new Hyperlink(Owner, new MarkupRef(node));
    }

    /// <summary>
    /// Adds an inline picture at the end of the paragraph. The picture is stored as a media part and shown at the size given; when only
    /// one side is given the other keeps the picture's proportions, and when neither is given the picture shows at its pixel size on a
    /// 96 dpi screen, scaled down to fit a 6 inch width.
    /// </summary>
    /// <param name="image">A PNG, JPEG, GIF or BMP picture.</param>
    /// <param name="widthPoints">The width in points (1/72 inch), or null.</param>
    /// <param name="heightPoints">The height in points, or null.</param>
    /// <param name="altText">The alternative text for readers who cannot see the picture; recommended.</param>
    /// <returns>The run that holds the picture.</returns>
    /// <exception cref="ArgumentException">The bytes are not a picture of a supported type, or a size is not positive.</exception>
    public Run AddPicture(byte[] image, double? widthPoints = null, double? heightPoints = null, string? altText = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (altText is not null)
        {
            WordXml.RequireXmlText(altText, nameof(altText));
        }

        if (!ImageInfo.TryRead(image, out ImageInfo info))
        {
            throw new ArgumentException("The bytes are not a PNG, JPEG, GIF or BMP picture.", nameof(image));
        }

        (long cx, long cy) = PictureSize(info, widthPoints, heightPoints);
        string rid = Owner.AddPictureRelationship(image, info);
        string id = Owner.NextDrawingId();
        string prefix = QualifiedFor(out string? declare);
        string drawing = Insertions.Drawing(Owner.IsStrict, prefix + ":", rid, id, "Picture " + id, WmlEdit.Escape(altText ?? string.Empty), cx, cy);
        string decl = declare is null ? string.Empty : " xmlns:" + prefix + "=\"" + declare + "\"";
        MarkupNode node = AppendChildXml("<" + prefix + ":r" + decl + ">" + drawing + "</" + prefix + ":r>");
        return new Run(Owner, new MarkupRef(node));
    }

    private static (long Cx, long Cy) PictureSize(ImageInfo info, double? widthPoints, double? heightPoints)
    {
        const double EmuPerPoint = 12700;
        if (widthPoints is <= 0 || heightPoints is <= 0 || (widthPoints is { } a && !double.IsFinite(a)) || (heightPoints is { } b && !double.IsFinite(b)))
        {
            throw new ArgumentException("A picture size must be a positive number of points.");
        }

        double pw = info.Width > 0 ? info.Width : 96;
        double ph = info.Height > 0 ? info.Height : 96;
        double w;
        double h;
        if (widthPoints is { } ww && heightPoints is { } hh)
        {
            w = ww;
            h = hh;
        }
        else if (widthPoints is { } w1)
        {
            w = w1;
            h = w1 * ph / pw;
        }
        else if (heightPoints is { } h1)
        {
            h = h1;
            w = h1 * pw / ph;
        }
        else
        {
            w = pw * 0.75;
            h = ph * 0.75;
            if (w > 432)
            {
                h *= 432 / w;
                w = 432;
            }
        }

        // ECMA-376 Part 1 §20.4.2.7 (extent): positive 64-bit EMU; 27,273,042,316,900 is the largest value Word accepts.
        return (Math.Max(1, (long)Math.Round(w * EmuPerPoint)), Math.Max(1, (long)Math.Round(h * EmuPerPoint)));
    }
}

public sealed partial class Body
{
    /// <summary>Adds a paragraph holding one inline picture at the end of the body; see <see cref="Paragraph.AddPicture(byte[], double?, double?, string?)"/>.</summary>
    /// <param name="image">A PNG, JPEG, GIF or BMP picture.</param>
    /// <param name="widthPoints">The width in points, or null.</param>
    /// <param name="heightPoints">The height in points, or null.</param>
    /// <param name="altText">The alternative text; recommended.</param>
    /// <returns>The new paragraph.</returns>
    /// <exception cref="ArgumentException">The bytes are not a supported picture, or a size is not positive; the body is unchanged.</exception>
    public Paragraph AddPicture(byte[] image, double? widthPoints = null, double? heightPoints = null, string? altText = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!ImageInfo.TryRead(image, out _))
        {
            throw new ArgumentException("The bytes are not a PNG, JPEG, GIF or BMP picture.", nameof(image));
        }

        Paragraph p = AddParagraph(string.Empty);
        p.AddPicture(image, widthPoints, heightPoints, altText);
        return p;
    }

    /// <summary>Adds an empty table of <paramref name="rows"/> by <paramref name="columns"/> cells; see <see cref="AddTable(IEnumerable{IEnumerable{string}})"/> for the layout.</summary>
    /// <param name="rows">The number of rows, at least 1.</param>
    /// <param name="columns">The number of columns, at least 1.</param>
    /// <returns>The new table.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A count is below 1 or above 1,000 (rows) or 63 (columns, Word's limit).</exception>
    public Table AddTable(int rows, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, 1_000_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, 63);
        return AddTableCore(rows, columns, null);
    }

    /// <summary>Adds a table filled from a two-dimensional array: the first index is the row, the second the column.</summary>
    /// <param name="cells">The cell texts; null entries are empty cells.</param>
    /// <returns>The new table.</returns>
    /// <exception cref="ArgumentException">The array is empty, or a text holds a character XML cannot carry.</exception>
    public Table AddTable(string?[,] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        int r = cells.GetLength(0);
        int c = cells.GetLength(1);
        if (r == 0 || c == 0)
        {
            throw new ArgumentException("A table needs at least one row and one column.", nameof(cells));
        }

        var rows = new List<List<string>>(r);
        for (int i = 0; i < r; i++)
        {
            var row = new List<string>(c);
            for (int j = 0; j < c; j++)
            {
                row.Add(cells[i, j] ?? string.Empty);
            }

            rows.Add(row);
        }

        return AddTableCore(r, c, rows);
    }

    /// <summary>
    /// Adds a table at the end of the body with one row per item of <paramref name="rows"/>. The table uses the <c>Table Grid</c> style
    /// (added when the document lacks it), has as many columns as the longest row (shorter rows get empty cells), and spans the
    /// page's text width in equal columns. Rows of strings, <c>string[][]</c> and lists all fit.
    /// </summary>
    /// <param name="rows">The rows, each a sequence of cell texts; null texts are empty.</param>
    /// <returns>The new table; reach a cell with <c>table[row, column]</c> or <see cref="Table.SetCellText(int, int, string)"/>.</returns>
    /// <exception cref="ArgumentException">There is no row or no cell, there are more than 63 columns, or a text holds a character XML cannot carry; the body is unchanged.</exception>
    public Table AddTable(IEnumerable<IEnumerable<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var all = new List<List<string>>();
        int columns = 0;
        foreach (IEnumerable<string> row in rows)
        {
            ArgumentNullException.ThrowIfNull(row);
            List<string> cells = [.. row.Select(static s => s ?? string.Empty)];
            columns = Math.Max(columns, cells.Count);
            all.Add(cells);
        }

        if (all.Count == 0 || columns == 0)
        {
            throw new ArgumentException("A table needs at least one row and one column.", nameof(rows));
        }

        if (columns > 63)
        {
            throw new ArgumentException("A table has at most 63 columns.", nameof(rows));
        }

        return AddTableCore(all.Count, columns, all);
    }

    // ECMA-376 Part 1 §17.4: tbl holds tblPr, tblGrid and rows; each tc holds tcPr and at least one block, so every cell gets a paragraph.
    private Table AddTableCore(int rowCount, int columnCount, List<List<string>>? cells)
    {
        if (!Container.IsValid)
        {
            throw new InvalidOperationException("The document has no body to add a table to.");
        }

        if (cells is not null)
        {
            foreach (List<string> row in cells)
            {
                foreach (string t in row)
                {
                    WordXml.RequireXmlText(t, nameof(cells));
                }
            }
        }

        StyleDefinition grid = Owner.Styles.AddBuiltIn("Table Grid");
        string ns = Container.NamespaceUriText;
        string p = Paragraph.Owner_Prefix(Container.Node, ns, out bool declare);
        int each = TextWidthTwips() / columnCount;
        string w = each.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append('<').Append(p).Append(":tbl");
        if (declare)
        {
            sb.Append(" xmlns:").Append(p).Append("=\"").Append(ns).Append('"');
        }

        sb.Append("><").Append(p).Append(":tblPr><").Append(p).Append(":tblStyle ").Append(p).Append(":val=\"").Append(WmlEdit.Escape(grid.Id)).Append("\"/><")
            .Append(p).Append(":tblW ").Append(p).Append(":w=\"0\" ").Append(p).Append(":type=\"auto\"/><")
            .Append(p).Append(":tblLook ").Append(p).Append(":val=\"04A0\" ").Append(p).Append(":firstRow=\"1\" ").Append(p).Append(":lastRow=\"0\" ")
            .Append(p).Append(":firstColumn=\"1\" ").Append(p).Append(":lastColumn=\"0\" ").Append(p).Append(":noHBand=\"0\" ").Append(p).Append(":noVBand=\"1\"/></")
            .Append(p).Append(":tblPr><").Append(p).Append(":tblGrid>");
        for (int c = 0; c < columnCount; c++)
        {
            sb.Append('<').Append(p).Append(":gridCol ").Append(p).Append(":w=\"").Append(w).Append("\"/>");
        }

        sb.Append("</").Append(p).Append(":tblGrid>");
        for (int r = 0; r < rowCount; r++)
        {
            sb.Append('<').Append(p).Append(":tr>");
            for (int c = 0; c < columnCount; c++)
            {
                sb.Append('<').Append(p).Append(":tc><").Append(p).Append(":tcPr><").Append(p).Append(":tcW ").Append(p).Append(":w=\"").Append(w).Append("\" ")
                    .Append(p).Append(":type=\"dxa\"/></").Append(p).Append(":tcPr><").Append(p).Append(":p>");
                string text = cells is not null && c < cells[r].Count ? cells[r][c] : string.Empty;
                if (text.Length > 0)
                {
                    sb.Append('<').Append(p).Append(":r>").Append(Paragraph.RunContent(text, p)).Append("</").Append(p).Append(":r>");
                }

                sb.Append("</").Append(p).Append(":p></").Append(p).Append(":tc>");
            }

            sb.Append("</").Append(p).Append(":tr>");
        }

        sb.Append("</").Append(p).Append(":tbl>");
        MarkupNode created = InsertBeforeSectionProperties(Encoding.UTF8.GetBytes(sb.ToString()), ns);
        return (Table)BlockElement.Wrap(Owner, new MarkupRef(created));
    }

    // The page's text width: page width less the left and right margins of the body's last section (§17.6.11, §17.6.13), else 6.5 inches.
    private int TextWidthTwips()
    {
        const int Fallback = 9360;
        MarkupRef? sect = SectionProperties;
        if (sect is null)
        {
            return Fallback;
        }

        long pageWidth = 0;
        long left = 0;
        long right = 0;
        foreach (MarkupNode child in sect.Value.Node.ChildElements)
        {
            if (child.LocalNameString == "pgSz")
            {
                pageWidth = ReadTwips(child, "w");
            }
            else if (child.LocalNameString == "pgMar")
            {
                left = ReadTwips(child, "left");
                right = ReadTwips(child, "right");
            }
        }

        long width = pageWidth - left - right;
        return width >= 720 && width <= 31680 ? (int)width : Fallback;
    }

    private static long ReadTwips(MarkupNode e, string local) =>
        long.TryParse(Wml.Attr(new MarkupRef(e), local), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0;

    private MarkupNode InsertBeforeSectionProperties(byte[] xml, string ns)
    {
        MarkupNode? sectPr = null;
        foreach (MarkupNode child in Container.Node.ChildElements)
        {
            if (child.IsElement(ns, "sectPr"))
            {
                sectPr = child;
            }
        }

        MarkupNode created;
        if (sectPr is { } s)
        {
            s.InsertXmlBefore(xml);
            created = s.PreviousSibling;
        }
        else
        {
            Container.Node.AppendXml(xml);
            created = Container.Node.LastChild;
        }

        while (!created.IsNull && created.Kind != MarkupNodeKind.Element)
        {
            created = created.PreviousSibling;
        }

        return created;
    }
}

public sealed partial class Table
{
    /// <summary>Gets the cell at a grid position (a merged region answers to any of its positions).</summary>
    /// <param name="row">The zero-based row.</param>
    /// <param name="column">The zero-based grid column.</param>
    /// <exception cref="ArgumentOutOfRangeException">There is no cell at the position.</exception>
    public TableCell this[int row, int column] =>
        GetCell(row, column) ?? throw new ArgumentOutOfRangeException(nameof(column), "There is no cell at row " + row.ToString(CultureInfo.InvariantCulture) + ", column " + column.ToString(CultureInfo.InvariantCulture) + ".");
}
