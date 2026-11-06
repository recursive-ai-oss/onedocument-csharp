using System.Text;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.IO;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests.Biff;

/// <summary>Little-endian byte builder for hand-made BIFF streams.</summary>
internal sealed class Bytes
{
    private readonly List<byte> _b = [];

    public int Length => _b.Count;

    public Bytes U8(int v)
    {
        _b.Add((byte)v);
        return this;
    }

    public Bytes U16(int v) => U8(v & 0xFF).U8((v >> 8) & 0xFF);

    public Bytes U32(uint v) => U16((int)(v & 0xFFFF)).U16((int)(v >> 16));

    public Bytes F64(double v)
    {
        long bits = BitConverter.DoubleToInt64Bits(v);
        for (int i = 0; i < 8; i++)
        {
            U8((int)((bits >> (8 * i)) & 0xFF));
        }

        return this;
    }

    public Bytes Raw(params byte[] bytes)
    {
        _b.AddRange(bytes);
        return this;
    }

    public Bytes Raw(Bytes other)
    {
        _b.AddRange(other._b);
        return this;
    }

    /// <summary>A 1-byte-length string in BIFF8 form: flag byte and 8-bit or UTF-16 characters.</summary>
    public Bytes Short8(string s) => U8(s.Length).Chars8(s);

    /// <summary>A 2-byte-length string in BIFF8 form.</summary>
    public Bytes Long8(string s) => U16(s.Length).Chars8(s);

    public Bytes Chars8(string s)
    {
        bool wide = s.Any(c => c > 0xFF);
        U8(wide ? 1 : 0);
        foreach (char c in s)
        {
            if (wide)
            {
                U16(c);
            }
            else
            {
                U8(c);
            }
        }

        return this;
    }

    public byte[] ToArray() => [.. _b];
}

/// <summary>Writes records and builds BIFF8 workbooks the way the specification lays them out, for tests of the reader.</summary>
internal sealed class XlsBuilder
{
    private readonly List<byte> _globals = [];
    private readonly List<byte[]> _sheetStreams = [];
    private readonly List<string> _sheetNames = [];
    private readonly List<byte> _tail = [];
    public List<string> Strings { get; } = [];
    public int SplitSstAt { get; set; } = -1;

    public static byte[] Rec(int type, byte[] data)
    {
        var b = new Bytes().U16(type).U16(data.Length);
        return [.. b.ToArray(), .. data];
    }

    public static byte[] Rec(int type, Bytes data) => Rec(type, data.ToArray());

    public static byte[] Bof(int version, int dt) => Rec(version == 8 ? 0x0809 : 0x0809, new Bytes().U16(version == 8 ? 0x0600 : 0x0500).U16(dt).U16(0x0DBB).U16(0x07CC).U32(0x000100C1).U32(0x06));

    public static byte[] Eof() => Rec(0x000A, []);

    public static byte[] Font(string name, int height = 200, int flags = 0, int color = 0x7FFF, int weight = 400, int underline = 0) =>
        Rec(0x0031, new Bytes().U16(height).U16(flags).U16(color).U16(weight).U16(0).U8(underline).U8(0).U8(0).U8(0).Short8(name));

    public static byte[] Xf(int font, int format, int flags = 0x0001, int align = 0x20, int border1 = 0, int border2 = 0, int fill = 0x20C0) =>
        Rec(0x00E0, new Bytes().U16(font).U16(format).U16(flags).U8(align).U8(0).U8(0).U8(0xFC).U32((uint)border1).U32((uint)border2).U16(fill));

    public static byte[] StyleXf(int font = 0, int format = 0) => Xf(font, format, flags: 0xFFF5, align: 0x20);

    public static byte[] Format(int id, string code) => Rec(0x041E, new Bytes().U16(id).Long8(code));

    public static byte[] Number(int row, int col, int xf, double v) => Rec(0x0203, new Bytes().U16(row).U16(col).U16(xf).F64(v));

