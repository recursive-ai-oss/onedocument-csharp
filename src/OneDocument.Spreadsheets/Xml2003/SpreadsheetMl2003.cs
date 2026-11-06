using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Xml2003;

/// <summary>
/// Reads Excel 2003 XML (SpreadsheetML 2003: a <c>Workbook</c> in the <c>urn:schemas-microsoft-com:office:spreadsheet</c> namespace)
/// into the workbook hub. The file is converted to an in-memory XLSX package, which <see cref="Workbook"/> then wraps like any other,
/// and everything that did not map is reported to a <see cref="LossReport"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mapped: worksheets and their tables (row and column indexes, spans, merged cells, widths and heights, hidden rows and columns), cell
/// values of every data type, rich text, formulas (R1C1 text through the public formula parser, printed back as A1), array formulas,
/// styles with inheritance, number formats, defined names (workbook and sheet level), comments, hyperlinks, conditional formatting, data
/// validation, auto filters, panes and print setup, and the document properties.
/// </para>
/// <para>
/// Cached formula results are kept as the file stores them; nothing is recalculated. Repairs are defects with the codes <c>sml2003.*</c>
/// (see the defect catalogue). Nothing is fetched: a hyperlink target becomes an external relationship and is never followed.
/// </para>
/// </remarks>
public static class SpreadsheetMl2003
{
    /// <summary>Converts an Excel 2003 XML file to an in-memory XLSX package.</summary>
    /// <param name="source">The file's bytes.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="loss">Receives what could not be carried over; null to discard it.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>A package that <see cref="Workbook.FromPackage(IOpcPackage, OpenContext?, bool)"/> accepts. The caller disposes it.</returns>
    /// <exception cref="OfficeFormatException">The file is not well-formed XML (<c>xml.*</c>) or its root is not a SpreadsheetML 2003 <c>Workbook</c> (<c>sml2003.not-spreadsheet</c>).</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static OpcPackage ToPackage(IByteSource source, OpenContext? context = null, ILossSink? loss = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        context ??= new OpenContext();
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), context.Limits.MaxPartSize, source.Length, DefectLocation.InPart("/", 0));
        ReadOnlyMemory<byte> bytes;
        if (source.TryGetMemory(out ReadOnlyMemory<byte> memory))
        {
            bytes = memory;
        }
        else
        {
            var buffer = new byte[(int)source.Length];
            int total = 0;
            while (total < buffer.Length)
            {
                int n = source.Read(total, buffer.AsSpan(total));
                if (n <= 0)
                {
                    break;
                }

                total += n;
            }

            bytes = buffer.AsMemory(0, total);
        }

        MarkupDocument markup = MarkupDocument.Parse(bytes, context);
        return new SpreadsheetMl2003Converter(markup, context, loss ?? new LossReport(), cancellationToken).Convert();
    }

    /// <summary>Opens Excel 2003 XML as a workbook. The source is read at once and left open.</summary>
    /// <param name="source">The file's bytes.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The workbook, which saves as XLSX; <see cref="Workbook.ImportLoss"/> lists what did not map.</returns>
    /// <exception cref="OfficeFormatException">The file is not well-formed XML or is not a SpreadsheetML 2003 workbook.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static Workbook Open(IByteSource source, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        context ??= new OpenContext();
        var loss = new LossReport();
        OpcPackage package = ToPackage(source, context, loss, cancellationToken);
        try
        {
            Workbook workbook = Workbook.FromPackage(package, context, ownsPackage: true);
            workbook.ImportLoss = loss;
            return workbook;
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }
}
