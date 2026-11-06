using System.Buffers.Binary;

namespace RecursiveAI.OneDocument.OfficeArt;

/// <summary>Property identifiers of the OfficeArt property table ([MS-ODRAW] §2.3) that converters read. Other identifiers are kept in the table and can be read by number.</summary>
public static class ArtPropertyId
{
    /// <summary>rotation: a 16.16 fixed-point angle in degrees (§2.3.2.1).</summary>
    public const int Rotation = 0x0004;

    /// <summary>dxTextLeft: left text inset in EMU, default 91440 (§2.3.21).</summary>
    public const int TextLeft = 0x0081;

    /// <summary>dyTextTop: default 45720.</summary>
    public const int TextTop = 0x0082;

    /// <summary>dxTextRight: default 91440.</summary>
    public const int TextRight = 0x0083;

    /// <summary>dyTextBottom: default 45720.</summary>
    public const int TextBottom = 0x0084;

    /// <summary>WrapText: 0 square, 1 by points, 2 none, 3 tight, 4 through.</summary>
    public const int WrapText = 0x0085;

    /// <summary>anchorText: vertical anchor of the text (0 top, 1 middle, 2 bottom, 3-5 centred variants).</summary>
    public const int AnchorText = 0x0087;

    /// <summary>txflTextFlow: 0 horizontal, 1 top-to-bottom, 2 bottom-to-top, 3 top-to-bottom non-rotated, 4 vertical Latin.</summary>
    public const int TextFlow = 0x0088;

    /// <summary>Text boolean properties (fFitShapeToText 0x2, fAutoTextMargin 0x8).</summary>
    public const int TextBooleans = 0x00BF;

    /// <summary>cropFromTop (§2.3.23): 16.16 fraction of the picture height.</summary>
    public const int CropFromTop = 0x0100;

    /// <summary>cropFromBottom.</summary>
    public const int CropFromBottom = 0x0101;

    /// <summary>cropFromLeft.</summary>
    public const int CropFromLeft = 0x0102;

    /// <summary>cropFromRight.</summary>
    public const int CropFromRight = 0x0103;

    /// <summary>pib: the one-based index into the BLIP store of the shape's picture.</summary>
    public const int Pib = 0x0104;

    /// <summary>pibName: the picture file name (complex, UTF-16).</summary>
    public const int PibName = 0x0105;

    /// <summary>pictureBrightness (16.16 fraction; 0x8000 is neutral on some hosts).</summary>
    public const int PictureBrightness = 0x0109;

    /// <summary>pictureContrast.</summary>
    public const int PictureContrast = 0x0108;

    /// <summary>geoLeft: the left of the geometry coordinate space.</summary>
    public const int GeoLeft = 0x0140;

    /// <summary>geoTop.</summary>
    public const int GeoTop = 0x0141;

    /// <summary>geoRight.</summary>
    public const int GeoRight = 0x0142;

    /// <summary>geoBottom.</summary>
    public const int GeoBottom = 0x0143;

    /// <summary>shapePath: 0 lines, 1 lines closed, 2 curves, 3 curves closed, 4 complex.</summary>
    public const int ShapePath = 0x0144;

    /// <summary>pVertices: complex array of points.</summary>
    public const int Vertices = 0x0145;

    /// <summary>pSegmentInfo: complex array of path segment commands.</summary>
    public const int SegmentInfo = 0x0146;

    /// <summary>adjustValue (adjust 1); adjust 2 to 8 follow at 0x0148 to 0x014E.</summary>
    public const int Adjust1 = 0x0147;

    /// <summary>pGuides: complex array of guide formulas.</summary>
    public const int Guides = 0x0156;

    /// <summary>Geometry boolean properties.</summary>
    public const int GeometryBooleans = 0x017F;

    /// <summary>fillType: 0 solid, 1 pattern, 2 texture, 3 picture, 4 shade, 5 shade to corner, 6 shade to title, 7 background.</summary>
    public const int FillType = 0x0180;

    /// <summary>fillColor: an OfficeArtCOLORREF.</summary>
    public const int FillColor = 0x0181;

    /// <summary>fillOpacity: 16.16 fraction, 0x10000 opaque.</summary>
    public const int FillOpacity = 0x0182;

    /// <summary>fillBackColor.</summary>
    public const int FillBackColor = 0x0183;

