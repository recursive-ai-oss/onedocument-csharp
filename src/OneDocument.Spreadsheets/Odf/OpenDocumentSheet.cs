using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Odf;

/// <summary>
/// Reads OpenDocument spreadsheets (ODS, OTS and flat FODS; ODF 1.2 and 1.3) into the workbook hub. The file is converted to an in-memory XLSX
/// package, which <see cref="Workbook"/> wraps like any other, and everything that did not map is reported to a <see cref="LossReport"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mapped: tables as sheets (hidden, tab colour, right-to-left, grid lines, frozen panes), columns and rows (widths, heights, hidden and filtered, repeats within the
/// limits), cells of every value type with their cached values (formulas are read as OpenFormula and kept next to them), merged cells, array formulas, cell styles with
/// their data styles as number formats, fonts, fills, borders, alignment and protection, named ranges and expressions, print ranges and titles, automatic filters,
/// data validation, conditional formats (conditions, colour scales, data bars, icon sets), notes, hyperlinks, page layout, headers and footers, and the document properties.
/// </para>
/// <para>
/// Reported as losses: pictures, charts and other shapes, pivot tables, sparklines, tracked changes, scenarios, the hash of a protection password, filter conditions,
/// formatting inside a cell's text, parts of number formats that have no counterpart, and formulas that cannot be read (they keep their cached value).
/// Nothing is recalculated and nothing is fetched. Repairs are defects with the codes <c>ods.*</c> and <c>odf.*</c> (see the defect catalogue).
/// </para>
/// </remarks>
public static class OpenDocumentSheet
{
    /// <summary>Converts an OpenDocument spreadsheet to an in-memory XLSX package.</summary>
    /// <param name="source">The file's bytes; read at once and left open.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="loss">Receives what could not be carried over; null to discard it.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>A package that <see cref="Workbook.FromPackage(IOpcPackage, OpenContext?, bool)"/> accepts. The caller disposes it.</returns>
    /// <exception cref="OfficeFormatException">The file is not an OpenDocument package or flat document, or not a spreadsheet (<c>ods.not-spreadsheet</c>).</exception>
    /// <exception cref="OfficeUnsupportedException">The package is encrypted.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static OpcPackage ToPackage(IByteSource source, OpenContext? context = null, ILossSink? loss = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        context ??= new OpenContext();
        byte[] bytes = Workbook.ReadAll(source, context, cancellationToken);
        using var odf = RecursiveAI.OneDocument.Odf.OdfPackage.Open(ByteSource.FromMemory(bytes), context);
        return new OdsImporter(odf, context, loss ?? new LossReport(), cancellationToken).Convert();
    }
}
