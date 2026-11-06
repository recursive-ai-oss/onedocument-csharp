using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
using System.Text;
using RecursiveAI.OneDocument.Cfb;

namespace RecursiveAI.OneDocument.Documents.Tests;


/// <summary>
/// Writes small Word 97-2003 files (and Word 6/95 ones) for tests: a compound file holding a WordDocument stream, a table stream and
/// optionally a Data stream. Only what a test asks for is written. The layout follows [MS-DOC]; the builder is test code, not a writer.
/// </summary>
internal sealed class DocBuilder
{
    internal sealed record Run(int CpStart, int CpEnd, byte[] Grpprl);

    internal sealed record ParaProps(int MarkCp, int Istd, byte[] Grpprl);

    private readonly List<(string Text, bool Eight)> _pieces = [];

    internal string Footnotes { get; set; } = string.Empty;

    internal string Headers { get; set; } = string.Empty;

    internal string Comments { get; set; } = string.Empty;

    internal List<Run> CharRuns { get; } = [];

    internal List<ParaProps> ParaRuns { get; } = [];

    internal bool Encrypted { get; set; }

    internal bool ReversePieces { get; set; }

    internal byte[]? Data { get; set; }

    internal List<(int Cp, int Id)> FootnoteRefs { get; } = [];

    internal byte[]? Sepx { get; set; }

    internal List<int> SectionEnds { get; } = [];

    /// <summary>Adds main-story text as one piece; the pieces are numbered in character order.</summary>
    internal DocBuilder Piece(string text, bool eightBit = true)
    {
        _pieces.Add((text, eightBit));
        return this;
    }

    internal string MainText => string.Concat(_pieces.Select(p => p.Text));

