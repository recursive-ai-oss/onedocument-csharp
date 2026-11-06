using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using OfficeOpenXml;
using HubDocument = RecursiveAI.OneDocument.Documents.Document;
using HubPresentation = RecursiveAI.OneDocument.Presentations.Presentation;
using HubWorkbook = RecursiveAI.OneDocument.Spreadsheets.Workbook;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using RecalcOptions = RecursiveAI.OneDocument.Spreadsheets.Calc.RecalcOptions;

namespace RecursiveAI.OneDocument.Bench;

/// <summary>
/// The rows beyond word-processing load/save and the xlsx streaming/model groups: legacy and binary workbook reads, XLS writes,
/// recalculation, presentations, text extraction, template fill, rendering and the legacy document readers.
/// Every row runs in a fresh process through <c>fmtbench run ROW TARGET [reps]</c>, with the same warm-up and statistics as the
/// <c>xlsxstream</c> rows, and prints the same TSV line (row, size, reps, min ms, median ms, allocated bytes per repetition,
/// peak working set MB, checksum, output bytes).
/// </summary>
/// <remarks>
/// A row does its setup, calls <see cref="Meter.Begin"/> and then does the one operation that is timed; rows with no setup are timed
/// from the start. Each library is used through its public API in the plain documented way. The checksum is derived from the
/// result so the optimiser cannot drop the work, and for read and recalc rows it is equal across libraries when they agree.
/// </remarks>
public static class FormatsBench
{
    private const int Columns = 10;

    private static readonly int[] LegacySizes = [10_000, 50_000];
    private static readonly int[] CalcSizes = [10_000, 50_000];
    private static readonly int[] DeckSizes = [50, 500];
    private static readonly int[] TemplateSizes = [1_000, 5_000];

    /// <summary>Timing helper handed to a row so setup stays out of the measurement.</summary>
    public sealed class Meter
    {
        /// <summary>The stopwatch of the measured region.</summary>
        public Stopwatch Clock { get; } = new();

        /// <summary>Total bytes allocated when the measured region began.</summary>
        public long AllocatedAtStart { get; private set; }

        /// <summary>Starts the measured region now: restarts the clock and the allocation count.</summary>
        public void Begin()
        {
            AllocatedAtStart = GC.GetTotalAllocatedBytes(true);
            Clock.Restart();
        }
    }

    /// <summary>The rows, grouped for the report and for run slots. Each group fits one queue slot of at most 20 minutes.</summary>
    public static readonly (string Group, string[] Rows, string Kind)[] Groups =
    [
        ("xls-read", ["onedocument.xls.model", "onedocument.xls.read", "sylvan.xls.read", "exceldatareader.xls.read", "npoi.xls.model"], "legacy-xls"),
        ("xlsb-read", ["onedocument.xlsb.model", "onedocument.xlsb.read", "sylvan.xlsb.read", "exceldatareader.xlsb.read"], "legacy-xlsb"),
        ("xls-write", ["onedocument.xls.write", "npoi.xls.write"], "write-xls"),
        ("calc-cold", ["onedocument.calc.cold", "closedxml.calc.cold", "npoi.calc.cold", "epplus.calc.cold"], "calc"),
        ("calc-incr", ["onedocument.calc.incr", "closedxml.calc.incr", "npoi.calc.incr", "epplus.calc.incr"], "calc"),
        ("pptx", ["onedocument.pptx.load", "openxml.pptx.load", "onedocument.pptx.loadsave", "openxml.pptx.loadsave", "onedocument.pptx.edit", "openxml.pptx.edit",
            "onedocument.pptx.loadsave.mem", "openxml.pptx.loadsave.mem", "onedocument.pptx.edit.mem", "openxml.pptx.edit.mem"], "deck"),
        ("extract", ["onedocument.pptx.extract", "openxml.pptx.extract", "onedocument.xlsx.extract", "openxml.xlsx.extract", "npoi.xlsx.extract"], "extract"),
        ("template", ["onedocument.tpl.xlsx", "closedxmlreport.tpl.xlsx", "onedocument.tpl.docx"], "template"),
        ("info", ["onedocument.render.docx", "onedocument.doc.extract", "onedocument.rtf.extract", "onedocument.odt.extract", "onedocument.ods.model"], "info"),
    ];

