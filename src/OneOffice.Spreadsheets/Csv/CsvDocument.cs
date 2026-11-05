using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.IO;
using RecursiveAI.OneOffice.NumberFormats;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Spreadsheets.Csv;

/// <summary>How a CSV or TSV becomes a worksheet.</summary>
public sealed class CsvImportOptions
{
    /// <summary>Gets how the text is split and decoded. Null means the defaults (delimiter detected).</summary>
    public CsvReadOptions? Read { get; init; }

    /// <summary>Gets how fields are read as values. Null means the defaults (numbers, dates, booleans and errors recognised).</summary>
    public CsvValueOptions? Values { get; init; }

    /// <summary>Gets a value indicating whether the first record is a header: it stays text whatever it looks like.</summary>
    public bool HasHeader { get; init; }

    /// <summary>Gets the name of the worksheet. The default is <c>Sheet1</c>.</summary>
    public string SheetName { get; init; } = "Sheet1";
}

/// <summary>
/// Moves delimited text in and out of the workbook model. A CSV has one table of text and nothing else: reading gives a workbook with
/// one sheet, and writing a sheet reports in a <see cref="LossReport"/> everything a CSV cannot hold.
/// </summary>
/// <remarks>
/// <para>
/// Reading: every field becomes a cell. With <see cref="CsvValueOptions.Infer"/> on (the default) numbers, percentages, dates and times,
/// booleans and error values become typed cells and the rest stay text; dates, times, percentages, grouped numbers and currency amounts get a
/// number format so that they display as in Excel. Records past row 1,048,576 and fields past column 16,384 are dropped with a
/// <c>csv.grid-exceeded</c> defect.
/// </para>
/// <para>
/// Writing: the used range from A1, one record per row (empty rows are empty lines), the first sheet unless one is given. A formula
/// cell writes its cached value. With <see cref="CsvWriteOptions.Formatted"/> numbers are written as the cell's number format displays
/// them. Losses reported: <c>loss.formulas</c> (cells), <c>loss.cell-styles</c> (cells with a style other than the default),
/// <c>loss.number-formats</c> (raw mode only), <c>loss.sheets</c> (sheets not written), <c>loss.merged-cells</c> (ranges),
/// <c>loss.embedded-objects</c> (drawings, charts, tables, comments and pivot tables).
/// </para>
/// </remarks>
public static class CsvDocument
{
    /// <summary>Reads delimited text into a new workbook.</summary>
    /// <param name="stream">The text as bytes; read forward once.</param>
    /// <param name="options">Import options; null for the defaults.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The workbook, which the caller disposes. It has one worksheet and no file path.</returns>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="IOException">The stream cannot be read.</exception>
    public static Workbook ToWorkbook(Stream stream, CsvImportOptions? options = null, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        context ??= new OpenContext();
        using var reader = new CsvReader(stream, options?.Read, context, leaveOpen: true);
        return Fill(reader, options, context, cancellationToken);
    }

