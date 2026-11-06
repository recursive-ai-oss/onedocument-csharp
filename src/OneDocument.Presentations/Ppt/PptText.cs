using System.Buffers.Binary;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.OfficeArt;

namespace RecursiveAI.OneDocument.Ppt.Binary;

/// <summary>A colour as text and bullets store it: RGB bytes and an index into the slide's colour scheme ([MS-PPT] §2.12.2, ColorIndexStruct).</summary>
/// <param name="Red">The red channel.</param>
/// <param name="Green">The green channel.</param>
/// <param name="Blue">The blue channel.</param>
/// <param name="Index">0 to 7 name a colour of the scheme (background, text, shadow, title, fill, accent 1 to 3); 0xFE means the RGB bytes are the colour; 0xFF means undefined.</param>
public readonly record struct PptColor(byte Red, byte Green, byte Blue, byte Index)
{
    /// <summary>True when <see cref="Index"/> names a scheme colour.</summary>
    public bool IsScheme => Index < 8;

    /// <summary>True when the RGB bytes are the colour.</summary>
    public bool IsRgb => Index == 0xFE;

    /// <summary>True when the colour is not specified.</summary>
    public bool IsUndefined => Index == 0xFF || (Index >= 8 && Index != 0xFE);
}

/// <summary>Bits of <see cref="PptParagraphFormat.Masks"/> ([MS-PPT] §2.9.21, PFMasks).</summary>
[Flags]
public enum PptParagraphMasks : uint
{
    /// <summary>No field set.</summary>
    None = 0,

    /// <summary>The bullet flag fHasBullet is valid.</summary>
    HasBullet = 1u << 0,

    /// <summary>The bullet flag fBulletHasFont is valid.</summary>
    BulletHasFont = 1u << 1,

    /// <summary>The bullet flag fBulletHasColor is valid.</summary>
    BulletHasColor = 1u << 2,

    /// <summary>The bullet flag fBulletHasSize is valid.</summary>
    BulletHasSize = 1u << 3,

    /// <summary>bulletFontRef exists.</summary>
    BulletFont = 1u << 4,

    /// <summary>bulletColor exists.</summary>
    BulletColor = 1u << 5,

    /// <summary>bulletSize exists.</summary>
    BulletSize = 1u << 6,

    /// <summary>bulletChar exists.</summary>
    BulletChar = 1u << 7,

    /// <summary>leftMargin exists.</summary>
    LeftMargin = 1u << 8,

    /// <summary>indent exists.</summary>
    Indent = 1u << 10,

    /// <summary>textAlignment exists.</summary>
    Align = 1u << 11,

    /// <summary>lineSpacing exists.</summary>
    LineSpacing = 1u << 12,

    /// <summary>spaceBefore exists.</summary>
    SpaceBefore = 1u << 13,

    /// <summary>spaceAfter exists.</summary>
    SpaceAfter = 1u << 14,

    /// <summary>defaultTabSize exists.</summary>
    DefaultTabSize = 1u << 15,

    /// <summary>fontAlign exists.</summary>
    FontAlign = 1u << 16,

    /// <summary>wrapFlags exists (charWrap).</summary>
    CharWrap = 1u << 17,

    /// <summary>wrapFlags exists (wordWrap).</summary>
    WordWrap = 1u << 18,

    /// <summary>wrapFlags exists (overflow).</summary>
    Overflow = 1u << 19,

    /// <summary>tabStops exists.</summary>
    TabStops = 1u << 20,

    /// <summary>textDirection exists.</summary>
    TextDirection = 1u << 21,
}

/// <summary>Paragraph formatting from a <c>TextPFException</c> ([MS-PPT] §2.9.20). A field is meaningful only when its mask bit is set.</summary>
public sealed class PptParagraphFormat
{
    /// <summary>Which fields are present.</summary>
    public PptParagraphMasks Masks { get; internal set; }

    /// <summary>The bullet flags: bit 0 has a bullet, bit 1 the bullet has its own font, bit 2 its own colour, bit 3 its own size.</summary>
    public int BulletFlags { get; internal set; }

