using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Ole;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>
/// Reads Excel 2.0 to 2003 workbooks (BIFF2 to BIFF8, [MS-XLS]) into a <see cref="Workbook"/>. The record layer
/// (<see cref="BiffRecordSet"/>) keeps every record; the conversion builds the SpreadsheetML hub from it and reports what the hub
/// cannot carry. Opening never recalculates: cached formula values stay as stored.
/// </summary>
public static class XlsConverter
{
    private static readonly byte[] CfbSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>Converts a BIFF workbook, in a compound file or as a bare stream, to a workbook.</summary>
    /// <param name="source">The file. The converter reads it whole (bounded by <see cref="OfficeLimits.MaxPartSize"/>) and does not keep it.</param>
    /// <param name="context">Limits and the defect sink; null for the defaults.</param>
    /// <param name="loss">Receives what the workbook cannot hold (charts, VBA, pivot tables, rich-text runs, and so on); null to ignore.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>A new workbook with no file path, so it is saved with <c>SaveAs</c>. <see cref="Workbook.ImportLoss"/> holds the loss report.</returns>
    /// <exception cref="OfficeEncryptedException">The workbook is encrypted (a FILEPASS record).</exception>
    /// <exception cref="OfficeFormatException">The stream is not a BIFF workbook or is damaged beyond repair.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static Workbook ToWorkbook(IByteSource source, OpenContext? context = null, ILossSink? loss = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        context ??= new OpenContext();
        var report = new LossReport();
        ILossSink sink = loss is null ? report : new TeeSink(report, loss);
        try
        {
            byte[] bytes = ReadAll(source, context.Limits);
            bool hasVba = false;
            byte[] summary = [];
            byte[] documentSummary = [];
            OleStorageSet objects = OleStorageSet.Empty;
            ReadOnlyMemory<byte> stream;
            string path = "/";
            if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(CfbSignature))
            {
                using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(bytes), context);
                IPackagePart? part = cfb.FindPart("/Workbook") ?? cfb.FindPart("/Book");
                if (part is null)
                {
                    throw new OfficeFormatException(DefectKind.Structure, "biff.no-workbook-stream", "The compound file has no Workbook or Book stream.");
                }

                path = part.Name;
                stream = part.GetBytes();
                // The property streams and the embedded-object storages ([MS-XLS] 2.1.2) are copied now: the compound file is released below.
                summary = cfb.FindPart("/" + OlePropertySets.SummaryInformationStreamName)?.GetBytes().ToArray() ?? [];
                documentSummary = cfb.FindPart("/" + OlePropertySets.DocumentSummaryInformationStreamName)?.GetBytes().ToArray() ?? [];
                objects = OleStorageSet.CaptureByPrefix(cfb, "MBD", context);
                foreach (IPackagePart p in cfb.Parts)
                {
                    if (p.Name.StartsWith("/_VBA_PROJECT_CUR/", StringComparison.OrdinalIgnoreCase) || p.Name.StartsWith("/_VBA_PROJECT", StringComparison.OrdinalIgnoreCase)
                        || p.Name.StartsWith("/MACROS/", StringComparison.OrdinalIgnoreCase) || p.Name.StartsWith("/_VBA_PROJECT_CUR", StringComparison.OrdinalIgnoreCase))
                    {
                        hasVba = true;
                        break;
                    }
                }
            }
            else
            {
                stream = bytes;
            }

            var importer = new XlsImporter(context, sink, cancellationToken);
            Workbook wb = importer.Import(stream, path, hasVba, summary, documentSummary, objects);
            wb.ImportLoss = report;
            return wb;
        }
        catch (Exception ex) when (ex is not (OfficeException or OperationCanceledException or IOException or OutOfMemoryException))
        {
            // The contract: damaged input surfaces as a typed exception, never as an index or cast error from deep inside a reader.
            throw new OfficeFormatException(DefectKind.Record, "biff.malformed", "The workbook is damaged: " + ex.GetType().Name + ".", default, ex);
        }
    }

    internal static Workbook FromSource(IByteSource source, OpenContext context, CancellationToken cancellationToken)
    {
        Workbook wb = ToWorkbook(source, context, null, cancellationToken);
        source.Dispose();
        return wb;
    }

    private static byte[] ReadAll(IByteSource source, OfficeLimits limits)
    {
        long length = source.Length;
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), limits.MaxPartSize, length);
        var bytes = new byte[length];
        long done = 0;
        while (done < length)
        {
            int n = source.Read(done, bytes.AsSpan((int)done, (int)Math.Min(length - done, 1 << 20)));
            if (n <= 0)
            {
                break;
            }

            done += n;
        }

        return done == length ? bytes : bytes.AsSpan(0, (int)done).ToArray();
    }

    private sealed class TeeSink(ILossSink a, ILossSink b) : ILossSink
    {
        public void Report(string code, string feature, long count, string message)
        {
            a.Report(code, feature, count, message);
            b.Report(code, feature, count, message);
        }
    }
}
