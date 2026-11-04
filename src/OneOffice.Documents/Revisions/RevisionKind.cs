namespace RecursiveAI.OneOffice.Documents.Revisions;

/// <summary>What a tracked change does (ECMA-376 Part 1 §17.13.5).</summary>
public enum RevisionKind
{
    /// <summary>Inserted content: <c>w:ins</c> around runs (§17.13.5.18).</summary>
    Insertion = 0,

    /// <summary>Deleted content: <c>w:del</c> around runs, whose text is <c>w:delText</c> (§17.13.5.14).</summary>
    Deletion = 1,

    /// <summary>The source of a move: <c>w:moveFrom</c> around runs (§17.13.5.22).</summary>
    MoveFrom = 2,

    /// <summary>The destination of a move: <c>w:moveTo</c> around runs (§17.13.5.25).</summary>
    MoveTo = 3,

    /// <summary>A paragraph mark that was inserted: <c>w:ins</c> in the paragraph mark's run properties (§17.13.5.16).</summary>
    ParagraphMarkInserted = 4,

    /// <summary>A paragraph mark that was deleted: <c>w:del</c> in the paragraph mark's run properties (§17.13.5.15).</summary>
    ParagraphMarkDeleted = 5,

    /// <summary>A paragraph mark that was moved away: <c>w:moveFrom</c> in the paragraph mark's run properties.</summary>
    ParagraphMarkMovedFrom = 6,

    /// <summary>A paragraph mark that arrived by a move: <c>w:moveTo</c> in the paragraph mark's run properties.</summary>
    ParagraphMarkMovedTo = 7,

    /// <summary>A change of run properties: <c>w:rPrChange</c> (§17.13.5.31).</summary>
    RunPropertiesChange = 8,

    /// <summary>A change of paragraph properties: <c>w:pPrChange</c> (§17.13.5.29).</summary>
    ParagraphPropertiesChange = 9,

    /// <summary>A change of section properties: <c>w:sectPrChange</c> (§17.13.5.32).</summary>
    SectionPropertiesChange = 10,

    /// <summary>A change of table properties: <c>w:tblPrChange</c> (§17.13.5.34).</summary>
    TablePropertiesChange = 11,

    /// <summary>A change of table-level property exceptions on a row: <c>w:tblPrExChange</c> (§17.13.5.35).</summary>
    TablePropertyExceptionsChange = 12,

    /// <summary>A change of the table grid: <c>w:tblGridChange</c> (§17.13.5.33).</summary>
    TableGridChange = 13,

    /// <summary>A change of table row properties: <c>w:trPrChange</c> (§17.13.5.37).</summary>
    TableRowPropertiesChange = 14,

    /// <summary>A change of table cell properties: <c>w:tcPrChange</c> (§17.13.5.36).</summary>
    TableCellPropertiesChange = 15,

    /// <summary>A table row that was inserted: <c>w:ins</c> in the row properties (§17.13.5.17).</summary>
    TableRowInserted = 16,

    /// <summary>A table row that was deleted: <c>w:del</c> in the row properties (§17.13.5.13).</summary>
    TableRowDeleted = 17,

    /// <summary>A table cell that was inserted: <c>w:cellIns</c> (§17.13.5.4).</summary>
    CellInserted = 18,

    /// <summary>A table cell that was deleted: <c>w:cellDel</c> (§17.13.5.2).</summary>
    CellDeleted = 19,

    /// <summary>A change to how table cells are merged: <c>w:cellMerge</c> (§17.13.5.3).</summary>
    CellMerged = 20,

    /// <summary>A paragraph whose numbering was changed: <c>w:numberingChange</c> (§17.13.5.26).</summary>
    NumberingChange = 21,

    /// <summary>A paragraph whose numbering was added: <c>w:ins</c> in the numbering properties (§17.9.19).</summary>
    NumberingInserted = 22,
}

/// <summary>What a revision applies to.</summary>
public enum RevisionScope
{
    /// <summary>Inline content: runs and the elements around them.</summary>
    Content = 0,

    /// <summary>A paragraph mark (the break between two paragraphs).</summary>
    ParagraphMark = 1,

    /// <summary>The properties of a run, paragraph, section, table, row or cell.</summary>
    Formatting = 2,

    /// <summary>The structure of a table: a row or a cell.</summary>
    Table = 3,

    /// <summary>The numbering of a paragraph.</summary>
    Numbering = 4,
}

/// <summary>Which version of the text a view shows.</summary>
public enum RevisionView
{
    /// <summary>The document as it is after every tracked change is accepted.</summary>
    Accepted = 0,

    /// <summary>The document as it was before any tracked change was made (every change rejected).</summary>
    Rejected = 1,
}