    /// <summary>The bullet character (UTF-16 code unit).</summary>
    public char BulletChar { get; internal set; }

    /// <summary>The bullet font as an index into the font collection.</summary>
    public int BulletFontRef { get; internal set; }

    /// <summary>The bullet size: 25 to 400 is a percentage of the text size, a negative value is points.</summary>
    public int BulletSize { get; internal set; }

    /// <summary>The bullet colour.</summary>
    public PptColor BulletColor { get; internal set; }

    /// <summary>The alignment: 0 left, 1 centre, 2 right, 3 justify, 4 distributed, 5 Thai distributed, 6 justify low.</summary>
    public int Alignment { get; internal set; }

    /// <summary>Line spacing: 0 to 13200 is a percentage of the line height, negative is master units.</summary>
    public int LineSpacing { get; internal set; }

    /// <summary>Space before: as <see cref="LineSpacing"/>.</summary>
    public int SpaceBefore { get; internal set; }

    /// <summary>Space after: as <see cref="LineSpacing"/>.</summary>
    public int SpaceAfter { get; internal set; }

    /// <summary>The left margin in master units (1/576 inch).</summary>
    public int LeftMargin { get; internal set; }

    /// <summary>The first-line indent in master units.</summary>
    public int Indent { get; internal set; }

    /// <summary>The default tab size in master units.</summary>
    public int DefaultTabSize { get; internal set; }

    /// <summary>The tab stops: position in master units and type (0 left, 1 centre, 2 right, 3 decimal).</summary>
    public IReadOnlyList<(int Position, int Type)> TabStops { get; internal set; } = [];

    /// <summary>The font alignment (0 auto, 1 top, 2 centre, 3 baseline, 4 bottom).</summary>
    public int FontAlign { get; internal set; }

    /// <summary>The text direction: 0 left to right, 1 right to left.</summary>
    public int TextDirection { get; internal set; }

    /// <summary>True when the mask bit is set.</summary>
    /// <param name="mask">The bit to test.</param>
    public bool Has(PptParagraphMasks mask) => (Masks & mask) != 0;

    /// <summary>True when the bullet flag fHasBullet is both valid and set.</summary>
    public bool? HasBulletFlag => Has(PptParagraphMasks.HasBullet) ? (BulletFlags & 1) != 0 : null;
}

/// <summary>Character formatting from a <c>TextCFException</c> ([MS-PPT] §2.9.14). A field is meaningful only when its mask bit is set.</summary>
public sealed class PptCharFormat
{
    /// <summary>The raw <c>CFMasks</c>: bit 0 bold, 1 italic, 2 underline, 4 shadow, 5 fehint, 7 kumi, 9 emboss, 16 typeface, 17 size, 18 colour, 19 position, 21 oldEATypeface, 22 ansiTypeface, 23 symbolTypeface.</summary>
    public uint Masks { get; internal set; }

    /// <summary>The style bits (<c>CFStyle</c>): bit 0 bold, 1 italic, 2 underline, 4 shadow, 9 emboss.</summary>
    public int Style { get; internal set; }

    /// <summary>The typeface as an index into the font collection.</summary>
    public int FontRef { get; internal set; }

    /// <summary>The East Asian typeface index.</summary>
    public int OldEaFontRef { get; internal set; }

    /// <summary>The ANSI typeface index.</summary>
    public int AnsiFontRef { get; internal set; }

    /// <summary>The symbol typeface index.</summary>
    public int SymbolFontRef { get; internal set; }

    /// <summary>The size in points.</summary>
    public int FontSize { get; internal set; }

    /// <summary>The colour.</summary>
    public PptColor Color { get; internal set; }

    /// <summary>The baseline offset as a percentage of the line height (-100 to 100).</summary>
    public int Position { get; internal set; }

    /// <summary>True when the bit of <see cref="Masks"/> is set.</summary>
    /// <param name="bit">The bit number (0-31).</param>
    public bool Has(int bit) => (Masks & (1u << bit)) != 0;

    /// <summary>Bold, when set.</summary>
    public bool? Bold => Has(0) ? (Style & 1) != 0 : null;

