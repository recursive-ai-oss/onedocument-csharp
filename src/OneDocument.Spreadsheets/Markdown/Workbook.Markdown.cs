using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Spreadsheets.Markdown;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    /// <summary>
    /// Writes the workbook as GitHub Flavored Markdown: a second-level heading with the name of each worksheet and a pipe table of its used range, cells shown the way
    /// their number format displays them. The first non-empty row is the table header; rows with no cell are left out. <see cref="MarkdownExportLoss"/> lists what a table cannot hold.
    /// </summary>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The Markdown.</returns>
    public string ToMarkdown(CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        WriteMarkdown(buffer, cancellationToken);
        return new UTF8Encoding(false).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Writes the workbook as Markdown in UTF-8 without a byte-order mark. See <see cref="ToMarkdown"/>.</summary>
    /// <param name="destination">The stream; it is left open.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    public void WriteMarkdown(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ThrowIfDisposed();
        CommitChanges();
        var loss = new LossReport();
        MarkdownWorkbook.Write(this, destination, loss, cancellationToken);
        MarkdownExportLoss = loss;
    }

    /// <summary>Gets what the last Markdown export could not carry (formulas as values, styles, merged cells, charts and the like), one entry per kind with a count; null until an export has run.</summary>
    public LossReport? MarkdownExportLoss { get; private set; }
}
