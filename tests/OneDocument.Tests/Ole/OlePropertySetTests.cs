using System.Buffers.Binary;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Ole;

namespace RecursiveAI.OneDocument.Tests;

public class OlePropertySetTests
{
    private static OlePropertySets Everything(int codePage)
    {
        var set = new OlePropertySet(OlePropertySets.UserDefinedPropertiesFormatId) { CodePage = codePage, Locale = 0x409, Behavior = 0 };
        set.SetNamed("short", OlePropertyValue.FromInt16(-7));
        set.SetNamed("int", OlePropertyValue.FromInt32(-123456));
        set.SetNamed("uint", OlePropertyValue.FromUInt32(4000000000));
        set.SetNamed("long", OlePropertyValue.FromInt64(long.MinValue + 5));
        set.SetNamed("ulong", OlePropertyValue.Create(OleVarType.VtUI8, ulong.MaxValue - 3));
        set.SetNamed("sbyte", OlePropertyValue.Create(OleVarType.VtI1, (sbyte)-3));
        set.SetNamed("byte", OlePropertyValue.Create(OleVarType.VtUI1, (byte)200));
        set.SetNamed("ushort", OlePropertyValue.Create(OleVarType.VtUI2, (ushort)65000));
        set.SetNamed("float", OlePropertyValue.Create(OleVarType.VtR4, 1.5f));
        set.SetNamed("double", OlePropertyValue.FromDouble(Math.PI));
        set.SetNamed("currency", OlePropertyValue.Create(OleVarType.VtCy, new OleCurrency(123_4500)));
        set.SetNamed("date", OlePropertyValue.Create(OleVarType.VtDate, new OleDate(44197.5)));
        set.SetNamed("decimal", OlePropertyValue.Create(OleVarType.VtDecimal, new OleDecimal(2, 0x80, 1, 12345)));
        set.SetNamed("error", OlePropertyValue.Create(OleVarType.VtError, 0x80004005u));
        set.SetNamed("true", OlePropertyValue.FromBoolean(true));
        set.SetNamed("false", OlePropertyValue.FromBoolean(false));
        set.SetNamed("ansi", OlePropertyValue.FromString("Grüße"));
        set.SetNamed("bstr", OlePropertyValue.Create(OleVarType.VtBstr, "bstr"));
        set.SetNamed("wide", OlePropertyValue.FromUnicodeString("日本語 text"));
        set.SetNamed("empty string", OlePropertyValue.FromString(string.Empty));
        set.SetNamed("time", OlePropertyValue.FromDate(new DateTimeOffset(2021, 3, 4, 5, 6, 7, TimeSpan.Zero)));
        set.SetNamed("blob", OlePropertyValue.FromBlob([1, 2, 3, 4, 5]));
        set.SetNamed("blobobject", OlePropertyValue.Create(OleVarType.VtBlobObject, new byte[] { 9 }));
        set.SetNamed("cf", OlePropertyValue.Create(OleVarType.VtCf, new OleClipboardData(0xFFFFFFFE, [1, 2, 3])));
        set.SetNamed("clsid", OlePropertyValue.Create(OleVarType.VtClsid, new Guid("00020906-0000-0000-C000-000000000046")));
        set.SetNamed("stream", OlePropertyValue.Create(OleVarType.VtStream, "prop9"));
        set.SetNamed("versioned", OlePropertyValue.Create(OleVarType.VtVersionedStream, new OleVersionedStreamRef(Guid.Parse("11111111-2222-3333-4444-555555555555"), "prop10")));
        set.SetNamed("strings", OlePropertyValue.FromStringVector(["one", "twenty", ""]));
        set.SetNamed("wides", OlePropertyValue.FromUnicodeStringVector(["α", "βγ"]));
        set.SetNamed("shorts", OlePropertyValue.Create((ushort)(OleVarType.VtVector | OleVarType.VtI2), new List<object?> { (short)1, (short)2, (short)3 }));
        set.SetNamed("bools", OlePropertyValue.Create((ushort)(OleVarType.VtVector | OleVarType.VtBool), new List<object?> { true, false, true }));
        set.SetNamed("doubles", OlePropertyValue.Create((ushort)(OleVarType.VtVector | OleVarType.VtR8), new List<object?> { 1.0, 2.5 }));
        set.SetNamed("variants", OlePropertyValue.FromVariantVector([OlePropertyValue.FromString("Sheet1"), OlePropertyValue.FromInt32(3), OlePropertyValue.FromInt16(4)]));
        set.SetNamed("array", OlePropertyValue.Create((ushort)(OleVarType.VtArray | OleVarType.VtI4), new OleArray([new OleArrayDimension(2, 0), new OleArrayDimension(3, 1)], [1, 2, 3, 4, 5, 6])));
        set.SetNamed("null", OlePropertyValue.Create(OleVarType.VtNull, null));
        set.SetNamed("emptyvalue", OlePropertyValue.Empty);

        var all = new OlePropertySets { Version = 1, ClassId = new Guid("00000000-0000-0000-C000-000000000046") };
        var summary = new OlePropertySet(OlePropertySets.DocumentSummaryInformationFormatId) { CodePage = codePage };
        summary.Set(2, OlePropertyValue.FromString("Category"));
        summary.Set(14, OlePropertyValue.FromString("Manager"));
        all.Add(summary);
        all.Add(set);
        return all;
    }

