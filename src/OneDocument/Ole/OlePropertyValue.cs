using System.Globalization;

namespace RecursiveAI.OneDocument.Ole;

/// <summary>The property types of [MS-OLEPS] §2.2, as the numbers stored in a property set. The flag bits are separate from the base types.</summary>
public static class OleVarType
{
    /// <summary>VT_EMPTY: no value.</summary>
    public const ushort VtEmpty = 0x0000;

    /// <summary>VT_NULL: a null value.</summary>
    public const ushort VtNull = 0x0001;

    /// <summary>VT_I2: 16-bit signed integer.</summary>
    public const ushort VtI2 = 0x0002;

    /// <summary>VT_I4: 32-bit signed integer.</summary>
    public const ushort VtI4 = 0x0003;

    /// <summary>VT_R4: single-precision float.</summary>
    public const ushort VtR4 = 0x0004;

    /// <summary>VT_R8: double-precision float.</summary>
    public const ushort VtR8 = 0x0005;

    /// <summary>VT_CY: currency.</summary>
    public const ushort VtCy = 0x0006;

    /// <summary>VT_DATE: an automation date.</summary>
    public const ushort VtDate = 0x0007;

    /// <summary>VT_BSTR: a string in the property set's code page.</summary>
    public const ushort VtBstr = 0x0008;

    /// <summary>VT_ERROR: an HRESULT.</summary>
    public const ushort VtError = 0x000A;

    /// <summary>VT_VARIANT: only as the element type of a vector or array whose elements carry their own types.</summary>
    public const ushort VtVariant = 0x000C;

    /// <summary>VT_BOOL: a Boolean.</summary>
    public const ushort VtBool = 0x000B;

    /// <summary>VT_DECIMAL: a decimal number.</summary>
    public const ushort VtDecimal = 0x000E;

    /// <summary>VT_I1: 8-bit signed integer.</summary>
    public const ushort VtI1 = 0x0010;

    /// <summary>VT_UI1: 8-bit unsigned integer.</summary>
    public const ushort VtUI1 = 0x0011;

    /// <summary>VT_UI2: 16-bit unsigned integer.</summary>
    public const ushort VtUI2 = 0x0012;

    /// <summary>VT_UI4: 32-bit unsigned integer.</summary>
    public const ushort VtUI4 = 0x0013;

    /// <summary>VT_I8: 64-bit signed integer.</summary>
    public const ushort VtI8 = 0x0014;

    /// <summary>VT_UI8: 64-bit unsigned integer.</summary>
    public const ushort VtUI8 = 0x0015;

    /// <summary>VT_INT: 32-bit signed integer (version 1 property sets).</summary>
    public const ushort VtInt = 0x0016;

    /// <summary>VT_UINT: 32-bit unsigned integer (version 1 property sets).</summary>
    public const ushort VtUInt = 0x0017;

    /// <summary>VT_LPSTR: a string in the property set's code page.</summary>
    public const ushort VtLpstr = 0x001E;

    /// <summary>VT_LPWSTR: a UTF-16 string.</summary>
    public const ushort VtLpwstr = 0x001F;

    /// <summary>VT_FILETIME: a time or, for the edit time, a duration, in 100-nanosecond units.</summary>
    public const ushort VtFileTime = 0x0040;

    /// <summary>VT_BLOB: bytes.</summary>
    public const ushort VtBlob = 0x0041;

    /// <summary>VT_STREAM: the name of a stream in a non-simple property set.</summary>
    public const ushort VtStream = 0x0042;

    /// <summary>VT_STORAGE: the name of a storage in a non-simple property set.</summary>
    public const ushort VtStorage = 0x0043;

    /// <summary>VT_STREAMED_Object: the name of a stream holding an object.</summary>
    public const ushort VtStreamedObject = 0x0044;

    /// <summary>VT_STORED_Object: the name of a storage holding an object.</summary>
    public const ushort VtStoredObject = 0x0045;

    /// <summary>VT_BLOB_Object: bytes of an object.</summary>
    public const ushort VtBlobObject = 0x0046;