    /// <summary>fillBlip: the one-based BLIP index of a picture, texture or pattern fill.</summary>
    public const int FillBlip = 0x0186;

    /// <summary>fillAngle: 16.16 degrees of a gradient.</summary>
    public const int FillAngle = 0x018B;

    /// <summary>fillFocus.</summary>
    public const int FillFocus = 0x018C;

    /// <summary>fillShadeColors: complex array of colour stops.</summary>
    public const int FillShadeColors = 0x0197;

    /// <summary>Fill boolean properties (fFilled 0x10).</summary>
    public const int FillBooleans = 0x01BF;

    /// <summary>lineColor.</summary>
    public const int LineColor = 0x01C0;

    /// <summary>lineOpacity.</summary>
    public const int LineOpacity = 0x01C1;

    /// <summary>lineBackColor.</summary>
    public const int LineBackColor = 0x01C2;

    /// <summary>lineType: 0 solid, 1 pattern, 2 texture, 3 picture.</summary>
    public const int LineType = 0x01C4;

    /// <summary>lineWidth in EMU, default 9525.</summary>
    public const int LineWidth = 0x01CB;

    /// <summary>lineStyle: 0 single, 1 double, 2 thick-thin, 3 thin-thick, 4 triple.</summary>
    public const int LineStyle = 0x01CD;

    /// <summary>lineDashing: 0 solid, then system and generic dash styles.</summary>
    public const int LineDashing = 0x01CE;

    /// <summary>lineStartArrowhead: 0 none, 1 arrow, 2 stealth, 3 diamond, 4 oval, 5 open arrow.</summary>
    public const int LineStartArrowhead = 0x01D0;

    /// <summary>lineEndArrowhead.</summary>
    public const int LineEndArrowhead = 0x01D1;

    /// <summary>lineStartArrowWidth (0 narrow, 1 medium, 2 wide).</summary>
    public const int LineStartArrowWidth = 0x01D2;

    /// <summary>lineStartArrowLength.</summary>
    public const int LineStartArrowLength = 0x01D3;

    /// <summary>lineEndArrowWidth.</summary>
    public const int LineEndArrowWidth = 0x01D4;

    /// <summary>lineEndArrowLength.</summary>
    public const int LineEndArrowLength = 0x01D5;

    /// <summary>lineJoinStyle: 0 bevel, 1 miter, 2 round.</summary>
    public const int LineJoinStyle = 0x01D6;

    /// <summary>lineEndCapStyle: 0 round, 1 square, 2 flat.</summary>
    public const int LineEndCapStyle = 0x01D7;

    /// <summary>Line boolean properties (fLine 0x8).</summary>
    public const int LineBooleans = 0x01FF;

    /// <summary>shadowColor.</summary>
    public const int ShadowColor = 0x0201;

    /// <summary>shadowOpacity.</summary>
    public const int ShadowOpacity = 0x0204;

    /// <summary>shadowOffsetX in EMU, default 25400.</summary>
    public const int ShadowOffsetX = 0x0205;

    /// <summary>shadowOffsetY in EMU, default 25400.</summary>
    public const int ShadowOffsetY = 0x0206;

    /// <summary>Shadow boolean properties (fShadow 0x2).</summary>
    public const int ShadowBooleans = 0x023F;

    /// <summary>Shape boolean properties.</summary>
    public const int ShapeBooleans = 0x033F;

    /// <summary>wzName: the shape name (complex, UTF-16).</summary>
    public const int Name = 0x0380;

    /// <summary>wzDescription: the shape description, used as alternative text (complex, UTF-16).</summary>
    public const int Description = 0x0381;

    /// <summary>pihlShape: the hyperlink of the shape.</summary>
    public const int Hyperlink = 0x0382;

    /// <summary>Group shape boolean properties (fHidden 0x2, fPrint 0x1).</summary>
    public const int GroupBooleans = 0x03BF;

    /// <summary>Table properties (a 1 here marks a table shape in the binary PowerPoint format).</summary>
    public const int TableProperties = 0x039F;

    /// <summary>Table row properties.</summary>
    public const int TableRowProperties = 0x03A0;
}