    private static void AssertEqual(OlePropertyValue a, OlePropertyValue b, string where)
    {
        Assert.True(a.Type == b.Type, where + ": type");
        AssertValue(a.Value, b.Value, where);
    }

    private static void AssertValue(object? a, object? b, string where)
    {
        switch (a)
        {
            case byte[] x:
                Assert.Equal(x, Assert.IsType<byte[]>(b));
                break;
            case OleClipboardData cf:
                var cg = Assert.IsType<OleClipboardData>(b);
                Assert.Equal(cf.Format, cg.Format);
                Assert.Equal(cf.Data, cg.Data);
                break;
            case OleArray arr:
                var brr = Assert.IsType<OleArray>(b);
                Assert.Equal(arr.Dimensions, brr.Dimensions);
                Assert.Equal(arr.Items.Count, brr.Items.Count);
                for (int i = 0; i < arr.Items.Count; i++)
                {
                    AssertValue(arr.Items[i], brr.Items[i], where + "[" + i + "]");
                }

                break;
            case IReadOnlyList<object?> list:
                var other = Assert.IsAssignableFrom<IReadOnlyList<object?>>(b);
                Assert.Equal(list.Count, other.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    AssertValue(list[i], other[i], where + "[" + i + "]");
                }

                break;
            case OlePropertyValue v:
                AssertEqual(v, Assert.IsType<OlePropertyValue>(b), where);
                break;
            default:
                Assert.True(Equals(a, b), where + ": " + a + " vs " + b);
                break;
        }
    }

    private static void AssertSame(OlePropertySets a, OlePropertySets b)
    {
        Assert.Equal(a.Version, b.Version);
        Assert.Equal(a.ClassId, b.ClassId);
        Assert.Equal(a.Sets.Count, b.Sets.Count);
        for (int s = 0; s < a.Sets.Count; s++)
        {
            OlePropertySet x = a.Sets[s];
            OlePropertySet y = b.Sets[s];
            Assert.Equal(x.FormatId, y.FormatId);
            Assert.Equal(x.CodePage, y.CodePage);
            Assert.Equal(x.Locale, y.Locale);
            Assert.Equal(x.Behavior, y.Behavior);
            Assert.Equal(x.Names.OrderBy(n => n.Key), y.Names.OrderBy(n => n.Key));
            Assert.Equal(x.Properties.Count, y.Properties.Count);
            for (int i = 0; i < x.Properties.Count; i++)
            {
                Assert.Equal(x.Properties[i].Id, y.Properties[i].Id);
                AssertEqual(x.Properties[i].Value, y.Properties[i].Value, "set " + s + " property " + x.Properties[i].Id);
            }
        }
    }

    [Theory]
    [InlineData(1200)]
    [InlineData(1252)]
    [InlineData(65001)]
    [InlineData(932)]
    public void EveryValueTypeRoundTrips(int codePage)
    {
        OlePropertySets original = Everything(codePage);
        if (codePage == 932)
        {
            // Shift-JIS cannot hold the Latin-1 text of the sample; a Japanese string stands in.
            original.Sets[1].SetNamed("ansi", OlePropertyValue.FromString("日本語"));
        }

        byte[] bytes = original.ToArray();
        var defects = new DefectCollection();
        OlePropertySets back = OlePropertySets.Read(bytes, new OpenContext(defects: defects), "/test");
        Assert.Empty(defects);
        AssertSame(original, back);
        Assert.Equal(bytes, back.ToArray());
    }

