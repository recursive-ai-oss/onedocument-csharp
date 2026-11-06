using RecursiveAI.OneDocument.Inspection;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    /// <summary>
    /// Removes hidden data and personal information from the workbook in memory, the categories of <paramref name="options"/> (by
    /// default <see cref="SanitizeOptions.Default"/>), so <c>wb.Sanitize(); wb.SaveAs("clean.xlsx");</c> works. Pending edits are committed
    /// first. The same sanitiser as <see cref="Office.Sanitize(string, string, OpenContext?, CancellationToken)"/> runs, including its verification: the
    /// result is inspected again on this call and, if a category reported as removed still holds something,
    /// <see cref="SanitizeVerificationException"/> is thrown and the workbook is left as it was. Only the parts the sanitiser changes
    /// are rewritten; the others stay byte-identical.
    /// </summary>
    /// <param name="options">The categories to remove; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the operation; the workbook is left as it was.</param>
    /// <returns>What was removed, what was kept and why, and the inspection of the result.</returns>
    /// <remarks>
    /// The sheets, cells and other objects obtained from the workbook before the call no longer belong to it afterwards; read them again.
    /// <see cref="OfficeFile.Format"/> does not change, so when the report's <see cref="SanitizeReport.OutputFormat"/> differs (a macro
    /// project was removed) name the file by that format. Cached values are kept as they were; nothing is recalculated.
    /// </remarks>
    /// <exception cref="OfficeUnsupportedException">The workbook is a binary workbook (XLSB); sanitise it into a new file with <c>Office.Sanitize</c>.</exception>
    /// <exception cref="SanitizeVerificationException">The result still holds something in a category reported as removed; nothing was changed.</exception>
    /// <exception cref="ObjectDisposedException">The workbook is disposed.</exception>
    public SanitizeReport Sanitize(SanitizeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsBinary)
        {
            throw new OfficeUnsupportedException("Sanitising a binary workbook in memory (use Office.Sanitize to write a new file)");
        }

        CommitChanges();
        SanitizeReport report = PackageSync.SanitizeInPlace(Package, options, Context, cancellationToken);
        Reload();
        return report;
    }

    // Everything read from the package is dropped and the workbook part is read again, as when the workbook was opened.
    private void Reload()
    {
        _sheets.Clear();
        _sst = null;
        _sstPartName = null;
        _structureChanged = false;
        _styles = null;
        _theme = null;
        _names = null;
        _externalLinks = null;
        _pivotCaches = null;
        _calc = null;
        LoadMain();
    }
}
