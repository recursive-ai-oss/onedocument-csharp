using System.Buffers.Binary;
using System.Linq;
using System.Text;

namespace RecursiveAI.OneDocument.Ole;

public sealed partial class OlePropertySets
{
    /// <summary>Encodes the stream.</summary>
    /// <returns>The bytes of the property set stream.</returns>
    /// <exception cref="ArgumentException">A property value does not match its type number, a string cannot be stored in the set's code page, or the set's code page is not available.</exception>
    /// <remarks>
    /// <para>Every set gets a code page property (<see cref="OlePropertySet.CodePage"/>, 1200 by default) first, then the dictionary when it has
    /// names, the locale and behavior when set, and then the properties in order. Strings in a code page other than 1200 are encoded
    /// with that code page and a character it cannot hold becomes <c>?</c>. Output is deterministic: it depends only on the content.</para>
    /// </remarks>
    public byte[] ToArray()
    {
        var w = new OleBuffer();
        w.U16(0xFFFE);
        w.U16(Version);
        w.U32(SystemIdentifier);
        w.Bytes(ClassId.ToByteArray());
        w.U32((uint)Sets.Count);
        int table = w.Length;
        for (int i = 0; i < Sets.Count; i++)
        {
            w.Bytes(Sets[i].FormatId.ToByteArray());
            w.U32(0);
        }

        for (int i = 0; i < Sets.Count; i++)
        {
            w.PatchU32(table + (20 * i) + 16, (uint)w.Length);
            OlePropsWriter.WriteSet(w, Sets[i]);
        }

        return w.ToArray();
    }

    /// <summary>Writes the stream to a <see cref="Stream"/>.</summary>
    /// <param name="destination">The stream to write to.</param>
    /// <exception cref="ArgumentException">As for <see cref="ToArray"/>.</exception>
    public void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Write(ToArray());
    }
}

internal sealed class OleBuffer
{
    private byte[] _data = new byte[256];

    internal int Length { get; private set; }

    internal void Ensure(int extra)
    {
        if (Length + extra > _data.Length)
        {
            Array.Resize(ref _data, Math.Max(_data.Length * 2, Length + extra));
        }
    }

    internal void U8(byte v)
    {
        Ensure(1);
        _data[Length++] = v;
    }

    internal void U16(ushort v)
    {
        Ensure(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(Length), v);
        Length += 2;
    }

    internal void U32(uint v)
    {
        Ensure(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(Length), v);
        Length += 4;
    }

    internal void U64(ulong v)
    {
        Ensure(8);
        BinaryPrimitives.WriteUInt64LittleEndian(_data.AsSpan(Length), v);
        Length += 8;
    }

    internal void Bytes(ReadOnlySpan<byte> v)
    {
        Ensure(v.Length);
        v.CopyTo(_data.AsSpan(Length));
        Length += v.Length;
    }

    internal void PatchU32(int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(at), v);

    internal void PadTo4(int origin)
    {
        while (((Length - origin) & 3) != 0)
        {
            U8(0);
        }
    }

    internal byte[] ToArray() => _data.AsSpan(0, Length).ToArray();
}

/// <summary>The writer of [MS-OLEPS] structures.</summary>
internal static class OlePropsWriter
{
    internal static void WriteSet(OleBuffer w, OlePropertySet set)
    {
        int origin = w.Length;
        if (!OleCodePages.TryGet(set.CodePage, out Encoding? enc) || enc is null)
        {
            throw new ArgumentException("Code page " + set.CodePage + " is not available.", nameof(set));
        }

        bool unicode = set.CodePage == OlePropertySet.UnicodeCodePage;
        var ids = new List<uint> { OlePropertySet.CodePageId };
        if (set.Names.Count > 0)
        {
            ids.Add(OlePropertySet.DictionaryId);
        }

        if (set.Locale is not null)
        {
            ids.Add(OlePropertySet.LocaleId);
        }

        if (set.Behavior is not null)
        {
            ids.Add(OlePropertySet.BehaviorId);
        }

        foreach (OleProperty p in set.Properties)
        {
            ids.Add(p.Id);
        }

        w.U32(0); // size, patched below
        w.U32((uint)ids.Count);
        int table = w.Length;
        for (int i = 0; i < ids.Count; i++)
        {
            w.U32(ids[i]);
            w.U32(0);
        }

        for (int i = 0; i < ids.Count; i++)
        {
            w.PatchU32(table + (8 * i) + 4, (uint)(w.Length - origin));
            uint id = ids[i];
            switch (id)
            {
                case OlePropertySet.CodePageId:
                    WriteTyped(w, origin, set, enc, OlePropertyValue.Create(OleVarType.VtI2, unchecked((short)(ushort)set.CodePage)));
                    break;
                case OlePropertySet.DictionaryId:
                    WriteDictionary(w, origin, set, enc, unicode);
                    break;
                case OlePropertySet.LocaleId:
                    WriteTyped(w, origin, set, enc, OlePropertyValue.FromUInt32(set.Locale!.Value));
                    break;
                case OlePropertySet.BehaviorId:
                    WriteTyped(w, origin, set, enc, OlePropertyValue.FromUInt32(set.Behavior!.Value));
                    break;
                default:
                    WriteTyped(w, origin, set, enc, set.Find(id)!);
                    break;
            }
        }

        w.PatchU32(origin, (uint)(w.Length - origin));
    }

