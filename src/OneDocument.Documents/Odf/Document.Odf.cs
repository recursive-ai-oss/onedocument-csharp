using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
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

    internal static bool IsOdfTextFormat(OfficeFormat format) => format is OfficeFormat.Odt or OfficeFormat.Ott or OfficeFormat.Fodt;

    // The reader behind Document.Open for OpenDocument text (zip, template or flat) and the registry entry for the formats.
    internal static Document OpenOdf(IByteSource source, OpenContext context, string? filePath, OfficeFormat format, CancellationToken cancellationToken)
    {
        string where = filePath ?? "odf";
        long length = source.Length;
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxTotalUncompressedSize), context.Limits.MaxTotalUncompressedSize, length, DefectLocation.InPart(where, 0));
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

        if (read < bytes.Length)
        {
            bytes = bytes[..read];
        }

        source.Dispose();
        var loss = new LossReport();
        try
        {
            using OdfPackage odf = OdfPackage.Open(ByteSource.FromMemory(bytes), context, filePath);
            OpcPackage package = new Odf.OdtImporter(odf, context, loss, template: format == OfficeFormat.Ott, cancellationToken).Convert();
            try
            {
                return new Document(package, context, "/word/document.xml", ownsPackage: true, format, filePath)
                {
                    _odfSource = bytes,
                    _odfPristine = true,
                    ImportLoss = loss,
                };
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

    // The writer behind SaveAs(.., OfficeFormat.Odt | Ott | Fodt).
    internal void WriteOdf(OfficeFormat format, Stream destination, CancellationToken cancellationToken)
    {
        CommitChanges();
        var loss = new LossReport();
        if (_odfPristine && _odfSource is not null && format == Format)
        {
            // OpenDocument read and saved without edits: the file it was read from, byte for byte.
            destination.Write(_odfSource);
            ExportLoss = loss;
            return;
        }

        Odf.OdtExporter.Write(this, format, destination, loss, cancellationToken);
        ExportLoss = loss;
    }
}