    [Fact]
    public void WritingIsDeterministicAndAligned()
    {
        byte[] a = Everything(1200).ToArray();
        byte[] b = Everything(1200).ToArray();
        Assert.Equal(a, b);
        Assert.Equal(0, a.Length % 4);
    }

    [Fact]
    public void FileTimeConvertsToDates()
    {
        var when = new DateTimeOffset(2021, 3, 4, 5, 6, 7, TimeSpan.Zero);
        OleFileTime t = OleFileTime.FromDateTimeOffset(when);
        Assert.Equal(when, t.ToDateTimeOffset());
        Assert.Null(new OleFileTime(0).ToDateTimeOffset());
        Assert.Null(new OleFileTime(ulong.MaxValue).ToDateTimeOffset());
    }

    [Fact]
    public void ShortAndTruncatedStreamsRaiseTypedDefects()
    {
        Assert.False(OlePropertySets.TryRead(new byte[10], out _, new OpenContext(defects: new DefectCollection())));
        var ex = Assert.Throws<OfficeFormatException>(() => OlePropertySets.Read(new byte[27]));
        Assert.Equal("ole.propset-header", ex.Defect.Code);
        Assert.Equal(DefectSeverity.Fatal, ex.Defect.Severity);

        byte[] bigEndian = Everything(1200).ToArray();
        bigEndian[0] = 0xFF;
        bigEndian[1] = 0xFE;
        Assert.Throws<OfficeFormatException>(() => OlePropertySets.Read(bigEndian));
    }

    [Fact]
    public void EveryTruncationIsHandledWithoutAnUntypedException()
    {
        byte[] full = Everything(1252).ToArray();
        for (int n = 0; n < full.Length; n++)
        {
            var defects = new DefectCollection();
            try
            {
                OlePropertySets s = OlePropertySets.Read(full.AsMemory(0, n), new OpenContext(defects: defects), "/cut");
                Assert.True(s.Sets.Count <= 2);
            }
            catch (OfficeFormatException ex)
            {
                Assert.Equal("ole.propset-header", ex.Defect.Code);
            }
        }
    }

    [Fact]
    public void BadOffsetsAndCountsAreReportedWithStreamOffsets()
    {
        byte[] bytes = Everything(1200).ToArray();

        // Point the first property set outside the stream.
        byte[] a = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(28 + 16), (uint)a.Length + 100);
        var defects = new DefectCollection();
        OlePropertySets s = OlePropertySets.Read(a, new OpenContext(defects: defects), "/x");
        Assert.Single(s.Sets);
        Defect d = Assert.Single(defects, x => x.Code == "ole.propset-bad-offset");
        Assert.Equal(44, d.Location.Offset);
        Assert.Equal("/x", d.Location.Container);

