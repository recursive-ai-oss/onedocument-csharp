namespace RecursiveAI.OneDocument.Spreadsheets.Streaming;

/// <summary>
/// What the row binding and the text extractor need from a forward cursor, so that one loop serves every workbook format. The three
/// public cursors (<see cref="SheetCursor"/>, <see cref="XlsbSheetCursor"/>, <see cref="XlsSheetCursor"/>) have these members with the same meaning.
/// </summary>
internal interface ICellCursor : IDisposable
{
    string SheetName { get; }

    int RowNumber { get; }

    int Column { get; }

    CellValueKind Kind { get; }

    double Number { get; }

    bool Boolean { get; }

    CellError Error { get; }

    bool IsDate { get; }

    bool ReadRow();

    bool ReadCell();

    bool TryGetDateTime(out DateTime value);

    bool TryCopyText(Span<char> destination, out int written);

    string GetText();
}