    public static byte[] Rk(int row, int col, int xf, uint rk) => Rec(0x027E, new Bytes().U16(row).U16(col).U16(xf).U32(rk));

    public static byte[] LabelSst(int row, int col, int xf, int isst) => Rec(0x00FD, new Bytes().U16(row).U16(col).U16(xf).U32((uint)isst));

    public static byte[] Blank(int row, int col, int xf) => Rec(0x0201, new Bytes().U16(row).U16(col).U16(xf));

    public static byte[] BoolErr(int row, int col, int xf, int value, bool error) => Rec(0x0205, new Bytes().U16(row).U16(col).U16(xf).U8(value).U8(error ? 1 : 0));

    public static byte[] Label(int row, int col, int xf, string s) => Rec(0x0204, new Bytes().U16(row).U16(col).U16(xf).Long8(s));

    public static byte[] Formula(int row, int col, int xf, byte[] value8, byte[] rgce, byte[]? rgcb = null) =>
        Rec(0x0006, new Bytes().U16(row).U16(col).U16(xf).Raw(value8).U16(0).U32(0).U16(rgce.Length).Raw(rgce).Raw(rgcb ?? []));

    public static byte[] NumValue(double v) => new Bytes().F64(v).ToArray();

    public static byte[] StrValue() => [0, 0, 0, 0, 0, 0, 0xFF, 0xFF];

    public static byte[] BoolValue(bool b) => [1, 0, (byte)(b ? 1 : 0), 0, 0, 0, 0xFF, 0xFF];

    public static byte[] ErrValue(int code) => [2, 0, (byte)code, 0, 0, 0, 0xFF, 0xFF];

    public static byte[] StringRec(string s) => Rec(0x0207, new Bytes().Long8(s));

    public static byte[] Dimensions(int r1, int r2, int c1, int c2) => Rec(0x0200, new Bytes().U32((uint)r1).U32((uint)r2).U16(c1).U16(c2).U16(0));

    public static byte[] Row(int row, int height = 0x00FF, int flags = 0x0100) =>
        Rec(0x0208, new Bytes().U16(row).U16(0).U16(1).U16(height).U16(0).U16(0).U16(flags).U16(0x000F));

    public static byte[] Window2(int flags = 0x06B6) => Rec(0x023E, new Bytes().U16(flags).U16(0).U16(0).U32(0x40).U16(0).U16(100).U32(0));

    public static byte[] Merge(params (int r1, int r2, int c1, int c2)[] ranges)
    {
        var b = new Bytes().U16(ranges.Length);
        foreach ((int r1, int r2, int c1, int c2) in ranges)
        {
            b.U16(r1).U16(r2).U16(c1).U16(c2);
        }

        return Rec(0x00E5, b);
    }

    public static byte[] ColInfo(int first, int last, int width, int xf = 15, int flags = 0) => Rec(0x007D, new Bytes().U16(first).U16(last).U16(width).U16(xf).U16(flags).U16(0));

    /// <summary>SST with optional CONTINUE split: the table is cut after <paramref name="splitBytes"/> bytes of string data.</summary>
    public static byte[] Sst(IReadOnlyList<string> strings, int splitBytes = -1)
    {
        var data = new Bytes().U32((uint)strings.Count).U32((uint)strings.Count);
        var first = new List<byte>(data.ToArray());
        var parts = new List<List<byte>> { first };
        List<byte> cur = first;
        bool needFlag = false;
        foreach (string s in strings)
        {
            bool wide = s.Any(c => c > 0xFF);
            cur.AddRange(new Bytes().U16(s.Length).U8(wide ? 1 : 0).ToArray());
            foreach (char c in s)
            {
                if (splitBytes > 0 && cur.Count >= splitBytes && parts.Count == 1)
                {
                    cur = [];
                    parts.Add(cur);
                    needFlag = true;
                }

                if (needFlag)
                {
                    cur.Add((byte)(wide ? 1 : 0));
                    needFlag = false;
                }

                cur.Add((byte)(c & 0xFF));
                if (wide)
                {
                    cur.Add((byte)(c >> 8));
                }
            }
        }

        var all = new List<byte>(Rec(0x00FC, first.ToArray()));
        for (int i = 1; i < parts.Count; i++)
        {
            all.AddRange(Rec(0x003C, parts[i].ToArray()));
        }

        return [.. all];
    }

