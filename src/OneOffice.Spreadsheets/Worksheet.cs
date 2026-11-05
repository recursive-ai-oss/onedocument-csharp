using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Spreadsheets;

/// <summary>Flags of a column range.</summary>
[Flags]
public enum ColumnOptions : byte
{
    /// <summary>None set.</summary>
    None = 0,

    /// <summary>The columns are hidden.</summary>
    Hidden = 1,

    /// <summary>The width was set by hand (<c>customWidth</c>).</summary>
    CustomWidth = 2,

    /// <summary>The width was fitted to the content (<c>bestFit</c>).</summary>
    BestFit = 4,

    /// <summary>The outline group is collapsed.</summary>
    Collapsed = 8,

    /// <summary>Phonetic information is shown.</summary>
    Phonetic = 16,
}

/// <summary>
/// The properties of a run of columns (ECMA-376 Part 1 CT_Col): the columns <see cref="Min"/> to <see cref="Max"/>, 1-based.
/// </summary>
/// <param name="Min">The first column.</param>
/// <param name="Max">The last column.</param>
/// <param name="Width">The width in characters of the default font, as stored; negative when the part has none.</param>
/// <param name="Style">The style index of the columns' empty cells.</param>
/// <param name="Flags">Hidden, custom width, best fit, collapsed, phonetic.</param>
/// <param name="OutlineLevel">The outline level, 0 to 7.</param>
/// <param name="RawAttributes">Other attributes of the element exactly as written, or null. Written back unchanged.</param>
public sealed record ColumnProperties(int Min, int Max, double Width, int Style, ColumnOptions Flags, int OutlineLevel = 0, string? RawAttributes = null);

/// <summary>The state of a sheet (ECMA-376 Part 1 ST_SheetState).</summary>
public enum SheetState : byte
{
    /// <summary>Shown.</summary>
    Visible = 0,

    /// <summary>Hidden; the user can unhide it.</summary>
    Hidden = 1,

    /// <summary>Hidden so that only code can unhide it.</summary>
    VeryHidden = 2,
}

/// <summary>What kind of sheet an entry of the workbook is.</summary>
public enum SheetKind : byte
{
    /// <summary>A worksheet: cells.</summary>
    Worksheet = 0,

    /// <summary>A chart sheet (CT_Chartsheet): kept untouched.</summary>
    Chartsheet = 1,

    /// <summary>A dialog sheet (CT_Dialogsheet): kept untouched.</summary>
    DialogSheet = 2,

    /// <summary>An Excel 4.0 macro sheet: kept untouched.</summary>
    MacroSheet = 3,
}

/// <summary>
/// A sheet of a workbook. For a worksheet it holds the cells (<see cref="Store"/>), loaded when first touched, and the row and column
/// properties and merged cells; everything else in the part (views, validations, conditional formats, extensions, anything unknown)
/// stays where it was and is written back in order. Chart sheets, dialog sheets and macro sheets are listed and never opened.
/// </summary>
/// <remarks>
/// <para>Editing a sheet marks its part to be written from the store the next time the workbook is saved; a sheet nobody edited is
/// copied as stored, byte for byte. Renaming a sheet does not rewrite formulas or defined names that mention it.</para>
/// <para>Not safe for concurrent mutation; read-only access from several threads is safe once the sheet is loaded
/// (<see cref="Load"/>).</para>
/// </remarks>
public sealed partial class Worksheet
{
    private readonly Workbook _workbook;
    private CellStore? _store;
    private SheetFrame? _frame;
    private FrameModel? _model;
    private bool _forceRewrite;
    private SheetState _state;

    internal Worksheet(Workbook workbook, string name, int sheetId, string relationshipId, string? partName, SheetKind kind, SheetState state, MarkupNode node)
    {
        _workbook = workbook;
        Name = name;
        SheetId = sheetId;
        RelationshipId = relationshipId;
        PartName = partName;
        Kind = kind;
        _state = state;
        Node = node;
    }

    /// <summary>Gets the workbook the sheet belongs to.</summary>
    public Workbook Workbook => _workbook;

    /// <summary>Gets the name as shown on the tab. Change it with <see cref="Rename"/>.</summary>
    public string Name { get; internal set; }

