using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;

namespace RecursiveAI.OneDocument.Spreadsheets.Streaming;

/// <summary>
/// Text extraction for the binary workbooks (<c>.xls</c>, BIFF2 to BIFF5 and <c>.xlsb</c>) through the forward cursors, so the workbook
/// model is never built. The text is the model path's: every text cell of every worksheet, one per line, in sheet and row order.
/// </summary>
internal static class BinaryTextExtractor
{
    public static void Register(FormatRegistry registry) =>
        registry.RegisterTextExtractor(Extract, OfficeFormat.Xls, OfficeFormat.XlsLegacy, OfficeFormat.Xlsb);

    private static void Extract(IByteSource source, OfficeFormat format, OpenContext context, TextWriter output, CancellationToken cancellationToken)
    {
        // The caller keeps the source: the readers get a stream over it and leave it open.
        using Stream stream = ByteSource.AsStream(source);
        if (format == OfficeFormat.Xlsb)
        {
            using XlsbStreamReader book = XlsbStreamReader.Open(stream, context, leaveOpen: true, cancellationToken);
            for (int i = 0; i < book.Sheets.Count; i++)
            {
                if (book.Sheets[i].Kind == SheetKind.Worksheet)
                {
                    using XlsbSheetCursor cursor = book.OpenSheet(i);
                    Write(cursor, output);
                }
            }

            return;
        }

        using XlsStreamReader xls = XlsStreamReader.Open(stream, context, leaveOpen: true, cancellationToken);
        for (int i = 0; i < xls.Sheets.Count; i++)
        {
            if (xls.Sheets[i].Kind == SheetKind.Worksheet)
            {
                using XlsSheetCursor cursor = xls.OpenSheet(i);
                Write(cursor, output);
            }
        }
    }

    private static void Write(ICellCursor cursor, TextWriter output)
    {
        while (cursor.ReadRow())
        {
            while (cursor.ReadCell())
            {
                if (cursor.Kind == CellValueKind.Text)
                {
                    output.WriteLine(cursor.GetText());
                }
            }
        }
    }
}
