using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Presentations;

public sealed partial class Presentation
{
    private byte[]? _odfSource;
    private bool _odfPristine;

    /// <summary>
    /// Gets what the conversion from an OpenDocument presentation (ODP, OTP or FODP) or a PowerPoint 97-2003 file could not carry over, or <c>null</c> for a
    /// presentation that was not converted. Entries have <c>loss.odp-*</c> codes for OpenDocument (see <c>docs/odp.md</c>) and <c>ppt.*</c> codes for binary files
    /// (see <c>docs/ppt.md</c>); the located detail of each repair is in <see cref="OfficeFile.Defects"/>. Such a presentation is a PresentationML package in memory once it is open.
    /// </summary>
    public LossReport? ImportLoss { get; internal set; }

    // An OpenDocument file read and saved without edits writes the bytes it was read from; any edit that reaches a part ends that.
    private void NoteEditsForOdf()
    {
        if (_odfPristine && HasUncommittedChanges)
        {
            _odfPristine = false;
        }
    }

    internal static bool IsOdfPresentationFormat(OfficeFormat format) => format is OfficeFormat.Odp or OfficeFormat.Otp or OfficeFormat.Fodp;

    // The reader behind Presentation.Open for OpenDocument presentations (zip, template or flat).
    internal static Presentation OpenOdf(IByteSource source, OpenContext context, string? filePath, OfficeFormat format, CancellationToken cancellationToken)
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
            OpcPackage package = new Odf.OdpImporter(odf, context, loss, template: format == OfficeFormat.Otp, cancellationToken).Convert();
            try
            {
                string main = FindMainPart(package)!;
                return new Presentation(package, context, main, ownsPackage: true, format, filePath)
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

    // The writer behind SaveAs(.., OfficeFormat.Odp | Otp | Fodp).
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

        Odf.OdpExporter.Write(this, format, destination, loss, cancellationToken);
        ExportLoss = loss;
    }
}