    /// <summary>Gets the sheet id (<c>sheetId</c>): stable for the life of the sheet, unlike its position.</summary>
    public int SheetId { get; internal set; }

    /// <summary>Gets the relationship id (<c>r:id</c>) of the sheet's part in the workbook's relationships.</summary>
    public string RelationshipId { get; internal set; }

    /// <summary>Gets the name of the sheet's part, for example <c>/xl/worksheets/sheet1.xml</c>; null when the relationship leads nowhere.</summary>
    public string? PartName { get; internal set; }

    /// <summary>Gets the kind of sheet.</summary>
    public SheetKind Kind { get; }

    /// <summary>Gets the 0-based position of the sheet in the workbook.</summary>
    public int Index => _workbook.Sheets.IndexOf(this);

    internal MarkupNode Node { get; set; }

    /// <summary>Gets or sets the state: visible, hidden or very hidden. A workbook must keep one visible sheet.</summary>
    public SheetState State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            if (value != SheetState.Visible && _workbook.VisibleSheetCount(this) == 0)
            {
                throw new InvalidOperationException("A workbook needs at least one visible sheet.");
            }

            _state = value;
            if (!Node.IsNull)
            {
                if (value == SheetState.Visible)
                {
                    Node.RemoveAttribute("state");
                }
                else
                {
                    Node.SetAttribute("state", value == SheetState.Hidden ? "hidden" : "veryHidden");
                }
            }
        }
    }

    /// <summary>Gets a value indicating whether the cells have been read from the part.</summary>
    public bool IsLoaded => _store is not null;

    /// <summary>Gets a value indicating whether the part will be written from the store at the next save.</summary>
    public bool IsModified => _forceRewrite || (_store?.IsModified ?? false) || (_model?.Changed ?? false);

    /// <summary>
    /// Gets the cells. The first use reads the sheet's part; nothing else is read for it. Not available for chart, dialog and macro sheets.
    /// </summary>
    /// <exception cref="NotSupportedException">The sheet is not a worksheet.</exception>
    /// <exception cref="OfficeFormatException">The part is not well-formed XML or is not a worksheet (<c>xlsx.wrong-root</c>).</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public CellStore Store
    {
        get
        {
            Load();
            return _store!;
        }
    }

    /// <summary>Reads the sheet's part into the store if that has not been done. Call it before sharing the sheet between threads.</summary>
    /// <exception cref="NotSupportedException">The sheet is not a worksheet.</exception>
    /// <exception cref="OfficeFormatException">The part is not well-formed XML or is not a worksheet.</exception>
    public void Load()
    {
        if (_store is not null)
        {
            return;
        }

        if (Kind != SheetKind.Worksheet)
        {
            throw new NotSupportedException("The sheet '" + Name + "' is not a worksheet; its part is kept as stored.");
        }

        var store = new CellStore();
        var frame = new SheetFrame();
        IPackagePart? part = PartName is null ? null : _workbook.FindPartForSheet(PartName);
        if (part is null)
        {
            _workbook.OpenContext.Defects.Report(new Defect(DefectKind.Packaging, "xlsx.sheet-part-missing", DefectSeverity.Dropped,
                "The sheet '" + Name + "' has no readable part; it reads as empty.", DefectLocation.InPart(PartName ?? "/xl/workbook.xml")));
            frame = NewEmptyFrame();
        }
        else
        {
            using Stream s = part.OpenRead();
            frame = SheetReader.Read(s, store, _workbook.OpenContext, part.Name, Name);
        }

        _frame = frame;
        _store = store;
    }

    internal static SheetFrame NewEmptyFrame() => new()
    {
        Head = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"),
        Tail = Encoding.UTF8.GetBytes("</worksheet>"),
        SheetDataTag = Encoding.UTF8.GetBytes("<sheetData>"),
        HadSheetData = true,
    };

    internal void InitialiseEmpty()
    {
        _store = new CellStore();
        _frame = NewEmptyFrame();
        _forceRewrite = true;
    }

    /// <summary>Marks the sheet to be written from the store at the next save even if nothing changed. The written part holds the same cells, row and column properties and merges, with unknown markup in place.</summary>
    public void Rewrite()
    {
        Load();
        _forceRewrite = true;
    }

    /// <summary>Gets the smallest range that holds every cell, or an invalid range when the sheet is empty.</summary>
    /// <returns>The used range.</returns>
    public CellRange UsedRange() => Store.UsedRange();

    /// <summary>Gets a cell by A1 reference, for example <c>sheet["B2"]</c>. The cell need not exist; reading an absent cell gives an empty value.</summary>
    /// <param name="address">The reference.</param>
    /// <returns>The cell handle.</returns>
    /// <exception cref="FormatException">The text is not a cell reference inside the sheet.</exception>
    public Cell this[string address] => this[CellAddress.Parse(address)];

    /// <summary>Gets a cell by address.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The cell handle.</returns>
    /// <exception cref="ArgumentException">The address is not valid.</exception>
    public Cell this[CellAddress address]
    {
        get
        {
            if (!address.IsValid)
            {
                throw new ArgumentException("The cell address is not valid.", nameof(address));
            }

            return new Cell(this, address.Row, address.Column);
        }
    }

    /// <summary>Gets a cell by 1-based row and column.</summary>
    /// <param name="row">The row, 1 to 1,048,576.</param>
    /// <param name="column">The column, 1 to 16,384.</param>
    /// <returns>The cell handle.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The row or column is outside the sheet.</exception>
    public Cell this[int row, int column]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(row, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(row, CellAddress.MaxRows);
            ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(column, CellAddress.MaxColumns);
            return new Cell(this, row, column);
        }
    }

    /// <summary>Gets the text of a string value of this sheet's workbook.</summary>
    /// <param name="value">A value read from this workbook.</param>
    /// <returns>The text of a string, the text of an error, the empty string for an empty value, and the invariant text of a number or boolean.</returns>
    public string GetText(CellValue value)
    {
        switch (value.Kind)
        {
            case CellValueKind.Text:
                {
                    int r = value.StringRef;
                    if (r < 0)
                    {
                        return Store.PoolAt(r).Text;
                    }

                    SharedStringTable sst = _workbook.SharedStrings;
                    if (r >= sst.Count)
                    {
                        _workbook.OpenContext.Defects.Report(new Defect(DefectKind.Reference, "xlsx.shared-string-missing", DefectSeverity.Repaired,
                            "A cell refers to shared string " + r.ToString(CultureInfo.InvariantCulture) + ", which the table does not have; it reads as empty text.",
                            DefectLocation.InPart(_workbook.SharedStringsPartName ?? "/xl/sharedStrings.xml")));
                        return string.Empty;
                    }

                    return sst.GetText(r);
                }

            case CellValueKind.Error:
                return value.ErrorRef >= 0 ? CellErrors.ToText(value.Error) : Store.PoolAt(value.ErrorRef).Text;
            default:
                return value.ToString();
        }
    }

    /// <summary>Gets the merged ranges (CT_MergeCells), in the order the part lists them.</summary>
    public IReadOnlyList<CellRange> MergedCells
    {
        get
        {
            EnsureModel();
            return _model!.Merges;
        }
    }

    /// <summary>Adds a merged range. The ranges are not checked against each other or the cells.</summary>
    /// <param name="range">The range.</param>
    public void Merge(CellRange range)
    {
        EnsureModel();
        _model!.Merges.Add(range);
        _model.Changed = true;
        _model.MergesChanged = true;
    }

    /// <summary>Removes a merged range exactly equal to <paramref name="range"/>.</summary>
    /// <param name="range">The range.</param>
    /// <returns>True when it was merged.</returns>
    public bool Unmerge(CellRange range)
    {
        EnsureModel();
        bool removed = _model!.Merges.Remove(range);
        if (removed)
        {
            _model.Changed = true;
            _model.MergesChanged = true;
        }

        return removed;
    }

    /// <summary>Gets the column properties (CT_Cols), in the order the part lists them.</summary>
    public IReadOnlyList<ColumnProperties> Columns
    {
        get
        {
            EnsureModel();
            return _model!.Columns;
        }
    }

    /// <summary>Replaces the column properties.</summary>
    /// <param name="columns">The runs of columns, in ascending order without overlap.</param>
    /// <exception cref="ArgumentException">A run is outside the sheet, reversed, or overlaps the one before.</exception>
    public void SetColumns(IEnumerable<ColumnProperties> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var list = new List<ColumnProperties>(columns);
        int prev = 0;
        foreach (ColumnProperties c in list)
        {
            if (c.Min < 1 || c.Max < c.Min || c.Max > CellAddress.MaxColumns || c.Min <= prev)
            {
                throw new ArgumentException("Column runs must be inside the sheet, ordered and not overlapping.", nameof(columns));
            }

            prev = c.Max;
        }

        EnsureModel();
        _model!.Columns = list;
        _model.Changed = true;
        _model.ColumnsChanged = true;
    }

    /// <summary>Gets the range the part declares in its <c>dimension</c> element, or an invalid range when it has none.</summary>
    public CellRange DeclaredDimension
    {
        get
        {
            EnsureModel();
            return _model!.Dimension;
        }
    }

    /// <summary>Reads the frozen panes of the first sheet view (CT_Pane with state <c>frozen</c>).</summary>
    /// <param name="rows">The number of frozen rows.</param>
    /// <param name="columns">The number of frozen columns.</param>
    /// <returns>True when the sheet has frozen panes.</returns>
    public bool TryGetFreezePane(out int rows, out int columns)
    {
        EnsureModel();
        rows = _model!.FrozenRows;
        columns = _model.FrozenColumns;
        return rows > 0 || columns > 0;
    }

    /// <summary>Gets the properties of a row. A row without properties gives the defaults.</summary>
    /// <param name="row">The 1-based row.</param>
    /// <returns>The row handle.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The row is outside the sheet.</exception>
    public RowProperties GetRow(int row)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(row, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(row, CellAddress.MaxRows);
        return new RowProperties(this, row);
    }

    internal bool OwnsStringHandle(CellValue v) => v.StringRef < 0 ? ~v.StringRef < Store.Pool.Count : _workbook.OwnsStringHandle(v);

    private void UpdateDimensionAndModel(SheetFrame frame, ref byte[] head, ref byte[] tail)
    {
        bool cellsEdited = _store!.IsModified;
        bool needTree = _model is not null || !frame.HadSheetData || (cellsEdited && frame.Head.AsSpan().IndexOf("dimension"u8) >= 0);
        if (!needTree)
        {
            return;
        }

        EnsureModel();
        _model!.Apply(_store, cellsEdited, out head, out tail);
    }

    private void EnsureModel()
    {
        if (_model is not null)
        {
            return;
        }

        Load();
        _model = FrameModel.Read(_frame!, _workbook.OpenContext, PartName ?? "/xl/worksheets/sheet.xml");
    }

    /// <summary>Writes the sheet's part if it needs writing. Called by the workbook when it is saved.</summary>
    internal void Commit()
    {
        if (!IsModified || PartName is null)
        {
            return;
        }

        IPackagePart? part = _workbook.FindPartForSheet(PartName);
        if (part is null)
        {
            return;
        }

        SheetFrame frame = _frame!;
        byte[] head = frame.Head;
        byte[] tail = frame.Tail;
        UpdateDimensionAndModel(frame, ref head, ref tail);

        using Stream output = part.OpenWrite();
        using var w = new StreamBufferWriter(output);
        XString.Raw(w, head);
        ReadOnlySpan<byte> prefixColon = frame.Prefix.Length == 0 ? default : [.. frame.Prefix, (byte)':'];
        if (frame.SheetDataSelfClosed)
        {
            // <sheetData/> becomes a start tag; the end tag is written below.
            ReadOnlySpan<byte> tag = frame.SheetDataTag;
            XString.Raw(w, tag[..tag.LastIndexOf((byte)'/')].TrimEnd((byte)' '));
            XString.Raw(w, ">"u8);
        }
        else if (frame.SheetDataTag.Length > 0)
        {
            XString.Raw(w, frame.SheetDataTag);
        }
        else
        {
            XString.Raw(w, "<"u8);
            XString.Raw(w, prefixColon);
            XString.Raw(w, "sheetData>"u8);
        }

        SheetWriter.WriteRows(w, _store!, _workbook.SharedStringsIfLoaded, prefixColon);
        XString.Raw(w, "</"u8);
        XString.Raw(w, prefixColon);
        XString.Raw(w, "sheetData>"u8);

        XString.Raw(w, tail);
        w.Flush();
    }
}
