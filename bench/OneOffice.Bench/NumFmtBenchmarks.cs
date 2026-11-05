using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using ClosedXML.Excel;
using NPOI.SS.UserModel;
using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Bench;

/// <summary>
/// Formatting a double with a code that is already parsed: rows <c>oneoffice.numfmt.*</c>. Each invocation formats the same 1,000 values
/// (numbers, or date-times for the date rows). The comparators go through their public "formatted value" calls: NPOI's
/// <c>DataFormatter.FormatRawCellContents</c> (which keeps its parsed formats between calls) and ClosedXML's
/// <c>IXLCell.GetFormattedString</c> (which also needs the value stored in the cell first, so its rows include that assignment).
/// </summary>
[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 1, iterationCount: 5)]
public class NumFmtBenchmarks : IDisposable
{
    private const int Count = 1000;
    private const string NumberCode = "#,##0.00";
    private const string DateCode = "yyyy-mm-dd hh:mm:ss";

    private readonly double[] _numbers = new double[Count];
    private readonly double[] _dates = new double[Count];
    private NumberFormat _number = NumberFormat.General;
    private NumberFormat _date = NumberFormat.General;
    private readonly char[] _buffer = new char[128];
    private DataFormatter _npoi = new();
    private XLWorkbook _book = new();
    private IXLCell _numberCell = null!;
    private IXLCell _dateCell = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rnd = new Random(12345);
        for (int i = 0; i < Count; i++)
        {
            _numbers[i] = Math.Round((rnd.NextDouble() - 0.3) * 2_000_000, 4);
            _dates[i] = 36526 + (rnd.NextDouble() * 20000);
        }

        _number = NumberFormat.Parse(NumberCode);
        _date = NumberFormat.Parse(DateCode);
        _npoi = new DataFormatter(System.Globalization.CultureInfo.InvariantCulture);
        _book = new XLWorkbook();
        IXLWorksheet ws = _book.AddWorksheet("s");
        _numberCell = ws.Cell(1, 1);
        _numberCell.Style.NumberFormat.Format = NumberCode;
        _dateCell = ws.Cell(1, 2);
        _dateCell.Style.NumberFormat.Format = DateCode;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _book.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark(OperationsPerInvoke = Count)]
    public long OneOfficeNumberString()
    {
        long n = 0;
        foreach (double v in _numbers)
        {
            n += _number.Format(v).Length;
        }

        return n;
    }

    [Benchmark(OperationsPerInvoke = Count)]
    public long OneOfficeNumberSpan()
    {
        long n = 0;
        foreach (double v in _numbers)
        {
            n += _number.TryFormat(v, _buffer).Length;
        }

        return n;
    }

    [Benchmark(OperationsPerInvoke = Count)]
    public long OneOfficeDateString()
    {
        long n = 0;
        foreach (double v in _dates)
        {
            n += _date.Format(v).Length;
        }

        return n;
    }

    [Benchmark(OperationsPerInvoke = Count)]
    public long OneOfficeDateSpan()
    {
        long n = 0;
        foreach (double v in _dates)
        {
            n += _date.TryFormat(v, _buffer).Length;
        }

        return n;
    }

    [Benchmark(OperationsPerInvoke = Count)]
    public long NpoiNumber()
    {
        long n = 0;
        foreach (double v in _numbers)
        {
            n += _npoi.FormatRawCellContents(v, 0, NumberCode).Length;
        }

        return n;
    }

    [Benchmark(OperationsPerInvoke = Count)]
    public long NpoiDate()
    {
        long n = 0;
        foreach (double v in _dates)
        {
            n += _npoi.FormatRawCellContents(v, 0, DateCode).Length;
        }

        return n;
    }

    [Benchmark(OperationsPerInvoke = Count)]
    public long ClosedXmlNumber()
    {
        long n = 0;
        foreach (double v in _numbers)
        {
            _numberCell.Value = v;
            n += _numberCell.GetFormattedString().Length;
        }

        return n;
    }

    [Benchmark(OperationsPerInvoke = Count)]
    public long ClosedXmlDate()
    {
        long n = 0;
        foreach (double v in _dates)
        {
            _dateCell.Value = v;
            n += _dateCell.GetFormattedString().Length;
        }

        return n;
    }
}
