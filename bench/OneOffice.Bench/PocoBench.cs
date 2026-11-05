using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using MiniExcelLibs;
using RecursiveAI.OneOffice.Spreadsheets;
using RecursiveAI.OneOffice.Spreadsheets.Binding;
using SylvanReader = Sylvan.Data.Excel.ExcelDataReader;

namespace RecursiveAI.OneOffice.Bench;

public enum BenchLevel
{
    Low,
    Mid,
    High,
}

/// <summary>A ten-column row: two strings and eight values (an integer, a double, a long, a decimal, a date, a boolean, an enum, a nullable short).</summary>
[SpreadsheetRow]
public sealed partial class BenchRow
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public double Amount { get; set; }

    public long Count { get; set; }

    public decimal Price { get; set; }

    public DateTime When { get; set; }

    public bool Flag { get; set; }

    public BenchLevel Level { get; set; }

    public short? Small { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Typed rows to and from XLSX (POCO binding): each row is one library reading a sheet of the same generated rows into objects, or
/// writing objects to a sheet, in a fresh process. Indicative: a few repetitions, min and median.
/// </summary>
/// <remarks>
/// Commands: <c>poco gen DIR</c> writes the read inputs (with the BCL zip classes, so no library under test makes its own input);
/// <c>poco run ROW FILE|ROWS [reps]</c> runs one row; <c>poco all DIR OUT.tsv</c> runs every row on every size. Each library is used
/// through its documented typed API: this library's <c>Spreadsheet.Read&lt;T&gt;</c>, MiniExcel's <c>Query&lt;T&gt;</c>, and Sylvan's
/// typed accessors written out by hand (the floor a binder could reach).
/// </remarks>
public static class PocoBench
{
    private static readonly string[] Words = [.. Enumerable.Range(0, 1000).Select(i => "word" + i.ToString(CultureInfo.InvariantCulture))];
    private static readonly string[] Names = [.. Enumerable.Range(0, 50).Select(i => "name" + i.ToString(CultureInfo.InvariantCulture))];
    private static readonly string[] Levels = ["Low", "Mid", "High"];
    private static readonly string[] Headers = ["Id", "Code", "Amount", "Count", "Price", "When", "Flag", "Level", "Small", "Name"];
    private static readonly int[] Sizes = [10_000, 100_000, 1_000_000];

    private static readonly string[] ReadRows = ["oneoffice.poco.read", "miniexcel.query", "sylvan.typed"];
    private static readonly string[] WriteRows = ["oneoffice.poco.write", "miniexcel.saveas"];

    /// <summary>Entry point of the <c>poco</c> command.</summary>
    public static int Run(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "gen" when args.Length == 2:
                Generate(args[1]);
                return 0;
            case "run" when args.Length is 3 or 4:
                return RunOne(args[1], args[2], args.Length == 4 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 3);
            case "all" when args.Length == 3:
                return RunAll(args[1], args[2]);
            default:
                Console.Error.WriteLine("usage: poco gen DIR | run ROW FILE|ROWS [reps] | all DIR OUT.tsv");
                return 2;
        }
    }

    private static string InputName(int rows) => "poco-" + rows.ToString(CultureInfo.InvariantCulture) + ".xlsx";

    private static BenchRow Make(int i) => new()
    {
        Id = i,
        Code = Words[(i * 7) % 1000],
        Amount = i * 0.37,
        Count = i * 1000L,
        Price = i % 1000 / 100m,
        When = new DateTime(2000, 1, 1).AddMinutes(i % 500_000),
        Flag = i % 2 == 0,
        Level = (BenchLevel)(i % 3),
        Small = i % 5 == 0 ? null : (short)(i % 100),
        Name = Names[i % 50],
    };

    private static void Generate(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (int rows in Sizes)
        {
            string path = Path.Combine(dir, InputName(rows));
            if (File.Exists(path))
            {
                continue;
            }

            using FileStream fs = File.Create(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
            const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Add(zip, "[Content_Types].xml", Decl + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
            Add(zip, "_rels/.rels", Decl + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + Rel + "/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add(zip, "xl/workbook.xml", Decl + "<workbook xmlns=\"" + Main + "\" xmlns:r=\"" + Rel + "\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels", Decl + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + Rel + "/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"" + Rel + "/sharedStrings\" Target=\"sharedStrings.xml\"/><Relationship Id=\"rId3\" Type=\"" + Rel + "/styles\" Target=\"styles.xml\"/></Relationships>");
            Add(zip, "xl/styles.xml", Decl + "<styleSheet xmlns=\"" + Main + "\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"22\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>");

            string[] all = [.. Words, .. Names, .. Levels, .. Headers];
            ZipArchiveEntry sst = zip.CreateEntry("xl/sharedStrings.xml", CompressionLevel.Fastest);
            using (var w = new StreamWriter(sst.Open(), new UTF8Encoding(false)))
            {
                w.Write(Decl + "<sst xmlns=\"" + Main + "\" uniqueCount=\"" + all.Length + "\">");
                foreach (string s in all)
                {
                    w.Write("<si><t>" + s + "</t></si>");
                }

                w.Write("</sst>");
            }

            int nameBase = 1000;
            int levelBase = 1050;
            int headerBase = 1053;
            ZipArchiveEntry sheet = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
            using (var w = new StreamWriter(sheet.Open(), new UTF8Encoding(false), 65536))
            {
                w.Write(Decl + "<worksheet xmlns=\"" + Main + "\"><sheetData><row r=\"1\">");
                for (int c = 0; c < Headers.Length; c++)
                {
                    w.Write("<c r=\"" + (char)('A' + c) + "1\" t=\"s\"><v>" + (headerBase + c) + "</v></c>");
                }

                w.Write("</row>");
                for (int i = 0; i < rows; i++)
                {
                    BenchRow b = Make(i);
                    int r = i + 2;
                    string n = r.ToString(CultureInfo.InvariantCulture);
                    w.Write("<row r=\"" + n + "\">");
                    w.Write("<c r=\"A" + n + "\"><v>" + b.Id + "</v></c>");
                    w.Write("<c r=\"B" + n + "\" t=\"s\"><v>" + ((i * 7) % 1000) + "</v></c>");
                    w.Write("<c r=\"C" + n + "\"><v>" + b.Amount.ToString("R", CultureInfo.InvariantCulture) + "</v></c>");
                    w.Write("<c r=\"D" + n + "\"><v>" + b.Count + "</v></c>");
                    w.Write("<c r=\"E" + n + "\"><v>" + b.Price.ToString(CultureInfo.InvariantCulture) + "</v></c>");
                    w.Write("<c r=\"F" + n + "\" s=\"1\"><v>" + (36526 + ((i % 500_000) / 1440.0)).ToString("R", CultureInfo.InvariantCulture) + "</v></c>");
                    w.Write("<c r=\"G" + n + "\" t=\"b\"><v>" + (b.Flag ? 1 : 0) + "</v></c>");
                    w.Write("<c r=\"H" + n + "\" t=\"s\"><v>" + (levelBase + (i % 3)) + "</v></c>");
                    if (b.Small is { } small)
                    {
                        w.Write("<c r=\"I" + n + "\"><v>" + small + "</v></c>");
                    }

                    w.Write("<c r=\"J" + n + "\" t=\"s\"><v>" + (nameBase + (i % 50)) + "</v></c></row>");
                }

                w.Write("</sheetData></worksheet>");
            }
        }
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private static long Tally(BenchRow b) =>
        b.Id + (long)b.Amount + b.Count + (long)(b.Price * 100) + (b.When.Ticks / TimeSpan.TicksPerMinute % 1000) + (b.Flag ? 1 : 0) + (int)b.Level + (b.Small ?? 0) + b.Code.Length + b.Name.Length;

    private static long ReadRow(string row, string path)
    {
        long sum = 0;
        switch (row)
        {
            case "oneoffice.poco.read":
                foreach (BenchRow b in Spreadsheet.Read<BenchRow>(path))
                {
                    sum += Tally(b);
                }

                break;
            case "miniexcel.query":
                foreach (BenchRow b in MiniExcel.Query<BenchRow>(path))
                {
                    sum += Tally(b);
                }

                break;
            case "sylvan.typed":
            {
                using var r = SylvanReader.Create(path);
                while (r.Read())
                {
                    var b = new BenchRow
                    {
                        Id = r.GetInt32(0),
                        Code = r.GetString(1),
                        Amount = r.GetDouble(2),
                        Count = r.GetInt64(3),
                        Price = r.GetDecimal(4),
                        When = r.GetDateTime(5),
                        Flag = r.GetBoolean(6),
                        Level = Enum.Parse<BenchLevel>(r.GetString(7)),
                        Small = r.IsDBNull(8) ? null : r.GetInt16(8),
                        Name = r.GetString(9),
                    };
                    sum += Tally(b);
                }

                break;
            }

            default:
                throw new ArgumentException("unknown row " + row);
        }

        return sum;
    }

    private static IEnumerable<BenchRow> Source(int rows)
    {
        for (int i = 0; i < rows; i++)
        {
            yield return Make(i);
        }
    }

    private static long WriteRow(string row, string path, int rows)
    {
        switch (row)
        {
            case "oneoffice.poco.write":
                Spreadsheet.Write(path, Source(rows));
                break;
            case "miniexcel.saveas":
                MiniExcel.SaveAs(path, Source(rows), overwriteFile: true);
                break;
            default:
                throw new ArgumentException("unknown row " + row);
        }

        return rows;
    }

    private static int RunOne(string row, string target, int reps)
    {
        bool read = ReadRows.Contains(row);
        if (!read && !WriteRows.Contains(row))
        {
            Console.Error.WriteLine("unknown row " + row);
            return 2;
        }

        int writeRows = read ? 0 : int.Parse(target, CultureInfo.InvariantCulture);
        string tmp = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "poco-" + Environment.ProcessId + ".xlsx");
        var times = new List<double>();
        long alloc = 0;
        long checksum = 0;
        long fileBytes = 0;
        for (int i = 0; i <= reps; i++)
        {
            // Repetition 0 is a warm-up.
            GC.Collect();
            long a0 = GC.GetTotalAllocatedBytes(true);
            var sw = Stopwatch.StartNew();
            if (read)
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
            if (i > 0)
            {
                times.Add(ms);
                alloc = a1 - a0;
            }

            if (!read && File.Exists(tmp))
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

    private static int RunAll(string dir, string outPath)
    {
        Generate(dir);
        string dll = typeof(PocoBench).Assembly.Location;
        using var output = new StreamWriter(outPath);
        output.WriteLine("row\tsize\treps\tmin_ms\tmedian_ms\talloc_bytes_per_rep\tpeak_ws_mb\tchecksum\tfile_bytes");
        string? sizeFilter = Environment.GetEnvironmentVariable("POCOBENCH_SIZES");
        foreach (int rows in Sizes)
        {
            if (sizeFilter is not null && !sizeFilter.Split(',').Contains(rows.ToString(CultureInfo.InvariantCulture)))
            {
                continue;
            }

            int reps = rows >= 1_000_000 ? 2 : rows >= 100_000 ? 3 : 5;
            var jobs = ReadRows.Select(r => (Row: r, Target: Path.Combine(dir, InputName(rows)))).Concat(WriteRows.Select(r => (Row: r, Target: rows.ToString(CultureInfo.InvariantCulture))));
            foreach ((string row, string target) in jobs)
            {
                var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, UseShellExecute = false };
                psi.ArgumentList.Add(dll);
                psi.ArgumentList.Add("poco");
                psi.ArgumentList.Add("run");
                psi.ArgumentList.Add(row);
                psi.ArgumentList.Add(target);
                psi.ArgumentList.Add(reps.ToString(CultureInfo.InvariantCulture));
                using Process p = Process.Start(psi)!;
                string line = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                output.WriteLine(p.ExitCode == 0 ? line : row + "\t" + target + "\tFAILED " + p.ExitCode);
                output.Flush();
            }
        }

        return 0;
    }
}
