using System.Globalization;
using System.Linq;
using RecursiveAI.OneDocument.NumberFormats;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets.Templating;

namespace RecursiveAI.OneDocument.Spreadsheets;

/// <summary>
/// A rectangle of cells on a sheet, for bulk reads and writes: <c>sheet.Range("A1:C10").Values = rows;</c>. A range is a lightweight
/// handle over the sheet (it holds no copy of the cells), so it sees later edits. Writes that would touch more than
/// <see cref="MaxWriteCells"/> cells are refused, so a whole-column address cannot create a million cells by accident.
/// </summary>
public sealed class SheetRange
{
    /// <summary>The most cells a single write (<see cref="Values"/>, styles, <see cref="Clear"/>) or read may cover: 4,194,304.</summary>
    public const int MaxWriteCells = 4_194_304;

    private readonly Worksheet _sheet;

    internal SheetRange(Worksheet sheet, CellRange range)
    {
        _sheet = sheet;
        Address = range;
    }

    /// <summary>Gets the sheet.</summary>
    public Worksheet Sheet => _sheet;

    /// <summary>Gets the rectangle.</summary>
    public CellRange Address { get; }

    /// <summary>Gets the number of rows.</summary>
    public int RowCount => Address.RowCount;

    /// <summary>Gets the number of columns.</summary>
    public int ColumnCount => Address.ColumnCount;