    /// <summary>Italic, when set.</summary>
    public bool? Italic => Has(1) ? (Style & 2) != 0 : null;

    /// <summary>Underline, when set.</summary>
    public bool? Underline => Has(2) ? (Style & 4) != 0 : null;

    /// <summary>Shadow, when set.</summary>
    public bool? Shadow => Has(4) ? (Style & 0x10) != 0 : null;

    /// <summary>Emboss, when set.</summary>
    public bool? Emboss => Has(9) ? (Style & 0x200) != 0 : null;
}

/// <summary>A run of characters with paragraph formatting (<c>TextPFRun</c>, [MS-PPT] §2.9.45).</summary>
/// <param name="Count">The number of characters the run covers.</param>
/// <param name="IndentLevel">The indent level (0 to 4).</param>
/// <param name="Format">The paragraph formatting.</param>
public sealed record PptParagraphRun(int Count, int IndentLevel, PptParagraphFormat Format);

/// <summary>A run of characters with character formatting (<c>TextCFRun</c>, [MS-PPT] §2.9.46).</summary>
/// <param name="Count">The number of characters the run covers.</param>
/// <param name="Format">The character formatting.</param>
public sealed record PptCharRun(int Count, PptCharFormat Format);

/// <summary>A run of characters with a language ([MS-PPT] §2.9.55, TextSIRun).</summary>
/// <param name="Count">The number of characters the run covers.</param>
/// <param name="LanguageId">The Windows language identifier, or 0 when the run names none.</param>
public sealed record PptLanguageRun(int Count, int LanguageId);

/// <summary>What a metacharacter stands for.</summary>
public enum PptMetaCharKind
{
    /// <summary>The slide number.</summary>
    SlideNumber = 0,

    /// <summary>A date or time in one of the numbered formats.</summary>
    DateTime = 1,

    /// <summary>The current date in a generic format.</summary>
    GenericDate = 2,

    /// <summary>The header text of a notes page.</summary>
    Header = 3,

    /// <summary>The footer text.</summary>
    Footer = 4,

    /// <summary>A date or time with an RTF-style format string.</summary>
    RtfDateTime = 5,
}

/// <summary>A field in text: the metacharacter at <paramref name="Position"/> is replaced by the slide number, date, header or footer ([MS-PPT] §2.9.47 to §2.9.52).</summary>
/// <param name="Position">The zero-based character position in the text.</param>
/// <param name="Kind">What the field shows.</param>
/// <param name="FormatId">The date-time format number (<c>DateTimeMCAtom</c>), or -1.</param>
public sealed record PptMetaChar(int Position, PptMetaCharKind Kind, int FormatId);

/// <summary>The indent and margin of each level of a text ruler ([MS-PPT] §2.9.30), in master units; null where the ruler says nothing.</summary>
public sealed class PptRuler
{
    /// <summary>The left margin of levels 0 to 4.</summary>
    public int?[] LeftMargin { get; } = new int?[5];

    /// <summary>The first-line indent of levels 0 to 4.</summary>
    public int?[] Indent { get; } = new int?[5];

    /// <summary>The default tab size, or null.</summary>
    public int? DefaultTabSize { get; internal set; }

    /// <summary>The tab stops, or empty.</summary>
    public IReadOnlyList<(int Position, int Type)> TabStops { get; internal set; } = [];
}

/// <summary>
/// One body of text: a <c>TextHeaderAtom</c> and the records that belong to it. Text excludes the terminating paragraph break
/// that [MS-PPT] §2.9.41 adds to every body, and the paragraph runs cover that break too.
/// </summary>
public sealed class PptTextBody
{
    /// <summary>The <c>TextTypeEnum</c> value: 0 title, 1 body, 2 notes, 4 other, 5 centre body, 6 centre title, 7 half body, 8 quarter body.</summary>
    public int TextType { get; internal set; } = 4;

    /// <summary>The header atom's instance: the index in the slide's placeholder text list when the body is in a slide list.</summary>
    public int Instance { get; internal set; }

    /// <summary>The characters. Paragraphs end at U+000D, forced line breaks are U+000B.</summary>
    public string Text { get; internal set; } = "";

