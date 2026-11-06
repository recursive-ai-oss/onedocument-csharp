using System.Buffers.Binary;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Ole;

public sealed partial class OlePropertySets
{
    /// <summary>Reads a property set stream.</summary>
    /// <param name="data">The stream's bytes.</param>
    /// <param name="context">The limits and the defect sink; null for the defaults.</param>
    /// <param name="streamName">The stream's name, for defect locations (for example <c>/\u0005SummaryInformation</c>).</param>
    /// <returns>The stream. Damaged properties are left out and reported to the context's defect sink.</returns>
    /// <exception cref="OfficeFormatException">The header is unusable: the stream is too short, has a byte order other than little-endian, or names no property set (<c>ole.propset-header</c>).</exception>
    public static OlePropertySets Read(ReadOnlyMemory<byte> data, OpenContext? context = null, string streamName = "")
    {
        context ??= new OpenContext();
        return new OlePropsReader(data, context, streamName).ReadStream();
    }

    /// <summary>Reads a property set stream without raising for an unusable header; the fatal defect goes to the context's sink instead.</summary>
    /// <param name="data">The stream's bytes.</param>
    /// <param name="stream">The stream, or null.</param>
    /// <param name="context">The limits and the defect sink; null for the defaults.</param>
    /// <param name="streamName">The stream's name, for defect locations.</param>
    /// <returns>True if the stream could be read.</returns>
    public static bool TryRead(ReadOnlyMemory<byte> data, out OlePropertySets? stream, OpenContext? context = null, string streamName = "")
    {
        OpenContext ctx = context ?? new OpenContext();
        try
        {
            stream = Read(data, ctx, streamName);
            return true;
        }
        catch (OfficeException ex)
        {
            ctx.Defects.Report(ex.Defect);
            stream = null;
            return false;
        }
    }
}

/// <summary>The reader of [MS-OLEPS] structures. Offsets in every defect are offsets into the stream.</summary>
internal sealed class OlePropsReader
{
    private const int HeaderSize = 28; // [MS-OLEPS] §2.21: ByteOrder, Version, SystemIdentifier, CLSID, NumPropertySets.
    private const int MaxSets = 64;

    private readonly byte[] _data;
    private readonly OpenContext _context;
    private readonly string _name;
    private int _setBase;
    private int _codePage = OlePropertySet.UnicodeCodePage;
    private bool _codePageKnown;
    private bool _codePageReported;
    private Encoding? _encoding;
    private OlePropertySet? _current;

    internal OlePropsReader(ReadOnlyMemory<byte> data, OpenContext context, string name)
    {
        _data = data.ToArray();
        _context = context;
        _name = name;
    }

    private sealed class Damage(string code, DefectKind kind, string message, long offset) : Exception(message)
    {
        public string Code { get; } = code;

        public DefectKind Kind { get; } = kind;

        public long Offset { get; } = offset;
    }