    /// <summary>Gets the cell at a zero-based offset from the range's top-left cell.</summary>
    /// <param name="row">Rows down from the top.</param>
    /// <param name="column">Columns right from the left.</param>
    /// <exception cref="ArgumentOutOfRangeException">The offset is outside the range.</exception>
    public Cell this[int row, int column]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, RowCount);
            ArgumentOutOfRangeException.ThrowIfNegative(column);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, ColumnCount);
            return _sheet[Address.TopLeft.Row + row, Address.TopLeft.Column + column];
        }
    }

    /// <summary>Enumerates the cells row by row.</summary>
    /// <returns>The cell handles (cells that hold nothing included).</returns>
    public IEnumerable<Cell> Cells()
    {
        for (int r = Address.TopLeft.Row; r <= Address.BottomRight.Row; r++)
        {
            for (int c = Address.TopLeft.Column; c <= Address.BottomRight.Column; c++)
            {
                yield return _sheet[r, c];
            }
        }
    }

    /// <summary>
    /// Gets or sets the values as a two-dimensional array, rows first. Reading gives <c>null</c> for an empty cell, <see cref="double"/>
    /// for a number, <see cref="DateTime"/> for a number the cell's format shows as a date, <see cref="string"/>, <see cref="bool"/>
    /// and <see cref="CellError"/>. Writing takes the same, plus any integer or floating type, <see cref="decimal"/>,
    /// <see cref="DateOnly"/> and <see cref="CellValue"/>; <c>null</c> empties the cell (style and formula stay). The array must have
    /// exactly the range's dimensions. Strings are stored as text, never parsed as formulas or numbers; set <see cref="Cell.Formula"/> for formulas.
    /// Nothing is written when any element is rejected.
    /// </summary>
    /// <exception cref="ArgumentException">When setting: the dimensions differ from the range's, an element has a type that is not listed, or a number is not finite.</exception>
    /// <exception cref="InvalidOperationException">The range covers more than <see cref="MaxWriteCells"/> cells.</exception>
    public object?[,] Values
    {
        get
        {
            RequireSize();
            var result = new object?[RowCount, ColumnCount];
            for (int r = 0; r < RowCount; r++)
            {
                for (int c = 0; c < ColumnCount; c++)
                {
                    result[r, c] = Read(this[r, c]);
                }
            }

            return result;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            RequireSize();
            if (value.GetLength(0) != RowCount || value.GetLength(1) != ColumnCount)
            {
                throw new ArgumentException("The array is " + value.GetLength(0).ToString(CultureInfo.InvariantCulture) + " by " + value.GetLength(1).ToString(CultureInfo.InvariantCulture)
                    + " but the range " + Address + " is " + RowCount.ToString(CultureInfo.InvariantCulture) + " by " + ColumnCount.ToString(CultureInfo.InvariantCulture) + ".", nameof(value));
            }

            for (int r = 0; r < RowCount; r++)
            {
                for (int c = 0; c < ColumnCount; c++)
                {
                    Validate(value[r, c], r, c);
                }
            }

            for (int r = 0; r < RowCount; r++)
            {
                for (int c = 0; c < ColumnCount; c++)
                {
                    Write(this[r, c], value[r, c]);
                }
            }
        }
    }

    private static object? Read(Cell cell)
    {
        CellValue v = cell.Value;
        switch (v.Kind)
        {
            case CellValueKind.Empty:
                return null;
            case CellValueKind.Number:
                return cell.IsDate && cell.DateValue is { } d ? d : v.Number;
            case CellValueKind.Boolean:
                return v.Boolean;
            case CellValueKind.Error:
                return v.Error;
            default:
                return cell.Text;
        }
    }

    private void Validate(object? item, int row, int column)
    {
        bool ok = item switch
        {
            null or string or bool or CellError or CellValue or DateTime or DateOnly => true,
            double d => double.IsFinite(d),
            float f => float.IsFinite(f),
            decimal or int or long or short or byte or sbyte or uint or ushort or ulong => true,
            _ => false,
        };
        if (ok && item is DateTime dt)
        {
            ok = DateInRange(dt);
        }

        if (ok && item is DateOnly @do)
        {
            ok = DateInRange(@do.ToDateTime(TimeOnly.MinValue));
        }

        if (ok && item is string s)
        {
            ok = IsXmlText(s);
        }

        if (!ok)
        {
            throw new ArgumentException("The value at row " + row.ToString(CultureInfo.InvariantCulture) + ", column " + column.ToString(CultureInfo.InvariantCulture) + " ("
                + (item?.GetType().Name ?? "null") + ") cannot be stored in a cell; use null, text, a finite number, bool, DateTime, DateOnly, CellError or CellValue.");
        }
    }

    private bool DateInRange(DateTime value) => ExcelDate.TryToSerial(value, _sheet.Workbook.DateSystem, out _);

    // Text with control characters XML cannot carry would be stored as escapes; reject lone surrogates, which cannot be written at all.
    private static bool IsXmlText(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(s[i]) || s[i] is '\uFFFE' or '\uFFFF')
            {
                return false;
            }
        }

        return true;
    }

    private static void Write(Cell cell, object? item)
    {
        switch (item)
        {
            case null:
                cell.ClearValue();
                break;
            case string s:
                cell.SetString(s);
                break;
            case bool b:
                cell.SetBoolean(b);
                break;
            case CellError e:
                cell.SetError(e);
                break;
            case CellValue cv:
                cell.Value = cv;
                break;
            case DateTime dt:
                cell.SetDate(dt);
                break;
            case DateOnly d:
                cell.SetDate(d.ToDateTime(TimeOnly.MinValue));
                break;
            case double x:
                cell.SetNumber(x);
                break;
            case float x:
                cell.SetNumber(x);
                break;
            case decimal x:
                cell.SetNumber((double)x);
                break;
            case int x:
                cell.SetNumber(x);
                break;
            case long x:
                cell.SetNumber(x);
                break;
            case short x:
                cell.SetNumber(x);
                break;
            case byte x:
                cell.SetNumber(x);
                break;
            case sbyte x:
                cell.SetNumber(x);
                break;
            case uint x:
                cell.SetNumber(x);
                break;
            case ushort x:
                cell.SetNumber(x);
                break;
            case ulong x:
                cell.SetNumber(x);
                break;
        }
    }

    private void RequireSize()
    {
        if ((long)RowCount * ColumnCount > MaxWriteCells)
        {
            throw new InvalidOperationException("The range " + Address + " covers more than " + MaxWriteCells.ToString(CultureInfo.InvariantCulture) + " cells.");
        }
    }

    /// <summary>Gets the style of the top-left cell, resolved.</summary>
    public ResolvedCellStyle Style => this[0, 0].Style;

    /// <summary>Gives every cell of the range the cell format described by <paramref name="spec"/> (one format is added to the style sheet and shared).</summary>
    /// <param name="spec">The description.</param>
    /// <exception cref="InvalidOperationException">The range covers more than <see cref="MaxWriteCells"/> cells.</exception>
    public void SetStyle(CellStyleSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        RequireSize();
        int index = _sheet.Workbook.Styles.GetOrAdd(spec);
        foreach (Cell cell in Cells())
        {
            cell.StyleIndex = index;
        }
    }

    /// <summary>
    /// Changes part of every cell's style, keeping the rest: <paramref name="change"/> receives a spec equal to a cell's current style and
    /// returns the new one. It is called once per distinct current style.
    /// </summary>
    /// <param name="change">The edit, for example <c>s =&gt; s with { Font = s.Font! with { Bold = true } }</c>.</param>
    /// <exception cref="InvalidOperationException">The range covers more than <see cref="MaxWriteCells"/> cells.</exception>
    public void UpdateStyle(Func<CellStyleSpec, CellStyleSpec> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        RequireSize();
        StyleSheet styles = _sheet.Workbook.Styles;
        var done = new Dictionary<int, int>();
        foreach (Cell cell in Cells())
        {
            int current = cell.StyleIndex;
            if (!done.TryGetValue(current, out int next))
            {
                next = styles.GetOrAdd(change(CellStyleSpec.From(styles.Resolve(current))));
                done[current] = next;
            }

            cell.StyleIndex = next;
        }
    }

    /// <summary>
    /// Gets or sets the number format code of the range ("0.00", "yyyy-mm-dd", "General"); reading gives the top-left cell's. Setting
    /// keeps every other part of each cell's style. Null or "General" selects General.
    /// </summary>
    /// <exception cref="InvalidOperationException">When setting: the range covers more than <see cref="MaxWriteCells"/> cells.</exception>
    public string? NumberFormat
    {
        get => Style.NumberFormatCode;
        set => UpdateStyle(s => s with { NumberFormatCode = value });
    }

    /// <summary>Removes every cell of the range completely: value, formula, style and any markup kept for it. Merged ranges that lie inside are unmerged.</summary>
    /// <returns>The number of cells that existed.</returns>
    /// <exception cref="InvalidOperationException">The range covers more than <see cref="MaxWriteCells"/> cells.</exception>
    public int Clear()
    {
        RequireSize();
        int n = 0;
        foreach (Cell cell in Cells())
        {
            if (cell.Clear())
            {
                n++;
            }
        }

        foreach (CellRange merged in _sheet.MergedCells.ToArray())
        {
            if (merged.TopLeft.Row >= Address.TopLeft.Row && merged.BottomRight.Row <= Address.BottomRight.Row
                && merged.TopLeft.Column >= Address.TopLeft.Column && merged.BottomRight.Column <= Address.BottomRight.Column)
            {
                _sheet.Unmerge(merged);
            }
        }

        return n;
    }

    /// <summary>
    /// Merges the range into one cell, as Excel's Merge Cells does: the top-left cell's value stays and the values of the other cells are
    /// emptied (their styles stay).
    /// </summary>
    /// <exception cref="InvalidOperationException">The range is a single cell, overlaps a range that is already merged, or covers more than <see cref="MaxWriteCells"/> cells.</exception>
    public void Merge()
    {
        RequireSize();
        if (RowCount == 1 && ColumnCount == 1)
        {
            throw new InvalidOperationException("A single cell cannot be merged.");
        }

        foreach (CellRange other in _sheet.MergedCells)
        {
            if (other.Intersects(Address))
            {
                throw new InvalidOperationException("The range " + Address + " overlaps the merged range " + other + ".");
            }
        }

        foreach (Cell cell in Cells())
        {
            if (cell.Row != Address.TopLeft.Row || cell.Column != Address.TopLeft.Column)
            {
                if (cell.Exists)
                {
                    cell.Formula = null;
                    cell.ClearValue();
                }
            }
        }

        _sheet.Merge(Address);
    }

    /// <summary>Removes the merged range that equals this range.</summary>
    /// <returns>True when the range was merged.</returns>
    public bool Unmerge() => _sheet.Unmerge(Address);

    /// <inheritdoc/>
    public override string ToString() => _sheet.Name + "!" + Address;
}