/// <summary>One entry of an OfficeArt property table.</summary>
/// <param name="Id">The 14-bit property number.</param>
/// <param name="IsBlipId">True when <see cref="Value"/> is a BLIP index (<c>fBid</c>).</param>
/// <param name="IsComplex">True when <see cref="Complex"/> holds extra data and <see cref="Value"/> is its length.</param>
/// <param name="Value">The operand: the value, a BLIP index or the size of the complex data.</param>
/// <param name="Complex">The complex data, pointing into the record; empty unless <see cref="IsComplex"/>.</param>
public readonly record struct ArtProperty(int Id, bool IsBlipId, bool IsComplex, uint Value, ReadOnlyMemory<byte> Complex);

/// <summary>An OfficeArtCOLORREF: three channel bytes and a flag byte ([MS-ODRAW] §2.2.2).</summary>
/// <param name="Raw">The 32-bit value as stored.</param>
public readonly record struct ArtColor(uint Raw)
{
    /// <summary>The red channel, or a palette or scheme index when <see cref="IsSchemeIndex"/> is set.</summary>
    public byte Red => (byte)(Raw & 0xFF);

    /// <summary>The green channel.</summary>
    public byte Green => (byte)((Raw >> 8) & 0xFF);

    /// <summary>The blue channel.</summary>
    public byte Blue => (byte)((Raw >> 16) & 0xFF);

    /// <summary>The flag byte.</summary>
    public byte Flags => (byte)(Raw >> 24);

    /// <summary>True when the red byte indexes the host's colour scheme (<c>fSchemeIndex</c>, 0x08).</summary>
    public bool IsSchemeIndex => (Flags & 0x08) != 0;

    /// <summary>True when the value names a system colour (<c>fSysIndex</c>, 0x10); the low bytes then hold the index and a mode.</summary>
    public bool IsSystemIndex => (Flags & 0x10) != 0;

    /// <summary>The colour as six hex digits (upper case), ignoring the flags.</summary>
    public string ToHex() => string.Create(6, this, static (span, c) =>
    {
        const string Digits = "0123456789ABCDEF";
        span[0] = Digits[c.Red >> 4];
        span[1] = Digits[c.Red & 15];
        span[2] = Digits[c.Green >> 4];
        span[3] = Digits[c.Green & 15];
        span[4] = Digits[c.Blue >> 4];
        span[5] = Digits[c.Blue & 15];
    });
}

/// <summary>
/// The property table of a shape: the primary table and any secondary or tertiary tables merged, later tables overriding earlier ones.
/// </summary>
public sealed class ArtProperties
{
    private readonly Dictionary<int, ArtProperty> _items = [];

    /// <summary>An empty table.</summary>
    public static ArtProperties Empty { get; } = new();

    /// <summary>The number of properties.</summary>
    public int Count => _items.Count;

    /// <summary>All properties, in no particular order.</summary>
    public IEnumerable<ArtProperty> All => _items.Values;

    /// <summary>
    /// Adds the properties of a table record (<c>OfficeArtFOPT</c>, secondary or tertiary). The entry count is the record's instance; a count
    /// that runs past the payload is clamped and reported as <c>art.property-overrun</c>; complex data past the payload is dropped the same way.
    /// </summary>
    /// <param name="table">An OfficeArtFOPT, OfficeArtSecondaryFOPT or OfficeArtTertiaryFOPT record.</param>
    /// <param name="context">Where repairs are reported.</param>
    public void AddTable(ArtRecord table, ArtReadContext context)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(context);
        ReadOnlySpan<byte> data = table.Payload.Span;
        int count = table.Header.Instance;
        if ((long)count * 6 > data.Length)
        {
            context.Repair("art.property-overrun", "A property table claims " + count + " entries but holds room for " + data.Length / 6 + ".", table.Offset);
            count = data.Length / 6;
        }

