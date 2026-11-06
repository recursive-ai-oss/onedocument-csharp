using System.Linq;
using RecursiveAI.OneDocument.Operations;

namespace RecursiveAI.OneDocument.Spreadsheets;

/// <summary>One workbook made by splitting a workbook by sheet.</summary>
public sealed class WorkbookPiece
{
    internal WorkbookPiece(Workbook workbook, string sheetName, MergeReport report)
    {
        Workbook = workbook;
        SheetName = sheetName;
        Report = report;
    }

    /// <summary>Gets the new workbook, which holds the one sheet. It is owned by the <see cref="WorkbookSplitResult"/>.</summary>
    public Workbook Workbook { get; }

    /// <summary>Gets the name the sheet had in the original.</summary>
    public string SheetName { get; }

    /// <summary>Gets what copying the sheet did (see <see cref="Worksheet.CopyTo"/>).</summary>
    public MergeReport Report { get; }
}

/// <summary>The workbooks a split produced. Disposing it disposes them.</summary>
public sealed class WorkbookSplitResult : IDisposable
{
    internal WorkbookSplitResult(IReadOnlyList<WorkbookPiece> pieces) => Pieces = pieces;

    /// <summary>Gets the pieces in sheet order.</summary>
    public IReadOnlyList<WorkbookPiece> Pieces { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (WorkbookPiece p in Pieces)
        {
            p.Workbook.Dispose();
        }
    }
}

public sealed partial class Workbook
{
    /// <summary>
    /// Splits the workbook by sheet: a new workbook for each worksheet (chart, dialog and macro sheets are skipped), made with
    /// <see cref="Worksheet.CopyTo"/>. The original is not changed.
    /// </summary>
    /// <param name="sourceLinkTarget">The file name that references to the sheets left out of a piece carry (see <see cref="SheetCopyOptions.SourceLinkTarget"/>); null uses this workbook's file name.</param>
    /// <returns>The pieces. Each piece's report lists what could not be copied (drawings, comments, pivot tables).</returns>
    public WorkbookSplitResult SplitBySheet(string? sourceLinkTarget = null)
    {
        var pieces = new List<WorkbookPiece>();
        try
        {
            foreach (Worksheet sheet in Sheets.Where(s => s.Kind == SheetKind.Worksheet).ToList())
            {
                Workbook piece = Create(Limits);
                Worksheet placeholder = piece.Sheets[0];
                SheetCopyResult r = sheet.CopyTo(piece, new SheetCopyOptions { SourceLinkTarget = sourceLinkTarget });
                piece.Sheets.Remove(placeholder);
                pieces.Add(new WorkbookPiece(piece, sheet.Name, r.Report));
            }
        }
        catch
        {
            foreach (WorkbookPiece p in pieces)
            {
                p.Workbook.Dispose();
            }

            throw;
        }

        return new WorkbookSplitResult(pieces);
    }
}