    /// <summary>VT_CF: clipboard data.</summary>
    public const ushort VtCf = 0x0047;

    /// <summary>VT_CLSID: a GUID.</summary>
    public const ushort VtClsid = 0x0048;

    /// <summary>VT_VERSIONED_STREAM: a stream with a version GUID.</summary>
    public const ushort VtVersionedStream = 0x0049;

    /// <summary>VT_VECTOR: the flag that makes a type a one-dimensional vector of it.</summary>
    public const ushort VtVector = 0x1000;

    /// <summary>VT_ARRAY: the flag that makes a type a multi-dimensional array of it.</summary>
    public const ushort VtArray = 0x2000;
}

/// <summary>A CURRENCY value ([MS-OLEPS] §2.3): a 64-bit integer scaled by 10,000.</summary>
/// <param name="Raw">The integer as stored.</param>
public readonly record struct OleCurrency(long Raw)
{
    /// <summary>Gets the value as a decimal number.</summary>
    public decimal Value => Raw / 10000m;
}

/// <summary>A DATE value ([MS-OLEPS] §2.4): days since 30 December 1899 as a double, the fraction being the time of day.</summary>
/// <param name="Value">The number as stored.</param>
public readonly record struct OleDate(double Value);

/// <summary>A DECIMAL value ([MS-OLEPS] §2.6), kept as its fields.</summary>
/// <param name="Scale">The power of ten the 96-bit integer is divided by.</param>
/// <param name="Sign">0x80 for negative, otherwise 0.</param>
/// <param name="High32">The high 32 bits of the integer.</param>
/// <param name="Low64">The low 64 bits of the integer.</param>
public readonly record struct OleDecimal(byte Scale, byte Sign, uint High32, ulong Low64);

/// <summary>
/// A FILETIME value ([MS-OLEPS] §2.8): 100-nanosecond units since 1601-01-01 UTC. The edit time of a document uses the same field as a
/// duration, so <see cref="Ticks"/> is kept as stored and a date is made on request.
/// </summary>
/// <param name="Ticks">The 64-bit value as stored.</param>
public readonly record struct OleFileTime(ulong Ticks)
{
    /// <summary>Gets the value as a UTC date, or null when it is zero or beyond the range of <see cref="DateTimeOffset"/>.</summary>
    public DateTimeOffset? ToDateTimeOffset()
    {
        // A zero FILETIME means "not set"; values past year 9999 cannot be represented.
        if (Ticks == 0 || Ticks > 2650467743999999999UL)
        {
            return null;
        }

        return new DateTimeOffset(new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(checked((long)Ticks)));
    }

    /// <summary>Makes a FILETIME from a date.</summary>
    /// <param name="value">The date; converted to UTC. Dates before 1601 give zero.</param>
    /// <returns>The value.</returns>
    public static OleFileTime FromDateTimeOffset(DateTimeOffset value)
    {
        long t = value.UtcDateTime.Ticks - new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        return new OleFileTime(t < 0 ? 0UL : (ulong)t);
    }
}

/// <summary>Clipboard data ([MS-OLEPS] §2.11), such as the thumbnail of a document.</summary>
/// <param name="Format">The application-specific format identifier: a clipboard format number, -1 for a Windows clipboard format with a number in the data, and so on.</param>
/// <param name="Data">The data after the format identifier.</param>
public sealed record OleClipboardData(uint Format, byte[] Data);

/// <summary>A stream with a version GUID ([MS-OLEPS] §2.13).</summary>
/// <param name="VersionId">The application-specific version GUID.</param>
/// <param name="StreamName">The name of the stream, <c>prop</c> and the property identifier.</param>
public sealed record OleVersionedStreamRef(Guid VersionId, string StreamName);

/// <summary>One dimension of an array property ([MS-OLEPS] §2.14.3).</summary>
/// <param name="Size">How many elements the dimension has.</param>
/// <param name="IndexOffset">The index of the first element: 0 or 1 usually.</param>
public readonly record struct OleArrayDimension(uint Size, int IndexOffset);

