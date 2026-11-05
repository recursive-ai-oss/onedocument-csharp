using System.Diagnostics;
using System.Globalization;
using ClosedXML.Excel;
using NPOI.XSSF.UserModel;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Bench;

/// <summary>
/// Memory per cell of a fully loaded workbook, for OneDocument and the managed libraries that load a whole model. Each measurement
/// runs in a fresh process (<c>bpc &lt;library&gt; &lt;file&gt;</c>): the file is read, every cell is touched through the library's public
/// API, the heap is collected twice, and what stays reachable is divided by the cell count. Timings printed beside it are
/// indicative only (one run, whatever load the host has).
/// </summary>
internal static class CellFootprint
{
    public static readonly string[] Libraries = ["onedocument", "closedxml", "npoi"];

    /// <summary>bpc &lt;library&gt; &lt;file&gt;: prints one TSV line: library, file, cells, retained bytes, bytes per cell, allocated bytes, load ms, peak working set.</summary>
    public static int Run(string[] args)
    {
        if (args.Length != 2 || !Libraries.Contains(args[0]))
        {
            Console.Error.WriteLine("usage: bpc <" + string.Join('|', Libraries) + "> <file.xlsx>");
            return 2;
        }

        string path = args[1];
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long heapBefore = GC.GetTotalMemory(true);
        long allocBefore = GC.GetTotalAllocatedBytes(true);
        var sw = Stopwatch.StartNew();
        (object model, long cells) = args[0] switch
        {
            "onedocument" => LoadOneDocument(path),
            "closedxml" => LoadClosedXml(path),
            _ => LoadNpoi(path),
        };
        double ms = sw.Elapsed.TotalMilliseconds;
        long allocated = GC.GetTotalAllocatedBytes(true) - allocBefore;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long retained = GC.GetTotalMemory(true) - heapBefore;
        using Process self = Process.GetCurrentProcess();
        GC.KeepAlive(model);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{args[0]}\t{Path.GetFileName(path)}\t{cells}\t{retained}\t{(cells == 0 ? 0 : (double)retained / cells):F1}\t{allocated}\t{ms:F0}\t{self.PeakWorkingSet64 >> 20}"));
        return 0;
    }

    private static (object, long) LoadOneDocument(string path)
    {
        Workbook wb = Workbook.Open(path);
        long cells = 0;
        foreach (Worksheet s in wb.Sheets)
        {
            if (s.Kind == SheetKind.Worksheet)
            {
                cells += s.Store.CellCount;
            }
        }

        return (wb, cells);
    }

    private static (object, long) LoadClosedXml(string path)
    {
        var wb = new XLWorkbook(path);
        long cells = 0;
        foreach (IXLWorksheet ws in wb.Worksheets)
        {
            foreach (IXLRow row in ws.RowsUsed())
            {
                cells += row.CellsUsed().Count();
            }
        }

        return (wb, cells);
    }

    private static (object, long) LoadNpoi(string path)
    {
        using FileStream fs = File.OpenRead(path);
        var wb = new XSSFWorkbook(fs);
        long cells = 0;
        for (int i = 0; i < wb.NumberOfSheets; i++)
        {
            NPOI.SS.UserModel.ISheet sheet = wb.GetSheetAt(i);
            foreach (NPOI.SS.UserModel.IRow row in sheet)
            {
                cells += row.PhysicalNumberOfCells;
            }
        }

        return (wb, cells);
    }

    /// <summary>bpcall &lt;inputsDir&gt; &lt;out.tsv&gt;: one fresh process per library and workbook.</summary>
    public static int RunAll(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: bpcall <inputsDir with .xlsx files> <out.tsv>");
            return 2;
        }

        using var output = new StreamWriter(args[1]);
        output.WriteLine("library\tfile\tcells\tretained_bytes\tbytes_per_cell\tallocated_bytes\tload_ms\tpeak_ws_mb");
        foreach (string file in Directory.EnumerateFiles(args[0], "*.xlsx").Order(StringComparer.Ordinal))
        {
            foreach (string lib in Libraries)
            {
                int exit = ChildProcess.Run(["bpc", lib, file], out string line, out long peakMb);
                string result = exit == 0 ? ChildProcess.WithPeak(line, 7, peakMb) : $"{lib}\t{Path.GetFileName(file)}\tFAILED\t\t\t\t\texit {exit}";
                output.WriteLine(result);
                output.Flush();
                Console.WriteLine(result);
            }
        }

        return 0;
    }
}
