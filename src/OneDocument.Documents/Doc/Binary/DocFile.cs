using System.Buffers.Binary;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Doc.Binary;

/// <summary>The text stories of a Word binary file, in the order they follow each other in the character-position space ([MS-DOC] 2.3).</summary>
public enum DocStory
{
    /// <summary>The main document.</summary>
    Main = 0,

    /// <summary>Footnote text.</summary>
    Footnotes = 1,

    /// <summary>Header and footer text (and the footnote and endnote separators).</summary>
    Headers = 2,

    /// <summary>Macro text (Word 6/95 only).</summary>
    Macros = 3,

    /// <summary>Comment (annotation) text.</summary>
    Comments = 4,

    /// <summary>Endnote text.</summary>
    Endnotes = 5,

    /// <summary>Text boxes anchored in the main document.</summary>
    TextBoxes = 6,

    /// <summary>Text boxes anchored in headers and footers.</summary>
    HeaderTextBoxes = 7,
}

/// <summary>
/// A Word binary file opened at its lowest layer: the streams, the FIB, the piece table and the whole text. Everything else (the
/// stylesheet, the property runs, lists, fields, notes, comments) is read from these on demand. The file is read into memory at
/// <see cref="Open"/> and the compound file is released; the object holds no handle.
/// </summary>
public sealed class DocFile
{
    private readonly OpenContext _context;
    private readonly string[] _streamNames;
    private string? _text;

    private DocFile(OpenContext context, DocFib fib, byte[] word, byte[] table, byte[] data, string tablePath, PieceTable pieces, string[] streamNames)
    {
        _context = context;
        Fib = fib;
        Word = word;
        Table = table;
        Data = data;
        TablePath = tablePath;
        Pieces = pieces;
        _streamNames = streamNames;
    }

    /// <summary>The path of the <c>WordDocument</c> stream in defect locations.</summary>
    public const string WordPath = "/WordDocument";

    /// <summary>Gets the file information block.</summary>
    public DocFib Fib { get; }

    /// <summary>Gets the piece table.</summary>
    public PieceTable Pieces { get; }

    /// <summary>Gets the names of the streams and storages at the top of the compound file, for reports.</summary>
    public IReadOnlyList<string> StreamNames => _streamNames;

    /// <summary>Gets the <c>WordDocument</c> stream.</summary>
    internal byte[] Word { get; }

    /// <summary>Gets the table stream (<c>0Table</c> or <c>1Table</c>; the <c>WordDocument</c> stream itself for Word 6/95).</summary>
    internal byte[] Table { get; }

    /// <summary>Gets the <c>Data</c> stream, empty if the file has none.</summary>
    internal byte[] Data { get; }

    /// <summary>Gets the table stream's path, for defect locations.</summary>
    internal string TablePath { get; }

    /// <summary>Gets the context (limits and defect sink) the file was opened with.</summary>
    internal OpenContext Context => _context;

    /// <summary>Gets the whole text, all stories one after the other, as characters (control characters kept).</summary>
    public string Text => _text ??= DecodeText();

    /// <summary>
    /// Opens a Word binary file. The compound file is read, the FIB and the piece table are checked, and the streams are loaded;
    /// the source is not used after this returns.
    /// </summary>
    /// <param name="source">The file's bytes.</param>
    /// <param name="context">Limits and the defect sink.</param>
    /// <returns>The file.</returns>
    /// <exception cref="OfficeFormatException">The file is not a Word binary file or is damaged beyond repair.</exception>
    /// <exception cref="OfficeEncryptedException">The file is encrypted.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static DocFile Open(IByteSource source, OpenContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        using CfbPackage cfb = CfbPackage.Open(new NonDisposingSource(source), context);
        IPackagePart? wordPart = cfb.FindPart(WordPath);
        if (wordPart is null)
        {
            throw new OfficeFormatException(DefectKind.Packaging, "doc.no-word-stream", "The compound file has no WordDocument stream.", DefectLocation.InRecord(WordPath));
        }

        byte[] word = ReadPart(wordPart, context);
        DocFib fib = DocFib.Read(word, context.Defects, WordPath);
        if (fib.IsEncrypted)
        {
            if (context.Password is not null)
            {
                throw new OfficeUnsupportedException("decrypting a password-protected Word binary file", DefectLocation.InRecord(WordPath, 10));
            }

            throw new OfficeEncryptedException(EncryptionProblem.PasswordRequired, DefectLocation.InRecord(WordPath, 10));
        }

        string tablePath;
        byte[] table;
        if (fib.IsLegacyWord6)
        {
            tablePath = WordPath;
            table = word;
        }
        else
        {
            tablePath = fib.UsesOneTable ? "/1Table" : "/0Table";
            IPackagePart? tablePart = cfb.FindPart(tablePath);
            if (tablePart is null)
            {
                throw new OfficeFormatException(DefectKind.Packaging, "doc.no-table-stream", $"The compound file has no {tablePath[1..]} stream, which the FIB names as the table stream.", DefectLocation.InRecord(tablePath));
            }

            table = ReadPart(tablePart, context);
        }

        byte[] data = cfb.FindPart("/Data") is { } dp ? ReadPart(dp, context) : [];
        var names = new List<string>();
        foreach (IPackagePart p in cfb.Parts)
        {
            names.Add(p.Name);
        }