/// <summary>An array property's elements and shape ([MS-OLEPS] §2.14.4), elements in row-major order.</summary>
/// <param name="Dimensions">The dimensions, 1 to 31.</param>
/// <param name="Items">The elements, as the values of the element type.</param>
public sealed record OleArray(IReadOnlyList<OleArrayDimension> Dimensions, IReadOnlyList<object?> Items);

/// <summary>
/// The typed value of a property ([MS-OLEPS] §2.15): the stored type number and the value as a .NET object.
/// </summary>
/// <remarks>
/// <para>The .NET type depends on <see cref="Type"/>: nothing for <c>VT_EMPTY</c>/<c>VT_NULL</c>; <see cref="short"/>, <see cref="int"/>,
/// <see cref="long"/>, <see cref="sbyte"/>, <see cref="byte"/>, <see cref="ushort"/>, <see cref="uint"/>, <see cref="ulong"/>,
/// <see cref="float"/>, <see cref="double"/> and <see cref="bool"/> for the numbers; <see cref="OleCurrency"/>, <see cref="OleDate"/>,
/// <see cref="OleDecimal"/> and <see cref="OleFileTime"/>; <see cref="string"/> for the three string types and the indirect names of
/// stream and storage properties; <c>byte[]</c> for blobs; <see cref="OleClipboardData"/>; <see cref="Guid"/> for <c>VT_CLSID</c>;
/// <see cref="OleVersionedStreamRef"/>; <c>IReadOnlyList&lt;object?&gt;</c> for a vector (elements of the scalar type, or
/// <see cref="OlePropertyValue"/> for <c>VT_VARIANT</c> vectors); <see cref="OleArray"/> for an array. An HRESULT is a <see cref="uint"/>.</para>
/// <para>Strings are held decoded. A string type of the property set's code page keeps the type number it had, so writing it back uses
/// the same form.</para>
/// </remarks>
public sealed class OlePropertyValue
{
    private OlePropertyValue(ushort type, object? value)
    {
        Type = type;
        Value = value;
    }

    /// <summary>Gets the stored type number, flag bits included (see <see cref="OleVarType"/>).</summary>
    public ushort Type { get; }

    /// <summary>Gets the value; see the remarks of the type for what it holds for each <see cref="Type"/>.</summary>
    public object? Value { get; }

    /// <summary>Gets the empty value (<c>VT_EMPTY</c>).</summary>
    public static OlePropertyValue Empty { get; } = new(OleVarType.VtEmpty, null);

    /// <summary>Makes a value from a stored type number and the .NET value for it. The pair is checked when the property set is written.</summary>
    /// <param name="type">The type number, flag bits included.</param>
    /// <param name="value">The value.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue Create(ushort type, object? value) => new(type, value);

    /// <summary>Makes a string in the property set's code page (<c>VT_LPSTR</c>).</summary>
    /// <param name="value">The text.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue FromString(string value) => new(OleVarType.VtLpstr, value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Makes a UTF-16 string (<c>VT_LPWSTR</c>).</summary>
    /// <param name="value">The text.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue FromUnicodeString(string value) => new(OleVarType.VtLpwstr, value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Makes a 32-bit signed integer (<c>VT_I4</c>).</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue FromInt32(int value) => new(OleVarType.VtI4, value);

    /// <summary>Makes a 16-bit signed integer (<c>VT_I2</c>).</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue FromInt16(short value) => new(OleVarType.VtI2, value);

    /// <summary>Makes a 32-bit unsigned integer (<c>VT_UI4</c>).</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue FromUInt32(uint value) => new(OleVarType.VtUI4, value);

    /// <summary>Makes a 64-bit signed integer (<c>VT_I8</c>).</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue FromInt64(long value) => new(OleVarType.VtI8, value);

    /// <summary>Makes a double-precision number (<c>VT_R8</c>).</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue FromDouble(double value) => new(OleVarType.VtR8, value);

    /// <summary>Makes a Boolean (<c>VT_BOOL</c>).</summary>
    /// <param name="value">The flag.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue FromBoolean(bool value) => new(OleVarType.VtBool, value);