    /// <summary>Entry point of the <c>fmtbench</c> command.</summary>
    public static int Run(string[] args)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        switch (args.FirstOrDefault())
        {
            case "gen" when args.Length == 2:
                Generate(args[1]);
                return 0;
            case "run" when args.Length is 3 or 4:
                return RunOne(args[1], args[2], args.Length == 4 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 3);
            case "group" when args.Length == 4:
                return RunGroup(args[1], args[2], args[3]);
            case "toxlsb" when args.Length == 3:
            {
                // Converts a workbook to a binary workbook: the memory rows need inputs of sizes the timing rows do not use.
                // The million-row input is far past the default allocation ceiling; this tool converts files it generated itself.
                var limits = RecursiveAI.OneDocument.OfficeLimits.Large;
                using HubWorkbook wb = HubWorkbook.Open(args[1], new RecursiveAI.OneDocument.OpenContext(limits));
                wb.SaveAs(args[2], RecursiveAI.OneDocument.OfficeFormat.Xlsb);
                return 0;
            }

            case "groups":
                foreach ((string g, string[] rows, _) in Groups)
                {
                    Console.WriteLine(g + "\t" + string.Join(' ', rows));
                }

                return 0;
            default:
                Console.Error.WriteLine("usage: fmtbench gen DIR | toxlsb IN OUT | run ROW FILE|N [reps] | group NAME DIR OUT.tsv | groups");
                return 2;
        }
    }

    // ------------------------------------------------------------------ inputs

    /// <summary>
    /// Writes the inputs this class can make without a library under test: recalculation workbooks and decks (BCL zip and string
    /// building), binary workbooks (converted from the generated xlsx by OneDocument, since no comparator writes XLSB), and the
    /// template files. The LibreOffice-made legacy inputs come from <c>bench/make-legacy-inputs.sh</c>.
    /// </summary>
    private static void Generate(string dir)
    {
        Directory.CreateDirectory(dir);
        string xlsxDir = Path.Combine(dir, "xlsx");
        Environment.SetEnvironmentVariable("XLSXBENCH_SIZES", string.Join(',', LegacySizes));
        XlsxStreamBench.Generate(xlsxDir);
        foreach (int n in LegacySizes)
        {
            string xlsb = Path.Combine(dir, "values-" + n.ToString(CultureInfo.InvariantCulture) + ".xlsb");
            if (!File.Exists(xlsb))
            {
                using HubWorkbook wb = HubWorkbook.Open(Path.Combine(xlsxDir, XlsxStreamBench.InputName(n)));
                wb.SaveAs(xlsb, RecursiveAI.OneDocument.OfficeFormat.Xlsb);
            }
        }

        foreach (int n in CalcSizes)
        {
            string path = Path.Combine(dir, "calc-" + n.ToString(CultureInfo.InvariantCulture) + ".xlsx");
            if (!File.Exists(path))
            {
                WriteCalcWorkbook(path, n);
            }
        }

        foreach (int n in DeckSizes)
        {
            string path = Path.Combine(dir, "deck-" + n.ToString(CultureInfo.InvariantCulture) + ".pptx");
            if (!File.Exists(path))
            {
                WriteDeck(path, n);
            }
        }

        foreach (int n in TemplateSizes)
        {
            string sn = n.ToString(CultureInfo.InvariantCulture);
            string ours = Path.Combine(dir, "tpl-xlsx-" + sn + ".template.xlsx");
            if (!File.Exists(ours))
            {
                File.Copy(Path.Combine(AppContext.BaseDirectory, "template-row-block.xlsx"), ours);
            }

            string theirs = Path.Combine(dir, "tpl-cxr-" + sn + ".template.xlsx");
            if (!File.Exists(theirs))
            {
                WriteReportTemplate(theirs);
            }

            string docx = Path.Combine(dir, "tpl-docx-" + sn + ".template.docx");
            if (!File.Exists(docx))
            {
                using HubDocument d = HubDocument.Create();
                d.Body.AddParagraph("Report {{ title }}");
                d.Body.AddParagraph("{% for it in items %}");
                d.Body.AddParagraph("{{ it.name }}: {{ it.qty }} at {{ TEXT(it.price, \"0.00\") }}");
                d.Body.AddParagraph("{% endfor %}");
                d.SaveAs(docx);
            }
        }

        foreach (string f in Directory.EnumerateFiles(dir).Order(StringComparer.Ordinal))
        {
            Console.WriteLine(f + " " + new FileInfo(f).Length + " bytes");
        }
    }

    // Formulas per row: B doubles A, C sums A:B, D branches on C, E rounds, G adds a fixed 50-cell sum; the two totals sum columns C and G.
    private static void WriteCalcWorkbook(string path, int rows)
    {
        using FileStream fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
        Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        Add(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        Add(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
        Add(zip, "xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"14\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>");
        ZipArchiveEntry sheet = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
        using var w = new StreamWriter(sheet.Open(), new UTF8Encoding(false), 65536);
        w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (int r = 1; r <= rows; r++)
        {
            string a = (1 + (r * 37 % 1000) * 0.25).ToString("R", CultureInfo.InvariantCulture);
            w.Write($"<row r=\"{r}\"><c r=\"A{r}\"><v>{a}</v></c>");
            w.Write($"<c r=\"B{r}\"><f>A{r}*2</f></c>");
            w.Write($"<c r=\"C{r}\"><f>SUM(A{r}:B{r})+1</f></c>");
            w.Write($"<c r=\"D{r}\" t=\"str\"><f>IF(C{r}&gt;300,\"hi\",\"lo\")</f></c>");
            w.Write($"<c r=\"E{r}\"><f>ROUND(B{r}*1.1,2)</f></c>");
            w.Write($"<c r=\"G{r}\"><f>A{r}+SUM($A$1:$A$50)</f></c></row>");
        }

        w.Write($"<row r=\"{rows + 2}\"><c r=\"C{rows + 2}\"><f>SUM(C1:C{rows})</f></c><c r=\"G{rows + 2}\"><f>SUM(G1:G{rows})</f></c></row>");
        w.Write("</sheetData></worksheet>");
    }

    private static void WriteDeck(string path, int slides)
    {
        using ZipArchive src = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "basedeck.pptx"));
        using FileStream fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        string slideXml = ReadEntry(src, "ppt/slides/slide1.xml");
        string slideRels = ReadEntry(src, "ppt/slides/_rels/slide1.xml.rels");
        foreach (ZipArchiveEntry e in src.Entries)
        {
            string name = e.FullName;
            if (name.StartsWith("ppt/slides/", StringComparison.Ordinal))
            {
                continue;
            }

            string text = ReadEntry(src, name);
            if (name == "ppt/presentation.xml")
            {
                var ids = new StringBuilder("<p:sldIdLst>");
                for (int i = 1; i <= slides; i++)
                {
                    ids.Append(CultureInfo.InvariantCulture, $"<p:sldId id=\"{255 + i}\" r:id=\"rId{100 + i}\"/>");
                }

                ids.Append("</p:sldIdLst>");
                int a = text.IndexOf("<p:sldIdLst>", StringComparison.Ordinal);
                int b = text.IndexOf("</p:sldIdLst>", StringComparison.Ordinal) + "</p:sldIdLst>".Length;
                text = text[..a] + ids + text[b..];
            }
            else if (name == "ppt/_rels/presentation.xml.rels")
            {
                var rels = new StringBuilder();
                for (int i = 1; i <= slides; i++)
                {
                    rels.Append(CultureInfo.InvariantCulture, $"<Relationship Id=\"rId{100 + i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide{i}.xml\"/>");
                }

                int a = text.IndexOf("<Relationship Id=\"rId10\"", StringComparison.Ordinal);
                int b = text.IndexOf("/>", a, StringComparison.Ordinal) + 2;
                text = text[..a] + rels + text[b..];
            }
            else if (name == "[Content_Types].xml")
            {
                var o = new StringBuilder();
                for (int i = 1; i <= slides; i++)
                {
                    o.Append(CultureInfo.InvariantCulture, $"<Override PartName=\"/ppt/slides/slide{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>");
                }

                int a = text.IndexOf("<Override PartName=\"/ppt/slides/slide1.xml\"", StringComparison.Ordinal);
                int b = text.IndexOf("/>", a, StringComparison.Ordinal) + 2;
                text = text[..a] + o + text[b..];
            }

            Add(zip, name, text);
        }

        for (int i = 1; i <= slides; i++)
        {
            // Each slide carries its own number in every text run so extraction output differs per slide.
            Add(zip, $"ppt/slides/slide{i}.xml", slideXml.Replace("<a:t>", "<a:t>S" + i.ToString(CultureInfo.InvariantCulture) + " ", StringComparison.Ordinal));
            Add(zip, $"ppt/slides/_rels/slide{i}.xml.rels", slideRels);
        }
    }

    private static string ReadEntry(ZipArchive zip, string name)
    {
        using var r = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8);
        return r.ReadToEnd();
    }

    // A ClosedXML.Report template: a one-row region named "items" whose cells bind the variable's members.
    private static void WriteReportTemplate(string path)
    {
        using var wb = new XLWorkbook();
        IXLWorksheet ws = wb.AddWorksheet("Sheet1");
        ws.Cell("A1").Value = "Name";
        ws.Cell("B1").Value = "Qty";
        ws.Cell("C1").Value = "Price";
        ws.Cell("A2").Value = "{{item.Name}}";
        ws.Cell("B2").Value = "{{item.Qty}}";
        ws.Cell("C2").Value = "{{item.Price}}";
        wb.DefinedNames.Add("items", ws.Range("A2:C2"));
        wb.SaveAs(path);
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    // ------------------------------------------------------------------ driver

    private static int RunOne(string row, string target, int reps)
    {
        string tmpDir = Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath();
        string tmp = Path.Combine(tmpDir, "fmtbench-" + Environment.ProcessId);
        var times = new List<double>();
        long alloc = 0;
        long checksum = 0;
        long outBytes = 0;
        var meter = new Meter();
        // Warm-up: at least two runs, then until twenty runs or three seconds have passed. A short operation is still in the runtime's
        // lower-tier code after three runs, so it gets the long warm-up and more measured repetitions; the same for every library.
        var total = Stopwatch.StartNew();
        double last = 0;
        for (int i = 0; i < 2 || (i < 20 && total.Elapsed.TotalSeconds < 3); i++)
        {
            GC.Collect();
            meter.Begin();
            checksum = Dispatch(row, target, tmp, meter, out outBytes);
            last = meter.Clock.Elapsed.TotalMilliseconds;
        }

        if (last < 100)
        {
            reps = Math.Max(reps, 15);
        }

        for (int i = 0; i < reps; i++)
        {
            GC.Collect();
            meter.Begin();
            checksum = Dispatch(row, target, tmp, meter, out outBytes);
            double ms = meter.Clock.Elapsed.TotalMilliseconds;
            long a1 = GC.GetTotalAllocatedBytes(true);
            times.Add(ms);
            alloc = a1 - meter.AllocatedAtStart;
        }

        using Process self = Process.GetCurrentProcess();
        times.Sort();
        string size = int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? "n-" + n
            : Path.GetFileName(target).Contains(".template.", StringComparison.Ordinal) ? "n-" + TemplateCount(target).ToString(CultureInfo.InvariantCulture)
            : Path.GetFileName(target);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{row}\t{size}\t{reps}\t{times[0]:F1}\t{times[times.Count / 2]:F1}\t{alloc}\t{self.PeakWorkingSet64 >> 20}\t{checksum}\t{outBytes}"));
        return 0;
    }

    private static int RunGroup(string name, string dir, string outPath)
    {
        (string Group, string[] Rows, string Kind) g = Groups.First(x => x.Group == name);
        string? sizeFilter = Environment.GetEnvironmentVariable("FMTBENCH_SIZES");
        bool Keep(string size) => sizeFilter is null || sizeFilter.Split(',').Contains(size);
        var targets = new List<(string Target, int Reps)>();
        switch (g.Kind)
        {
            case "legacy-xls":
                targets.AddRange(LegacySizes.Where(n => Keep(n.ToString(CultureInfo.InvariantCulture))).Select(n => (Path.Combine(dir, "legacy", "values-" + n + ".xls"), n > 10_000 ? 3 : 5)));
                break;
            case "legacy-xlsb":
                targets.AddRange(LegacySizes.Where(n => Keep(n.ToString(CultureInfo.InvariantCulture))).Select(n => (Path.Combine(dir, "values-" + n + ".xlsb"), n > 10_000 ? 3 : 5)));
                break;
            case "write-xls":
                targets.AddRange(LegacySizes.Where(n => Keep(n.ToString(CultureInfo.InvariantCulture))).Select(n => (n.ToString(CultureInfo.InvariantCulture), 3)));
                break;
            case "calc":
                targets.AddRange(CalcSizes.Where(n => Keep(n.ToString(CultureInfo.InvariantCulture))).Select(n => (Path.Combine(dir, "calc-" + n + ".xlsx"), 3)));
                break;
            case "deck":
                targets.AddRange(DeckSizes.Where(n => Keep(n.ToString(CultureInfo.InvariantCulture))).Select(n => (Path.Combine(dir, "deck-" + n + ".pptx"), 5)));
                break;
            case "extract":
                break;
            case "template":
                targets.AddRange(TemplateSizes.Where(n => Keep(n.ToString(CultureInfo.InvariantCulture))).Select(n => (n.ToString(CultureInfo.InvariantCulture), 3)));
                break;
            default:
                break;
        }

        using var output = new StreamWriter(outPath);
        output.WriteLine("row\tsize\treps\tmin_ms\tmedian_ms\talloc_bytes_per_rep\tpeak_ws_mb\tchecksum\tfile_bytes");
        var jobs = new List<(string Row, string Target, int Reps)>();
        if (g.Kind == "extract")
        {
            foreach (string row in g.Rows)
            {
                string[] files = row.Contains(".pptx.", StringComparison.Ordinal) ? DeckSizes.Select(n => Path.Combine(dir, "deck-" + n + ".pptx")).ToArray()
                    : row.Contains(".xls.", StringComparison.Ordinal) ? LegacySizes.Take(1).Select(n => Path.Combine(dir, "legacy", "values-" + n + ".xls")).ToArray()
                    : LegacySizes.Select(n => Path.Combine(dir, "xlsx", XlsxStreamBench.InputName(n))).ToArray();
                foreach (string f in files)
                {
                    jobs.Add((row, f, 5));
                }
            }
        }
        else if (g.Kind == "info")
        {
            foreach (string row in g.Rows)
            {
                string? f = row switch
                {
                    "onedocument.render.docx" => Path.Combine(dir, "medium.docx"),
                    "onedocument.doc.extract" => Path.Combine(dir, "legacy", "medium.doc"),
                    "onedocument.rtf.extract" => Path.Combine(dir, "legacy", "medium.rtf"),
                    "onedocument.odt.extract" => Path.Combine(dir, "legacy", "medium.odt"),
                    _ => Path.Combine(dir, "legacy", "values-10000.ods"),
                };
                jobs.Add((row, f, 3));
                if (row == "onedocument.render.docx")
                {
                    jobs.Add((row, Path.Combine(dir, "small.docx"), 3));
                }
            }
        }
        else
        {
            foreach ((string target, int reps) in targets)
            {
                foreach (string row in g.Rows)
                {
                    string t = target;
                    if (g.Kind == "template")
                    {
                        string key = row.StartsWith("closedxmlreport", StringComparison.Ordinal) ? "tpl-cxr-" : row.EndsWith(".docx", StringComparison.Ordinal) ? "tpl-docx-" : "tpl-xlsx-";
                        string ext = row.EndsWith(".docx", StringComparison.Ordinal) ? ".template.docx" : ".template.xlsx";
                        t = Path.Combine(dir, key + target + ext);
                    }

                    jobs.Add((row, t, reps));
                }
            }
        }

        foreach ((string row, string target, int reps) in jobs)
        {
            int exit = File.Exists(target) || int.TryParse(target, out _) || target.EndsWith(".template.xlsx", StringComparison.Ordinal) || target.EndsWith(".template.docx", StringComparison.Ordinal)
                ? ChildProcess.Run(["fmtbench", "run", row, target, reps.ToString(CultureInfo.InvariantCulture)], out string line, out long peakMb)
                : Missing(out line, out peakMb);
            string result = exit == 0 && line.Length > 0 ? ChildProcess.WithPeak(line, 6, peakMb) : row + "\t" + Path.GetFileName(target) + "\tFAILED exit " + exit;
            output.WriteLine(result);
            output.Flush();
            Console.WriteLine(result);
        }

        return 0;

        static int Missing(out string line, out long peak)
        {
            line = string.Empty;
            peak = 0;
            return 66;
        }
    }

    // ------------------------------------------------------------------ rows

    private static long Dispatch(string row, string target, string tmp, Meter m, out long outBytes)
    {
        outBytes = 0;
        switch (row)
        {
            case "onedocument.xls.model":
            case "onedocument.xlsb.model":
            case "onedocument.ods.model":
            {
                long sum = 0;
                using HubWorkbook wb = HubWorkbook.Open(target);
                foreach (RecursiveAI.OneDocument.Spreadsheets.Worksheet s in wb.Sheets)
                {
                    sum += XlsxStreamBench.VisitSheet(s);
                }

                return sum;
            }

            case "onedocument.xls.read":
            {
                // The forward cursor, read the way the forward readers are: the native type of each cell, text as a string.
                long sum = 0;
                using var book = RecursiveAI.OneDocument.Spreadsheets.Streaming.XlsStreamReader.Open(target);
                using RecursiveAI.OneDocument.Spreadsheets.Streaming.XlsSheetCursor c = book.OpenSheet(0);
                while (c.ReadRow())
                {
                    while (c.ReadCell())
                    {
                        sum += c.Kind switch
                        {
                            RecursiveAI.OneDocument.Spreadsheets.CellValueKind.Number => c.IsDate && c.TryGetDateTime(out DateTime when) ? (long)when.ToOADate() : (long)c.Number,
                            RecursiveAI.OneDocument.Spreadsheets.CellValueKind.Boolean => c.Boolean ? 1 : 0,
                            RecursiveAI.OneDocument.Spreadsheets.CellValueKind.Text => c.GetText().Length,
                            _ => 0,
                        };
                    }
                }

                return sum;
            }

            case "onedocument.xlsb.read":
            {
                long sum = 0;
                using var book = RecursiveAI.OneDocument.Spreadsheets.Streaming.XlsbStreamReader.Open(target);
                using RecursiveAI.OneDocument.Spreadsheets.Streaming.XlsbSheetCursor c = book.OpenSheet(0);
                while (c.ReadRow())
                {
                    while (c.ReadCell())
                    {
                        sum += c.Kind switch
                        {
                            RecursiveAI.OneDocument.Spreadsheets.CellValueKind.Number => c.IsDate && c.TryGetDateTime(out DateTime when) ? (long)when.ToOADate() : (long)c.Number,
                            RecursiveAI.OneDocument.Spreadsheets.CellValueKind.Boolean => c.Boolean ? 1 : 0,
                            RecursiveAI.OneDocument.Spreadsheets.CellValueKind.Text => c.GetText().Length,
                            _ => 0,
                        };
                    }
                }

                return sum;
            }

            case "sylvan.xls.read":
            case "sylvan.xlsb.read":
                return XlsxStreamBench.ReadRow("sylvan.read", target);
            case "exceldatareader.xls.read":
            case "exceldatareader.xlsb.read":
                return XlsxStreamBench.ReadRow("exceldatareader.read", target);
            case "npoi.xls.model":
            {
                long sum = 0;
                using FileStream fs = File.OpenRead(target);
                var wb = new HSSFWorkbook(fs);
                for (int i = 0; i < wb.NumberOfSheets; i++)
                {
                    foreach (IRow r in wb.GetSheetAt(i))
                    {
                        foreach (ICell c in r)
                        {
                            sum += c.CellType switch
                            {
                                CellType.Numeric => (long)c.NumericCellValue,
                                CellType.String => c.StringCellValue.Length,
                                CellType.Boolean => c.BooleanCellValue ? 1 : 0,
                                _ => 0,
                            };
                        }
                    }
                }

                return sum;
            }

            case "onedocument.xls.write":
            {
                int rows = int.Parse(target, CultureInfo.InvariantCulture);
                string path = tmp + ".xls";
                using (HubWorkbook wb = HubWorkbook.Create())
                {
                    RecursiveAI.OneDocument.Spreadsheets.Worksheet ws = wb.Sheets[0];
                    for (int r = 1; r <= rows; r++)
                    {
                        for (int c = 1; c <= Columns; c++)
                        {
                            switch (c % 4)
                            {
                                case 0:
                                    ws[r, c].SetBoolean(r % 2 == 0);
                                    break;
                                case 1:
                                    ws[r, c].SetString(Word(r, c));
                                    break;
                                default:
                                    ws[r, c].SetNumber(XlsxStreamBench.Number(r, c));
                                    break;
                            }
                        }
                    }

                    wb.SaveAs(path, RecursiveAI.OneDocument.OfficeFormat.Xls);
                }

                return Finish(path, out outBytes);
            }

            case "npoi.xls.write":
            {
                int rows = int.Parse(target, CultureInfo.InvariantCulture);
                string path = tmp + ".xls";
                var wb = new HSSFWorkbook();
                ISheet ws = wb.CreateSheet("Sheet1");
                for (int r = 1; r <= rows; r++)
                {
                    IRow row0 = ws.CreateRow(r - 1);
                    for (int c = 1; c <= Columns; c++)
                    {
                        ICell cell = row0.CreateCell(c - 1);
                        switch (c % 4)
                        {
                            case 0:
                                cell.SetCellValue(r % 2 == 0);
                                break;
                            case 1:
                                cell.SetCellValue(Word(r, c));
                                break;
                            default:
                                cell.SetCellValue(XlsxStreamBench.Number(r, c));
                                break;
                        }
                    }
                }

                using (FileStream o = File.Create(path))
                {
                    wb.Write(o, false);
                }

                return Finish(path, out outBytes);
            }

            case "onedocument.calc.cold":
            case "onedocument.calc.incr":
            {
                using HubWorkbook wb = HubWorkbook.Open(target);
                RecursiveAI.OneDocument.Spreadsheets.Worksheet ws = wb.Sheets[0];
                int n = CalcRows(target);
                if (row.EndsWith(".cold", StringComparison.Ordinal))
                {
                    m.Begin();
                    wb.Recalculate(new RecalcOptions { Full = true, MaxDegreeOfParallelism = 1 });
                }
                else
                {
                    wb.Recalculate(new RecalcOptions { Full = true, MaxDegreeOfParallelism = 1 });
                    ws[n / 2, 1].SetNumber(12345.5);
                    m.Begin();
                    wb.Recalculate(new RecalcOptions { MaxDegreeOfParallelism = 1 });
                }

                return (long)ws[n + 2, 3].Value.Number + (long)ws[n + 2, 7].Value.Number;
            }

            case "closedxml.calc.cold":
            case "closedxml.calc.incr":
            {
                using var wb = new XLWorkbook(target);
                IXLWorksheet ws = wb.Worksheet(1);
                int n = CalcRows(target);
                if (row.EndsWith(".cold", StringComparison.Ordinal))
                {
                    m.Begin();
                    wb.RecalculateAllFormulas();
                }
                else
                {
                    wb.RecalculateAllFormulas();
                    ws.Cell(n / 2, 1).Value = 12345.5;
                    m.Begin();
                    // ClosedXML marks dependants dirty on a write and evaluates them when a value is read.
                    _ = ws.Cell(n + 2, 3).Value;
                    _ = ws.Cell(n + 2, 7).Value;
                }

                return (long)ws.Cell(n + 2, 3).Value.GetNumber() + (long)ws.Cell(n + 2, 7).Value.GetNumber();
            }

            case "npoi.calc.cold":
            case "npoi.calc.incr":
            {
                using FileStream fs = File.OpenRead(target);
                var wb = new XSSFWorkbook(fs);
                ISheet ws = wb.GetSheetAt(0);
                int n = CalcRows(target);
                var ev = new XSSFFormulaEvaluator(wb);
                if (row.EndsWith(".cold", StringComparison.Ordinal))
                {
                    m.Begin();
                    ev.EvaluateAll();
                }
                else
                {
                    ev.EvaluateAll();
                    ICell edited = ws.GetRow(n / 2 - 1).GetCell(0);
                    edited.SetCellValue(12345.5);
                    ev.NotifyUpdateCell(edited);
                    m.Begin();
                    ev.EvaluateFormulaCell(ws.GetRow(n + 1).GetCell(2));
                    ev.EvaluateFormulaCell(ws.GetRow(n + 1).GetCell(6));
                }

                return (long)ws.GetRow(n + 1).GetCell(2).NumericCellValue + (long)ws.GetRow(n + 1).GetCell(6).NumericCellValue;
            }

            case "epplus.calc.cold":
            case "epplus.calc.incr":
            {
                using var pkg = new ExcelPackage(new FileInfo(target));
                ExcelWorksheet ws = pkg.Workbook.Worksheets.First();
                int n = CalcRows(target);
                if (row.EndsWith(".cold", StringComparison.Ordinal))
                {
                    m.Begin();
                    pkg.Workbook.Calculate();
                }
                else
                {
                    pkg.Workbook.Calculate();
                    ws.Cells[n / 2, 1].Value = 12345.5;
                    m.Begin();
                    // EPPlus 4.5.3.3 has no dirty tracking: a recalculation is a full one.
                    pkg.Workbook.Calculate();
                }

                return Convert.ToInt64(ws.Cells[n + 2, 3].Value, CultureInfo.InvariantCulture) + Convert.ToInt64(ws.Cells[n + 2, 7].Value, CultureInfo.InvariantCulture);
            }

            case "onedocument.pptx.load":
            {
                using HubPresentation deck = HubPresentation.Open(target);
                return deck.Slides[0].Title?.Length ?? 0;
            }

            case "openxml.pptx.load":
            {
                using var doc = PresentationDocument.Open(target, false);
                SlidePart first = doc.PresentationPart!.SlideParts.First();
                return first.Slide?.InnerText.Length ?? 0;
            }

            case "onedocument.pptx.loadsave.mem":
            {
                using HubPresentation deck = HubPresentation.Open(target);
                using var ms = new MemoryStream();
                deck.SaveAs(ms, RecursiveAI.OneDocument.OfficeFormat.Pptx);
                return ms.Length;
            }

            case "openxml.pptx.loadsave.mem":
            {
                using var doc = PresentationDocument.Open(target, false);
                using var ms = new MemoryStream();
                doc.Clone(ms, false).Dispose();
                return ms.Length;
            }

            case "onedocument.pptx.edit.mem":
            {
                using HubPresentation deck = HubPresentation.Open(target);
                RecursiveAI.OneDocument.Presentations.Slide sl = deck.Slides[deck.Slides.Count / 2];
                foreach (RecursiveAI.OneDocument.Presentations.Shape sh in sl.Shapes)
                {
                    if (sh.TextBody is { } tb)
                    {
                        tb.SetText("Edited by the benchmark");
                        break;
                    }
                }

                using var ms = new MemoryStream();
                deck.SaveAs(ms, RecursiveAI.OneDocument.OfficeFormat.Pptx);
                return ms.Length;
            }

            case "openxml.pptx.edit.mem":
            {
                using var ms = new MemoryStream();
                using (FileStream f = File.OpenRead(target))
                {
                    f.CopyTo(ms);
                }

                using (var doc = PresentationDocument.Open(ms, true))
                {
                    SlidePart[] parts = [.. doc.PresentationPart!.SlideParts];
                    DocumentFormat.OpenXml.Drawing.Text t = parts[parts.Length / 2].Slide!.Descendants<DocumentFormat.OpenXml.Drawing.Text>().First();
                    t.Text = "Edited by the benchmark";
                    parts[parts.Length / 2].Slide!.Save();
                }

                return ms.Length;
            }

            case "onedocument.pptx.loadsave":
            {
                string path = tmp + ".pptx";
                using (HubPresentation deck = HubPresentation.Open(target))
                {
                    deck.SaveAs(path);
                }

                return Finish(path, out outBytes);
            }

            case "openxml.pptx.loadsave":
            {
                string path = tmp + ".pptx";
                using (var doc = PresentationDocument.Open(target, false))
                {
                    doc.Clone(path, false).Dispose();
                }

                return Finish(path, out outBytes);
            }

            case "onedocument.pptx.edit":
            {
                string path = tmp + ".pptx";
                using (HubPresentation deck = HubPresentation.Open(target))
                {
                    RecursiveAI.OneDocument.Presentations.Slide s = deck.Slides[deck.Slides.Count / 2];
                    foreach (RecursiveAI.OneDocument.Presentations.Shape sh in s.Shapes)
                    {
                        if (sh.TextBody is { } tb)
                        {
                            tb.SetText("Edited by the benchmark");
                            break;
                        }
                    }

                    deck.SaveAs(path);
                }

                return Finish(path, out outBytes);
            }

            case "openxml.pptx.edit":
            {
                string path = tmp + ".pptx";
                File.Copy(target, path, true);
                using (var doc = PresentationDocument.Open(path, true))
                {
                    SlidePart[] parts = [.. doc.PresentationPart!.SlideParts];
                    SlidePart s = parts[parts.Length / 2];
                    DocumentFormat.OpenXml.Drawing.Text t = s.Slide!.Descendants<DocumentFormat.OpenXml.Drawing.Text>().First();
                    t.Text = "Edited by the benchmark";
                    s.Slide.Save();
                }

                return Finish(path, out outBytes);
            }

            case "onedocument.pptx.extract":
            case "onedocument.xlsx.extract":
            case "onedocument.doc.extract":
            case "onedocument.rtf.extract":
            case "onedocument.odt.extract":
            {
                var counter = new CountingWriter();
                RecursiveAI.OneDocument.Office.ExtractText(target, counter);
                return counter.Count;
            }

            case "openxml.pptx.extract":
            {
                long sum = 0;
                using var doc = PresentationDocument.Open(target, false);
                foreach (SlidePart sp in doc.PresentationPart!.SlideParts)
                {
                    using OpenXmlReader reader = OpenXmlReader.Create(sp);
                    while (reader.Read())
                    {
                        if (reader.ElementType == typeof(DocumentFormat.OpenXml.Drawing.Text) && reader.IsStartElement)
                        {
                            sum += reader.LoadCurrentElement()!.InnerText.Length;
                        }
                    }
                }

                return sum;
            }

            case "openxml.xlsx.extract":
            {
                long sum = 0;
                using var doc = SpreadsheetDocument.Open(target, false);
                WorkbookPart wbp = doc.WorkbookPart!;
                string[] shared = [];
                if (wbp.SharedStringTablePart is { } sst)
                {
                    var list = new List<string>();
                    using OpenXmlReader sr = OpenXmlReader.Create(sst);
                    while (sr.Read())
                    {
                        if (sr.ElementType == typeof(DocumentFormat.OpenXml.Spreadsheet.SharedStringItem) && sr.IsStartElement)
                        {
                            list.Add(sr.LoadCurrentElement()!.InnerText);
                        }
                    }

                    shared = [.. list];
                }

                foreach (WorksheetPart wsp in wbp.WorksheetParts)
                {
                    using OpenXmlReader reader = OpenXmlReader.Create(wsp);
                    while (reader.Read())
                    {
                        if (reader.ElementType == typeof(DocumentFormat.OpenXml.Spreadsheet.Cell) && reader.IsStartElement)
                        {
                            var cell = (DocumentFormat.OpenXml.Spreadsheet.Cell)reader.LoadCurrentElement()!;
                            string? v = cell.CellValue?.Text;
                            if (v is null)
                            {
                                continue;
                            }

                            sum += cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString ? shared[int.Parse(v, CultureInfo.InvariantCulture)].Length : v.Length;
                        }
                    }
                }

                return sum;
            }

            case "npoi.xlsx.extract":
            {
                using FileStream fs = File.OpenRead(target);
                var wb = new XSSFWorkbook(fs);
                return new NPOI.XSSF.Extractor.XSSFExcelExtractor(wb).Text.Length;
            }

            case "npoi.xls.extract":
            {
                using FileStream fs = File.OpenRead(target);
                var wb = new HSSFWorkbook(fs);
                return new NPOI.HSSF.Extractor.ExcelExtractor(wb).Text.Length;
            }

            case "onedocument.tpl.xlsx":
            case "onedocument.tpl.docx":
            {
                int n = TemplateCount(target);
                string path = tmp + (row.EndsWith(".docx", StringComparison.Ordinal) ? ".docx" : ".xlsx");
                var data = TemplateData(n);
                m.Begin();
                RecursiveAI.OneDocument.Template.Render(target, data, path);
                return Finish(path, out outBytes);
            }

            case "closedxmlreport.tpl.xlsx":
            {
                int n = TemplateCount(target);
                string path = tmp + ".xlsx";
                IList<Item> items = [.. Enumerable.Range(0, n).Select(i => new Item(Word(i, 1), i % 17, i * 0.25))];
                m.Begin();
                using (var tpl = new ClosedXML.Report.XLTemplate(target))
                {
                    tpl.AddVariable("items", items);
                    tpl.Generate();
                    tpl.SaveAs(path);
                }

                return Finish(path, out outBytes);
            }

            case "onedocument.render.docx":
            {
                string path = tmp + ".pdf";
                RecursiveAI.OneDocument.Office.Convert(target, path);
                return Finish(path, out outBytes);
            }

            default:
                throw new ArgumentException("unknown row " + row);
        }
    }

    /// <summary>The item the report-template comparator binds (public members, read by name).</summary>
    public sealed record Item(string Name, int Qty, double Price);

    private static Dictionary<string, object?> TemplateData(int n) => new()
    {
        ["title"] = "Autumn",
        ["items"] = Enumerable.Range(0, n).Select(i => new Dictionary<string, object?> { ["name"] = Word(i, 1), ["qty"] = (double)(i % 17), ["price"] = i * 0.25 }).ToList(),
    };

    private static int TemplateCount(string target)
    {
        // File names carry the count: tpl-xlsx-1000.template.xlsx.
        string name = Path.GetFileName(target);
        string[] parts = name.Split('-', '.');
        return int.Parse(parts[2], CultureInfo.InvariantCulture);
    }

    private static int CalcRows(string target) => int.Parse(Path.GetFileNameWithoutExtension(target).Split('-')[1], CultureInfo.InvariantCulture);

    private static string Word(int r, int c) => "word" + ((r * 7 + c) % 1000).ToString(CultureInfo.InvariantCulture);

    private static long Finish(string path, out long bytes)
    {
        bytes = new FileInfo(path).Length;
        File.Delete(path);
        return bytes;
    }

    /// <summary>Counts characters written, so extraction rows measure the work without holding the text.</summary>
    private sealed class CountingWriter : TextWriter
    {
        public long Count { get; private set; }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => Count++;

        public override void Write(char[] buffer, int index, int count) => Count += count;

        public override void Write(ReadOnlySpan<char> buffer) => Count += buffer.Length;

        public override void Write(string? value) => Count += value?.Length ?? 0;
    }
}