    // [MS-OLEPS] §2.17: a count, then each entry as identifier, length and name. Unicode names are padded to 4; other names are not,
    // and the whole dictionary is padded to 4 at its end.
    private static void WriteDictionary(OleBuffer w, int origin, OlePropertySet set, Encoding enc, bool unicode)
    {
        w.U32((uint)set.Names.Count);
        foreach (KeyValuePair<uint, string> n in set.Names.OrderBy(k => k.Key))
        {
            w.U32(n.Key);
            if (unicode)
            {
                byte[] b = Encoding.Unicode.GetBytes(n.Value + "\0");
                w.U32((uint)b.Length / 2);
                w.Bytes(b);
                w.PadTo4(origin);
            }
            else
            {
                byte[] b = Terminated(enc, n.Value);
                w.U32((uint)b.Length);
                w.Bytes(b);
            }
        }

        w.PadTo4(origin);
    }

    private static byte[] Terminated(Encoding enc, string s)
    {
        byte[] body = enc.GetBytes(s);
        byte[] nul = enc.GetBytes("\0");
        byte[] all = new byte[body.Length + nul.Length];
        body.CopyTo(all, 0);
        return all;
    }

    private static void WriteTyped(OleBuffer w, int origin, OlePropertySet set, Encoding enc, OlePropertyValue v)
    {
        ushort type = v.Type;
        w.U16(type);
        w.U16(0);
        if ((type & OleVarType.VtVector) != 0 && (type & OleVarType.VtArray) == 0)
        {
            WriteVector(w, origin, set, enc, v);
        }
        else if ((type & OleVarType.VtArray) != 0 && (type & OleVarType.VtVector) == 0)
        {
            WriteArray(w, origin, set, enc, v);
        }
        else
        {
            WriteScalar(w, origin, set, enc, type, v.Value, packed: false);
        }
    }

    private static void WriteVector(OleBuffer w, int origin, OlePropertySet set, Encoding enc, OlePropertyValue v)
    {
        ushort e = (ushort)(v.Type & 0x0FFF);
        if (v.Value is not IReadOnlyList<object?> items)
        {
            throw Mismatch(v);
        }

        w.U32((uint)items.Count);
        foreach (object? item in items)
        {
            if (e == OleVarType.VtVariant)
            {
                WriteTyped(w, origin, set, enc, item as OlePropertyValue ?? throw Mismatch(v));
            }
            else
            {
                WriteScalar(w, origin, set, enc, e, item, packed: true);
            }
        }

        w.PadTo4(origin);
    }

    private static void WriteArray(OleBuffer w, int origin, OlePropertySet set, Encoding enc, OlePropertyValue v)
    {
        ushort e = (ushort)(v.Type & 0x0FFF);
        if (v.Value is not OleArray a || a.Dimensions.Count is < 1 or > 31)
        {
            throw Mismatch(v);
        }

        w.U32(e);
        w.U32((uint)a.Dimensions.Count);
        ulong total = 1;
        foreach (OleArrayDimension d in a.Dimensions)
        {
            w.U32(d.Size);
            w.U32(unchecked((uint)d.IndexOffset));
            total *= d.Size;
        }

        if (total != (ulong)a.Items.Count)
        {
            throw new ArgumentException("An array property has " + a.Items.Count + " elements but its dimensions call for " + total + ".");
        }

        foreach (object? item in a.Items)
        {
            if (e == OleVarType.VtVariant)
            {
                WriteTyped(w, origin, set, enc, item as OlePropertyValue ?? throw Mismatch(v));
            }
            else
            {
                WriteScalar(w, origin, set, enc, e, item, packed: true);
            }
        }

        w.PadTo4(origin);
    }

    private static ArgumentException Mismatch(OlePropertyValue v) =>
        new("The value of type 0x" + v.Type.ToString("X4") + " is a " + (v.Value?.GetType().Name ?? "null") + ", which does not fit that type.");