    /// <summary>Makes a date (<c>VT_FILETIME</c>).</summary>
    /// <param name="value">The date.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue FromDate(DateTimeOffset value) => new(OleVarType.VtFileTime, OleFileTime.FromDateTimeOffset(value));

    /// <summary>Makes a FILETIME holding a duration or a raw tick count (<c>VT_FILETIME</c>).</summary>
    /// <param name="ticks">100-nanosecond units.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue FromFileTime(ulong ticks) => new(OleVarType.VtFileTime, new OleFileTime(ticks));

    /// <summary>Makes a blob (<c>VT_BLOB</c>).</summary>
    /// <param name="value">The bytes; not copied.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue FromBlob(byte[] value) => new(OleVarType.VtBlob, value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Makes a vector of strings in the property set's code page (<c>VT_VECTOR | VT_LPSTR</c>).</summary>
    /// <param name="values">The strings.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue FromStringVector(IReadOnlyList<string> values) =>
        new((ushort)(OleVarType.VtVector | OleVarType.VtLpstr), new List<object?>(values ?? throw new ArgumentNullException(nameof(values))));

    /// <summary>Makes a vector of UTF-16 strings (<c>VT_VECTOR | VT_LPWSTR</c>).</summary>
    /// <param name="values">The strings.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue FromUnicodeStringVector(IReadOnlyList<string> values) =>
        new((ushort)(OleVarType.VtVector | OleVarType.VtLpwstr), new List<object?>(values ?? throw new ArgumentNullException(nameof(values))));

    /// <summary>Makes a vector of typed values (<c>VT_VECTOR | VT_VARIANT</c>).</summary>
    /// <param name="values">The elements.</param>
    /// <returns>The value.</returns>
    public static OlePropertyValue FromVariantVector(IReadOnlyList<OlePropertyValue> values) =>
        new((ushort)(OleVarType.VtVector | OleVarType.VtVariant), new List<object?>(values ?? throw new ArgumentNullException(nameof(values))));

    /// <summary>Gets the text of a string-typed value (<c>VT_LPSTR</c>, <c>VT_LPWSTR</c>, <c>VT_BSTR</c>), or null for another type.</summary>
    /// <returns>The text, without terminating nulls.</returns>
    public string? AsString() => Value as string is { } s && Type is OleVarType.VtLpstr or OleVarType.VtLpwstr or OleVarType.VtBstr ? s : null;

    /// <summary>Gets a whole-number value as a 64-bit integer, or null for another type.</summary>
    /// <returns>The integer.</returns>
    public long? AsInt64() => Value switch
    {
        short v => v,
        int v => v,
        long v => v,
        sbyte v => v,
        byte v => v,
        ushort v => v,
        uint v when Type != OleVarType.VtError => v,
        ulong v when v <= long.MaxValue => (long)v,
        _ => null,
    };

    /// <summary>Gets a whole-number value as a 32-bit integer, or null when the type is not a number or the value does not fit.</summary>
    /// <returns>The integer.</returns>
    public int? AsInt32() => AsInt64() is { } v && v >= int.MinValue && v <= int.MaxValue ? (int)v : null;

    /// <summary>Gets a numeric value as a double, or null for another type.</summary>
    /// <returns>The number.</returns>
    public double? AsDouble() => Value switch
    {
        double d => d,
        float f => f,
        OleDate d => d.Value,
        OleCurrency c => (double)c.Value,
        _ => AsInt64(),
    };

    /// <summary>Gets a Boolean value, or null for another type.</summary>
    /// <returns>The flag.</returns>
    public bool? AsBoolean() => Value as bool?;

    /// <summary>Gets a date or duration value, or null for another type.</summary>
    /// <returns>The FILETIME as stored.</returns>
    public OleFileTime? AsFileTime() => Value as OleFileTime?;

    /// <summary>Returns the type and value, for logs.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => "0x" + Type.ToString("X4", CultureInfo.InvariantCulture) + ": " + (Value is byte[] b ? b.Length.ToString(CultureInfo.InvariantCulture) + " bytes" : Convert.ToString(Value, CultureInfo.InvariantCulture));
}
