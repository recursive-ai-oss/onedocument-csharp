using System.Text;
using RecursiveAI.OneDocument.Spreadsheets.Streaming;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

/// <summary>The sheet scanner and the full tokenizer must read the same cells from any well-formed sheet.</summary>
public class SheetScannerTests
{
    /// <summary>Hands out one byte per read, so every token straddles a refill.</summary>
    private sealed class Trickle(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, 1));
        public override int Read(Span<byte> buffer) => inner.Read(buffer[..Math.Min(buffer.Length, 1)]);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static string Dump(byte[] file, bool full)
    {
        using var r = XlsxStreamReader.Open(new MemoryStream(file));
        r.ForceFullTokenizer = full;
        var sb = new StringBuilder();
        using SheetCursor c = r.OpenSheet(0);
        while (c.ReadRow())
        {
            sb.Append('R').Append(c.RowNumber).Append(' ').Append(c.RowHeight).Append(c.IsRowHidden).Append(c.RowOutlineLevel).AppendLine();
            while (c.ReadCell())
            {
                sb.Append(c.Address).Append('|').Append(c.Style).Append('|').Append(c.Kind).Append('|').Append(c.Number).Append('|').Append(c.SharedStringIndex)
                    .Append('|').Append(c.Kind == CellValueKind.Text && c.SharedStringIndex < 0 ? c.GetText() : string.Empty)
                    .Append('|').Append(c.GetFormula()).Append('|').Append(c.FormulaKind).Append('|').Append(Encoding.UTF8.GetString(c.FormulaRangeUtf8)).AppendLine();
            }
        }

        return sb.ToString();
    }

    private const string Head = "<worksheet xmlns=\"" + TestBook.Main + "\"><sheetData>";

    public static TheoryData<string> Sheets => new()
    {
        TestBook.Sheet1Xml,
        "<?xml version='1.0'?><!-- c --><?pi x?>" + Head + "<row r='1'><c r='A1' t='inlineStr'><is><t><![CDATA[a<b]]></t></is></c><!-- gap --><c r='B1'><v>2</v></c></row></sheetData></worksheet>",
        "<x:worksheet xmlns:x=\"" + TestBook.Main + "\"><x:sheetData><x:row r=\"1\"><x:c r=\"A1\"><x:v>3</x:v></x:c><y:c xmlns:y=\"urn:y\" r=\"B1\"><y:v>9</y:v></y:c></x:row></x:sheetData></x:worksheet>",
        Head + "<row r=\"1\"><c r=\"A1\" t=\"str\"><f>IF(A2&gt;1,\"a&gt;b\",'x')</f><v>q &lt; r &amp;amp;</v></c><c r=\"B1\" t=\"e\"><v>#N/A</v></c><c r=\"C1\"/></row><row r=\"3\"/></sheetData></worksheet>",
        Head + "<row r=\"1\"><c r=\"A1\" t=\"str\"><v>line one\r\nline two\rthree &#13;four</v></c><c r=\"B1\" t=\"inlineStr\"><is><t>a\r\nb</t></is></c></row></sheetData></worksheet>",
        "\uFEFF" + Head + "<row r=\"2\"><c r=\"A2\" t=\"b\"><v>1</v></c></row></sheetData></worksheet>",
    };

    [Theory]
    [MemberData(nameof(Sheets))]
    public void ScannerAndFullTokenizerAgree(string sheet)
    {
        byte[] file = TestBook.Bytes(sheet);
        Assert.Equal(Dump(file, full: true), Dump(file, full: false));
    }

    [Fact]
    public void Utf16SheetFallsBackToFullTokenizer()
    {
        byte[] u16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Head + "<row r=\"1\"><c r=\"A1\"><v>5</v></c></row></sheetData></worksheet>")).ToArray();
        byte[] file = TestBook.Bytes(sheet1Bytes: u16);
        string d = Dump(file, full: false);
        Assert.Contains("A1|0|Number|5", d);
    }

    [Fact]
    public void TokensStraddlingRefillsAreRead()
    {
        byte[] file = TestBook.Bytes();
        Assert.Equal(Dump(file, full: true), Dump(file, full: false));
        static string Elements(SheetXml x)
        {
            var sb = new StringBuilder();
            while (x.Read())
            {
                if (x.Kind is Markup.Xml.XmlTokenKind.StartElement or Markup.Xml.XmlTokenKind.EmptyElement or Markup.Xml.XmlTokenKind.EndElement)
                {
                    sb.Append((int)x.Kind).Append(':').Append(x.Depth).Append(':').Append(Encoding.UTF8.GetString(x.Prefix)).Append(':').Append(Encoding.UTF8.GetString(x.LocalName));
                    foreach (SheetAttribute a in x.Attributes)
                    {
                        sb.Append(' ').Append(Encoding.UTF8.GetString(a.LocalName)).Append('=').Append(Encoding.UTF8.GetString(a.RawValue));
                    }

                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        byte[] xmlBytes = Encoding.UTF8.GetBytes(TestBook.Sheet1Xml);
        string trickled = Elements(new SheetXml(new Trickle(new MemoryStream(xmlBytes)), new OpenContext(), "/s.xml"));
        string reference = Elements(new SheetXml(new MemoryStream(xmlBytes), new OpenContext(), "/s.xml", forceFullTokenizer: true));
        Assert.True(trickled.Length > 500);
        Assert.Equal(reference, trickled);
    }

    [Theory]
    [InlineData("<worksheet><sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c></row>")]
    [InlineData("<worksheet><!DOCTYPE x><sheetData/></worksheet>")]
    [InlineData("<worksheet></a></worksheet>")]
    public void TruncatedOrForbiddenSheetsRaiseTypedErrors(string sheet)
    {
        byte[] file = TestBook.Bytes(sheet);
        Assert.ThrowsAny<OfficeException>(() => Dump(file, full: false));
    }
}

public class NumberParseTests
{
    [Fact]
    public void ShortcutAgreesWithTheFullParser()
    {
        var rng = new Random(12345);
        var samples = new List<string> { "0", "-0", "1", "-1", "0.5", ".5", "5.", "-.25", "123456789012345", "1234567890123456", "0.000000000000001", "1E5", "1.5e-3", "+3", " 4", "-", ".", "", "1.2.3", "1e", "9007199254740993", "0.1", "2.74", "1.37" };
        for (int i = 0; i < 200_000; i++)
        {
            int digits = 1 + rng.Next(17);
            var sb = new System.Text.StringBuilder();
            if (rng.Next(4) == 0)
            {
                sb.Append('-');
            }

            int dot = rng.Next(3) == 0 ? -1 : rng.Next(digits + 1);
            for (int d = 0; d < digits; d++)
            {
                if (d == dot)
                {
                    sb.Append('.');
                }

                sb.Append((char)('0' + rng.Next(10)));
            }

            samples.Add(sb.ToString());
        }

        foreach (string s in samples)
        {
            byte[] b = System.Text.Encoding.ASCII.GetBytes(s);
            bool ok = Streaming.StreamXml.TryParseDouble(b, out double got);
            bool expectedOk = double.TryParse(b, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double expected);
            Assert.True(ok == expectedOk, s);
            Assert.True(BitConverter.DoubleToInt64Bits(got) == BitConverter.DoubleToInt64Bits(expected), s);
        }
    }
}