        // [MS-ODRAW] §2.2.9: the fixed six-byte entries come first, then the data of the complex ones in entry order.
        int complexAt = count * 6;
        for (int i = 0; i < count; i++)
        {
            ushort opid = BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 6)..]);
            uint op = BinaryPrimitives.ReadUInt32LittleEndian(data[(i * 6 + 2)..]);
            bool complex = (opid & 0x8000) != 0;
            ReadOnlyMemory<byte> extra = default;
            if (complex)
            {
                long size = op;
                if (complexAt + size > data.Length)
                {
                    context.Repair("art.property-overrun", "The complex data of property 0x" + (opid & 0x3FFF).ToString("X", System.Globalization.CultureInfo.InvariantCulture) + " runs past the table.", table.Offset);
                    size = Math.Max(0, data.Length - complexAt);
                }

                extra = table.Payload.Slice(complexAt, (int)size);
                complexAt += (int)size;
            }

            int id = opid & 0x3FFF;
            _items[id] = new ArtProperty(id, (opid & 0x4000) != 0, complex, op, extra);
        }
    }

    /// <summary>Reads the property with the given number.</summary>
    /// <param name="id">The property number (see <see cref="ArtPropertyId"/>).</param>
    /// <param name="property">The property, when present.</param>
    public bool TryGet(int id, out ArtProperty property) => _items.TryGetValue(id, out property);

    /// <summary>True when the table sets the property.</summary>
    /// <param name="id">The property number.</param>
    public bool Has(int id) => _items.ContainsKey(id);

    /// <summary>The operand as a signed 32-bit value, or <paramref name="fallback"/>.</summary>
    /// <param name="id">The property number.</param>
    /// <param name="fallback">The value when the table does not set it.</param>
    public int GetInt(int id, int fallback = 0) => _items.TryGetValue(id, out ArtProperty p) ? unchecked((int)p.Value) : fallback;

    /// <summary>The operand as a colour, or null when the table does not set it.</summary>
    /// <param name="id">The property number.</param>
    public ArtColor? GetColor(int id) => _items.TryGetValue(id, out ArtProperty p) ? new ArtColor(p.Value) : null;

    /// <summary>
    /// Reads one flag of a boolean property set. Each flag has a "use" bit 16 positions higher; a flag whose use bit is clear is not set by
    /// this table and the default applies ([MS-ODRAW] §2.3, boolean property sets).
    /// </summary>
    /// <param name="setId">The property number of the boolean set (for example <see cref="ArtPropertyId.FillBooleans"/>).</param>
    /// <param name="flag">The flag's bit in the low half (for example 0x10 for <c>fFilled</c>).</param>
    /// <param name="fallback">The value when the use bit is clear or the set is absent.</param>
    public bool GetFlag(int setId, int flag, bool fallback)
    {
        if (!_items.TryGetValue(setId, out ArtProperty p) || (p.Value & ((uint)flag << 16)) == 0)
        {
            return fallback;
        }

        return (p.Value & (uint)flag) != 0;
    }

    /// <summary>The complex data of a property decoded as UTF-16 text with the terminating NULs removed, or null.</summary>
    /// <param name="id">The property number.</param>
    public string? GetString(int id)
    {
        if (!_items.TryGetValue(id, out ArtProperty p) || !p.IsComplex)
        {
            return null;
        }

        ReadOnlySpan<byte> bytes = p.Complex.Span;
        int chars = bytes.Length / 2;
        var buffer = new char[chars];
        for (int i = 0; i < chars; i++)
        {
            buffer[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i * 2)..]);
        }

        return new string(buffer).TrimEnd('\0');
    }

    /// <summary>
    /// Reads an IMsoArray property ([MS-ODRAW] §2.2.51): element count, allocated count, element size, then the elements. An element size of
    /// 0xFFF0 means each element is four bytes stored as a pair of 16-bit values (used for fixed-point or two-component elements in some arrays).
    /// </summary>
    /// <param name="id">The property number.</param>
    /// <param name="elementSize">The size of each element in bytes (4 for a pair of 16-bit values reported as 4).</param>
    /// <returns>The element bytes in order, or an empty list when the property is absent or damaged.</returns>
    public IReadOnlyList<ReadOnlyMemory<byte>> GetArray(int id, out int elementSize)
    {
        elementSize = 0;
        if (!_items.TryGetValue(id, out ArtProperty p) || !p.IsComplex || p.Complex.Length < 6)
        {
            return [];
        }

        ReadOnlySpan<byte> head = p.Complex.Span;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(head);
        int size = BinaryPrimitives.ReadUInt16LittleEndian(head[4..]);
        // §2.2.51: cbElem 0xFFF0 is a four-byte element whose two halves are 16-bit values.
        int width = size == 0xFFF0 ? 4 : size;
        elementSize = width;
        if (width <= 0)
        {
            return [];
        }

        var items = new List<ReadOnlyMemory<byte>>(Math.Min(count, 4096));
        for (int i = 0; i < count; i++)
        {
            int at = 6 + (i * width);
            if (at + width > p.Complex.Length)
            {
                break;
            }

            items.Add(p.Complex.Slice(at, width));
        }

        return items;
    }
}