    internal OlePropertySets ReadStream()
    {
        byte[] d = _data;
        if (d.Length < HeaderSize)
        {
            throw Fatal("The property set stream is " + d.Length + " bytes; its header alone is 28.", 0);
        }

        ushort order = BinaryPrimitives.ReadUInt16LittleEndian(d);
        if (order != 0xFFFE)
        {
            throw Fatal("The byte order mark is 0x" + order.ToString("X4") + ", not 0xFFFE; only little-endian property sets are read.", 0);
        }

        var stream = new OlePropertySets
        {
            Version = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(2)),
            SystemIdentifier = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(4)),
            ClassId = new Guid(d.AsSpan(8, 16)),
        };

        if (stream.Version > 1)
        {
            Report("ole.propset-header", DefectKind.Value, DefectSeverity.Repaired, "The version is " + stream.Version + "; 0 and 1 are defined. It is read as 1.", 2);
            stream.Version = 1;
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(24));
        long fit = (d.Length - HeaderSize) / 20L;
        if (count > fit || count > MaxSets)
        {
            Report("ole.propset-count", DefectKind.Record, DefectSeverity.Repaired, "The header says " + count + " property sets; " + Math.Min(fit, MaxSets) + " are read.", 24);
            count = (uint)Math.Min(fit, MaxSets);
        }

        for (int i = 0; i < count; i++)
        {
            int at = HeaderSize + (20 * i);
            var fmtid = new Guid(d.AsSpan(at, 16));
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at + 16));
            if (offset < HeaderSize + (20L * count) || offset > d.Length - 8)
            {
                Report("ole.propset-bad-offset", DefectKind.Record, DefectSeverity.Dropped, "Property set " + i + " starts at " + offset + ", outside the stream's " + d.Length + " bytes or inside the header; it is dropped.", at + 16);
                continue;
            }

            stream.Add(ReadSet(fmtid, (int)offset));
        }

        return stream;
    }

    private OlePropertySet ReadSet(Guid fmtid, int start)
    {
        byte[] d = _data;
        _setBase = start;
        _codePage = OlePropertySet.UnicodeCodePage;
        _codePageKnown = false;
        _codePageReported = false;
        _encoding = null;
        var set = new OlePropertySet(fmtid);
        _current = set;

        uint size = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(start));
        int end = d.Length;
        if (size < 8 || size > d.Length - start)
        {
            Report("ole.propset-size", DefectKind.Record, DefectSeverity.Repaired, "The property set declares " + size + " bytes but " + (d.Length - start) + " remain; the rest of the stream is read.", start);
        }
        else
        {
            end = start + (int)size;
        }

        uint n = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(start + 4));
        long room = Math.Max(0, (end - start - 8) / 8);
        if (n > room)
        {
            Report("ole.propset-count", DefectKind.Record, DefectSeverity.Repaired, "The property set lists " + n + " properties; only " + room + " fit.", start + 4);
            n = (uint)room;
        }

        var entries = new List<(uint Id, int Offset)>((int)n);
        var seen = new HashSet<uint>();
        for (int i = 0; i < n; i++)
        {
            int at = start + 8 + (8 * i);
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at));
            uint off = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at + 4));
            if (off < 8 + (8L * n) || off > end - start - 4)
            {
                Report("ole.propset-truncated", DefectKind.Record, DefectSeverity.Dropped, "Property " + id + " has its value at " + off + ", outside the property set; it is dropped.", at + 4);
                continue;
            }

            if (!seen.Add(id))
            {
                Report("ole.propset-duplicate-id", DefectKind.Structure, DefectSeverity.Dropped, "Property " + id + " appears twice; the first is kept.", at);
                continue;
            }

            entries.Add((id, start + (int)off));
        }

        // The end of each value is the start of the next one in the file, which bounds the vector-of-strings check below.
        var starts = new List<int>(entries.Count);
        foreach ((uint _, int o) in entries)
        {
            starts.Add(o);
        }

        starts.Sort();

        // The code page comes first: every string needs it.
        foreach ((uint id, int o) in entries)
        {
            if (id == OlePropertySet.CodePageId)
            {
                TryProperty(id, o, LimitFor(starts, o, end), () =>
                {
                    OlePropertyValue v = ReadTyped(o, LimitFor(starts, o, end), out _);
                    if (v.Value is short s)
                    {
                        _codePage = (ushort)s;
                        _codePageKnown = true;
                        set.HasCodePageProperty = true;
                        _encoding = ResolveEncoding(o); // an unavailable code page is reported here and the set is read as Windows-1252
                        set.CodePage = _codePage;
                    }
                    else
                    {
                        throw new Damage("ole.propset-type-unsupported", DefectKind.Value, "The code page property has type 0x" + v.Type.ToString("X4") + ", not VT_I2.", o);
                    }
                });
            }
        }

        foreach ((uint id, int o) in entries)
        {
            int limit = LimitFor(starts, o, end);
            switch (id)
            {
                case OlePropertySet.CodePageId:
                    break;
                case OlePropertySet.DictionaryId:
                    TryProperty(id, o, limit, () => ReadDictionary(o, limit, set));
                    break;
                case OlePropertySet.LocaleId:
                case OlePropertySet.BehaviorId:
                    TryProperty(id, o, limit, () =>
                    {
                        OlePropertyValue v = ReadTyped(o, limit, out _);
                        if (v.AsInt64() is { } x && x >= 0)
                        {
                            if (id == OlePropertySet.LocaleId)
                            {
                                set.Locale = (uint)x;
                            }
                            else
                            {
                                set.Behavior = (uint)x;
                            }
                        }
                    });
                    break;
                default:
                    TryProperty(id, o, limit, () => set.AddOrReplace(id, ReadTyped(o, limit, out _)));
                    break;
            }
        }

        return set;
    }

    private static int LimitFor(List<int> starts, int offset, int end)
    {
        foreach (int s in starts)
        {
            if (s > offset)
            {
                return s;
            }
        }

        return end;
    }

    private void TryProperty(uint id, int offset, int limit, Action read)
    {
        _ = limit;
        try
        {
            read();
        }
        catch (Damage ex)
        {
            Report(ex.Code, ex.Kind, DefectSeverity.Dropped, "Property " + id + ": " + ex.Message + " It is dropped.", ex.Offset >= 0 ? ex.Offset : offset);
        }
    }

    // ---- values ----

    /// <summary>Reads a TypedPropertyValue at <paramref name="pos"/> ([MS-OLEPS] §2.15); <paramref name="limit"/> bounds the value.</summary>
    private OlePropertyValue ReadTyped(int pos, int limit, out int next)
    {
        if (limit - pos < 4)
        {
            throw new Damage("ole.propset-truncated", DefectKind.Record, "The value is cut off before its type.", pos);
        }

        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(pos));
        pos += 4; // type and two padding bytes ([MS-OLEPS] §2.15: Padding MUST be zero; not enforced)
        object? value;
        if ((type & OleVarType.VtVector) != 0 && (type & OleVarType.VtArray) == 0)
        {
            value = ReadVector(type, ref pos, limit);
        }
        else if ((type & OleVarType.VtArray) != 0 && (type & OleVarType.VtVector) == 0)
        {
            value = ReadArray(type, ref pos, limit);
        }
        else if ((type & (OleVarType.VtVector | OleVarType.VtArray)) != 0 || (type & 0xC000) != 0)
        {
            throw new Damage("ole.propset-type-unsupported", DefectKind.Unsupported, "Type 0x" + type.ToString("X4") + " is not a property type.", pos - 4);
        }
        else
        {
            value = ReadScalar(type, ref pos, limit, packed: false, alignedStrings: true);
        }

        next = pos;
        return OlePropertyValue.Create(type, value);
    }

    private int Align(int pos) => _setBase + (((pos - _setBase) + 3) & ~3);

    private static int ElementSize(ushort baseType) => baseType switch
    {
        OleVarType.VtI1 or OleVarType.VtUI1 => 1,
        OleVarType.VtI2 or OleVarType.VtUI2 or OleVarType.VtBool => 2,
        OleVarType.VtI4 or OleVarType.VtUI4 or OleVarType.VtInt or OleVarType.VtUInt or OleVarType.VtR4 or OleVarType.VtError => 4,
        OleVarType.VtR8 or OleVarType.VtI8 or OleVarType.VtUI8 or OleVarType.VtCy or OleVarType.VtDate or OleVarType.VtFileTime => 8,
        OleVarType.VtDecimal or OleVarType.VtClsid => 16,
        OleVarType.VtBstr or OleVarType.VtLpstr or OleVarType.VtLpwstr or OleVarType.VtBlob or OleVarType.VtBlobObject or OleVarType.VtCf => 4,
        OleVarType.VtVariant => 4,
        _ => -1,
    };

    private List<object?> ReadVector(ushort type, ref int pos, int limit)
    {
        ushort e = (ushort)(type & 0x0FFF);
        int min = ElementSize(e);
        if (min < 0)
        {
            throw new Damage("ole.propset-type-unsupported", DefectKind.Unsupported, "Vector type 0x" + type.ToString("X4") + " is not a vector of a property type.", pos - 4);
        }

        if (limit - pos < 4)
        {
            throw new Damage("ole.propset-truncated", DefectKind.Record, "The vector is cut off before its length.", pos);
        }

        uint len = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(pos));
        pos += 4;
        if (len > (uint)(limit - pos) / (uint)min)
        {
            throw new Damage("ole.propset-truncated", DefectKind.Record, "The vector holds " + len + " elements; the bytes left cannot hold them.", pos - 4);
        }

        bool ansi = (e is OleVarType.VtLpstr or OleVarType.VtBstr) && _codePage != OlePropertySet.UnicodeCodePage;
        if (ansi && len > 0)
        {
            // [MS-OLEPS] §2.5 pads each string to 4 bytes; [MS-OSHARED] §2.3.3.1.5 (UnalignedLpstr) lists a form that does not. Take the
            // reading that ends where the value ends, the aligned one when both or neither do.
            int start = pos;
            List<object?>? a = TryStrings(start, limit, len, aligned: true, out int endA);
            List<object?>? u = TryStrings(start, limit, len, aligned: false, out int endU);
            bool aOk = a is not null && Align(endA) == limit;
            bool uOk = u is not null && Align(endU) == limit;
            if (u is not null && (a is null || (!aOk && uOk)))
            {
                pos = Align(endU);
                return u;
            }

            if (a is null)
            {
                throw new Damage("ole.propset-truncated", DefectKind.Record, "A string of the vector is cut off.", pos);
            }

            pos = Align(endA);
            return a;
        }

        var items = new List<object?>((int)len);
        for (uint i = 0; i < len; i++)
        {
            items.Add(e == OleVarType.VtVariant ? ReadTyped(pos, limit, out pos) : ReadScalar(e, ref pos, limit, packed: true, alignedStrings: true));
        }

        pos = Align(pos);
        return items;
    }

    private List<object?>? TryStrings(int pos, int limit, uint len, bool aligned, out int end)
    {
        var items = new List<object?>((int)len);
        try
        {
            for (uint i = 0; i < len; i++)
            {
                items.Add(ReadCodePageString(ref pos, limit, aligned));
            }
        }
        catch (Damage)
        {
            end = pos;
            return null;
        }

        end = pos;
        return items;
    }

    private OleArray ReadArray(ushort type, ref int pos, int limit)
    {
        ushort e = (ushort)(type & 0x0FFF);
        int min = ElementSize(e);
        if (min < 0 || e == OleVarType.VtStream)
        {
            throw new Damage("ole.propset-type-unsupported", DefectKind.Unsupported, "Array type 0x" + type.ToString("X4") + " is not an array of a property type.", pos - 4);
        }

        if (limit - pos < 8)
        {
            throw new Damage("ole.propset-truncated", DefectKind.Record, "The array is cut off before its header.", pos);
        }

        // [MS-OLEPS] §2.14.4: Type, then NumDimensions (1 to 31), then one (Size, IndexOffset) pair each.
        uint elementType = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(pos));
        uint dims = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(pos + 4));
        pos += 8;
        if (elementType != e || dims is < 1 or > 31)
        {
            throw new Damage("ole.propset-type-unsupported", DefectKind.Record, "The array header has element type 0x" + elementType.ToString("X") + " and " + dims + " dimensions.", pos - 8);
        }

        if (limit - pos < 8L * dims)
        {
            throw new Damage("ole.propset-truncated", DefectKind.Record, "The array is cut off inside its dimensions.", pos);
        }

        var dimensions = new List<OleArrayDimension>((int)dims);
        ulong total = 1;
        for (int i = 0; i < dims; i++)
        {
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(pos));
            int offset = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(pos + 4));
            pos += 8;
            dimensions.Add(new OleArrayDimension(size, offset));
            total *= size;
            if (total > (ulong)(limit - pos) / (ulong)min)
            {
                throw new Damage("ole.propset-truncated", DefectKind.Record, "The array's dimensions call for more elements than the bytes left can hold.", pos);
            }
        }

        var items = new List<object?>((int)total);
        for (ulong i = 0; i < total; i++)
        {
            items.Add(e == OleVarType.VtVariant ? ReadTyped(pos, limit, out pos) : ReadScalar(e, ref pos, limit, packed: true, alignedStrings: true));
        }

        pos = Align(pos);
        return new OleArray(dimensions, items);
    }

    private object? ReadScalar(ushort type, ref int pos, int limit, bool packed, bool alignedStrings)
    {
        byte[] d = _data;
        int need = type switch
        {
            OleVarType.VtEmpty or OleVarType.VtNull => 0,
            _ => ElementSize(type),
        };

        if (need < 0 || type == OleVarType.VtVariant)
        {
            if (type is OleVarType.VtStream or OleVarType.VtStorage or OleVarType.VtStreamedObject or OleVarType.VtStoredObject or OleVarType.VtVersionedStream)
            {
                need = 4;
            }
            else
            {
                throw new Damage("ole.propset-type-unsupported", DefectKind.Unsupported, "Type 0x" + type.ToString("X4") + " is not a property type.", pos - 4);
            }
        }

        if (limit - pos < Math.Min(need, 4) || (need < 4 && limit - pos < need))
        {
            throw new Damage("ole.propset-truncated", DefectKind.Record, "The value is cut off.", pos);
        }

        if (ElementSize(type) > 0 && type is not (OleVarType.VtBstr or OleVarType.VtLpstr or OleVarType.VtLpwstr or OleVarType.VtBlob or OleVarType.VtBlobObject or OleVarType.VtCf or OleVarType.VtVariant))
        {
            int size = ElementSize(type);
            if (limit - pos < size)
            {
                throw new Damage("ole.propset-truncated", DefectKind.Record, "The value is cut off.", pos);
            }

            ReadOnlySpan<byte> s = d.AsSpan(pos, size);
            object fixedValue = type switch
            {
                OleVarType.VtI2 => BinaryPrimitives.ReadInt16LittleEndian(s),
                OleVarType.VtUI2 => BinaryPrimitives.ReadUInt16LittleEndian(s),
                OleVarType.VtI1 => (sbyte)s[0],
                OleVarType.VtUI1 => s[0],
                OleVarType.VtBool => BinaryPrimitives.ReadUInt16LittleEndian(s) != 0,
                OleVarType.VtI4 or OleVarType.VtInt => BinaryPrimitives.ReadInt32LittleEndian(s),
                OleVarType.VtUI4 or OleVarType.VtUInt or OleVarType.VtError => BinaryPrimitives.ReadUInt32LittleEndian(s),
                OleVarType.VtR4 => BinaryPrimitives.ReadSingleLittleEndian(s),
                OleVarType.VtR8 => BinaryPrimitives.ReadDoubleLittleEndian(s),
                OleVarType.VtI8 => BinaryPrimitives.ReadInt64LittleEndian(s),
                OleVarType.VtUI8 => BinaryPrimitives.ReadUInt64LittleEndian(s),
                OleVarType.VtCy => new OleCurrency(BinaryPrimitives.ReadInt64LittleEndian(s)),
                OleVarType.VtDate => new OleDate(BinaryPrimitives.ReadDoubleLittleEndian(s)),
                OleVarType.VtFileTime => new OleFileTime(BinaryPrimitives.ReadUInt64LittleEndian(s)),
                OleVarType.VtDecimal => new OleDecimal(s[2], s[3], BinaryPrimitives.ReadUInt32LittleEndian(s[4..]), BinaryPrimitives.ReadUInt64LittleEndian(s[8..])),
                _ => new Guid(s),
            };
            pos += size;
            if (!packed && size < 4)
            {
                // [MS-OLEPS] §2.15: a value narrower than 4 bytes is followed by zero padding to 4.
                pos = Math.Min(limit, Align(pos));
            }

            return fixedValue;
        }

        if (type is OleVarType.VtEmpty or OleVarType.VtNull)
        {
            return null;
        }

        switch (type)
        {
            case OleVarType.VtBstr:
            case OleVarType.VtLpstr:
            case OleVarType.VtStream:
            case OleVarType.VtStorage:
            case OleVarType.VtStreamedObject:
            case OleVarType.VtStoredObject:
                return ReadCodePageString(ref pos, limit, alignedStrings);
            case OleVarType.VtLpwstr:
                return ReadUnicodeString(ref pos, limit);
            case OleVarType.VtBlob:
            case OleVarType.VtBlobObject:
                {
                    uint size = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(pos));
                    pos += 4;
                    if (size > limit - pos)
                    {
                        throw new Damage("ole.propset-truncated", DefectKind.Record, "A blob of " + size + " bytes runs past the end of the value.", pos - 4);
                    }

                    byte[] bytes = d.AsSpan(pos, (int)size).ToArray();
                    pos = Align(pos + (int)size);
                    return bytes;
                }

            case OleVarType.VtCf:
                {
                    uint size = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(pos));
                    pos += 4;
                    if (size < 4 || size > limit - pos)
                    {
                        throw new Damage("ole.propset-truncated", DefectKind.Record, "Clipboard data of " + size + " bytes is shorter than its format field or runs past the end of the value.", pos - 4);
                    }

                    uint format = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(pos));
                    byte[] bytes = d.AsSpan(pos + 4, (int)size - 4).ToArray();
                    pos = Align(pos + (int)size);
                    return new OleClipboardData(format, bytes);
                }

            case OleVarType.VtVersionedStream:
                {
                    if (limit - pos < 20)
                    {
                        throw new Damage("ole.propset-truncated", DefectKind.Record, "The versioned stream is cut off.", pos);
                    }

                    var version = new Guid(d.AsSpan(pos, 16));
                    pos += 16;
                    string name = ReadCodePageString(ref pos, limit, alignedStrings);
                    return new OleVersionedStreamRef(version, name);
                }

            default:
                throw new Damage("ole.propset-type-unsupported", DefectKind.Unsupported, "Type 0x" + type.ToString("X4") + " is not a property type.", pos - 4);
        }
    }

    /// <summary>Reads a CodePageString ([MS-OLEPS] §2.5): a byte count, then the characters in the property set's code page.</summary>
    private string ReadCodePageString(ref int pos, int limit, bool aligned)
    {
        if (limit - pos < 4)
        {
            throw new Damage("ole.propset-truncated", DefectKind.Record, "A string is cut off before its size.", pos);
        }

        uint size = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(pos));
        int at = pos;
        pos += 4;
        if (size > limit - pos)
        {
            throw new Damage("ole.propset-truncated", DefectKind.Record, "A string of " + size + " bytes runs past the end of the value.", at);
        }

        string text = Decode(_data.AsSpan(pos, (int)size), at);
        pos += (int)size;
        if (aligned)
        {
            pos = Math.Min(limit, Align(pos));
        }

        return text;
    }

    /// <summary>Reads a UnicodeString ([MS-OLEPS] §2.7): a count of UTF-16 characters, then the characters, padded to 4 bytes.</summary>
    private string ReadUnicodeString(ref int pos, int limit)
    {
        if (limit - pos < 4)
        {
            throw new Damage("ole.propset-truncated", DefectKind.Record, "A string is cut off before its length.", pos);
        }

        uint chars = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(pos));
        int at = pos;
        pos += 4;
        if (chars > (uint)(limit - pos) / 2)
        {
            throw new Damage("ole.propset-truncated", DefectKind.Record, "A string of " + chars + " characters runs past the end of the value.", at);
        }

        string text = TrimNulls(Encoding.Unicode.GetString(_data, pos, (int)chars * 2));
        pos = Math.Min(limit, Align(pos + ((int)chars * 2)));
        return text;
    }

    private string Decode(ReadOnlySpan<byte> bytes, int offset)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        if (!_codePageKnown && !_codePageReported)
        {
            _codePageReported = true;
            Report("ole.propset-codepage-unsupported", DefectKind.Encoding, DefectSeverity.Repaired, "The property set has no code page property; its strings are read as Windows-1252.", offset);
            _codePage = 1252;
            _encoding = null;
        }

        Encoding enc = _encoding ??= ResolveEncoding(offset);
        if (_codePage is OlePropertySet.UnicodeCodePage)
        {
            // [MS-OLEPS] §2.5: with CP_WINUNICODE the size is in bytes and a multiple of 2.
            if ((bytes.Length & 1) != 0)
            {
                Report("ole.propset-truncated", DefectKind.Record, DefectSeverity.Repaired, "A UTF-16 string has an odd byte count; the last byte is ignored.", offset);
                bytes = bytes[..^1];
            }
        }

        return TrimNulls(enc.GetString(bytes));
    }

    private Encoding ResolveEncoding(int offset)
    {
        if (OleCodePages.TryGet(_codePage, out Encoding? enc))
        {
            return enc;
        }

        Report("ole.propset-codepage-unsupported", DefectKind.Encoding, DefectSeverity.Repaired, "Code page " + _codePage + " is not available; the property set's strings are read as Windows-1252.", offset);
        _codePage = 1252;
        if (_current is not null)
        {
            _current.CodePage = 1252; // the strings are now Windows-1252 text, and a write must store them as that
        }

        OleCodePages.TryGet(1252, out Encoding? fallback);
        return fallback ?? Encoding.Latin1;
    }

    private static string TrimNulls(string s) => s.TrimEnd('\0');

    // ---- dictionary ----

    /// <summary>Reads the Dictionary property ([MS-OLEPS] §2.17): a count and entries of an identifier, a length and a name.</summary>
    private void ReadDictionary(int pos, int limit, OlePropertySet set)
    {
        if (limit - pos < 4)
        {
            throw new Damage("ole.propset-dictionary", DefectKind.Record, "The dictionary is cut off before its entry count.", pos);
        }

        uint n = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(pos));
        pos += 4;
        if (n > (uint)(limit - pos) / 8)
        {
            throw new Damage("ole.propset-dictionary", DefectKind.Record, "The dictionary lists " + n + " entries; the bytes left cannot hold them.", pos - 4);
        }

        bool unicode = _codePage == OlePropertySet.UnicodeCodePage;
        for (uint i = 0; i < n; i++)
        {
            if (limit - pos < 8)
            {
                throw new Damage("ole.propset-dictionary", DefectKind.Record, "Dictionary entry " + i + " is cut off.", pos);
            }

            uint id = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(pos));
            uint len = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(pos + 4));
            int at = pos;
            pos += 8;
            long bytes = unicode ? 2L * len : len;
            if (bytes > limit - pos)
            {
                throw new Damage("ole.propset-dictionary", DefectKind.Record, "Dictionary entry " + i + " has a name of " + len + " characters that runs past the end.", at);
            }

            string name = unicode
                ? TrimNulls(Encoding.Unicode.GetString(_data, pos, (int)bytes))
                : Decode(_data.AsSpan(pos, (int)bytes), at);
            pos += (int)bytes;
            if (unicode)
            {
                pos = Math.Min(limit, Align(pos));
            }

            if (id < 2 || id > 0x7FFFFFFF || set.Names.ContainsKey(id))
            {
                Report("ole.propset-dictionary", DefectKind.Structure, DefectSeverity.Dropped, "Dictionary entry " + i + " has identifier " + id + ", which is a special property or already named; it is dropped.", at);
                continue;
            }

            set.Names[id] = name;
        }
    }

    // ---- reporting ----

    private OfficeFormatException Fatal(string message, long offset) =>
        new(DefectKind.Record, "ole.propset-header", message, DefectLocation.InRecord(_name, offset));

    private void Report(string code, DefectKind kind, DefectSeverity severity, string message, long offset) =>
        _context.Defects.Report(new Defect(kind, code, severity, message, DefectLocation.InRecord(_name, offset)));
}

/// <summary>Code pages through the BCL, with the Windows code pages registered once.</summary>
internal static class OleCodePages
{
    private static readonly object Gate = new();
    private static bool _registered;

    internal static bool TryGet(int codePage, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Encoding? encoding)
    {
        lock (Gate)
        {
            if (!_registered)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _registered = true;
            }
        }

        try
        {
            encoding = codePage == OlePropertySet.UnicodeCodePage
                ? Encoding.Unicode
                : Encoding.GetEncoding(codePage, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
            return true;
        }
        catch (ArgumentException)
        {
            encoding = null;
            return false;
        }
        catch (NotSupportedException)
        {
            encoding = null;
            return false;
        }
    }
}