    private static void WriteScalar(OleBuffer w, int origin, OlePropertySet set, Encoding enc, ushort type, object? value, bool packed)
    {
        switch (type)
        {
            case OleVarType.VtEmpty:
            case OleVarType.VtNull:
                return;
            case OleVarType.VtI2:
                w.U16(unchecked((ushort)(value is short s ? s : throw Bad(type, value))));
                break;
            case OleVarType.VtUI2:
                w.U16(value is ushort us ? us : throw Bad(type, value));
                break;
            case OleVarType.VtI1:
                w.U8(unchecked((byte)(value is sbyte sb ? sb : throw Bad(type, value))));
                break;
            case OleVarType.VtUI1:
                w.U8(value is byte b ? b : throw Bad(type, value));
                break;
            case OleVarType.VtBool:
                w.U16((value is bool f ? f : throw Bad(type, value)) ? (ushort)0xFFFF : (ushort)0);
                break;
            case OleVarType.VtI4:
            case OleVarType.VtInt:
                w.U32(unchecked((uint)(value is int i ? i : throw Bad(type, value))));
                break;
            case OleVarType.VtUI4:
            case OleVarType.VtUInt:
            case OleVarType.VtError:
                w.U32(value is uint u ? u : throw Bad(type, value));
                break;
            case OleVarType.VtR4:
                w.U32(BitConverter.SingleToUInt32Bits(value is float sg ? sg : throw Bad(type, value)));
                break;
            case OleVarType.VtR8:
                w.U64(BitConverter.DoubleToUInt64Bits(value is double dd ? dd : throw Bad(type, value)));
                break;
            case OleVarType.VtI8:
                w.U64(unchecked((ulong)(value is long l ? l : throw Bad(type, value))));
                break;
            case OleVarType.VtUI8:
                w.U64(value is ulong ul ? ul : throw Bad(type, value));
                break;
            case OleVarType.VtCy:
                w.U64(unchecked((ulong)(value is OleCurrency c ? c.Raw : throw Bad(type, value))));
                break;
            case OleVarType.VtDate:
                w.U64(BitConverter.DoubleToUInt64Bits(value is OleDate dt ? dt.Value : throw Bad(type, value)));
                break;
            case OleVarType.VtFileTime:
                w.U64(value is OleFileTime ft ? ft.Ticks : throw Bad(type, value));
                break;
            case OleVarType.VtDecimal:
                {
                    OleDecimal dec = value is OleDecimal x ? x : throw Bad(type, value);
                    w.U16(0);
                    w.U8(dec.Scale);
                    w.U8(dec.Sign);
                    w.U32(dec.High32);
                    w.U64(dec.Low64);
                    break;
                }

            case OleVarType.VtClsid:
                w.Bytes((value is Guid g ? g : throw Bad(type, value)).ToByteArray());
                break;
            case OleVarType.VtBstr:
            case OleVarType.VtLpstr:
            case OleVarType.VtStream:
            case OleVarType.VtStorage:
            case OleVarType.VtStreamedObject:
            case OleVarType.VtStoredObject:
                WriteCodePageString(w, origin, enc, set.CodePage, value as string ?? throw Bad(type, value));
                return;
            case OleVarType.VtLpwstr:
                {
                    byte[] bytes = Encoding.Unicode.GetBytes((value as string ?? throw Bad(type, value)) + "\0");
                    w.U32((uint)bytes.Length / 2);
                    w.Bytes(bytes);
                    w.PadTo4(origin);
                    return;
                }

            case OleVarType.VtBlob:
            case OleVarType.VtBlobObject:
                {
                    byte[] bytes = value as byte[] ?? throw Bad(type, value);
                    w.U32((uint)bytes.Length);
                    w.Bytes(bytes);
                    w.PadTo4(origin);
                    return;
                }

            case OleVarType.VtCf:
                {
                    OleClipboardData cf = value as OleClipboardData ?? throw Bad(type, value);
                    w.U32((uint)cf.Data.Length + 4);
                    w.U32(cf.Format);
                    w.Bytes(cf.Data);
                    w.PadTo4(origin);
                    return;
                }

            case OleVarType.VtVersionedStream:
                {
                    OleVersionedStreamRef vs = value as OleVersionedStreamRef ?? throw Bad(type, value);
                    w.Bytes(vs.VersionId.ToByteArray());
                    WriteCodePageString(w, origin, enc, set.CodePage, vs.StreamName);
                    return;
                }

            default:
                throw new ArgumentException("Type 0x" + type.ToString("X4") + " is not a property type.");
        }

        // [MS-OLEPS] §2.15: a value narrower than 4 bytes that stands alone is followed by zero padding to 4.
        if (!packed)
        {
            w.PadTo4(origin);
        }
    }

    // [MS-OLEPS] §2.5: the byte count includes the terminator and the characters are padded to 4.
    private static void WriteCodePageString(OleBuffer w, int origin, Encoding enc, int codePage, string text)
    {
        byte[] bytes = codePage == OlePropertySet.UnicodeCodePage ? Encoding.Unicode.GetBytes(text + "\0") : Terminated(enc, text);
        w.U32((uint)bytes.Length);
        w.Bytes(bytes);
        w.PadTo4(origin);
    }

    private static ArgumentException Bad(ushort type, object? value) =>
        new("A value of type " + (value?.GetType().Name ?? "null") + " cannot be written as property type 0x" + type.ToString("X4") + ".");
}
