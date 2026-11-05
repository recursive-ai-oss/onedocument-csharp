using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Worksheet
{
    private TableCollection? _tables;
    private ConditionalFormatCollection? _conditionalFormats;
    private DataValidationCollection? _validations;

    /// <summary>Gets the tables on the sheet (ECMA-376 Part 1 §18.5). The table parts are read when the collection is first enumerated.</summary>
    /// <exception cref="NotSupportedException">The sheet is not a worksheet.</exception>
    public TableCollection Tables => _tables ??= new TableCollection(this);

    /// <summary>Gets the conditional formatting rules of the sheet (§18.3.1.18), including the extended rules in the part's extension list.</summary>
    /// <exception cref="NotSupportedException">The sheet is not a worksheet.</exception>
    public ConditionalFormatCollection ConditionalFormats => _conditionalFormats ??= new ConditionalFormatCollection(this);

    /// <summary>Gets the data validations of the sheet (§18.3.1.32), including the extended ones in the part's extension list.</summary>
    /// <exception cref="NotSupportedException">The sheet is not a worksheet.</exception>
    public DataValidationCollection Validations => _validations ??= new DataValidationCollection(this);

    internal bool TablesModified => _tables?.IsModified ?? false;

    internal void CommitTables() => _tables?.Commit();

    /// <summary>The root of the small tree that holds everything in the sheet part except the rows. Editing it makes the sheet be written at the next save.</summary>
    internal MarkupNode FrameRoot
    {
        get
        {
            EnsureModel();
            return _model!.Root;
        }
    }

    /// <summary>
    /// Gets the worksheet element with everything in the part except the rows' cells (<c>sheetPr</c>, <c>sheetViews</c>, <c>sheetFormatPr</c>,
    /// <c>cols</c>, <c>pageSetup</c>, <c>headerFooter</c>, breaks, <c>drawing</c>, and so on), for readers that need settings the model has no
    /// property for, such as print rendering. The <c>sheetData</c> element is an empty marker. Reading it does not mark the sheet changed;
    /// do not edit it.
    /// </summary>
    /// <exception cref="NotSupportedException">The sheet is not a worksheet.</exception>
    /// <exception cref="OfficeFormatException">The part is not well-formed XML or is not a worksheet.</exception>
    public MarkupNode LayoutMarkup => FrameRoot;

    /// <summary>
    /// Tells whether a text value is an entry of the shared string table, and which. Text set in this session and not yet saved is not.
    /// </summary>
    /// <param name="value">A value read from this sheet.</param>
    /// <param name="index">The 0-based index into <see cref="Workbook.SharedStrings"/>.</param>
    /// <returns>True for a stored shared string.</returns>
    public bool TryGetSharedStringIndex(CellValue value, out int index)
    {
        index = value.StringRef;
        return value.Kind == CellValueKind.Text && index >= 0 && index < _workbook.SharedStrings.Count;
    }

    internal MarkupNode InsertFrameElement(string local, string xml)
    {
        EnsureModel();
        return _model!.InsertElement(local, xml);
    }

    internal void NoteFrameEdited()
    {
        EnsureModel();
        _model!.Changed = true;
    }
}
