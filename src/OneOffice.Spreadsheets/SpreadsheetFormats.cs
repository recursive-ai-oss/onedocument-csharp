using RecursiveAI.OneOffice.Formats;

namespace RecursiveAI.OneOffice.Spreadsheets;

/// <summary>
/// Registers what this package can do with the OOXML workbook formats, so that <see cref="Office.Open(string, OpenContext?, CancellationToken)"/>
/// and <c>SaveAs</c> reach it. <see cref="Workbook.Open(string, OpenContext?, CancellationToken)"/> needs no registration.
/// </summary>
public static class SpreadsheetFormats
{
    /// <summary>The formats this package reads and writes: XLSX, XLSM, XLTX, XLTM, XLAM and Flat OPC.</summary>
    public static IReadOnlyList<OfficeFormat> Formats { get; } =
        [OfficeFormat.Xlsx, OfficeFormat.Xlsm, OfficeFormat.Xltx, OfficeFormat.Xltm, OfficeFormat.Xlam, OfficeFormat.FlatOpcWorkbook];

    /// <summary>Registers the readers and writers of <see cref="Formats"/>.</summary>
    /// <param name="registry">The registry to fill.</param>
    public static void Register(FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        OfficeFormat[] formats = [.. Formats];
        registry.RegisterReader(
            static (source, _, context, filePath, cancellationToken) => Workbook.OpenSource(source, context, filePath, cancellationToken),
            formats);
        registry.RegisterWriter(
            static (file, format, destination, cancellationToken) =>
            {
                if (file is not Workbook workbook)
                {
                    throw new FormatNotAvailableException(format, "write a " + file.Kind + " as");
                }

                workbook.Write(format, destination, cancellationToken);
            },
            formats);
    }
}