    /// <summary>Builds a workbook with the given sheets. Each sheet is a list of records placed between BOF and EOF.</summary>
    public static byte[] Workbook(IReadOnlyList<(string Name, byte[][] Records)> sheets, byte[][]? extraGlobals = null, int sstSplit = -1, IReadOnlyList<string>? sst = null,
        byte[][]? fonts = null, byte[][]? xfs = null, byte[][]? formats = null, byte[]? codePage = null)
    {
        var bw = new List<byte>();
        void Add(byte[] r) => bw.AddRange(r);
        byte[] bof = Bof(8, 0x05);

        // Everything before the BOUNDSHEET records is fixed; the sheet positions depend on the length of the globals.
        var pre = new List<byte>();
        pre.AddRange(bof);
        pre.AddRange(codePage ?? Rec(0x0042, new Bytes().U16(1200)));
        foreach (byte[] f in fonts ?? [Font("Arial"), Font("Arial"), Font("Arial"), Font("Arial"), Font("Arial")])
        {
            pre.AddRange(f);
        }

        foreach (byte[] f in formats ?? [])
        {
            pre.AddRange(f);
        }

        if (xfs is null)
        {
            for (int i = 0; i < 15; i++)
            {
                pre.AddRange(StyleXf());
            }

            pre.AddRange(Xf(0, 0));
        }
        else
        {
            foreach (byte[] x in xfs)
            {
                pre.AddRange(x);
            }
        }

        pre.AddRange(Rec(0x0293, new Bytes().U16(0x8000).U8(0).U8(0xFF)));

        var post = new List<byte>();
        foreach (byte[] g in extraGlobals ?? [])
        {
            post.AddRange(g);
        }

        if (sst is not null)
        {
            post.AddRange(Sst(sst, sstSplit));
        }

        post.AddRange(Eof());

        int boundLength = sheets.Sum(s => 4 + 6 + 2 + s.Name.Length + (s.Name.Any(c => c > 0xFF) ? s.Name.Length : 0));
        long pos = pre.Count + boundLength + post.Count;
        var sheetBytes = new List<byte[]>();
        foreach ((string name, byte[][] records) in sheets)
        {
            var sb = new List<byte>();
            sb.AddRange(Bof(8, 0x10));
            foreach (byte[] r in records)
            {
                sb.AddRange(r);
            }

            sb.AddRange(Eof());
            sheetBytes.Add([.. sb]);
        }

        Add([.. pre]);
        long at = pos;
        for (int i = 0; i < sheets.Count; i++)
        {
            Add(Rec(0x0085, new Bytes().U32((uint)at).U8(0).U8(0).Short8(sheets[i].Name)));
            at += sheetBytes[i].Length;
        }

        Add([.. post]);
        foreach (byte[] s in sheetBytes)
        {
            Add(s);
        }

        return [.. bw];
    }

    /// <summary>Wraps a workbook stream in a compound file as the <c>Workbook</c> stream.</summary>
    public static byte[] InCompoundFile(byte[] workbookStream, string streamName = "Workbook")
    {
        using var pkg = CfbPackage.Create();
        pkg.AddPart("/" + streamName, string.Empty, workbookStream);
        using var ms = new MemoryStream();
        pkg.WriteTo(ms);
        return ms.ToArray();
    }

    public static IByteSource Source(byte[] bytes) => ByteSource.FromMemory(bytes);

    public static string Latin(string s) => Encoding.Latin1.GetString(Encoding.Latin1.GetBytes(s));
}