    /// <summary>The paragraph runs, in order, covering the text and its terminating break.</summary>
    public IReadOnlyList<PptParagraphRun> ParagraphRuns { get; internal set; } = [];

    /// <summary>The character runs, in order.</summary>
    public IReadOnlyList<PptCharRun> CharRuns { get; internal set; } = [];

    /// <summary>The language runs, in order.</summary>
    public IReadOnlyList<PptLanguageRun> LanguageRuns { get; internal set; } = [];

    /// <summary>The fields.</summary>
    public IReadOnlyList<PptMetaChar> MetaChars { get; internal set; } = [];

    /// <summary>The text ruler, or null.</summary>
    public PptRuler? Ruler { get; internal set; }

    /// <summary>Indent levels of master text (<c>MasterTextPropAtom</c>): count and level per run.</summary>
    public IReadOnlyList<(int Count, int Level)> MasterLevels { get; internal set; } = [];

    /// <summary>For a body that only points at text kept in the slide list (<c>OutlineTextRefAtom</c>), the index it names; otherwise -1.</summary>
    public int OutlineReference { get; internal set; } = -1;

    /// <summary>The stream offset of the first record of the body.</summary>
    public long Offset { get; internal set; }

    /// <summary>A copy of the body, for callers that replace one member (a ruler kept in the shape rather than the list).</summary>
    internal PptTextBody Clone() => (PptTextBody)MemberwiseClone();

    /// <summary>The interactive-info records (hyperlinks and actions) that follow the text, kept as records.</summary>
    public IReadOnlyList<ArtRecord> Interactive { get; internal set; } = [];
}

/// <summary>Style of one text type on a master: the paragraph and character formatting of up to five levels ([MS-PPT] §2.9.35).</summary>
/// <param name="TextType">The <c>TextTypeEnum</c> the style applies to (the record's instance).</param>
/// <param name="Levels">The level formats, level 0 first; fewer than five when the record says so.</param>
public sealed record PptMasterStyle(int TextType, IReadOnlyList<(PptParagraphFormat Paragraph, PptCharFormat Character)> Levels);

/// <summary>Reads the text records of PowerPoint files.</summary>
public static class PptText
{
    /// <summary>
    /// Splits records into text bodies. A body starts at a <c>TextHeaderAtom</c> (or an <c>OutlineTextRefAtom</c>) and takes the characters,
    /// style, field, ruler and interaction records that follow, up to the next header or slide persist atom.
    /// </summary>
    /// <param name="records">Records in stream order (a slide list's children, or an <c>OfficeArtClientTextbox</c>'s payload records).</param>
    /// <param name="context">Where repairs go.</param>
    /// <returns>The bodies in order.</returns>
    public static IReadOnlyList<PptTextBody> ReadBodies(IEnumerable<ArtRecord> records, ArtReadContext context)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(context);
        var bodies = new List<PptTextBody>();
        PptTextBody? current = null;
        List<ArtRecord> group = [];
        void Flush()
        {
            if (current is not null)
            {
                Finish(current, group, context);
            }

            group = [];
        }

        foreach (ArtRecord record in records)
        {
            switch (record.Type)
            {
                case PptRecordType.SlidePersistAtom:
                    Flush();
                    current = null;
                    break;
                case PptRecordType.TextHeaderAtom:
                    Flush();
                    current = new PptTextBody { TextType = record.Int32At(0, 4), Instance = record.Header.Instance, Offset = record.Offset };
                    bodies.Add(current);
                    break;
                case PptRecordType.OutlineTextRefAtom:
                    Flush();
                    current = new PptTextBody { OutlineReference = record.Int32At(0, -1), Offset = record.Offset };
                    bodies.Add(current);
                    break;
                default:
                    if (current is null && record.Type is PptRecordType.TextCharsAtom or PptRecordType.TextBytesAtom)
                    {
                        // Text with no header atom: treat it as "other" text rather than lose it.
                        current = new PptTextBody { Offset = record.Offset };
                        bodies.Add(current);
                    }

                    if (current is not null)
                    {
                        group.Add(record);
                    }

                    break;
            }
        }

