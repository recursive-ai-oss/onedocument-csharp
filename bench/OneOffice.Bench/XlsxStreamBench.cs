using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ClosedXML.Excel;
using MiniExcelLibs;
using NPOI.XSSF.UserModel;
using OfficeOpenXml;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using OoCellValue = RecursiveAI.OneOffice.Spreadsheets.CellValue;
using OoWorkbook = RecursiveAI.OneOffice.Spreadsheets.Workbook;
using OoCellRowView = RecursiveAI.OneOffice.Spreadsheets.CellRowView;
using OoSheetKind = RecursiveAI.OneOffice.Spreadsheets.SheetKind;
using OoValueKind = RecursiveAI.OneOffice.Spreadsheets.CellValueKind;
using OoWorksheet = RecursiveAI.OneOffice.Spreadsheets.Worksheet;
using RecursiveAI.OneOffice.Spreadsheets.Streaming;
using EdrReaderFactory = ExcelDataReader.ExcelReaderFactory;
using SylvanReader = Sylvan.Data.Excel.ExcelDataReader;

namespace RecursiveAI.OneOffice.Bench;

/// <summary>
/// Streaming spreadsheet rows (values only): each row is one library reading every cell of the first sheet, or writing a sheet of the
/// same generated cells, in a fresh process so memory figures never mix. Indicative: a few repetitions, no statistics beyond min/median.
/// </summary>
/// <remarks>
/// Commands: <c>xlsxstream gen DIR</c> writes the read inputs; <c>xlsxstream run ROW FILE|ROWS [reps]</c> runs one row; <c>xlsxstream all DIR OUT.tsv</c> runs every row on every size.
/// Each library is used through its public API in the plain, documented way for forward value access.
/// </remarks>
public static class XlsxStreamBench
{
    private const int Columns = 10;
    private static readonly string[] Words = BuildWords("word", 1000);
    private static readonly string[] Names = BuildWords("name", 50);
    private static readonly string[] Texts = BuildWords("some longer text value number ", 1000);

    private static readonly int[] Sizes = [10_000, 100_000, 1_000_000];

    // Whole-model rows hold every cell of every library's model at once; 1M rows would need 10-15 GB for NPOI, so the timing rows stop at 100k (bytes per cell covers 1M).
    private static readonly int[] ModelSizes = [10_000, 100_000];

    private static string[] BuildWords(string prefix, int n) => [.. Enumerable.Range(0, n).Select(i => prefix + i.ToString(CultureInfo.InvariantCulture))];

    /// <summary>The read rows, by name.</summary>
    public static readonly string[] ReadRows = ["oneoffice.read.span", "oneoffice.read.string", "sylvan.read", "exceldatareader.read", "miniexcel.read"];

    /// <summary>The whole-model rows, by name: <c>load</c> opens and visits every cell; <c>editsave</c> opens, changes one cell and saves (a model that rewrites its parts pays for all of them).</summary>
    public static readonly string[] ModelRows =
    [
        "oneoffice.model.load", "closedxml.model.load", "npoi.model.load", "epplus.model.load",
        "oneoffice.model.editsave", "closedxml.model.editsave", "npoi.model.editsave", "epplus.model.editsave",
        "oneoffice.model.loadsave-untouched",
    ];

    /// <summary>The write rows, by name.</summary>
    public static readonly string[] WriteRows = ["oneoffice.write.shared", "oneoffice.write.inline", "miniexcel.write", "openxml.write"];

    /// <summary>Entry point of the <c>xlsxstream</c> command.</summary>
    public static int Run(string[] args)
    {
        // ExcelDataReader reads legacy code pages through this provider.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        switch (args.FirstOrDefault())
        {
            case "gen" when args.Length == 2:
                Generate(args[1]);
                return 0;
            case "run" when args.Length is 3 or 4:
                return RunOne(args[1], args[2], args.Length == 4 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 3);
            case "peek" when args.Length == 2:
                Peek(args[1]);
                return 0;
            case "all" when args.Length == 3:
                return RunAll(args[1], args[2]);
            default:
                Console.Error.WriteLine("usage: xlsxstream gen DIR | run ROW FILE|ROWS [reps] | all DIR OUT.tsv");
                return 2;
        }
    }

