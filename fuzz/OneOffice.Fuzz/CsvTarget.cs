using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RecursiveAI.OneOffice.Spreadsheets;
using RecursiveAI.OneOffice.Spreadsheets.Csv;

namespace RecursiveAI.OneOffice.Fuzz;

/// <summary>
/// The delimited-text layer. The first byte selects options (delimiter, blank-line skipping, quote character); the rest is the file.
/// <c>csv</c> reads it whole and in one- to seven-byte reads, which must give the same records; reads every field as a value; writes the records
/// back and reads them again, which must give the same fields; and imports the file as a workbook and writes the sheet out. Only
/// <see cref="OfficeException"/> may escape.
/// </summary>
internal static class CsvTarget
{
    private static readonly char?[] Delimiters = [null, ',', ';', '\t', '|'];

    // Small limits keep each execution fast.
    private static readonly OfficeLimits Limits = new() { MaxAllocationBytes = 1 << 22, MaxRecordCount = 50_000, MaxDefects = 100 };

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return;
        }

        byte mode = data[0];
        byte[] file = data[1..].ToArray();
        var options = new CsvReadOptions
        {
            Delimiter = Delimiters[mode % Delimiters.Length],
            SkipBlankLines = (mode & 0x08) != 0,
            Quote = (mode & 0x10) != 0 ? '\'' : '"',
        };
        try
        {
            List<string[]> whole = ReadAll(new MemoryStream(file), options, out char delimiter);
            List<string[]> pieces = ReadAll(new TrickleStream(file, 1 + (mode >> 5)), options, out _);
            if (!Same(whole, pieces))
            {
                throw new InvalidOperationException("reading in small pieces gives different records");
            }

            var parser = new CsvValueParser(new CsvValueOptions { PreserveLeadingZeros = (mode & 0x04) != 0 });
            foreach (string[] r in whole)
            {
                foreach (string f in r)
                {
                    _ = parser.Parse(f);
                }
            }

            if (options.Quote == '"' && (options.Delimiter ?? delimiter) != '\r')
            {
                var ms = new MemoryStream();
                using (var w = new CsvWriter(ms, new CsvWriteOptions { Delimiter = delimiter, FormulaInjection = CsvFormulaInjection.None, ByteOrderMark = false }))
                {
                    foreach (string[] r in whole)
                    {
                        foreach (string f in r)
                        {
                            w.WriteField(f, isText: false);
                        }

                        w.EndRecord();
                    }
                }

                ms.Position = 0;
                List<string[]> again = ReadAll(ms, new CsvReadOptions { Delimiter = delimiter, SkipBlankLines = options.SkipBlankLines }, out _);
                if (!Same(whole, again, ignoreBlankDifference: true))
                {
                    throw new InvalidOperationException("write then read changed the fields");
                }
            }

            if (file.Length <= 1 << 16)
            {
                using Workbook wb = CsvDocument.ToWorkbook(new MemoryStream(file), new CsvImportOptions { Read = options }, new OpenContext(Limits));
                CsvDocument.Write(wb, Stream.Null, new CsvWriteOptions { Delimiter = delimiter });
            }
        }
        catch (OfficeException)
        {
            // A typed rejection is the right answer for hostile input.
        }
    }

    private static List<string[]> ReadAll(Stream stream, CsvReadOptions options, out char delimiter)
    {
        using var r = new CsvReader(stream, options, new OpenContext(Limits));
        var rows = new List<string[]>();
        while (r.ReadRecord())
        {
            if (r.FieldCount < 1)
            {
                throw new InvalidOperationException("a record without fields");
            }

            var f = new string[r.FieldCount];
            for (int i = 0; i < f.Length; i++)
            {
                f[i] = r.GetField(i).ToString();
            }

            rows.Add(f);
        }

        delimiter = r.Delimiter;
        return rows;
    }

    // A record that is one empty field is an empty line; writing it gives an empty line again, so the comparison is exact except
    // when the writer's output ends the file differently, which does not change records.
    private static bool Same(List<string[]> a, List<string[]> b, bool ignoreBlankDifference = false)
    {
        if (ignoreBlankDifference)
        {
            a = a.FindAll(static r => !(r.Length == 1 && r[0].Length == 0));
            b = b.FindAll(static r => !(r.Length == 1 && r[0].Length == 0));
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].AsSpan().SequenceEqual(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        void Seed(string name, byte mode, string text) => File.WriteAllBytes(Path.Combine(dir, name), [mode, .. Encoding.UTF8.GetBytes(text)]);
        Seed("plain.csv", 0, "a,b,c\r\n1,2,3\r\n");
        Seed("quoted.csv", 0, "a,\"b,\"\"c\"\"\"\r\n\"x\ny\",2\r\n");
        Seed("semicolon.csv", 2, "a;b\n1;2\n");
        Seed("tab.csv", 3, "a\tb\n1\t2\n");
        Seed("values.csv", 0, "n,v\n1,12.5%\n2,2020-01-15 12:30\n3,\"1,234.5\"\n4,TRUE\n5,#N/A\n6,007\n7,Jan 15, 2020\n");
        Seed("ragged.csv", 0, "a,b\n1\n1,2,3\n\n\"open");
        File.WriteAllBytes(Path.Combine(dir, "utf16.csv"), [0, .. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("a,b\n1,2\n")]);
        File.WriteAllBytes(Path.Combine(dir, "bom8.csv"), [0, .. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("sep=;\na;b\n")]);
        return 0;
    }

    // A stream that never returns more than a few bytes per read, so every refill boundary is exercised.
    private sealed class TrickleStream(byte[] data, int chunk) : Stream
    {
        private int _pos;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _pos;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(Math.Min(count, chunk), data.Length - _pos);
            Buffer.BlockCopy(data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