        Flush();
        return bodies;
    }

    private static void Finish(PptTextBody body, List<ArtRecord> records, ArtReadContext context)
    {
        string text = "";
        ArtRecord? style = null;
        var meta = new List<PptMetaChar>();
        var interactive = new List<ArtRecord>();
        foreach (ArtRecord record in records)
        {
            switch (record.Type)
            {
                case PptRecordType.TextCharsAtom:
                    text = Decode16(record.Payload.Span);
                    break;
                case PptRecordType.TextBytesAtom:
                    text = DecodeLatin1(record.Payload.Span);
                    break;
                case PptRecordType.StyleTextPropAtom:
                    style = record;
                    break;
                case PptRecordType.SlideNumberMetaCharAtom:
                    meta.Add(new PptMetaChar(record.Int32At(0), PptMetaCharKind.SlideNumber, -1));
                    break;
                case PptRecordType.DateTimeMetaCharAtom:
                    meta.Add(new PptMetaChar(record.Int32At(0), PptMetaCharKind.DateTime, record.Payload.Length > 4 ? record.Payload.Span[4] : -1));
                    break;
                case PptRecordType.GenericDateMetaCharAtom:
                    meta.Add(new PptMetaChar(record.Int32At(0), PptMetaCharKind.GenericDate, -1));
                    break;
                case PptRecordType.HeaderMetaCharAtom:
                    meta.Add(new PptMetaChar(record.Int32At(0), PptMetaCharKind.Header, -1));
                    break;
                case PptRecordType.FooterMetaCharAtom:
                    meta.Add(new PptMetaChar(record.Int32At(0), PptMetaCharKind.Footer, -1));
                    break;
                case PptRecordType.RtfDateTimeMetaCharAtom:
                    meta.Add(new PptMetaChar(record.Int32At(0), PptMetaCharKind.RtfDateTime, -1));
                    break;
                case PptRecordType.TextSpecialInfoAtom:
                    body.LanguageRuns = ReadLanguageRuns(record, context);
                    break;
                case PptRecordType.TextRulerAtom:
                    body.Ruler = ReadRuler(record, context);
                    break;
                case PptRecordType.MasterTextPropAtom:
                    body.MasterLevels = ReadMasterLevels(record);
                    break;
                case PptRecordType.InteractiveInfo:
                case PptRecordType.TextInteractiveInfoAtom:
                    interactive.Add(record);
                    break;
            }
        }

        body.Text = text;
        body.MetaChars = meta;
        body.Interactive = interactive;
        if (style is not null)
        {
            ReadStyle(body, style, context);
        }
    }

    // [MS-PPT] §2.9.44: the paragraph runs come first, then the character runs; each family's counts sum to the text length plus the
    // terminating break. Damaged counts are tolerated: the reader stops at the record's end.
    private static void ReadStyle(PptTextBody body, ArtRecord record, ArtReadContext context)
    {
        ReadOnlySpan<byte> data = record.Payload.Span;
        int position = 0;
        int total = body.Text.Length + 1;
        var paragraphs = new List<PptParagraphRun>();
        int covered = 0;
        while (covered < total && position + 6 <= data.Length)
        {
            int count = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(data[position..]));
            int level = BinaryPrimitives.ReadUInt16LittleEndian(data[(position + 4)..]);
            position += 6;
            if (!TryReadParagraph(data, ref position, out PptParagraphFormat? format))
            {
                context.Repair("ppt.text-style-truncated", "A paragraph formatting run is cut short by the end of its record.", record.Offset);
                break;
            }

            paragraphs.Add(new PptParagraphRun(count, Math.Min(level, 4), format!));
            covered += count;
        }

        var chars = new List<PptCharRun>();
        covered = 0;
        while (covered < total && position + 4 <= data.Length)
        {
            int count = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(data[position..]));
            position += 4;
            if (!TryReadCharacter(data, ref position, out PptCharFormat? format))
            {
                context.Repair("ppt.text-style-truncated", "A character formatting run is cut short by the end of its record.", record.Offset);
                break;
            }

            chars.Add(new PptCharRun(count, format!));
            covered += count;
        }

        body.ParagraphRuns = paragraphs;
        body.CharRuns = chars;
    }

    /// <summary>Reads a <c>TextPFException</c> at <paramref name="position"/>, advancing it. Returns false (position unspecified) when the data ends first.</summary>
    /// <param name="data">The record payload.</param>
    /// <param name="position">The position to read at; advanced past the structure.</param>
    /// <param name="format">The format read.</param>
    public static bool TryReadParagraph(ReadOnlySpan<byte> data, ref int position, out PptParagraphFormat? format)
    {
        format = null;
        if (position + 4 > data.Length)
        {
            return false;
        }

        uint masks = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
        position += 4;
        var pf = new PptParagraphFormat { Masks = (PptParagraphMasks)(masks & 0x3FFFFFF) };
        if ((masks & 0xF) != 0)
        {
            if ((position + 2 > data.Length))
            {
                return false;
            }

            pf.BulletFlags = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
            position += 2;
        }

        if ((masks & (1u << 7)) != 0)
        {
            if ((position + 2 > data.Length))
            {
                return false;
            }

            pf.BulletChar = (char)BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
            position += 2;
        }

        if ((masks & (1u << 4)) != 0)
        {
            if ((position + 2 > data.Length))
            {
                return false;
            }

            pf.BulletFontRef = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
            position += 2;
        }

        if ((masks & (1u << 6)) != 0)
        {
            if ((position + 2 > data.Length))
            {
                return false;
            }

            pf.BulletSize = BinaryPrimitives.ReadInt16LittleEndian(data[position..]);
            position += 2;
        }

        if ((masks & (1u << 5)) != 0)
        {
            if ((position + 4 > data.Length))
            {
                return false;
            }

            pf.BulletColor = new PptColor(data[position], data[position + 1], data[position + 2], data[position + 3]);
            position += 4;
        }

        bool good;
        pf.Alignment = ReadField(data, ref position, masks, 11, false, out good);
        if (!good)
        {
            return false;
        }

        pf.LineSpacing = ReadField(data, ref position, masks, 12, true, out good);
        if (!good)
        {
            return false;
        }

        pf.SpaceBefore = ReadField(data, ref position, masks, 13, true, out good);
        if (!good)
        {
            return false;
        }

        pf.SpaceAfter = ReadField(data, ref position, masks, 14, true, out good);
        if (!good)
        {
            return false;
        }

        pf.LeftMargin = ReadField(data, ref position, masks, 8, true, out good);
        if (!good)
        {
            return false;
        }

        pf.Indent = ReadField(data, ref position, masks, 10, true, out good);
        if (!good)
        {
            return false;
        }

        pf.DefaultTabSize = ReadField(data, ref position, masks, 15, true, out good);
        if (!good)
        {
            return false;
        }

        if ((masks & (1u << 20)) != 0)
        {
            if (!TryReadTabs(data, ref position, out IReadOnlyList<(int, int)> tabs))
            {
                return false;
            }

            pf.TabStops = tabs;
        }

        pf.FontAlign = ReadField(data, ref position, masks, 16, false, out good);
        if (!good)
        {
            return false;
        }

        if ((masks & ((1u << 17) | (1u << 18) | (1u << 19))) != 0)
        {
            if ((position + 2 > data.Length))
            {
                return false;
            }

            position += 2;
        }

        pf.TextDirection = ReadField(data, ref position, masks, 21, false, out good);
        if (!good)
        {
            return false;
        }

        format = pf;
        return true;
    }

    // Reads a two-byte field that exists when its bit is set in the mask. ok is false when the data ends first.
    private static int ReadField(ReadOnlySpan<byte> data, ref int position, uint masks, int bit, bool signed, out bool ok)
    {
        ok = true;
        if ((masks & (1u << bit)) == 0)
        {
            return 0;
        }

        if (position + 2 > data.Length)
        {
            ok = false;
            return 0;
        }

        int value = signed ? BinaryPrimitives.ReadInt16LittleEndian(data[position..]) : BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
        position += 2;
        return value;
    }

    private static bool TryReadTabs(ReadOnlySpan<byte> data, ref int position, out IReadOnlyList<(int Position, int Type)> tabs)
    {
        tabs = [];
        if (position + 2 > data.Length)
        {
            return false;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
        position += 2;
        if (position + (count * 4L) > data.Length)
        {
            return false;
        }

        var list = new List<(int, int)>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add((BinaryPrimitives.ReadInt16LittleEndian(data[position..]), BinaryPrimitives.ReadUInt16LittleEndian(data[(position + 2)..])));
            position += 4;
        }

        tabs = list;
        return true;
    }

    /// <summary>Reads a <c>TextCFException</c> at <paramref name="position"/>, advancing it. Returns false when the data ends first.</summary>
    /// <param name="data">The record payload.</param>
    /// <param name="position">The position to read at; advanced past the structure.</param>
    /// <param name="format">The format read.</param>
    public static bool TryReadCharacter(ReadOnlySpan<byte> data, ref int position, out PptCharFormat? format)
    {
        format = null;
        if (position + 4 > data.Length)
        {
            return false;
        }

        uint masks = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
        position += 4;
        var cf = new PptCharFormat { Masks = masks };
        const uint StyleBits = 0x1 | 0x2 | 0x4 | 0x10 | 0x20 | 0x80 | 0x200 | 0x3C00;
        if ((masks & StyleBits) != 0)
        {
            if ((position + 2 > data.Length))
            {
                return false;
            }

            cf.Style = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
            position += 2;
        }

        bool good;
        cf.FontRef = ReadField(data, ref position, masks, 16, false, out good);
        if (!good)
        {
            return false;
        }

        cf.OldEaFontRef = ReadField(data, ref position, masks, 21, false, out good);
        if (!good)
        {
            return false;
        }

        cf.AnsiFontRef = ReadField(data, ref position, masks, 22, false, out good);
        if (!good)
        {
            return false;
        }

        cf.SymbolFontRef = ReadField(data, ref position, masks, 23, false, out good);
        if (!good)
        {
            return false;
        }

        cf.FontSize = ReadField(data, ref position, masks, 17, false, out good);
        if (!good)
        {
            return false;
        }

        if ((masks & (1u << 18)) != 0)
        {
            if ((position + 4 > data.Length))
            {
                return false;
            }

            cf.Color = new PptColor(data[position], data[position + 1], data[position + 2], data[position + 3]);
            position += 4;
        }

        if ((masks & (1u << 19)) != 0)
        {
            if ((position + 2 > data.Length))
            {
                return false;
            }

            cf.Position = BinaryPrimitives.ReadInt16LittleEndian(data[position..]);
            position += 2;
        }

        format = cf;
        return true;
    }

    /// <summary>Reads a <c>TextMasterStyleAtom</c> ([MS-PPT] §2.9.35).</summary>
    /// <param name="record">The atom.</param>
    /// <param name="context">Where repairs go.</param>
    /// <returns>The style; levels that cannot be read are left out.</returns>
    public static PptMasterStyle ReadMasterStyle(ArtRecord record, ArtReadContext context)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(context);
        ReadOnlySpan<byte> data = record.Payload.Span;
        var levels = new List<(PptParagraphFormat, PptCharFormat)>();
        int type = record.Header.Instance;
        if (data.Length < 2)
        {
            return new PptMasterStyle(type, levels);
        }

        int count = Math.Min((int)BinaryPrimitives.ReadUInt16LittleEndian(data), 5);
        int position = 2;
        for (int i = 0; i < count; i++)
        {
            if (type >= 5)
            {
                // §2.9.36: the level number is present only for the instances from 5 on.
                position += 2;
            }

            if (!TryReadParagraph(data, ref position, out PptParagraphFormat? pf) || !TryReadCharacter(data, ref position, out PptCharFormat? cf))
            {
                context.Repair("ppt.text-style-truncated", "A master text style level is cut short by the end of its record.", record.Offset);
                break;
            }

            levels.Add((pf!, cf!));
        }

        return new PptMasterStyle(type, levels);
    }

    private static List<PptLanguageRun> ReadLanguageRuns(ArtRecord record, ArtReadContext context)
    {
        ReadOnlySpan<byte> data = record.Payload.Span;
        var runs = new List<PptLanguageRun>();
        int position = 0;
        while (position + 8 <= data.Length)
        {
            int count = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(data[position..]));
            uint masks = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 4)..]);
            position += 8;
            int lid = 0;
            // §2.9.32: spellInfo, lid, altLid, bidi, then the pp10 word; the smart tag list ends the structure and is not read.
            if ((masks & 1) != 0)
            {
                position += 2;
            }

            if ((masks & 2) != 0)
            {
                if (position + 2 > data.Length)
                {
                    break;
                }

                lid = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
                position += 2;
            }

            if ((masks & 4) != 0)
            {
                position += 2;
            }

            if ((masks & (1u << 6)) != 0)
            {
                position += 2;
            }

            if ((masks & (1u << 5)) != 0)
            {
                position += 4;
            }

            if ((masks & (1u << 9)) != 0)
            {
                context.Repair("ppt.text-special-info-unread", "Smart tags in a text special info record are not read; later language runs of the body are ignored.", record.Offset);
                runs.Add(new PptLanguageRun(count, lid));
                break;
            }

            runs.Add(new PptLanguageRun(count, lid));
        }

        return runs;
    }

    private static PptRuler? ReadRuler(ArtRecord record, ArtReadContext context)
    {
        ReadOnlySpan<byte> data = record.Payload.Span;
        if (data.Length < 4)
        {
            return null;
        }

        uint masks = BinaryPrimitives.ReadUInt32LittleEndian(data);
        int position = 4;
        var ruler = new PptRuler();
        if ((masks & 2) != 0)
        {
            // cLevels: only the count; margins of every level follow by their own mask bits.
            if ((position + 2 > data.Length))
            {
                return ruler;
            }

            position += 2;
        }

        if ((masks & 1) != 0)
        {
            if ((position + 2 > data.Length))
            {
                return ruler;
            }

            ruler.DefaultTabSize = BinaryPrimitives.ReadInt16LittleEndian(data[position..]);
            position += 2;
        }

        if ((masks & 4) != 0)
        {
            if (!TryReadTabs(data, ref position, out IReadOnlyList<(int, int)> tabs))
            {
                context.Repair("ppt.text-style-truncated", "A text ruler is cut short by the end of its record.", record.Offset);
                return ruler;
            }

            ruler.TabStops = tabs;
        }

        // §2.9.30: leftMargin1, indent1, leftMargin2, indent2 ... in this order, each only if its mask bit is set.
        for (int level = 0; level < 5; level++)
        {
            if ((masks & (1u << (3 + level))) != 0)
            {
                if ((position + 2 > data.Length))
                {
                    return ruler;
                }

                ruler.LeftMargin[level] = BinaryPrimitives.ReadInt16LittleEndian(data[position..]);
                position += 2;
            }

            if ((masks & (1u << (8 + level))) != 0)
            {
                if ((position + 2 > data.Length))
                {
                    return ruler;
                }

                ruler.Indent[level] = BinaryPrimitives.ReadInt16LittleEndian(data[position..]);
                position += 2;
            }
        }

        return ruler;
    }

    private static List<(int Count, int Level)> ReadMasterLevels(ArtRecord record)
    {
        ReadOnlySpan<byte> data = record.Payload.Span;
        var list = new List<(int, int)>();
        for (int position = 0; position + 6 <= data.Length; position += 6)
        {
            list.Add(((int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(data[position..])), BinaryPrimitives.ReadUInt16LittleEndian(data[(position + 4)..])));
        }

        return list;
    }

    private static string Decode16(ReadOnlySpan<byte> bytes)
    {
        int chars = bytes.Length / 2;
        return string.Create(chars, bytes.ToArray(), static (span, b) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(i * 2));
            }
        });
    }

    // [MS-PPT] §2.9.43: each byte is the low byte of a UTF-16 character whose high byte is zero.
    private static string DecodeLatin1(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);
}