    private static void U16(byte[] b, int at, int v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), (ushort)v);

    private static void U32(byte[] b, int at, long v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), (uint)v);

    private static byte[] PadTo(byte[] b, int multiple)
    {
        int n = (b.Length + multiple - 1) / multiple * multiple;
        Array.Resize(ref b, n);
        return b;
    }

    internal byte[] Build()
    {
        // The stories follow each other: main, footnotes, headers, (macros), comments. Each ends in a paragraph mark.
        string main = MainText;
        string all = main + Footnotes + Headers + Comments;
        var pieces = new List<(string Text, bool Eight)>(_pieces);
        string tail = Footnotes + Headers + Comments;
        if (tail.Length > 0)
        {
            pieces.Add((tail, true));
        }

        // The text: written after the FIB, pieces in file order (reversed for a fast-saved file).
        var word = new MemoryStream();
        word.Write(new byte[0x400]);
        var offsets = new long[pieces.Count];
        int[] order = [.. Enumerable.Range(0, pieces.Count)];
        if (ReversePieces)
        {
            Array.Reverse(order);
        }

        foreach (int i in order)
        {
            offsets[i] = word.Position;
            byte[] bytes = pieces[i].Eight ? Encoding.Latin1.GetBytes(pieces[i].Text) : Encoding.Unicode.GetBytes(pieces[i].Text);
            word.Write(bytes);
        }

        // fc of a character position, for the property runs.
        long Fc(int cp)
        {
            int start = 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                int len = pieces[i].Text.Length;
                if (cp < start + len || i == pieces.Count - 1)
                {
                    return offsets[i] + ((long)(cp - start) * (pieces[i].Eight ? 1 : 2));
                }

                start += len;
            }

            return 0;
        }

        // The size in bytes of the character at a position.
        int Width(int cp)
        {
            int start = 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                int len = pieces[i].Text.Length;
                if (cp < start + len || i == pieces.Count - 1)
                {
                    return pieces[i].Eight ? 1 : 2;
                }

                start += len;
            }

            return 1;
        }

        var table = new MemoryStream();
        var pairs = new Dictionary<int, (long Fc, long Lcb)>();

        // Clx: the piece table.
        int clxAt = (int)table.Position;
        var clx = new MemoryStream();
        clx.WriteByte(2);
        int n = pieces.Count;
        var plc = new byte[((n + 1) * 4) + (n * 8)];
        int cp0 = 0;
        for (int i = 0; i < n; i++)
        {
            U32(plc, i * 4, cp0);
            cp0 += pieces[i].Text.Length;
            long fcRaw = pieces[i].Eight ? (offsets[i] * 2) | 0x40000000 : offsets[i];
            U32(plc, ((n + 1) * 4) + (i * 8) + 2, fcRaw);
        }

        U32(plc, n * 4, cp0);
        var lcb = new byte[4];
        U32(lcb, 0, plc.Length);
        clx.Write(lcb);
        clx.Write(plc);
        table.Write(clx.ToArray());
        pairs[33] = (clxAt, clx.Length);

        // Character and paragraph property pages, 512-aligned in the WordDocument stream.
        void Align()
        {
            while (word.Position % 512 != 0)
            {
                word.WriteByte(0);
            }
        }

        if (CharRuns.Count > 0)
        {
            Align();
            long page = word.Position;
            var fkp = new byte[512];
            var runs = CharRuns.OrderBy(r => r.CpStart).ToList();
            fkp[511] = (byte)runs.Count;
            int top = 511;
            for (int i = 0; i < runs.Count; i++)
            {
                U32(fkp, i * 4, Fc(runs[i].CpStart));
                int need = 1 + runs[i].Grpprl.Length;
                top = (top - need) & ~1;
                fkp[top] = (byte)runs[i].Grpprl.Length;
                runs[i].Grpprl.CopyTo(fkp, top + 1);
                fkp[((runs.Count + 1) * 4) + i] = (byte)(top / 2);
            }

            U32(fkp, runs.Count * 4, Fc(runs[^1].CpEnd));
            word.Write(fkp);
            int at = (int)table.Position;
            var bte = new byte[12];
            U32(bte, 0, Fc(runs[0].CpStart));
            U32(bte, 4, Fc(runs[^1].CpEnd));
            U32(bte, 8, page / 512);
            table.Write(bte);
            pairs[12] = (at, bte.Length);
        }

        if (ParaRuns.Count > 0)
        {
            Align();
            long page = word.Position;
            var fkp = new byte[512];
            var runs = ParaRuns.OrderBy(r => r.MarkCp).ToList();
            fkp[511] = (byte)runs.Count;
            int top = 511;
            long prev = 0;
            for (int i = 0; i < runs.Count; i++)
            {
                // A paragraph's properties cover its characters up to and including the mark.
                long start = i == 0 ? Fc(0) : prev;
                U32(fkp, i * 4, start);
                prev = Fc(runs[i].MarkCp) + Width(runs[i].MarkCp);
                int content = 2 + runs[i].Grpprl.Length;
                int cb = (content + 2) / 2;
                int total = 1 + (cb * 2) - 1;
                top = (top - total) & ~1;
                fkp[top] = (byte)cb;
                U16(fkp, top + 1, runs[i].Istd);
                runs[i].Grpprl.CopyTo(fkp, top + 3);
                fkp[((runs.Count + 1) * 4) + (i * 13)] = (byte)(top / 2);
            }

            U32(fkp, runs.Count * 4, prev);
            word.Write(fkp);
            int at = (int)table.Position;
            var bte = new byte[12];
            U32(bte, 0, Fc(0));
            U32(bte, 4, prev);
            U32(bte, 8, page / 512);
            table.Write(bte);
            pairs[13] = (at, bte.Length);
        }

        // Sections: one by default, ending at the end of the main story.
        {
            var ends = SectionEnds.Count > 0 ? [.. SectionEnds] : new List<int> { main.Length };
            if (ends[^1] != main.Length)
            {
                ends.Add(main.Length);
            }

            var sed = new byte[((ends.Count + 1) * 4) + (ends.Count * 12)];
            int prevEnd = 0;
            for (int i = 0; i < ends.Count; i++)
            {
                U32(sed, i * 4, prevEnd);
                prevEnd = ends[i];
                U32(sed, ((ends.Count + 1) * 4) + (i * 12) + 2, 0xFFFFFFFFL);
            }

            U32(sed, ends.Count * 4, prevEnd);
            if (Sepx is not null)
            {
                Align();
                long sepx = word.Position;
                var buf = new byte[2 + Sepx.Length];
                U16(buf, 0, Sepx.Length);
                Sepx.CopyTo(buf, 2);
                word.Write(buf);
                for (int i = 0; i < ends.Count; i++)
                {
                    U32(sed, ((ends.Count + 1) * 4) + (i * 12) + 2, sepx);
                }
            }

            int at = (int)table.Position;
            table.Write(sed);
            pairs[6] = (at, sed.Length);
        }

        if (FootnoteRefs.Count > 0)
        {
            // PlcffndRef: one reference per footnote (2-byte data), PlcffndTxt: where each note's text starts in the footnote story.
            int m = FootnoteRefs.Count;
            var refs = new byte[((m + 1) * 4) + (m * 2)];
            var txt = new byte[(m + 2) * 4];
            string[] notes = Footnotes.Split('\u0001', StringSplitOptions.None);
            int pos = 0;
            for (int i = 0; i < m; i++)
            {
                U32(refs, i * 4, FootnoteRefs[i].Cp);
                U16(refs, ((m + 1) * 4) + (i * 2), 1);
                U32(txt, i * 4, pos);
                pos += i < notes.Length - 1 ? notes[i].Length + 1 : Footnotes.Length - pos;
            }

            U32(refs, m * 4, main.Length);
            U32(txt, m * 4, Footnotes.Length);
            U32(txt, (m + 1) * 4, Footnotes.Length + 1);
            int at = (int)table.Position;
            table.Write(refs);
            pairs[2] = (at, refs.Length);
            at = (int)table.Position;
            table.Write(txt);
            pairs[3] = (at, txt.Length);
        }

        if (Headers.Length > 0)
        {
            // PlcfHdd: six separator entries, then six per section (even header, odd header, ...); the text sits in the odd header of section 1.
            var hdd = new byte[13 * 4];
            for (int i = 0; i < 13; i++)
            {
                U32(hdd, i * 4, i < 8 ? 0 : Headers.Length);
            }

            int at = (int)table.Position;
            table.Write(hdd);
            pairs[11] = (at, hdd.Length);
        }

        // Stylesheet header only (no styles) so a reader that needs one finds it.
        {
            var stsh = new byte[2 + 18];
            U16(stsh, 0, 18);
            U16(stsh, 4, 10);
            int at = (int)table.Position;
            table.Write(stsh);
            pairs[1] = (at, stsh.Length);
        }

        byte[] fib = new byte[0x400];
        U16(fib, 0, 0xA5EC);
        U16(fib, 2, 0x00C1);
        U16(fib, 6, 0x0409);
        int flags = 0x0200 | (n > 1 ? 0x0004 : 0) | (Encrypted ? 0x0100 : 0);
        U16(fib, 10, flags);
        U16(fib, 12, 0x00BF);
        U16(fib, 32, 14);
        U16(fib, 62, 22);
        U32(fib, 64 + (3 * 4), main.Length);
        U32(fib, 64 + (4 * 4), Footnotes.Length);
        U32(fib, 64 + (5 * 4), Headers.Length);
        U32(fib, 64 + (7 * 4), Comments.Length);
        U16(fib, 152, 93);
        foreach ((int idx, (long fc, long len)) in pairs)
        {
            U32(fib, 154 + (idx * 8), fc);
            U32(fib, 154 + (idx * 8) + 4, len);
        }

        U16(fib, 154 + (93 * 8), 0);
        fib.CopyTo(word.GetBuffer(), 0);
        _ = all;

        using CfbPackage cfb = CfbPackage.Create();
        cfb.AddPart("/WordDocument", string.Empty, word.ToArray());
        cfb.AddPart("/1Table", string.Empty, table.ToArray());
        if (Data is not null)
        {
            cfb.AddPart("/Data", string.Empty, Data);
        }

        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        return ms.ToArray();
    }

    /// <summary>A Word 6/95 file: one 8-bit text run between fcMin and fcMac and no table stream.</summary>
    internal static byte[] BuildWord6(string text, string footnotes = "")
    {
        var word = new byte[0x300 + text.Length + footnotes.Length + 16];
        U16(word, 0, 0xA5DC);
        U16(word, 2, 0x0065);
        U16(word, 6, 0x0409);
        U16(word, 12, 0x0065);
        U32(word, 0x18, 0x300);
        U32(word, 0x1C, 0x300 + text.Length + footnotes.Length);
        U32(word, 0x34, text.Length);
        U32(word, 0x38, footnotes.Length);
        Encoding.Latin1.GetBytes(text + footnotes).CopyTo(word, 0x300);
        using CfbPackage cfb = CfbPackage.Create();
        cfb.AddPart("/WordDocument", string.Empty, word);
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        return ms.ToArray();
    }

    // Property list helpers.
    internal static byte[] Sprm(int sprm, params byte[] operand) => [(byte)sprm, (byte)(sprm >> 8), .. operand];

    internal static byte[] Cat(params byte[][] parts) => [.. parts.SelectMany(p => p)];
}
