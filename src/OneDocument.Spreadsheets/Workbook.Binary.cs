using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Xlsb;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    private XlsbBook? _binary;
    private bool _ownsBinary;

    /// <summary>Gets a value indicating whether the workbook was opened from a binary workbook (XLSB); <see cref="OfficeFile.Format"/> is then <see cref="OfficeFormat.Xlsb"/>.</summary>
    public bool IsBinary => _binary is not null;

    /// <summary>
    /// Gets the names of the parts of the binary workbook that this hub has no counterpart for (pivot caches and tables, connections,
    /// external links, the calculation chain, …). They are written back unchanged by a binary save and left out of every other format.
    /// Empty for a workbook that was not opened from a binary workbook.
    /// </summary>
    public IReadOnlyList<string> BinaryOnlyParts => _binary?.HiddenParts ?? [];

    internal static Workbook FromBinary(OpcPackage package, OpenContext context, bool ownsPackage)
    {
        XlsbBook book = XlsbBook.Open(package, context);
        Workbook wb;
        try
        {
            wb = new Workbook(book.Hub, OfficeFormat.Xlsb, context, book.HubMainName, ownsPackage: true) { _binary = book, _ownsBinary = ownsPackage };
            wb.LoadMain();
        }
        catch
        {
            if (ownsPackage)
            {
                book.Dispose();
            }

            throw;
        }

        return wb;
    }

    /// <summary>
    /// Writes a copy in an explicit format and reports in <paramref name="loss"/> everything the format cannot hold (for a binary workbook:
    /// pivot tables, connections, external links and the calculation chain, which have no binary form written yet).
    /// </summary>
    /// <param name="destination">The stream; it is left open.</param>
    /// <param name="format">The format to write.</param>
    /// <param name="loss">Receives one item per kind of feature that was dropped or approximated.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="FormatNotAvailableException">The format cannot be written from a workbook.</exception>
    /// <exception cref="OfficeUnsupportedException">The workbook holds something the format cannot represent at all (a macro project saved as a macro-free format, a chart sheet in a binary workbook).</exception>
    public void SaveAs(Stream destination, OfficeFormat format, ILossSink loss, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(loss);
        ThrowIfDisposed();
        CommitChanges();
        Write(format, destination, cancellationToken, loss);
    }

    private bool WriteBinary(OfficeFormat format, Stream destination, ILossSink? loss, CancellationToken cancellationToken)
    {
        loss ??= new LossReport();
        if (_binary is not null)
        {
            if (format == OfficeFormat.Xlsb)
            {
                _binary.SyncToSource(loss, cancellationToken);
                _binary.Source.WriteZipTo(destination, cancellationToken);
            }
            else
            {
                XlsbExport.ToOoxml(this, _binary, format, destination, loss, cancellationToken);
            }

            return true;
        }

        if (format == OfficeFormat.Xlsb)
        {
            XlsbExport.ToBinary(this, destination, loss, cancellationToken);
            return true;
        }

        return false;
    }

    internal static string MainContentType(OfficeFormat format) => MainContentTypeOf(format);

    internal static string VbaContentType => VbaProjectContentType;
}