        // Point one property's value outside its set.
        byte[] b = (byte[])bytes.Clone();
        int set0 = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(28 + 16));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(set0 + 8 + 8 + 4), 0x7FFFFFF0);
        defects = new DefectCollection();
        s = OlePropertySets.Read(b, new OpenContext(defects: defects), "/x");
        Assert.Equal(2, s.Sets.Count);
        Assert.Contains(defects, x => x.Code == "ole.propset-truncated" && x.Severity == DefectSeverity.Dropped);

        // An absurd property count is clamped.
        byte[] c = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(set0 + 4), 0x7FFFFFFF);
        defects = new DefectCollection();
        OlePropertySets.Read(c, new OpenContext(defects: defects), "/x");
        Assert.Contains(defects, x => x.Code == "ole.propset-count");
    }

    [Fact]
    public void HugeVectorLengthsAreRejectedBeforeAllocating()
    {
        var set = new OlePropertySet(OlePropertySets.SummaryInformationFormatId);
        set.Set(2, OlePropertyValue.FromStringVector(["a", "b"]));
        var all = new OlePropertySets();
        all.Add(set);
        byte[] bytes = all.ToArray();
        int at = bytes.AsSpan().IndexOf(new byte[] { 0x1E, 0x10, 0x00, 0x00 });
        Assert.True(at > 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + 4), 0xFFFFFFF0);
        var defects = new DefectCollection();
        OlePropertySets back = OlePropertySets.Read(bytes, new OpenContext(defects: defects), "/v");
        Assert.Null(back.Sets[0].Find(2));
        Assert.Contains(defects, x => x.Code == "ole.propset-truncated");
    }

    [Fact]
    public void UnknownCodePageIsReportedAndReadAsWindows1252()
    {
        var set = new OlePropertySet(OlePropertySets.SummaryInformationFormatId) { CodePage = 1252 };
        set.Set(2, OlePropertyValue.FromString("caf\u00e9"));
        var all = new OlePropertySets();
        all.Add(set);
        byte[] bytes = all.ToArray();
        // Replace the code page value 1252 (0x04E4) with an unassigned number.
        int at = bytes.AsSpan().IndexOf(new byte[] { 0x02, 0x00, 0x00, 0x00, 0xE4, 0x04 });
        Assert.True(at > 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + 4), 9999);
        var defects = new DefectCollection();
        OlePropertySets back = OlePropertySets.Read(bytes, new OpenContext(defects: defects), "/c");
        Assert.Equal("caf\u00e9", back.Sets[0].Find(2)!.AsString());
        Assert.Contains(defects, x => x.Code == "ole.propset-codepage-unsupported" && x.Kind == DefectKind.Encoding);
    }

    [Fact]
    public void AStringVectorWithoutPaddingBetweenElementsIsRead()
    {
        // [MS-OSHARED] describes a vector of ANSI strings without padding between elements; the reader accepts both layouts.
        var set = new OlePropertySet(OlePropertySets.DocumentSummaryInformationFormatId) { CodePage = 1252 };
        set.Set(13, OlePropertyValue.FromStringVector(["a", "bc"]));
        var all = new OlePropertySets();
        all.Add(set);
        byte[] aligned = all.ToArray();

        // Rebuild the same stream by hand with strings of 2 and 3 bytes and no padding: size 2 "a\0", size 3 "bc\0", then pad once.
        var raw = new List<byte>();
        raw.AddRange(BitConverter.GetBytes((uint)0x101E)); // type
        raw.AddRange(BitConverter.GetBytes(2u));
        raw.AddRange(BitConverter.GetBytes(2u));
        raw.AddRange("a\0"u8.ToArray());
        raw.AddRange(BitConverter.GetBytes(3u));
        raw.AddRange("bc\0"u8.ToArray());
        raw.Add(0);
        byte[] value = [.. raw];

        int at = aligned.AsSpan().IndexOf(BitConverter.GetBytes((uint)0x101E));
        Assert.True(at > 0);
        byte[] rebuilt = [.. aligned.AsSpan(0, at).ToArray(), .. value];
        // Fix the set size and the property set stream length fields.
        int setStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(rebuilt.AsSpan(28 + 16));
        BinaryPrimitives.WriteUInt32LittleEndian(rebuilt.AsSpan(setStart), (uint)(rebuilt.Length - setStart));

        OlePropertySets back = OlePropertySets.Read(rebuilt, new OpenContext(defects: new DefectCollection()), "/u");
        var strings = Assert.IsAssignableFrom<IReadOnlyList<object?>>(back.Sets[0].Find(13)!.Value);
        Assert.Equal(["a", "bc"], strings.Cast<string>());
        Assert.Equal(["a", "bc"], Assert.IsAssignableFrom<IReadOnlyList<object?>>(OlePropertySets.Read(aligned).Sets[0].Find(13)!.Value).Cast<string>());
    }

    [Fact]
    public void WriterRejectsMismatchedValues()
    {
        var set = new OlePropertySet(OlePropertySets.SummaryInformationFormatId);
        set.Set(2, OlePropertyValue.Create(OleVarType.VtI4, "not an int"));
        var all = new OlePropertySets();
        all.Add(set);
        Assert.Throws<ArgumentException>(() => all.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => set.Set(1, OlePropertyValue.FromInt32(1)));
    }

    [Fact]
    public void NamedPropertiesAreFoundIgnoringCase()
    {
        var set = new OlePropertySet(OlePropertySets.UserDefinedPropertiesFormatId);
        uint id = set.SetNamed("Project", OlePropertyValue.FromString("x"));
        Assert.Equal(2u, id);
        Assert.Equal(id, set.FindByName("PROJECT")!.Id);
        Assert.Equal(3u, set.SetNamed("Other", OlePropertyValue.FromInt32(1)));
        Assert.Equal(id, set.SetNamed("project", OlePropertyValue.FromString("y")));
        Assert.Equal("y", set.Find(id)!.AsString());
        Assert.True(set.Remove(id));
        Assert.Null(set.FindByName("Project"));
    }
}