    // Prints the first rows as Sylvan returns them, to see what its values are before comparing checksums.
    private static void Peek(string path)
    {
        using var r = SylvanReader.Create(path);
        Console.WriteLine("fields " + r.FieldCount + " header " + r.GetName(0));
        for (int row = 0; row < 3 && r.Read(); row++)
        {
            Console.WriteLine(string.Join(" | ", Enumerable.Range(0, r.FieldCount).Select(i => (r.GetValue(i)?.GetType().Name ?? "null") + ":" + r.GetValue(i))));
        }
    }

    // ------------------------------------------------------------------ inputs

    private static string InputName(int rows) => "values-" + rows.ToString(CultureInfo.InvariantCulture) + ".xlsx";

    /// <summary>Writes the read inputs with the BCL zip classes and plain string building, so no library under test makes its own input.</summary>
    private static void Generate(string dir)
    {
        Directory.CreateDirectory(dir);
        string? only = Environment.GetEnvironmentVariable("XLSXBENCH_SIZES");
        foreach (int rows in Sizes)
        {
            string path = Path.Combine(dir, InputName(rows));
            if (File.Exists(path) || (only is not null && !only.Split(',').Contains(rows.ToString(CultureInfo.InvariantCulture))))
            {
                continue;
            }

            using FileStream fs = File.Create(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
            Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Add(zip, "xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"14\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>");
            var all = Words.Concat(Names).Concat(Texts).ToArray();
            ZipArchiveEntry sst = zip.CreateEntry("xl/sharedStrings.xml", CompressionLevel.Fastest);
            using (var w = new StreamWriter(sst.Open(), new UTF8Encoding(false)))
            {
                w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" uniqueCount=\"" + all.Length + "\">");
                foreach (string s in all)
                {
                    w.Write("<si><t>" + s + "</t></si>");
                }

                w.Write("</sst>");
            }

            ZipArchiveEntry sheet = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
            using (var w = new StreamWriter(sheet.Open(), new UTF8Encoding(false), 65536))
            {
                w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
                for (int r = 1; r <= rows; r++)
                {
                    w.Write("<row r=\"" + r + "\">");
                    for (int c = 0; c < Columns; c++)
                    {
                        string cell = ((char)('A' + c)).ToString() + r.ToString(CultureInfo.InvariantCulture);
                        switch (c)
                        {
                            case 2:
                                w.Write("<c r=\"" + cell + "\" t=\"s\"><v>" + ((r * 7) % 1000) + "</v></c>");
                                break;
                            case 4:
                                w.Write("<c r=\"" + cell + "\" t=\"s\"><v>" + (1000 + (r % 50)) + "</v></c>");
                                break;
                            case 7:
                                w.Write("<c r=\"" + cell + "\" t=\"s\"><v>" + (1050 + (r % 1000)) + "</v></c>");
                                break;
                            case 5:
                                w.Write("<c r=\"" + cell + "\" t=\"b\"><v>" + (r % 2 == 0 ? 1 : 0) + "</v></c>");
                                break;
                            case 9:
                                w.Write("<c r=\"" + cell + "\" s=\"1\"><v>" + (44000 + (r % 3000)) + "</v></c>");
                                break;
                            default:
                                w.Write("<c r=\"" + cell + "\"><v>" + Number(r, c).ToString("R", CultureInfo.InvariantCulture) + "</v></c>");
                                break;
                        }
                    }

                    w.Write("</row>");
                }

                w.Write("</sheetData></worksheet>");
            }

            Console.WriteLine(path + " " + new FileInfo(path).Length + " bytes");
        }
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private static double Number(int r, int c) => c switch
    {
        0 => r,
        1 => r * 0.5,
        3 => (r % 977) * 1.37,
        6 => r % 100,
        _ => ((r * 31) % 10007) * 0.01,
    };

    // ------------------------------------------------------------------ one row in this process

    private static int RunOne(string row, string target, int reps)
    {
        bool model = ModelRows.Contains(row);
        bool read = model || ReadRows.Contains(row);
        if (!read && !WriteRows.Contains(row))
        {
            Console.Error.WriteLine("unknown row " + row);
            return 2;
        }

        int writeRows = read ? 0 : int.Parse(target, CultureInfo.InvariantCulture);
        string tmp = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "xlsxstream-" + Environment.ProcessId + ".xlsx");
        var times = new List<double>();
        long alloc = 0;
        long checksum = 0;
        long fileBytes = 0;
        // Whole-model rows run big loops that the runtime only promotes to optimised code after a couple of runs, so they warm up three times;
        // the streaming rows reach steady state after one.
        int warmups = model ? 3 : 1;
        for (int i = 0; i < warmups + reps; i++)
        {
            // The first repetitions are warm-ups: the JIT and the first read of the file are not what is measured.
            GC.Collect();
            long a0 = GC.GetTotalAllocatedBytes(true);
            var sw = Stopwatch.StartNew();
            if (model)
            {
                checksum = ModelRow(row, target, tmp);
                if (File.Exists(tmp))
                {
                    fileBytes = new FileInfo(tmp).Length;
                }
            }
            else if (read)
            {
                checksum = ReadRow(row, target);
            }
            else
            {
                checksum = WriteRow(row, tmp, writeRows);
                fileBytes = new FileInfo(tmp).Length;
            }

            double ms = sw.Elapsed.TotalMilliseconds;
            long a1 = GC.GetTotalAllocatedBytes(true);
            if (Environment.GetEnvironmentVariable("XLSXBENCH_VERBOSE") is not null)
            {
                Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{row} rep {i}: {ms:F1} ms, gen0/1/2 collections {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}"));
            }

            if (i >= warmups)
            {
                times.Add(ms);
                alloc = a1 - a0;
            }

            if ((!read || model) && File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }

        using Process self = Process.GetCurrentProcess();
        times.Sort();
        string size = read ? Path.GetFileNameWithoutExtension(target) : "write-" + writeRows;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{row}\t{size}\t{reps}\t{times[0]:F1}\t{times[times.Count / 2]:F1}\t{alloc}\t{self.PeakWorkingSet64 >> 20}\t{checksum}\t{fileBytes}"));
        return 0;
    }

    private static long ReadRow(string row, string path)
    {
        long sum = 0;
        switch (row)
        {
            case "oneoffice.read.span":
            {
                using var r = XlsxStreamReader.Open(path);
                using SheetCursor c = r.OpenSheet(0);
                while (c.ReadRow())
                {
                    while (c.ReadCell())
                    {
                        sum += Tally(c, false);
                    }
                }

                break;
            }

            case "oneoffice.read.string":
            {
                using var r = XlsxStreamReader.Open(path);
                using SheetCursor c = r.OpenSheet(0);
                while (c.ReadRow())
                {
                    while (c.ReadCell())
                    {
                        sum += Tally(c, true);
                    }
                }

                break;
            }

            case "sylvan.read":
            {
                // The documented typed path: ask the native cell type, then use the matching accessor. (GetValue under the default
                // schema returns every cell as a string, which would be a different job.)
                using var r = SylvanReader.Create(path, new Sylvan.Data.Excel.ExcelDataReaderOptions { Schema = Sylvan.Data.Excel.ExcelSchema.NoHeaders });
                int n = r.FieldCount;
                while (r.Read())
                {
                    for (int i = 0; i < n; i++)
                    {
                        switch (r.GetExcelDataType(i))
                        {
                            case Sylvan.Data.Excel.ExcelDataType.Numeric:
                                sum += (long)r.GetDouble(i);
                                break;
                            case Sylvan.Data.Excel.ExcelDataType.DateTime:
                                sum += (long)r.GetDateTime(i).ToOADate();
                                break;
                            case Sylvan.Data.Excel.ExcelDataType.String:
                                sum += r.GetString(i).Length;
                                break;
                            case Sylvan.Data.Excel.ExcelDataType.Boolean:
                                sum += r.GetBoolean(i) ? 1 : 0;
                                break;
                        }
                    }
                }

                break;
            }

            case "exceldatareader.read":
            {
                using FileStream fs = File.OpenRead(path);
                using var r = EdrReaderFactory.CreateReader(fs);
                int n = r.FieldCount;
                while (r.Read())
                {
                    for (int i = 0; i < n; i++)
                    {
                        sum += Tally(r.GetValue(i));
                    }
                }

                break;
            }

            case "miniexcel.read":
            {
                foreach (object? rowObj in MiniExcel.Query(path))
                {
                    foreach (KeyValuePair<string, object?> kv in (IDictionary<string, object?>)rowObj)
                    {
                        sum += Tally(kv.Value);
                    }
                }

                break;
            }
        }

        return sum;
    }

    // Whole-model rows. Each library is used through its documented object model: open, visit every cell's value, or open, set one
    // cell and save. The edit is a real change so a library that tracks dirtiness has to rewrite the sheet.
    private static long ModelRow(string row, string path, string tmp)
    {
        long sum = 0;
        switch (row)
        {
            case "oneoffice.model.load":
            case "oneoffice.model.editsave":
            case "oneoffice.model.loadsave-untouched":
            {
                OoWorkbook wb = OoWorkbook.Open(path);
                if (row.EndsWith("load", StringComparison.Ordinal))
                {
                    foreach (OoWorksheet s in wb.Sheets)
                    {
                        sum += VisitSheet(s);
                    }
                }
                else if (row.EndsWith("untouched", StringComparison.Ordinal))
                {
                    wb.SaveAs(tmp);
                }
                else
                {
                    wb.Sheets[0][1, 1].SetNumber(42);
                    wb.SaveAs(tmp);
                }

                break;
            }

            case "closedxml.model.load":
            case "closedxml.model.editsave":
            {
                using var wb = new XLWorkbook(path);
                if (row.EndsWith("load", StringComparison.Ordinal))
                {
                    foreach (IXLWorksheet ws in wb.Worksheets)
                    {
                        foreach (IXLRow r in ws.RowsUsed())
                        {
                            foreach (IXLCell c in r.CellsUsed())
                            {
                                XLCellValue v = c.Value;
                                sum += v.IsNumber ? (long)v.GetNumber() : v.IsText ? v.GetText().Length : v.IsBoolean ? (v.GetBoolean() ? 1 : 0) : v.IsDateTime ? (long)v.GetDateTime().ToOADate() : 0;
                            }
                        }
                    }
                }
                else
                {
                    wb.Worksheet(1).Cell(1, 1).Value = 42;
                    wb.SaveAs(tmp);
                }

                break;
            }

            case "npoi.model.load":
            case "npoi.model.editsave":
            {
                using FileStream fs = File.OpenRead(path);
                var wb = new XSSFWorkbook(fs);
                if (row.EndsWith("load", StringComparison.Ordinal))
                {
                    for (int i = 0; i < wb.NumberOfSheets; i++)
                    {
                        foreach (NPOI.SS.UserModel.IRow r in wb.GetSheetAt(i))
                        {
                            foreach (NPOI.SS.UserModel.ICell c in r)
                            {
                                switch (c.CellType)
                                {
                                    case NPOI.SS.UserModel.CellType.Numeric:
                                        sum += NPOI.SS.UserModel.DateUtil.IsCellDateFormatted(c) ? (long)c.DateCellValue!.Value.ToOADate() : (long)c.NumericCellValue;
                                        break;
                                    case NPOI.SS.UserModel.CellType.String:
                                        sum += c.StringCellValue.Length;
                                        break;
                                    case NPOI.SS.UserModel.CellType.Boolean:
                                        sum += c.BooleanCellValue ? 1 : 0;
                                        break;
                                }
                            }
                        }
                    }
                }
                else
                {
                    wb.GetSheetAt(0).GetRow(0).GetCell(0).SetCellValue(42);
                    using FileStream o = File.Create(tmp);
                    wb.Write(o, false);
                }

                break;
            }

            case "epplus.model.load":
            case "epplus.model.editsave":
            {
                using var pkg = new ExcelPackage(new FileInfo(path));
                if (row.EndsWith("load", StringComparison.Ordinal))
                {
                    foreach (ExcelWorksheet ws in pkg.Workbook.Worksheets)
                    {
                        foreach (ExcelRangeBase c in ws.Cells)
                        {
                            sum += Tally(c.Value);
                        }
                    }
                }
                else
                {
                    _ = pkg.Workbook.Worksheets.First().Cells[1, 1].Value = 42;
                    pkg.SaveAs(new FileInfo(tmp));
                }

                break;
            }
        }

        return sum;
    }

    private static long VisitSheet(OoWorksheet s)
    {
        long sum = 0;
        if (s.Kind != OoSheetKind.Worksheet)
        {
            return 0;
        }

        foreach (OoCellRowView r in s.Store.EnumerateRows())
        {
            for (int i = 0; i < r.Count; i++)
            {
                OoCellValue v = r.ValueAt(i);
                sum += v.Kind switch
                {
                    OoValueKind.Number => (long)v.Number,
                    OoValueKind.Boolean => v.Boolean ? 1 : 0,
                    OoValueKind.Text => s.GetText(v).Length,
                    _ => 0,
                };
            }
        }

        return sum;
    }

    private static long Tally(SheetCursor c, bool asString)
    {
        switch (c.Kind)
        {
            case RecursiveAI.OneOffice.Spreadsheets.CellValueKind.Number:
                // The comparators return a DateTime for a date-formatted number, so this row decodes it too.
                return c.IsDate && c.TryGetDateTime(out DateTime when) ? (long)when.ToOADate() : (long)c.Number;
            case RecursiveAI.OneOffice.Spreadsheets.CellValueKind.Boolean:
                return c.Boolean ? 1 : 0;
            case RecursiveAI.OneOffice.Spreadsheets.CellValueKind.Text:
                return asString ? c.GetText().Length : c.TextUtf8.Length;
            default:
                return 0;
        }
    }

    private static long Tally(object? v) => v switch
    {
        double n => (long)n,
        string s => s.Length,
        bool b => b ? 1 : 0,
        DateTime d => (long)d.ToOADate(),
        int i => i,
        long l => l,
        decimal m => (long)m,
        float f => (long)f,
        _ => 0,
    };

    // ------------------------------------------------------------------ writing

    private static long WriteRow(string row, string path, int rows)
    {
        switch (row)
        {
            case "oneoffice.write.shared":
            case "oneoffice.write.inline":
            {
                using var w = XlsxStreamWriter.Create(path, new XlsxWriteOptions { StringStorage = row.EndsWith("inline", StringComparison.Ordinal) ? StreamStringStorage.Inline : StreamStringStorage.Shared });
                SheetStreamWriter s = w.AddSheet("Sheet1");
                for (int r = 1; r <= rows; r++)
                {
                    s.BeginRow();
                    for (int c = 0; c < Columns; c++)
                    {
                        switch (c)
                        {
                            case 2:
                                s.WriteText(Words[(r * 7) % 1000]);
                                break;
                            case 4:
                                s.WriteText(Names[r % 50]);
                                break;
                            case 7:
                                s.WriteText(Texts[r % 1000]);
                                break;
                            case 5:
                                s.WriteBoolean(r % 2 == 0);
                                break;
                            case 9:
                                s.WriteNumber(44000 + (r % 3000));
                                break;
                            default:
                                s.WriteNumber(Number(r, c));
                                break;
                        }
                    }
                }

                break;
            }

            case "miniexcel.write":
                MiniExcel.SaveAs(path, MiniRows(rows), printHeader: false, overwriteFile: true);
                break;
            case "openxml.write":
                WriteOpenXml(path, rows);
                break;
        }

        return rows;
    }

    private static IEnumerable<IDictionary<string, object?>> MiniRows(int rows)
    {
        string[] keys = [.. Enumerable.Range(0, Columns).Select(i => ((char)('A' + i)).ToString())];
        for (int r = 1; r <= rows; r++)
        {
            var d = new Dictionary<string, object?>(Columns);
            for (int c = 0; c < Columns; c++)
            {
                d[keys[c]] = c switch
                {
                    2 => Words[(r * 7) % 1000],
                    4 => Names[r % 50],
                    7 => Texts[r % 1000],
                    5 => r % 2 == 0,
                    9 => (double)(44000 + (r % 3000)),
                    _ => Number(r, c),
                };
            }

            yield return d;
        }
    }

    private static void WriteOpenXml(string path, int rows)
    {
        using SpreadsheetDocument doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        WorkbookPart wb = doc.AddWorkbookPart();
        WorksheetPart ws = wb.AddNewPart<WorksheetPart>();
        using (OpenXmlWriter w = OpenXmlWriter.Create(ws))
        {
            w.WriteStartElement(new Worksheet());
            w.WriteStartElement(new SheetData());
            for (int r = 1; r <= rows; r++)
            {
                w.WriteStartElement(new Row());
                for (int c = 0; c < Columns; c++)
                {
                    Cell cell = c switch
                    {
                        2 => Inline(Words[(r * 7) % 1000]),
                        4 => Inline(Names[r % 50]),
                        7 => Inline(Texts[r % 1000]),
                        5 => new Cell { DataType = CellValues.Boolean, CellValue = new CellValue(r % 2 == 0 ? "1" : "0") },
                        9 => new Cell { CellValue = new CellValue((44000 + (r % 3000)).ToString(CultureInfo.InvariantCulture)) },
                        _ => new Cell { CellValue = new CellValue(Number(r, c).ToString("R", CultureInfo.InvariantCulture)) },
                    };
                    w.WriteElement(cell);
                }

                w.WriteEndElement();
            }

            w.WriteEndElement();
            w.WriteEndElement();
        }

        using (OpenXmlWriter w = OpenXmlWriter.Create(wb))
        {
            w.WriteStartElement(new Workbook());
            w.WriteStartElement(new Sheets());
            w.WriteElement(new Sheet { Name = "Sheet1", SheetId = 1, Id = wb.GetIdOfPart(ws) });
            w.WriteEndElement();
            w.WriteEndElement();
        }
    }

    private static Cell Inline(string s) => new() { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(s)) };

    // ------------------------------------------------------------------ all rows in fresh processes

    private static int RunAll(string dir, string outPath)
    {
        Generate(dir);
        using var output = new StreamWriter(outPath);
        output.WriteLine("row\tsize\treps\tmin_ms\tmedian_ms\talloc_bytes_per_rep\tpeak_ws_mb\tchecksum\tfile_bytes");
        var jobs = new List<(string Row, string Target, int Reps)>();
        // XLSXBENCH_SIZES (comma-separated row counts) and XLSXBENCH_KIND (read or write) narrow a run so it fits a queue slot.
        string? sizeFilter = Environment.GetEnvironmentVariable("XLSXBENCH_SIZES");
        string? kind = Environment.GetEnvironmentVariable("XLSXBENCH_KIND");
        foreach (int rows in Sizes)
        {
            if (sizeFilter is not null && !sizeFilter.Split(',').Contains(rows.ToString(CultureInfo.InvariantCulture)))
            {
                continue;
            }

            int reps = rows >= 1_000_000 ? 2 : rows >= 100_000 ? 3 : 5;
            if (kind is null or "model" && ModelSizes.Contains(rows))
            {
                foreach (string row in ModelRows)
                {
                    jobs.Add((row, Path.Combine(dir, InputName(rows)), rows >= 100_000 ? 3 : 5));
                }
            }

            if (kind is null or "read")
            {
                foreach (string row in ReadRows)
                {
                    jobs.Add((row, Path.Combine(dir, InputName(rows)), reps));
                }
            }

            if (kind is null or "write")
            {
                foreach (string row in WriteRows)
                {
                    jobs.Add((row, rows.ToString(CultureInfo.InvariantCulture), reps));
                }
            }
        }

        foreach ((string row, string target, int reps) in jobs)
        {
            int exit = ChildProcess.Run(["xlsxstream", "run", row, target, reps.ToString(CultureInfo.InvariantCulture)], out string line, out long peakMb);
            string result = exit == 0 ? ChildProcess.WithPeak(line, 6, peakMb) : row + "\t" + Path.GetFileName(target) + "\tFAILED exit " + exit;
            output.WriteLine(result);
            output.Flush();
            Console.WriteLine(result);
        }

        return 0;
    }
}