public sealed partial class Worksheet
{
    /// <summary>Gets a range of cells by A1 reference: <c>"A1:C10"</c>, or a single cell <c>"B2"</c>.</summary>
    /// <param name="address">The reference; the corners may be given in either order.</param>
    /// <returns>The range handle.</returns>
    /// <exception cref="FormatException">The text is not a cell or cell range reference inside the sheet.</exception>
    public SheetRange Range(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (CellRange.TryParse(address, out CellRange range))
        {
            return new SheetRange(this, range);
        }

        CellAddress cell = CellAddress.Parse(address);
        return new SheetRange(this, new CellRange(cell, cell));
    }

    /// <summary>Gets a range of cells by its corners.</summary>
    /// <param name="range">The rectangle.</param>
    /// <returns>The range handle.</returns>
    /// <exception cref="ArgumentException">The rectangle is not valid.</exception>
    public SheetRange Range(CellRange range)
    {
        if (!range.IsValid)
        {
            throw new ArgumentException("The range is not valid.", nameof(range));
        }

        return new SheetRange(this, range);
    }

    /// <summary>
    /// Adds a picture to the sheet. It is stored as a media part and shown from the top-left corner of a cell at the size given (the
    /// other side keeps the picture's proportions; with neither, the picture's pixel size). It moves with its cell but does not resize with the columns.
    /// </summary>
    /// <param name="image">A PNG, JPEG, GIF or BMP picture.</param>
    /// <param name="topLeft">The cell the picture's top-left corner sits in, such as <c>"E2"</c>.</param>
    /// <param name="widthPixels">The width in pixels at 96 dpi, or null.</param>
    /// <param name="heightPixels">The height in pixels, or null.</param>
    /// <param name="altText">The alternative text for readers who cannot see the picture; recommended.</param>
    /// <returns>The shape id of the picture in the sheet's drawing.</returns>
    /// <exception cref="ArgumentException">The bytes are not a supported picture, a size is not positive, or the text holds a character XML cannot carry.</exception>
    /// <exception cref="InvalidOperationException">The sheet is not a worksheet or has no part yet.</exception>
    public int AddPicture(byte[] image, string topLeft, int? widthPixels = null, int? heightPixels = null, string? altText = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        CellAddress anchor = CellAddress.Parse(topLeft);
        if (widthPixels is <= 0 || heightPixels is <= 0)
        {
            throw new ArgumentException("A picture size must be a positive number of pixels.");
        }

        if (!ImageHeader.TryRead(image, out ImageHeader info))
        {
            throw new ArgumentException("The bytes are not a PNG, JPEG, GIF or BMP picture.", nameof(image));
        }

        if (altText is not null)
        {
            foreach (char c in altText)
            {
                if (char.IsControl(c) && c is not ('\t' or '\n' or '\r'))
                {
                    throw new ArgumentException("The alternative text holds a control character.", nameof(altText));
                }
            }
        }

        if (Kind != SheetKind.Worksheet || PartName is not { } sheetPart)
        {
            throw new InvalidOperationException("Pictures can be added to worksheets only.");
        }

        Workbook wb = Workbook;
        string drawingPart = SheetFeatures.FindOrCreateDrawing(wb, this, sheetPart);
        IPackagePart part = wb.Package.FindPart(drawingPart)!;
        MarkupDocument doc = MarkupDocument.Parse(part.GetBytes(), wb.OpenContext, drawingPart);
        int shapeId = 1;
        foreach (MarkupNode n in PartTools.Descendants(doc.Root))
        {
            if (n.LocalNameString == "cNvPr" && int.TryParse(n.GetAttribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int existing))
            {
                shapeId = Math.Max(shapeId, existing + 1);
            }
        }

        string media = PartTools.NextName(wb.Package, "/xl/media/image", info.Extension);
        wb.Package.AddPart(media, info.ContentType, image);
        var rels = new List<Relationship>(wb.Package.GetRelationships(drawingPart));
        string rid = PartTools.NextRelationshipId(rels);
        rels.Add(new Relationship(rid, PartTools.RelBase + "image", PartTools.Relative(drawingPart, media)));
        wb.Package.SetRelationships(drawingPart, rels);

        int w = info.Width > 0 ? info.Width : 96;
        int h = info.Height > 0 ? info.Height : 96;
        int widthPx = widthPixels ?? (heightPixels is { } hh ? Math.Max(1, (int)Math.Round((double)hh * w / h)) : w);
        int heightPx = heightPixels ?? (widthPixels is { } ww ? Math.Max(1, (int)Math.Round((double)ww * h / w)) : h);

        (int col2, int colOff) = SheetFeatures.Walk(anchor.Column, widthPx, c => SheetFeatures.ColumnPixels(this, c));
        (int row2, int rowOff) = SheetFeatures.Walk(anchor.Row, heightPx, r => SheetFeatures.RowPixels(this, r));
        var sb = new StringBuilder();
        // ECMA-376 Part 1 §20.5.2.33 (twoCellAnchor) and §20.5.2.25 (pic): 9525 EMU per pixel at 96 dpi.
        sb.Append("<xdr:twoCellAnchor xmlns:xdr=\"").Append(SheetFeatures.SpreadsheetDrawingNs).Append("\" xmlns:a=\"").Append(PartTools.DrawingNs).Append("\" xmlns:r=\"").Append(PartTools.RelNs).Append("\" editAs=\"oneCell\">");
        sb.Append("<xdr:from><xdr:col>").Append(anchor.Column - 1).Append("</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>").Append(anchor.Row - 1).Append("</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>");
        sb.Append("<xdr:to><xdr:col>").Append(col2 - 1).Append("</xdr:col><xdr:colOff>").Append(colOff * 9525L).Append("</xdr:colOff><xdr:row>").Append(row2 - 1).Append("</xdr:row><xdr:rowOff>").Append(rowOff * 9525L).Append("</xdr:rowOff></xdr:to>");
        sb.Append("<xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"").Append(shapeId).Append("\" name=\"Picture ").Append(shapeId).Append('"');
        if (altText is not null)
        {
            sb.Append(" descr=\"").Append(PartTools.Escape(altText)).Append('"');
        }

        sb.Append("/><xdr:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></xdr:cNvPicPr></xdr:nvPicPr><xdr:blipFill><a:blip r:embed=\"").Append(rid).Append("\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>");
        sb.Append("<xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").Append(widthPx * 9525L).Append("\" cy=\"").Append(heightPx * 9525L).Append("\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:twoCellAnchor>");
        doc.Root.AppendXml(Encoding.UTF8.GetBytes(sb.ToString()));
        PartTools.Write(part, doc);
        return shapeId;
    }
}
