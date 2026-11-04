using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.IO;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Tests;

public class LimitsAndDefectsTests
{
    [Fact]
    public void Defaults_are_positive_and_validate()
    {
        OfficeLimits.Default.Validate();
        OfficeLimits.Large.Validate();
        Assert.True(OfficeLimits.Large.MaxTotalUncompressedSize > OfficeLimits.Default.MaxTotalUncompressedSize);
        Assert.Equal(64, OfficeLimits.Default.MaxFormulaDepth);
        Assert.Equal(8192, OfficeLimits.Default.MaxFormulaLength);
    }

    [Fact]
    public void A_non_positive_limit_is_refused_by_the_open_context()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenContext(new OfficeLimits { MaxXmlDepth = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenContext(new OfficeLimits { MaxRecordSize = -1 }));
    }

    [Fact]
    public void Archive_limits_carry_the_container_values()
    {
        var limits = new OfficeLimits { MaxTotalUncompressedSize = 123, MaxPartCount = 45, MaxExpansionRatio = 6 };
        RecursiveAI.OneArchive.ArchiveLimits a = limits.ToArchiveLimits();
        Assert.Equal(123, a.MaxTotalSize);
        Assert.Equal(45, a.MaxEntryCount);
        Assert.Equal(6, a.MaxExpansionRatio);
    }

    [Fact]
    public void Limit_exception_names_the_property_and_has_a_code()
    {
        var ex = Assert.Throws<OfficeLimitException>(() => OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxXmlDepth), 256, 300, DefectLocation.InPart("/word/document.xml", 99)));
        Assert.Equal("MaxXmlDepth", ex.LimitName);
        Assert.Equal("limit.max-xml-depth", ex.Defect.Code);
        Assert.Equal(DefectKind.Limit, ex.Defect.Kind);
        Assert.Equal(DefectSeverity.Fatal, ex.Defect.Severity);
        Assert.Equal("/word/document.xml@99", ex.Location.ToString());
        OfficeLimitException.ThrowIfExceeded("X", 5, 5);
    }

    [Fact]
    public void Locations_have_stable_text_forms()
    {
        Assert.Equal("/xl/sheet1.xml@10", DefectLocation.InPart("/xl/sheet1.xml", 10).ToString());
        Assert.Equal("/xl/sheet1.xml", DefectLocation.InPart("/xl/sheet1.xml").ToString());
        Assert.Equal("/WordDocument@512", DefectLocation.InRecord("/WordDocument", 512).ToString());
        Assert.Equal("Sheet1!B7", DefectLocation.InCell("Sheet1", "B7").ToString());
        Assert.Equal("text@88", DefectLocation.AtText(88).ToString());
        Assert.Equal(string.Empty, DefectLocation.None.ToString());
    }

    [Fact]
    public void Collection_keeps_order_caps_and_always_keeps_fatal()
    {
        var sink = new DefectCollection(2);
        Defect Make(string code, DefectSeverity s) => new(DefectKind.Structure, code, s, "m", DefectLocation.None);
        sink.Report(Make("a", DefectSeverity.Repaired));
        sink.Report(Make("b", DefectSeverity.Dropped));
        sink.Report(Make("c", DefectSeverity.Repaired));
        Assert.Equal(["a", "b"], sink.Select(d => d.Code));
        Assert.Equal(1, sink.Overflow);
        Assert.False(sink.HasFatal);
        sink.Report(Make("f", DefectSeverity.Fatal));
        Assert.True(sink.HasFatal);
        Assert.Equal(3, sink.Count);
    }

    [Fact]
    public void Collection_accepts_concurrent_reports()
    {
        var sink = new DefectCollection(100_000);
        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 1000; i++)
            {
                sink.Report(new Defect(DefectKind.Value, "v", DefectSeverity.Repaired, "m", DefectLocation.None));
            }
        });
        Assert.Equal(8000, sink.Count);
    }

    [Fact]
    public void Exceptions_carry_their_defect()
    {
        var format = new OfficeFormatException(DefectKind.Markup, "xml.unclosed-element", "No end tag.", DefectLocation.InPart("/word/document.xml", 7));
        Assert.IsAssignableFrom<OfficeException>(format);
        Assert.Equal("xml.unclosed-element", format.Defect.Code);
        Assert.Equal(DefectSeverity.Fatal, format.Defect.Severity);

        var enc = new OfficeEncryptedException(EncryptionProblem.PasswordRequired);
        Assert.Equal("encryption.password-required", enc.Defect.Code);
        var unsupported = new OfficeUnsupportedException("Agile encryption with a custom provider");
        Assert.Equal(DefectKind.Unsupported, unsupported.Defect.Kind);
        var na = new FormatNotAvailableException(OfficeFormat.Doc, "read");
        Assert.IsAssignableFrom<OfficeUnsupportedException>(na);
        Assert.Equal(OfficeFormat.Doc, na.Format);
    }

    [Fact]
    public void Archive_exceptions_translate_to_typed_office_exceptions()
    {
        var limit = ArchiveErrors.Translate(new RecursiveAI.OneCompress.CompressionLimitException("zip", nameof(RecursiveAI.OneArchive.ArchiveLimits.MaxEntryCount), 5, 20));
        var typed = Assert.IsType<OfficeLimitException>(limit);
        Assert.Equal(nameof(OfficeLimits.MaxPartCount), typed.LimitName);
        Assert.IsType<OfficeFormatException>(ArchiveErrors.Translate(new RecursiveAI.OneCompress.CompressionFormatException("zip", 12, "a header")));
        Assert.IsType<OfficeEncryptedException>(ArchiveErrors.Translate(new RecursiveAI.OneArchive.ArchivePasswordException("zip", "a", RecursiveAI.OneArchive.ArchivePasswordProblem.Required)));
        Assert.IsType<OfficeUnsupportedException>(ArchiveErrors.Translate(new RecursiveAI.OneArchive.ArchiveUnsupportedException("zip", "strong encryption")));
    }
}