        PieceTable pieces;
        (uint clxFc, uint clxLcb) = fib.Pair(DocFib.Clx);
        if (fib.IsLegacyWord6 && (!fib.IsComplex || clxLcb == 0))
        {
            // Word 6/95 documents saved in full have no piece table: the text is one 8-bit run from fcMin to fcMac.
            long len = Math.Max(0, (long)fib.FcMac - fib.FcMin);
            if (fib.FcMin + len > word.Length)
            {
                len = Math.Max(0, word.Length - (long)fib.FcMin);
                context.Defects.Report(new Defect(DefectKind.Record, "doc.piece-out-of-range", DefectSeverity.Repaired, "The text runs past the end of the WordDocument stream.", DefectLocation.InRecord(WordPath, fib.FcMac)));
            }

            pieces = PieceTable.Single(fib.FcMin, (int)len, eightBit: true);
        }
        else
        {
            pieces = PieceTable.Read(table, clxFc, clxLcb, word.Length, context.Defects, tablePath, fib.IsLegacyWord6);
        }

        return new DocFile(context, fib, word, table, data, tablePath, pieces, [.. names]);
    }

    private static byte[] ReadPart(IPackagePart part, OpenContext context)
    {
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), context.Limits.MaxPartSize, part.Length, DefectLocation.InRecord(part.Name));
        return part.GetBytes().ToArray();
    }

    /// <summary>Gets the character positions a story covers.</summary>
    /// <param name="story">The story.</param>
    /// <returns>The first position and the length; the length is 0 for a story the file does not have. Clipped to the text.</returns>
    public (int Start, int Length) StoryRange(DocStory story)
    {
        long start = 0;
        long[] lens = [Fib.CcpText, Fib.CcpFtn, Fib.CcpHdd, Fib.CcpMcr, Fib.CcpAtn, Fib.CcpEdn, Fib.CcpTxbx, Fib.CcpHdrTxbx];
        for (int i = 0; i < (int)story; i++)
        {
            start += Math.Max(0, lens[i]);
        }

        long len = Math.Max(0, lens[(int)story]);
        long total = Pieces.Length;
        if (start > total)
        {
            return ((int)total, 0);
        }

        if (start + len > total)
        {
            _context.Defects.Report(new Defect(DefectKind.Record, "doc.story-out-of-range", DefectSeverity.Repaired,
                $"The {story} story is {len} characters but only {total - start} remain in the piece table.", DefectLocation.InRecord(WordPath)));
            len = total - start;
        }

        return ((int)start, (int)len);
    }

    /// <summary>Gets the text of a story.</summary>
    /// <param name="story">The story.</param>
    /// <returns>Its characters, control characters included.</returns>
    public ReadOnlyMemory<char> StoryText(DocStory story)
    {
        (int s, int l) = StoryRange(story);
        return Text.AsMemory(s, l);
    }

    private string DecodeText()
    {
        int total = Pieces.Length;
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), _context.Limits.MaxAllocationBytes, (long)total * 2, DefectLocation.InRecord(WordPath));
        var buf = new char[total];
        bool warned = false;
        foreach (Piece p in Pieces.Pieces)
        {
            int count = p.CpEnd - p.CpStart;
            if (count <= 0 || p.CpStart >= total)
            {
                continue;
            }

            if (p.IsEightBit)
            {
                if (Fib.IsLegacyWord6 && Fib.Charset != 0 && !warned)
                {
                    warned = true;
                    _context.Defects.Report(new Defect(DefectKind.Encoding, "doc.codepage-unsupported", DefectSeverity.Repaired,
                        $"The 8-bit text uses character set {Fib.Charset}; it was read as Windows-1252.", DefectLocation.InRecord(WordPath, 0x14)));
                }

                for (int i = 0; i < count; i++)
                {
                    long at = p.Offset + i;
                    buf[p.CpStart + i] = at < Word.Length ? AnsiToChar(Word[at]) : ' ';
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    long at = p.Offset + (2L * i);
                    buf[p.CpStart + i] = at + 1 < Word.Length ? (char)BinaryPrimitives.ReadUInt16LittleEndian(Word.AsSpan((int)at)) : ' ';
                }
            }
        }

        return new string(buf);
    }

    // [MS-DOC] 2.9.73 FcCompressed: 8-bit text is Latin-1 except the bytes 0x82..0x9F, which this table maps.
    private static char AnsiToChar(byte b)
    {
        if (b < 0x80 || b >= 0xA0)
        {
            return (char)b;
        }

        return b switch
        {
            0x82 => '\u201A',
            0x83 => '\u0192',
            0x84 => '\u201E',
            0x85 => '\u2026',
            0x86 => '\u2020',
            0x87 => '\u2021',
            0x88 => '\u02C6',
            0x89 => '\u2030',
            0x8A => '\u0160',
            0x8B => '\u2039',
            0x8C => '\u0152',
            0x91 => '\u2018',
            0x92 => '\u2019',
            0x93 => '\u201C',
            0x94 => '\u201D',
            0x95 => '\u2022',
            0x96 => '\u2013',
            0x97 => '\u2014',
            0x98 => '\u02DC',
            0x99 => '\u2122',
            0x9A => '\u0161',
            0x9B => '\u203A',
            0x9C => '\u0153',
            0x9F => '\u0178',
            _ => (char)b,
        };
    }

    // Lets the compound-file package read the caller's source without disposing it.
    private sealed class NonDisposingSource(IByteSource inner) : IByteSource
    {
        public long Length => inner.Length;

        public bool CanReadSync => inner.CanReadSync;

        public int Read(long offset, Span<byte> destination) => inner.Read(offset, destination);

        public ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(offset, destination, cancellationToken);

        public bool TryGetMemory(out ReadOnlyMemory<byte> memory) => inner.TryGetMemory(out memory);

        public void Dispose()
        {
        }
    }
}
