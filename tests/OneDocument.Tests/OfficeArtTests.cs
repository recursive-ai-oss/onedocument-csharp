using System.Buffers.Binary;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.OfficeArt;

namespace RecursiveAI.OneDocument.Tests;

public class OfficeArtTests
{
    private static byte[] Record(int version, int instance, int type, byte[] payload)
    {
        var bytes = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)(version | (instance << 4)));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] Int32s(params int[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
        }

        return bytes;
    }

    private static byte[] Fopt(params (int id, uint value)[] entries)
    {
        var payload = new byte[entries.Length * 6];
        for (int i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(i * 6), (ushort)entries[i].id);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(i * 6 + 2), entries[i].value);
        }

        return Record(3, entries.Length, ArtRecordType.Fopt, payload);
    }

    private static byte[] Shape(int spid, int type, int flags, byte[] extra) =>
        Record(0xF, 0, ArtRecordType.SpContainer, Cat(Record(2, type, ArtRecordType.Fsp, Int32s(spid, flags)), extra));

    [Fact]
    public void A_drawing_parses_into_groups_shapes_and_properties()
    {
        byte[] patriarch = Shape(1024, 0, 0x5, Record(1, 0, ArtRecordType.Fspgr, Int32s(0, 0, 5760, 4320)));
        byte[] rect = Shape(1025, 1, 0x200, Cat(
            Fopt((ArtPropertyId.FillColor, 0x00FF8040), (ArtPropertyId.FillBooleans, 0x00100010)),
            Record(0, 0, ArtRecordType.ClientAnchor, Int32s(10, 20, 30, 40))));
        byte[] drawing = Record(0xF, 1, ArtRecordType.DgContainer,
            Cat(Record(0, 1, ArtRecordType.Fdg, Int32s(2, 1025)), Record(0xF, 0, ArtRecordType.SpgrContainer, Cat(patriarch, rect))));

        IReadOnlyList<ArtRecord> top = ArtRecords.Parse(drawing, 0, ArtReadContext.Default);
        ArtDrawing parsed = ArtDrawing.Parse(top[0], ArtReadContext.Default);

        Assert.Equal(1, parsed.DrawingId);
        Assert.NotNull(parsed.Root);
        Assert.Equal(new ArtRect(0, 0, 5760, 4320), parsed.Root!.GroupSpace);
        ArtShape shape = Assert.Single(parsed.Shapes);
        Assert.Equal(1025, shape.ShapeId);
        Assert.Equal(1, shape.ShapeType);
        Assert.True(shape.Properties.GetFlag(ArtPropertyId.FillBooleans, 0x10, false));
        Assert.Equal("4080FF", shape.Properties.GetColor(ArtPropertyId.FillColor)!.Value.ToHex());
        Assert.False(shape.Properties.GetFlag(ArtPropertyId.LineBooleans, 0x8, false));
        Assert.NotNull(shape.ClientAnchor);
    }

    [Fact]
    public void A_record_that_runs_past_the_data_is_clamped_and_reported()
    {
        byte[] data = Record(0, 0, 0x1234, new byte[16]);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 4000);
        var defects = new DefectCollection(100);
        IReadOnlyList<ArtRecord> records = ArtRecords.Parse(data, 100, new ArtReadContext(OfficeLimits.Default, defects, "S"));

        Assert.Equal(16, records[0].Payload.Length);
        Defect defect = Assert.Single(defects);
        Assert.Equal("art.record-overrun", defect.Code);
        Assert.Equal(100, defect.Location.Offset);
        Assert.Equal("S", defect.Location.Container);
    }

    [Fact]
    public void Nesting_past_the_limit_raises_a_limit_exception()
    {
        byte[] inner = Record(0, 0, 1, []);
        for (int i = 0; i < 40; i++)
        {
            inner = Record(0xF, 0, 0xF003, inner);
        }

        var limits = new OfficeLimits { MaxRecursionDepth = 8 };
        Assert.Throws<OfficeLimitException>(() => ArtRecords.Parse(inner, 0, new ArtReadContext(limits, DiscardDefectSink.Instance, "S")));
    }

    [Fact]
    public void A_property_table_with_too_many_entries_is_clamped()
    {
        byte[] fopt = Fopt((0x181, 5));
        BinaryPrimitives.WriteUInt16LittleEndian(fopt, (ushort)(3 | (50 << 4)));
        var defects = new DefectCollection(10);
        ArtRecord record = ArtRecords.Parse(fopt, 0, ArtReadContext.Default)[0];
        var properties = new ArtProperties();
        properties.AddTable(record, new ArtReadContext(OfficeLimits.Default, defects, "S"));

        Assert.Equal(5, properties.GetInt(0x181));
        Assert.Contains(defects, d => d.Code == "art.property-overrun");
    }

    [Fact]
    public void A_dib_blip_in_the_store_becomes_a_bmp_file()
    {
        // 2x1, 24-bit bitmap: BITMAPINFOHEADER (40 bytes) and one 8-byte padded row.
        var dib = new byte[40 + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 24);
        byte[] blip = Record(0, 0x7A8, ArtRecordType.BlipDib, Cat(new byte[16], [0xFF], dib));
        var bse = new byte[36];
        bse[0] = 7;
        bse[1] = 7;
        BinaryPrimitives.WriteUInt32LittleEndian(bse.AsSpan(20), (uint)blip.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bse.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bse.AsSpan(28), 0xFFFFFFFF);
        byte[] store = Record(0xF, 1, ArtRecordType.BStoreContainer, Record(2, 7, ArtRecordType.Bse, Cat(bse, blip)));

        ArtBlipStore parsed = ArtBlipStore.Parse(ArtRecords.Parse(store, 0, ArtReadContext.Default)[0], ArtReadContext.Default);
        ArtImage? image = parsed.GetImage(1, default, ArtReadContext.Default);

        Assert.NotNull(image);
        Assert.Equal(ArtImageFormat.Dib, image!.Format);
        Assert.Equal((byte)'B', image.Data.Span[0]);
        Assert.Equal(14 + 48, image.Data.Length);
        Assert.Equal(54u, BinaryPrimitives.ReadUInt32LittleEndian(image.Data.Span[10..]));
        Assert.Null(parsed.GetImage(5, default, ArtReadContext.Default));
    }

    [Fact]
    public void Trailing_bytes_too_short_for_a_header_are_reported()
    {
        var defects = new DefectCollection(10);
        IReadOnlyList<ArtRecord> records = ArtRecords.Parse(Cat(Record(0, 0, 0x1234, []), [1, 2, 3]), 40, new ArtReadContext(OfficeLimits.Default, defects, "S"));

        Assert.Single(records);
        Defect d = Assert.Single(defects);
        Assert.Equal("art.record-truncated", d.Code);
        Assert.Equal(48, d.Location.Offset);
    }

    [Fact]
    public void Damaged_store_entries_and_blips_are_reported_not_thrown()
    {
        var defects = new DefectCollection(20);
        var context = new ArtReadContext(OfficeLimits.Default, defects, "S");
        // A BSE shorter than its fixed part, a BSE whose picture lives past the end of the delay stream, and a PNG BLIP with no data.
        var bse = new byte[36];
        BinaryPrimitives.WriteUInt32LittleEndian(bse.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bse.AsSpan(28), 5000);
        byte[] store = Record(0xF, 3, ArtRecordType.BStoreContainer, Cat(
            Record(2, 6, ArtRecordType.Bse, new byte[10]),
            Record(2, 6, ArtRecordType.Bse, bse),
            Record(0, 0x6E0, ArtRecordType.BlipPng, new byte[16]),
            Record(0, 1, 0xF0F0, [])));
        ArtBlipStore parsed = ArtBlipStore.Parse(ArtRecords.Parse(store, 0, context)[0], context);

        Assert.Null(parsed.GetImage(2, new byte[100], context));
        Assert.Null(parsed.GetImage(3, default, context));
        Assert.Contains(defects, d => d.Code == "art.bse-truncated");
        Assert.Contains(defects, d => d.Code == "art.blip-missing");
        Assert.Contains(defects, d => d.Code == "art.blip-invalid");
    }
}
