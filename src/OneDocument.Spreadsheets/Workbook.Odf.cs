using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    private byte[]? _odfSource;
    private bool _odfPristine;

    // An OpenDocument file read and saved without edits writes the bytes it was read from; any edit that reaches a part ends that.
    private void NoteEditsForOdf()
    {
        if (_odfPristine && HasUncommittedChanges)
        {
            _odfPristine = false;
        }
    }

    internal static bool IsOdfSheetFormat(OfficeFormat format) => format is OfficeFormat.Ods or OfficeFormat.Ots or OfficeFormat.Fods;

    internal static byte[] ReadAll(IByteSource source, OpenContext context, CancellationToken cancellationToken)
    {
        long length = source.Length;
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxTotalUncompressedSize), context.Limits.MaxTotalUncompressedSize, length, DefectLocation.InPart("odf", 0));
        byte[] bytes = new byte[length];
        int read = 0;
        while (read < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int n = source.Read(read, bytes.AsSpan(read));
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        return read < bytes.Length ? bytes[..read] : bytes;
    }

    // The reader behind Workbook.Open for OpenDocument spreadsheets (zip, template or flat).
    internal static Workbook OpenOds(IByteSource source, OpenContext context, string? filePath, OfficeFormat format, CancellationToken cancellationToken)
    {
        string where = filePath ?? "odf";
        byte[] bytes = ReadAll(source, context, cancellationToken);
        source.Dispose();
        var loss = new LossReport();
        try
        {
            using RecursiveAI.OneDocument.Odf.OdfPackage odf = RecursiveAI.OneDocument.Odf.OdfPackage.Open(ByteSource.FromMemory(bytes), context, filePath);
            OpcPackage package = new Odf.OdsImporter(odf, context, loss, cancellationToken).Convert();
            try
            {
                string main = FindMainPart(package)!;
                var wb = new Workbook(package, format, context, main, ownsPackage: true);
                wb.LoadMain();
                wb._odfSource = bytes;
                wb._odfPristine = true;
                wb.ImportLoss = loss;
                return wb;
            }
            catch
            {
                package.Dispose();
                throw;
            }
        }
        catch (Exception ex) when (ex is not (OfficeException or OperationCanceledException))
        {
            // Reading must not fail with anything but a typed error, whatever the bytes held.
            throw new OfficeFormatException(DefectKind.Structure, "odf.damaged", "The OpenDocument file could not be read: " + ex.GetType().Name + ".", DefectLocation.InPart(where, 0), ex);
        }
    }

    // The writer behind SaveAs(.., OfficeFormat.Ods | Ots | Fods).
    internal void WriteOds(OfficeFormat format, Stream destination, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        NoteEditsForOdf();
        CommitChanges();
        var loss = new LossReport();
        if (_odfPristine && _odfSource is not null && format == Format)
        {
            // OpenDocument read and saved without edits: the file it was read from, byte for byte.
            destination.Write(_odfSource);
            ExportLoss = loss;
            return;
        }

        Odf.OdsExporter.Write(this, format, destination, loss, cancellationToken);
        ExportLoss = loss;
    }
}
