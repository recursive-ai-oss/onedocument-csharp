using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Rtf;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Rtf;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    private byte[]? _rtfSource;
    private bool _rtfPristine;
    private IReadOnlyList<byte[]> _rtfPreserved = [];

    /// <summary>
    /// Gets what reading the file as RTF could not carry into the document, or null when the document was not read from RTF. The
    /// codes are <c>loss.rtf-*</c> (see <c>docs/rtf.md</c>). Reading and writing RTF share one conversion path through the DOCX model, so a
    /// document opened from RTF is a WordprocessingML package in memory.
    /// </summary>
    public LossReport? ImportLoss { get; private set; }

    /// <summary>
    /// Gets what the last write to RTF could not carry (drawing shapes, text boxes, charts, equations, content controls and the like), or
    /// null when nothing has been written as RTF yet.
    /// </summary>
    public LossReport? ExportLoss { get; private set; }

    // RTF opened and saved without edits writes the bytes it was read from; any edit that reaches a part ends that.
    private void NoteEditsForRtf()
    {
        if (_rtfPristine && HasUncommittedChanges)
        {
            _rtfPristine = false;
        }
    }

    // The reader behind Document.Open for RTF and the registry entry for the format.
    internal static Document OpenRtf(IByteSource source, OpenContext context, string? filePath, CancellationToken cancellationToken)
    {
        long length = source.Length;
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), context.Limits.MaxPartSize, length, DefectLocation.InRecord(filePath ?? "rtf", 0));
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

        string stream = filePath ?? "rtf";
        try
        {
            var options = new RtfReaderOptions { MaxGroupDepth = Math.Clamp(context.Limits.MaxRecursionDepth, 16, 1000) };
            RtfTree tree = RtfTree.Parse(bytes, options, (int)Math.Min(context.Limits.MaxRecordCount, int.MaxValue));
            RtfDefects.ReportTo(tree.Defects, context.Defects, stream);
            if (System.Linq.Enumerable.Any(tree.Defects, d => d.Kind == RtfDefectKind.NotRtf))
            {
                throw new OfficeFormatException(DefectKind.Structure, "rtf.not-rtf", "The content does not start with {\\rtf.", DefectLocation.InRecord(stream, 0));
            }

            var importer = new RtfImporter(tree, context, cancellationToken);
            importer.Run();
            OpcPackage package = importer.BuildPackage();
            try
            {
                var doc = new Document(package, context, "/word/document.xml", ownsPackage: true, OfficeFormat.Rtf, filePath)
                {
                    _rtfSource = bytes,
                    _rtfPristine = true,
                    _rtfPreserved = importer.Preserved,
                    ImportLoss = importer.Loss,
                };
                return doc;
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
            throw new OfficeFormatException(DefectKind.Record, "rtf.damaged", "The RTF could not be read: " + ex.GetType().Name + ".", DefectLocation.InRecord(stream, 0), ex);
        }
        finally
        {
            source.Dispose();
        }
    }

    // The writer behind SaveAs(.., OfficeFormat.Rtf).
    internal void WriteRtf(Stream destination, CancellationToken cancellationToken)
    {
        CommitChanges();
        var loss = new LossReport();
        if (_rtfPristine && _rtfSource is not null)
        {
            // RTF read and saved without an edit: the file it was read from, byte for byte.
            destination.Write(_rtfSource);
            ExportLoss = loss;
            return;
        }

        RtfExporter.Write(this, destination, loss, _rtfPreserved, cancellationToken);
        ExportLoss = loss;
    }
}