    /// <summary>Reads delimited text from a file into a new workbook.</summary>
    /// <param name="path">The file.</param>
    /// <param name="options">Import options; null for the defaults.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The workbook, which the caller disposes.</returns>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static Workbook ToWorkbook(string path, CsvImportOptions? options = null, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        return ToWorkbook(stream, options, context, cancellationToken);
    }

    internal static Workbook FromSource(IByteSource source, OfficeFormat format, OpenContext context, CancellationToken cancellationToken)
    {
        using Stream stream = ByteSource.AsStream(source);
        var read = new CsvReadOptions { Delimiter = format == OfficeFormat.Tsv ? '\t' : null };
        Workbook wb = ToWorkbook(stream, new CsvImportOptions { Read = read }, context, cancellationToken);
        source.Dispose();
        return wb;
    }

    private static Workbook Fill(CsvReader reader, CsvImportOptions? options, OpenContext context, CancellationToken cancellationToken)
    {
        var parser = new CsvValueParser(options?.Values);
        Workbook wb = Workbook.Create(context.Limits);
        Worksheet sheet = wb.Sheets[0];
        if (options?.SheetName is { } name && name != sheet.Name)
        {
            sheet.Rename(name);
        }

        CellStore store = sheet.Store;
        var styles = new Dictionary<string, int>(StringComparer.Ordinal);
        var codes = new List<string>();
        long cells = 0;
        long maxCells = context.Limits.MaxRecordCount;
        int row = 0;
        bool reportedRows = false;
        bool reportedColumns = false;
        while (reader.ReadRecord(cancellationToken))
        {
            row++;
            if (row > CellAddress.MaxRows)
            {
                if (!reportedRows)
                {
                    reportedRows = true;
                    context.Defects.Report(new Defect(DefectKind.Value, "csv.grid-exceeded", DefectSeverity.Dropped,
                        "The text has more than 1,048,576 records; the rest are dropped.", DefectLocation.AtText(reader.RecordOffset)));
                }

                break;
            }

            int count = reader.FieldCount;
            if (count > CellAddress.MaxColumns)
            {
                if (!reportedColumns)
                {
                    reportedColumns = true;
                    context.Defects.Report(new Defect(DefectKind.Value, "csv.grid-exceeded", DefectSeverity.Dropped,
                        "A record has more than 16,384 fields; the rest are dropped.", DefectLocation.AtText(reader.RecordOffset)));
                }

                count = CellAddress.MaxColumns;
            }

            for (int c = 0; c < count; c++)
            {
                ReadOnlySpan<char> field = reader.GetField(c);
                if (field.Length == 0)
                {
                    continue;
                }

                OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), maxCells, ++cells, DefectLocation.AtText(reader.RecordOffset));
                CsvValue v = options is { HasHeader: true } && row == 1 ? new CsvValue(CsvValueKind.Text) : parser.Parse(field);
                switch (v.Kind)
                {
                    case CsvValueKind.Text:
                        store.SetValue(row, c + 1, wb.MakeStringValue(field.ToString()));
                        break;
                    case CsvValueKind.Boolean:
                        store.SetValue(row, c + 1, CellValue.FromBoolean(v.Boolean));
                        break;
                    case CsvValueKind.Error:
                        store.SetValue(row, c + 1, CellValue.FromError(v.Error));
                        break;
                    default:
                        store.SetValue(row, c + 1, CellValue.FromNumber(v.Number));
                        if (v.FormatCode is { } code)
                        {
                            if (!styles.TryGetValue(code, out int xf))
                            {
                                xf = codes.Count + 1;
                                styles[code] = xf;
                                codes.Add(code);
                            }

                            store.SetStyle(row, c + 1, xf);
                        }

                        break;
                }
            }
        }

        if (codes.Count > 0)
        {
            wb.Package.FindPart("/xl/styles.xml")!.SetBytes(BuildStyles(codes));
        }

        return wb;
    }

    // A style sheet that holds the default format and one format per code the import used, in order of first use.
    private static byte[] BuildStyles(List<string> codes)
    {
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<styleSheet xmlns=\"").Append(ns).Append("\">");
        var custom = new List<(int Id, string Code)>();
        var ids = new int[codes.Count];
        for (int i = 0; i < codes.Count; i++)
        {
            int builtIn = NumberFormat.FindBuiltInId(codes[i]);
            if (builtIn >= 0)
            {
                ids[i] = builtIn;
            }
            else
            {
                ids[i] = 164 + custom.Count;
                custom.Add((ids[i], codes[i]));
            }
        }

        if (custom.Count > 0)
        {
            sb.Append("<numFmts count=\"").Append(custom.Count).Append("\">");
            foreach ((int id, string code) in custom)
            {
                sb.Append("<numFmt numFmtId=\"").Append(id).Append("\" formatCode=\"").Append(code.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)).Append("\"/>");
            }

            sb.Append("</numFmts>");
        }

        sb.Append("<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font></fonts>");
        sb.Append("<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>");
        sb.Append("<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>");
        sb.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
        sb.Append("<cellXfs count=\"").Append(codes.Count + 1).Append("\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>");
        foreach (int id in ids)
        {
            sb.Append("<xf numFmtId=\"").Append(id).Append("\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>");
        }

        sb.Append("</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ---- writing ----

    /// <summary>Writes a worksheet as delimited text.</summary>
    /// <param name="sheet">The sheet; it must be a worksheet.</param>
    /// <param name="destination">Where the text goes.</param>
    /// <param name="options">Delimiter, quoting, encoding, formatted or raw values; null for the defaults (comma, CR LF, UTF-8 with a mark, formatted).</param>
    /// <param name="loss">Receives what a CSV cannot hold (see the type's remarks); null to ignore.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="NotSupportedException">The sheet is not a worksheet.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="IOException">The destination cannot be written.</exception>
    public static void Write(Worksheet sheet, Stream destination, CsvWriteOptions? options = null, ILossSink? loss = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(destination);
        options ??= new CsvWriteOptions();
        using var writer = new CsvWriter(destination, options, leaveOpen: true);
        WriteRows(sheet, writer, options, loss, cancellationToken);
        writer.Flush();
    }

    /// <summary>Writes a workbook's first visible worksheet as delimited text.</summary>
    /// <param name="workbook">The workbook.</param>
    /// <param name="destination">Where the text goes.</param>
    /// <param name="options">As for <see cref="Write(Worksheet, Stream, CsvWriteOptions?, ILossSink?, CancellationToken)"/>.</param>
    /// <param name="loss">Receives what a CSV cannot hold, including the sheets not written; null to ignore.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="NotSupportedException">The workbook has no worksheet.</exception>
    public static void Write(Workbook workbook, Stream destination, CsvWriteOptions? options = null, ILossSink? loss = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        Worksheet? target = null;
        foreach (Worksheet s in workbook.Sheets)
        {
            if (s.Kind == SheetKind.Worksheet && (target is null || (target.State != SheetState.Visible && s.State == SheetState.Visible)))
            {
                target = s;
                if (s.State == SheetState.Visible)
                {
                    break;
                }
            }
        }

        if (target is null)
        {
            throw new NotSupportedException("The workbook has no worksheet to write.");
        }

        Write(target, destination, options, loss, cancellationToken);
        if (loss is not null)
        {
            ReportWorkbookLoss(workbook, target, loss);
        }
    }

    private static void ReportWorkbookLoss(Workbook workbook, Worksheet written, ILossSink loss)
    {
        int others = workbook.Sheets.Count - 1;
        if (others > 0)
        {
            loss.Report("loss.sheets", "other sheets", others, "A CSV holds one sheet; the others are not written.");
        }

        long objects = 0;
        foreach (IPackagePart part in workbook.Package.Parts)
        {
            string t = part.ContentType;
            if (t.Contains("drawing", StringComparison.Ordinal) || t.Contains("chart", StringComparison.Ordinal) || t.Contains("spreadsheetml.table+", StringComparison.Ordinal)
                || t.Contains("spreadsheetml.comments", StringComparison.Ordinal) || t.Contains("pivot", StringComparison.Ordinal))
            {
                objects++;
            }
        }

        if (objects > 0)
        {
            loss.Report("loss.embedded-objects", "drawings, charts, tables, comments and pivot tables", objects, "A CSV holds cell text only; these parts are not written.");
        }
    }

    private static void WriteRows(Worksheet sheet, CsvWriter writer, CsvWriteOptions options, ILossSink? loss, CancellationToken cancellationToken)
    {
        CellStore store = sheet.Store;
        if (store.CellCount == 0)
        {
            return;
        }

        var formats = new StyleFormats(sheet.Workbook);
        DateSystem dateSystem = StyleFormats.IsDate1904(sheet.Workbook) ? DateSystem.Date1904 : DateSystem.Date1900;
        var formatOptions = new FormatOptions(dateSystem, null, 20, 0);
        long formulas = 0;
        long styled = 0;
        long formatted = 0;
        int nextRow = 1;
        foreach (CellRowView row in store.EnumerateRows())
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (nextRow < row.Row)
            {
                writer.EndRecord();
                nextRow++;
            }

            int col = 1;
            for (int i = 0; i < row.Count; i++)
            {
                int c = row.ColumnAt(i);
                for (; col < c; col++)
                {
                    writer.WriteField([]);
                }

                CellValue v = row.ValueAt(i);
                int style = row.StyleAt(i);
                if (style != 0)
                {
                    styled++;
                }

                if (row.HasFormulaAt(i))
                {
                    formulas++;
                }

                WriteValue(sheet, writer, v, style, options, formats, formatOptions, ref formatted);
                col = c + 1;
            }

            writer.EndRecord();
            nextRow = row.Row + 1;
        }

        if (loss is not null)
        {
            if (formulas > 0)
            {
                loss.Report("loss.formulas", "formulas", formulas, "A CSV holds values; formula cells are written as their cached results.");
            }

            if (styled > 0)
            {
                loss.Report("loss.cell-styles", "cell styles", styled, "Fonts, fills, borders and alignment are not part of a CSV.");
            }

            if (formatted > 0)
            {
                loss.Report("loss.number-formats", "number formats", formatted, "Raw values were written: dates are serial numbers and numbers lose their display format.");
            }

            if (sheet.MergedCells.Count > 0)
            {
                loss.Report("loss.merged-cells", "merged cells", sheet.MergedCells.Count, "Only the top left cell of a merged range holds a value in the text.");
            }
        }
    }

    private static void WriteValue(Worksheet sheet, CsvWriter writer, CellValue v, int style, CsvWriteOptions options, StyleFormats formats, FormatOptions formatOptions, ref long rawFormatted)
    {
        switch (v.Kind)
        {
            case CellValueKind.Empty:
                writer.WriteField([]);
                break;
            case CellValueKind.Text:
                writer.WriteField(sheet.GetText(v), isText: true);
                break;
            case CellValueKind.Boolean:
                writer.WriteField(v.Boolean ? "TRUE" : "FALSE", isText: false);
                break;
            case CellValueKind.Error:
                writer.WriteField(sheet.GetText(v), isText: false);
                break;
            default:
                {
                    NumberFormat? nf = formats.Get(style);
                    if (!options.Formatted)
                    {
                        if (nf is not null)
                        {
                            rawFormatted++;
                        }

                        writer.WriteNumber(v.Number);
                    }
                    else if (nf is null)
                    {
                        Span<char> buf = stackalloc char[64]; // General at width 20 is at most 24 characters
                        FormatResult r = NumberFormat.General.TryFormat(v.Number, buf, formatOptions);
                        writer.WriteField(r.Status == FormatStatus.Ok ? buf[..r.Length] : NumberFormat.General.Format(v.Number, formatOptions), isText: false);
                    }
                    else
                    {
                        // The text of a number, so a format such as "-0" or a literal that starts with "=" cannot become a formula here.
                        writer.WriteField(nf.Format(v.Number, formatOptions), isText: false);
                    }

                    break;
                }
        }
    }
}
