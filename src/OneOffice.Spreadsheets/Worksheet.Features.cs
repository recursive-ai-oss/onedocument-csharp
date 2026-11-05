using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Spreadsheets;

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
