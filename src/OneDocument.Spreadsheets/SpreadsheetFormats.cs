using RecursiveAI.OneDocument.Formats;

namespace RecursiveAI.OneDocument.Spreadsheets;

/// <summary>
/// Registers what this package can do with the OOXML workbook formats, so that <see cref="Office.Open(string, OpenContext?, CancellationToken)"/>
/// and <c>SaveAs</c> reach it. <see cref="Workbook.Open(string, OpenContext?, CancellationToken)"/> needs no registration.
/// </summary>
public static class SpreadsheetFormats
{
    /// <summary>The formats this package reads and writes: XLSX, XLSM, XLTX, XLTM, XLAM, XLSB and Flat OPC.</summary>
    public static IReadOnlyList<OfficeFormat> Formats { get; } =
        [OfficeFormat.Xlsx, OfficeFormat.Xlsm, OfficeFormat.Xltx, OfficeFormat.Xltm, OfficeFormat.Xlam, OfficeFormat.Xlsb, OfficeFormat.FlatOpcWorkbook];

    /// <summary>The formats this package reads but does not write: Excel 2003 XML, which opens as a workbook that saves as XLSX.</summary>
    public static IReadOnlyList<OfficeFormat> ReadOnlyFormats { get; } = [OfficeFormat.Excel2003Xml];

    /// <summary>Registers the readers and writers of <see cref="Formats"/> and the readers of <see cref="ReadOnlyFormats"/>.</summary>
    /// <param name="registry">The registry to fill.</param>
    public static void Register(FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        OfficeFormat[] formats = [.. Formats];
        OfficeFormat[] delimited = [OfficeFormat.Csv, OfficeFormat.Tsv];
        registry.RegisterReader(
            static (source, _, context, filePath, cancellationToken) => Workbook.OpenSource(source, context, filePath, cancellationToken),
            formats);
        registry.RegisterReader(
            static (source, _, context, filePath, cancellationToken) => Workbook.OpenSource(source, context, filePath, cancellationToken),
            delimited);

        // Excel 2003 XML is read only: it opens as a workbook that saves as XLSX.
        registry.RegisterReader(
            static (source, _, context, filePath, cancellationToken) => Workbook.OpenSource(source, context, filePath, cancellationToken),
            [.. ReadOnlyFormats]);

        registry.RegisterReader(
            static (source, _, context, filePath, cancellationToken) => Workbook.OpenSource(source, context, filePath, cancellationToken),
            OfficeFormat.Xls, OfficeFormat.XlsLegacy);
        registry.RegisterWriter(
            static (file, format, destination, cancellationToken) =>
            {
                if (file is not Workbook workbook)
                {
                    throw new FormatNotAvailableException(format, "write a " + file.Kind + " as");
                }

                Csv.CsvDocument.Write(workbook, destination, new Csv.CsvWriteOptions { Delimiter = format == OfficeFormat.Tsv ? '\t' : ',' }, null, cancellationToken);
            },
            delimited);
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
        registry.RegisterWriter(
            static (file, format, destination, cancellationToken) =>
            {
                if (file is not Workbook workbook)
                {
                    throw new FormatNotAvailableException(format, "write a " + file.Kind + " as");
                }

                workbook.WriteXls(destination, cancellationToken);
            },
            OfficeFormat.Xls);

        // Markdown is written by every kind; this entry takes its own kind and passes the rest on to whichever package registered before.
        registry.TryGetWriter(OfficeFormat.Markdown, out WriteHandler? previousMarkdown);
        registry.RegisterWriter(
            (file, format, destination, cancellationToken) =>
            {
                if (file is Workbook workbook)
                {
                    workbook.WriteMarkdown(destination, cancellationToken);
                }
                else if (previousMarkdown is not null)
                {
                    previousMarkdown(file, format, destination, cancellationToken);
                }
                else
                {
                    throw new FormatNotAvailableException(format, "write a " + file.Kind + " as");
                }
            },
            OfficeFormat.Markdown);
        registry.RegisterTemplateBinder(Templating.XlsxTemplateBinderAdapter.Instance, formats);
    }
}
