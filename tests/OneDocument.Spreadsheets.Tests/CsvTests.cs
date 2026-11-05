using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Spreadsheets.Csv;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class CsvTests
{
    private static List<string[]> Read(string text, CsvReadOptions? options = null, OpenContext? ctx = null)
    {
        using var r = new CsvReader(new StringReader(text), options, ctx);
        var rows = new List<string[]>();
        while (r.ReadRecord())
        {
            var f = new string[r.FieldCount];
            for (int i = 0; i < f.Length; i++)
            {
                f[i] = r.GetField(i).ToString();
            }

            rows.Add(f);
        }

        return rows;
    }

    private static string[] Codes(OpenContext ctx) => [.. ctx.Defects is DefectCollection d ? d.Select(x => x.Code) : []];

    [Fact]
    public void Rfc4180Basics()
    {
        List<string[]> rows = Read("a,b,c\r\n1,\"x,y\",\"he said \"\"hi\"\"\"\r\n\"line1\r\nline2\",,\r\n");
        Assert.Equal(3, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["1", "x,y", "he said \"hi\""], rows[1]);
        Assert.Equal(["line1\r\nline2", "", ""], rows[2]);
    }

    [Theory]
    [InlineData("a,b\nc,d", 2)]
    [InlineData("a,b\rc,d\r", 2)]
    [InlineData("a,b\r\n\r\nc,d", 3)]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("a,", 1)]
    public void LineEndings(string text, int records)
    {
        Assert.Equal(records, Read(text).Count);
    }

    [Fact]
    public void TrailingDelimiterGivesEmptyLastField()
    {
        Assert.Equal(["a", ""], Read("a,")[0]);
        Assert.Equal(["", ""], Read(",")[0]);
    }

    [Fact]
    public void RaggedAndStrayQuotesAreDefects()
    {
        var ctx = new OpenContext();
        List<string[]> rows = Read("a,b\n1,2,3\n\"x\"y,\"open", ctx: ctx);
        Assert.Equal(["1", "2", "3"], rows[1]);
        Assert.Equal(["xy", "open"], rows[2]);
        Assert.Contains("csv.ragged-record", Codes(ctx));
        Assert.Contains("csv.stray-quote", Codes(ctx));
        Assert.Contains("csv.quote-unterminated", Codes(ctx));
        Defect d = ((DefectCollection)ctx.Defects).First(x => x.Code == "csv.ragged-record");
        Assert.Equal(DefectLocationKind.Text, d.Location.Kind);
        Assert.Equal(4, d.Location.Offset);
    }

    [Fact]
    public void QuoteInsideUnquotedFieldIsText()
    {
        Assert.Equal(["5\" pipe", "x"], Read("5\" pipe,x")[0]);
    }

    [Theory]
    [InlineData("a;b;c\n1;2;3\n", ';')]
    [InlineData("a\tb\tc\n1\t2\t3\n", '\t')]
    [InlineData("a|b|c\n1|2|3\n", '|')]
    [InlineData("a,b\n\"1;2;3\",4\n", ',')]
    [InlineData("just text\n", ',')]
    public void DelimiterIsDetected(string text, char expected)
    {
        using var r = new CsvReader(new StringReader(text));
        Assert.True(r.ReadRecord());
        Assert.Equal(expected, r.Delimiter);
    }

    [Fact]
    public void EncodingsAreRead()
    {
        byte[] utf8 = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("é,€\n1,2\n")];
        byte[] le = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("é,€\n1,2\n")];
        byte[] be = [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes("é,€\n1,2\n")];
        byte[] plain = Encoding.UTF8.GetBytes("é,€\n1,2\n");
        foreach (byte[] b in new[] { utf8, le, be, plain })
        {
            using var r = new CsvReader(new MemoryStream(b));
            Assert.True(r.ReadRecord());
            Assert.Equal("é", r.GetField(0).ToString());
            Assert.Equal("€", r.GetField(1).ToString());
        }
    }

    [Fact]
    public void LegacyBytesReadAsWindows1252WithADefect()
    {
        var ctx = new OpenContext();
        byte[] b = [0x63, 0x61, 0x66, 0xE9, 0x2C, 0x80, 0x0A, 0x31, 0x2C, 0x32, 0x0A];
        using var r = new CsvReader(new MemoryStream(b), context: ctx);
        Assert.True(r.ReadRecord());
        Assert.Equal("café", r.GetField(0).ToString());
        Assert.Equal("€", r.GetField(1).ToString());
        Assert.Contains("csv.encoding-assumed", Codes(ctx));
    }

    [Fact]
    public void InvalidBytesPastTheFirstBlockAreReplacedWithADefect()
    {
        var ctx = new OpenContext();
        byte[] b = [.. Encoding.ASCII.GetBytes(new string('a', 70000)), 0xFF, 0x0A, 0x62];
        // A seekable stream is checked whole, so the late byte makes the file Windows-1252; a forward-only stream is decided by its first block.
        using (var seekable = new CsvReader(new MemoryStream(b), context: new OpenContext()))
        {
            Assert.True(seekable.ReadRecord());
            Assert.EndsWith("\u00FF", seekable.GetField(0).ToString());
        }

        using var r = new CsvReader(new ForwardOnly(b), context: ctx);
        Assert.True(r.ReadRecord());
        Assert.EndsWith("\uFFFD", r.GetField(0).ToString());
        Assert.Contains("csv.encoding-invalid", Codes(ctx));
        var ctx2 = new OpenContext();
        using var r2 = new CsvReader(new MemoryStream([0xFF, 0xFE, 0x61, 0x00, 0x62]), context: ctx2);
        Assert.True(r2.ReadRecord());
        Assert.Equal("a", r2.GetField(0).ToString());
        Assert.Contains("csv.encoding-invalid", Codes(ctx2));
    }

    private sealed class ForwardOnly(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    [Fact]
    public void SepLineNamesTheDelimiter()
    {
        using var r = new CsvReader(new StringReader("sep=;\r\na;b\r\n1;2\r\n"));
        Assert.True(r.ReadRecord());
        Assert.Equal(';', r.Delimiter);
        Assert.Equal(["a", "b"], new[] { r.GetField(0).ToString(), r.GetField(1).ToString() });
    }

    [Fact]
    public void ImportDropsWhatDoesNotFitTheGrid()
    {
        var ctx = new OpenContext();
        string row = string.Join(',', Enumerable.Repeat("x", CellAddress.MaxColumns + 3));
        using Workbook wb = CsvDocument.ToWorkbook(new MemoryStream(Encoding.UTF8.GetBytes(row + "\n1,2\n")), null, ctx);
        Assert.Contains("csv.grid-exceeded", Codes(ctx));
        Assert.Equal(CellAddress.MaxColumns + 2, wb.Sheets[0].Store.CellCount);
    }

    [Fact]
    public void CallerEncodingIsUsed()
    {
        byte[] b = [0xE9, 0x2C, 0xE8, 0x0A, 0x31, 0x2C, 0x32];
        using var r = new CsvReader(new MemoryStream(b), new CsvReadOptions { Encoding = Encoding.Latin1 });
        Assert.True(r.ReadRecord());
        Assert.Equal("é", r.GetField(0).ToString());
        Assert.Equal("è", r.GetField(1).ToString());
    }

    [Fact]
    public void RecordsSpanBufferRefills()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 30000; i++)
        {
            sb.Append("row").Append(i).Append(",\"multi\nline ").Append(i).Append("\",x\r\n");
        }

        using var r = new CsvReader(new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())));
        int n = 0;
        while (r.ReadRecord())
        {
            Assert.Equal(3, r.FieldCount);
            Assert.Equal("row" + n, r.GetField(0).ToString());
            Assert.Equal("multi\nline " + n, r.GetField(1).ToString());
            n++;
        }

        Assert.Equal(30000, n);
    }

    [Fact]
    public void ReadingFieldsAllocatesNothingPerField()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 2000; i++)
        {
            sb.Append("abc,\"q,uo\"\"ted\",12345,hello world\r\n");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        long Run()
        {
            using var r = new CsvReader(new MemoryStream(bytes));
            long total = 0;
            while (r.ReadRecord())
            {
                for (int i = 0; i < r.FieldCount; i++)
                {
                    total += r.GetField(i).Length;
                }
            }

            return total;
        }

        Run();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long t = Run();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(t > 0);
        // The fixed cost is the reader, its buffers and the stream; 8,000 records of four fields must not add to it.
        Assert.True(allocated < 400_000, "allocated " + allocated);
    }

    [Fact]
    public void HugeRecordHitsTheAllocationLimit()
    {
        var ctx = new OpenContext(new OfficeLimits { MaxAllocationBytes = 4096 });
        string text = "\"" + new string('x', 10000) + "\"\n";
        var ex = Assert.Throws<OfficeLimitException>(() => Read(text, ctx: ctx));
        Assert.Equal(nameof(OfficeLimits.MaxAllocationBytes), ex.LimitName);
    }

    // ---- values ----

    private static readonly CsvValueParser Parser = new();

    [Theory]
    [InlineData("42", CsvValueKind.Number, 42.0)]
    [InlineData("-3.5", CsvValueKind.Number, -3.5)]
    [InlineData("+7", CsvValueKind.Number, 7.0)]
    [InlineData("1,234,567", CsvValueKind.Number, 1234567.0)]
    [InlineData("1,234.50", CsvValueKind.Number, 1234.5)]
    [InlineData("$1,234.50", CsvValueKind.Number, 1234.5)]
    [InlineData("(5)", CsvValueKind.Number, -5.0)]
    [InlineData("--5", CsvValueKind.Number, 5.0)]
    [InlineData("1,2345", CsvValueKind.Number, 12345.0)]
    [InlineData("%5", CsvValueKind.Percent, 0.05)]
    [InlineData("-$5", CsvValueKind.Number, -5.0)]
    [InlineData("1E3", CsvValueKind.Number, 1000.0)]
    [InlineData("1.5e-2", CsvValueKind.Number, 0.015)]
    [InlineData(" 12 ", CsvValueKind.Number, 12.0)]
    [InlineData(".5", CsvValueKind.Number, 0.5)]
    [InlineData("12%", CsvValueKind.Percent, 0.12)]
    [InlineData("12.5%", CsvValueKind.Percent, 0.125)]
    [InlineData("TRUE", CsvValueKind.Boolean, 1.0)]
    [InlineData("false", CsvValueKind.Boolean, 0.0)]
    [InlineData("2020-01-15", CsvValueKind.Date, 43845.0)]
    [InlineData("1/15/2020", CsvValueKind.Date, 43845.0)]
    [InlineData("15-Jan-2020", CsvValueKind.Date, 43845.0)]
    [InlineData("Jan 15, 2020", CsvValueKind.Date, 43845.0)]
    [InlineData("January 15, 2020", CsvValueKind.Date, 43845.0)]
    [InlineData("15 Jan 20", CsvValueKind.Date, 43845.0)]
    [InlineData("Jan 2020", CsvValueKind.Date, 43831.0)]
    [InlineData("2020-01-15 12:00", CsvValueKind.DateTime, 43845.5)]
    [InlineData("2020-01-15 06:00:00", CsvValueKind.DateTime, 43845.25)]
    [InlineData("25:30", CsvValueKind.Time, 1.0625)]
    [InlineData("1:45 P", CsvValueKind.Time, 0.57291666666666663)]
    [InlineData("12:00", CsvValueKind.Time, 0.5)]
    [InlineData("6:00 PM", CsvValueKind.Time, 0.75)]
    [InlineData("12:00 AM", CsvValueKind.Time, 0.0)]
    [InlineData("01:30:00", CsvValueKind.Time, 0.0625)]
    public void ValuesAreInferred(string text, CsvValueKind kind, double number)
    {
        CsvValue v = Parser.Parse(text);
        Assert.Equal(kind, v.Kind);
        Assert.Equal(number, v.Number, 1e-12);
    }

    [Theory]
    [InlineData("007")]
    [InlineData("-007")]
    [InlineData("12345678901234567890")]
    [InlineData("1,23")]
    [InlineData("5-")]
    [InlineData("(-5)")]
    [InlineData("5$")]
    [InlineData(" TRUE")]
    [InlineData("0,123")]
    [InlineData("1,234,56")]
    [InlineData("4.9E-324")]
    [InlineData("12abc")]
    [InlineData("1/2")]
    [InlineData("Jan 5")]
    [InlineData("13/13/2020")]
    [InlineData("2/30/2020")]
    [InlineData("2020-01-15T06:00:00")]
    [InlineData("1:45PM")]
    [InlineData("Jan 2 2020")]
    [InlineData("99-01-02")]
    [InlineData("1.2.3")]
    [InlineData("-")]
    [InlineData("%")]
    [InlineData("e5")]
    [InlineData("0x10")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    [InlineData("1e999")]
    public void LookAlikesStayText(string text)
    {
        Assert.Equal(CsvValueKind.Text, Parser.Parse(text).Kind);
    }

    [Fact]
    public void LeadingZerosAndLongNumbersCanFollowExcel()
    {
        var excel = new CsvValueParser(new CsvValueOptions { PreserveLeadingZeros = false, PreserveLongNumbers = false });
        Assert.Equal(7, excel.Parse("007").Number);
        Assert.Equal(CsvValueKind.Number, excel.Parse("12345678901234567890").Kind);
    }

    [Fact]
    public void CultureControlsSeparatorsAndDateOrder()
    {
        var de = new CsvValueParser(new CsvValueOptions { Culture = NumberFormatCulture.Get(0x0407) });
        Assert.Equal(1234.5, de.Parse("1.234,5").Number);
        Assert.Equal(43845.0, de.Parse("15.01.2020").Number);
        Assert.Equal(43845.0, de.Parse("15. Januar 2020").Number);
        Assert.Equal(CsvValueKind.Text, new CsvValueParser(new CsvValueOptions { Infer = false }).Parse("12").Kind);
    }

    [Fact]
    public void ErrorValues()
    {
        CsvValue v = Parser.Parse("#N/A");
        Assert.Equal(CsvValueKind.Error, v.Kind);
        Assert.Equal(CellError.NotAvailable, v.Error);
        Assert.Equal(CsvValueKind.Text, Parser.Parse("#nonsense").Kind);
    }

    [Fact]
    public void ParsingAllocatesNothing()
    {
        string[] samples = ["12345", "1,234.50", "2020-01-15", "Jan 15, 2020", "12:30:15", "hello", "TRUE", "12.5%"];
        long sum = 0;
        foreach (string s in samples)
        {
            sum += (long)Parser.Parse(s).Kind;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            foreach (string s in samples)
            {
                sum += (long)Parser.Parse(s).Kind;
            }
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(sum > 0);
    }

    // ---- writing ----

    private static string Write(Action<CsvWriter> body, CsvWriteOptions? options = null)
    {
        var sw = new StringWriter();
        using (var w = new CsvWriter(sw, options))
        {
            body(w);
        }

        return sw.ToString();
    }

    [Fact]
    public void WriterQuotesMinimally()
    {
        string s = Write(w =>
        {
            w.WriteField("plain");
            w.WriteField("a,b");
            w.WriteField("say \"hi\"");
            w.WriteField("two\nlines");
            w.WriteField("");
            w.WriteField(" padded ");
            w.EndRecord();
        });
        Assert.Equal("plain,\"a,b\",\"say \"\"hi\"\"\",\"two\nlines\",, padded \r\n", s);
    }

    [Fact]
    public void WriterEscapesFormulaTextOnlyForText()
    {
        string s = Write(w =>
        {
            w.WriteField("=1+1");
            w.WriteField("-5", isText: false);
            w.WriteField("+49 123");
            w.WriteField("@x");
            w.WriteField("fine");
            w.EndRecord();
        });
        Assert.Equal("\"'=1+1\",-5,\"'+49 123\",\"'@x\",fine\r\n", s);
        string raw = Write(w => { w.WriteField("=1+1"); w.EndRecord(); }, new CsvWriteOptions { FormulaInjection = CsvFormulaInjection.None });
        Assert.Equal("=1+1\r\n", raw);
    }

    [Fact]
    public void WriterOptions()
    {
        string s = Write(w => { w.WriteField("a"); w.WriteField("b c"); w.EndRecord(); },
            new CsvWriteOptions { Delimiter = '\t', LineEnding = "\n", Quoting = CsvQuoting.Always });
        Assert.Equal("\"a\"\t\"b c\"\n", s);
        Assert.Throws<ArgumentException>(() => new CsvWriter(new StringWriter(), new CsvWriteOptions { Delimiter = '"' }));
    }

    [Fact]
    public void WriterEncodingsAndByteOrderMark()
    {
        var ms = new MemoryStream();
        using (var w = new CsvWriter(ms, new CsvWriteOptions()))
        {
            w.WriteField("é");
            w.EndRecord();
        }

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, 0xC3, 0xA9, 0x0D, 0x0A }, ms.ToArray());
        var ms2 = new MemoryStream();
        using (var w = new CsvWriter(ms2, new CsvWriteOptions { ByteOrderMark = false }))
        {
            w.WriteField("é");
        }

        Assert.Equal(new byte[] { 0xC3, 0xA9 }, ms2.ToArray());
        var ms3 = new MemoryStream();
        using (var w = new CsvWriter(ms3, new CsvWriteOptions { Encoding = Encoding.Unicode }))
        {
            w.WriteField("a");
        }

        Assert.Equal(new byte[] { 0xFF, 0xFE, 0x61, 0x00 }, ms3.ToArray());
    }

    [Fact]
    public void WriteThenReadGivesTheSameFields()
    {
        string[][] data =
        [
            ["a", "b,c", "d\"e", "f\ng", "", "  ", "é€"],
            ["", "", "", "", "", "", ""],
            ["=x", "1", "2", "3", "4", "5", "6"],
        ];
        string text = Write(w =>
        {
            foreach (string[] row in data)
            {
                foreach (string f in row)
                {
                    w.WriteField(f, isText: false);
                }

                w.EndRecord();
            }
        });
        List<string[]> back = Read(text);
        Assert.Equal(data.Length, back.Count);
        for (int i = 0; i < data.Length; i++)
        {
            Assert.Equal(data[i], back[i]);
        }
    }

    // ---- the workbook ----

    [Fact]
    public void WorkbookOpenSniffsCsvAndTypesTheCells()
    {
        string path = Path.Combine(Path.GetTempPath(), "csv-workbook-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            File.WriteAllText(path, "name,qty,price,when,pct,zip\nbolt,10,\"1,234.50\",2020-01-15,12.5%,00501\nnut,,TRUE,#N/A,x,\n", new UTF8Encoding(true));
            using Workbook wb = Workbook.Open(path);
            Worksheet s = wb.Sheets[0];
            Assert.Single(wb.Sheets);
            Assert.Equal("name", s["A1"].Text);
            Assert.Equal(10, s["B2"].Number);
            Assert.Equal(1234.5, s["C2"].Number);
            Assert.Equal(43845, s["D2"].Number);
            Assert.NotEqual(0, s["D2"].StyleIndex);
            Assert.Equal(0.125, s["E2"].Number, 1e-12);
            Assert.Equal("00501", s["F2"].Text);
            Assert.Equal(CellValueKind.Empty, s["B3"].Value.Kind);
            Assert.True(s["C3"].Value.Boolean);
            Assert.Equal(CellError.NotAvailable, s["D3"].Value.Error);
            // saving as XLSX and reopening keeps the values
            var ms = new MemoryStream();
            wb.SaveAs(ms, OfficeFormat.Xlsx);
            ms.Position = 0;
            using Workbook again = Workbook.Open(ms);
            Assert.Equal(43845, again.Sheets[0]["D2"].Number);
            Assert.Equal("00501", again.Sheets[0]["F2"].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SheetWritesAsFormattedOrRawTextWithALossReport()
    {
        using Workbook wb = Workbook.Create();
        Worksheet s = wb.Sheets[0];
        s["A1"].SetString("=danger");
        s["B1"].SetNumber(0.1 + 0.2);
        s["A3"].SetBoolean(true);
        s["C3"].SetNumber(43845);
        s["C3"].StyleIndex = 0;
        s["B3"].Formula = "1+1";
        s["B3"].SetNumber(2);
        wb.Sheets.Add("Second");
        var loss = new LossReport();
        var ms = new MemoryStream();
        CsvDocument.Write(wb, ms, new CsvWriteOptions { ByteOrderMark = false }, loss);
        Assert.Equal("\"'=danger\",0.3\r\n\r\nTRUE,2,43845\r\n", Encoding.UTF8.GetString(ms.ToArray()));
        Assert.Equal(1, loss.CountOf("loss.formulas"));
        Assert.Equal(1, loss.CountOf("loss.sheets"));
        Assert.False(loss.IsLossless);
        var raw = new MemoryStream();
        CsvDocument.Write(wb, raw, new CsvWriteOptions { ByteOrderMark = false, Formatted = false });
        Assert.Contains("0.30000000000000004", Encoding.UTF8.GetString(raw.ToArray()));
    }

    [Fact]
    public void RegistryWritesCsvAndTsv()
    {
        using Workbook wb = Workbook.Create();
        wb.Sheets[0]["A1"].SetString("x");
        wb.Sheets[0]["B1"].SetNumber(2);
        var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Tsv);
        Assert.Equal("x\t2\r\n", Encoding.UTF8.GetString(ms.ToArray()).TrimStart('\uFEFF'));
    }

    [Fact]
    public void LossReportAddsCountsPerCode()
    {
        var l = new LossReport();
        Assert.True(l.IsLossless);
        l.Report("loss.x", "x", 2, "first");
        l.Report("loss.x", "x", 3, "second");
        Assert.Equal(5, l.CountOf("loss.x"));
        Assert.Equal("first", l.Items[0].Message);
        Assert.Single(l.Items);
    }
}