public class ByteSourceTests
{
    private static readonly byte[] s_data = [.. Enumerable.Range(0, 100).Select(i => (byte)i)];

    [Fact]
    public void Memory_source_reads_at_offsets_and_hands_out_memory()
    {
        using IByteSource s = ByteSource.FromMemory(s_data);
        Assert.Equal(100, s.Length);
        byte[] buf = new byte[10];
        Assert.Equal(10, s.Read(5, buf));
        Assert.Equal(5, buf[0]);
        Assert.Equal(4, s.Read(96, buf));
        Assert.Equal(0, s.Read(100, buf));
        Assert.Equal(0, s.Read(500, buf));
        Assert.True(s.TryGetMemory(out ReadOnlyMemory<byte> mem));
        Assert.Equal(100, mem.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Read(-1, buf));
    }

    [Fact]
    public async Task File_and_stream_sources_agree_with_memory()
    {
        string path = Path.Combine(Path.GetTempPath(), "oneoffice-src-" + Environment.ProcessId + ".bin");
        File.WriteAllBytes(path, s_data);
        try
        {
            using IByteSource file = ByteSource.FromFile(path);
            using IByteSource stream = ByteSource.FromStream(new MemoryStream(s_data), leaveOpen: false);
            foreach (IByteSource s in new[] { file, stream })
            {
                Assert.Equal(100, s.Length);
                byte[] a = new byte[30];
                Assert.Equal(30, s.Read(40, a));
                Assert.Equal(s_data[40..70], a);
                byte[] b = new byte[30];
                Assert.Equal(30, await s.ReadAsync(40, b));
                Assert.Equal(a, b);
                Assert.Equal(10, s.Read(90, new byte[30]));
                Assert.False(s.TryGetMemory(out _));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Stream_source_needs_a_seekable_stream() =>
        Assert.Throws<ArgumentException>(() => ByteSource.FromStream(new ForwardOnly()));

    [Fact]
    public async Task Ranged_source_is_async_only_and_can_be_buffered()
    {
        using IByteSource r = ByteSource.FromRanged(s_data.Length, (o, buf, _) =>
        {
            int n = Math.Min(buf.Length, Math.Min(7, s_data.Length - (int)o)); // short reads are legal
            s_data.AsMemory((int)o, n).CopyTo(buf);
            return new ValueTask<int>(n);
        });
        Assert.False(r.CanReadSync);
        Assert.Throws<NotSupportedException>(() => r.Read(0, new byte[1]));
        byte[] buf = new byte[20];
        Assert.Equal(20, await r.ReadAsync(30, buf));
        Assert.Equal(s_data[30..50], buf);

        using IByteSource mem = await ByteSource.ToMemoryAsync(r, 1000);
        Assert.True(mem.TryGetMemory(out ReadOnlyMemory<byte> all));
        Assert.Equal(s_data, all.ToArray());
        await Assert.ThrowsAsync<OfficeLimitException>(async () => await ByteSource.ToMemoryAsync(r, 10));
    }

    [Fact]
    public void As_stream_seeks_and_reads()
    {
        using IByteSource s = ByteSource.FromMemory(s_data);
        using Stream st = ByteSource.AsStream(s);
        Assert.Equal(100, st.Length);
        st.Seek(-10, SeekOrigin.End);
        byte[] b = new byte[20];
        Assert.Equal(10, st.Read(b, 0, 20));
        Assert.Equal(90, b[0]);
        Assert.Throws<NotSupportedException>(() => st.WriteByte(1));
    }

    private sealed class ForwardOnly : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
