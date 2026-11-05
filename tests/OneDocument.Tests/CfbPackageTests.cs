using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneArchive;
using RecursiveAI.OneArchive.Cfb;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Tests;

public sealed class CfbPackageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "onedocument-cfb-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);
    private static readonly Guid RootClsid = new("00020906-0000-0000-C000-000000000046");

    public CfbPackageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static byte[] Bytes(int length, int seed)
    {
        byte[] b = new byte[length];
        new Random(seed).NextBytes(b);
        return b;
    }

    // A small document-like compound file: a root CLSID, a big stream, a mini stream, a stream with a control character in its
    // name, and a nested storage with its own CLSID.
    private static byte[] Sample(int major = 3)
    {
        using var ms = new MemoryStream();
        using (var w = new CompoundFileWriter(ms, new CompoundFileWriterOptions { MajorVersion = major }, leaveOpen: true))
        {
            w.WriteEntry(new CfbEntry { Name = string.Empty, RawName = string.Empty, Kind = ArchiveEntryKind.Directory, ClassId = RootClsid });
            void File(string name, byte[] data) => w.WriteEntry(new ArchiveEntry { Name = name, Kind = ArchiveEntryKind.File, Size = data.Length }, data);
            File("WordDocument", Bytes(20_000, 1));
            File("1Table", Bytes(300, 2));
            File("%0005SummaryInformation", Bytes(120, 3));
            w.WriteEntry(new CfbEntry { Name = "ObjectPool", RawName = "ObjectPool", Kind = ArchiveEntryKind.Directory, ClassId = new Guid("11111111-2222-3333-4444-555555555555"), StateBits = 7 });
            File("ObjectPool/_1/CONTENTS", Bytes(5000, 4));
            File("ObjectPool/_1/Ole", Bytes(20, 5));
            w.Finish();
        }

        return ms.ToArray();
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    private static Dictionary<string, byte[]> Streams(byte[] cfb)
    {
        var d = new Dictionary<string, byte[]>();
        using var r = new CompoundFileReader(new MemoryStream(cfb));
        while (r.GetNextEntry() is { } e)
        {
            if (e.ObjectType == CfbObjectType.Stream)
            {
                using var s = r.OpenEntryStream(e);
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                d["/" + e.Name] = ms.ToArray();
            }
        }

        return d;
    }

    [Fact]
    public void Opens_lazily_and_lists_streams_by_path()
    {
        byte[] cfb = Sample();
        using CfbPackage pkg = CfbPackage.Open(new MemoryStream(cfb), Ctx(out DefectCollection defects));
        Assert.Empty(defects);
        Assert.Equal(PackageKind.CompoundFile, pkg.Kind);
        Assert.Equal(0, pkg.LoadedPartCount);
        Assert.False(pkg.IsModified);
        Assert.Equal(RootClsid, pkg.RootClassId);
        Assert.Equal(5, pkg.Parts.Count);
        Assert.Contains("/WordDocument", pkg.Parts.Select(p => p.Name));
        Assert.Contains("/\u0005SummaryInformation", pkg.Parts.Select(p => p.Name));
        Assert.Contains("/ObjectPool/_1/CONTENTS", pkg.Parts.Select(p => p.Name));
        CfbStorage pool = Assert.Single(pkg.Storages, s => s.Path == "/ObjectPool");
        Assert.Equal(7u, pool.StateBits);
        Assert.Equal(20_000, pkg.FindPart("/WordDocument")!.Length);
        Assert.Equal(0, pkg.LoadedPartCount);
        Assert.Equal(Bytes(20_000, 1), pkg.FindPart("/worddocument")!.GetBytes().ToArray());
        Assert.Equal(1, pkg.LoadedPartCount);
        Assert.Equal(Bytes(120, 3), pkg.FindPart("/\u0005SummaryInformation")!.GetBytes().ToArray());
        using Stream s = pkg.FindPart("/ObjectPool/_1/CONTENTS")!.OpenRead();
        byte[] got = new byte[5000];
        Assert.Equal(5000, s.Read(got, 0, got.Length));
        Assert.Equal(Bytes(5000, 4), got);
        Assert.Null(pkg.FindPart("/nope"));
    }

    private static OpenContext Ctx(out DefectCollection defects, OfficeLimits? limits = null)
    {
        defects = new DefectCollection();
        return new OpenContext(limits, defects);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Unmodified_package_writes_the_original_bytes(int major)
    {
        byte[] cfb = Sample(major);
        using CfbPackage pkg = CfbPackage.Open(new MemoryStream(cfb));
        using var output = new MemoryStream();
        pkg.WriteTo(output);
        Assert.Equal(Sha(cfb), Sha(output.ToArray()));
    }

    [Fact]
    public void Editing_one_stream_keeps_the_others_the_names_and_the_clsids()
    {
        byte[] cfb = Sample();
        using CfbPackage pkg = CfbPackage.Open(new MemoryStream(cfb));
        pkg.FindPart("/1Table")!.SetBytes(Bytes(40_000, 9));
        pkg.AddPart("/ObjectPool/_2/Extra", "", new byte[] { 1, 2, 3 });
        Assert.True(pkg.RemovePart("/ObjectPool/_1/Ole"));
        Assert.True(pkg.IsModified);
        using var output = new MemoryStream();
        pkg.WriteTo(output);

        using CfbPackage back = CfbPackage.Open(new MemoryStream(output.ToArray()), Ctx(out DefectCollection defects));
        Assert.Empty(defects);
        Assert.Equal(RootClsid, back.RootClassId);
        Assert.Equal(7u, back.Storages.Single(s => s.Path == "/ObjectPool").StateBits);
        Assert.Equal(new Guid("11111111-2222-3333-4444-555555555555"), back.Storages.Single(s => s.Path == "/ObjectPool").ClassId);
        Assert.Equal(Bytes(40_000, 9), back.FindPart("/1Table")!.GetBytes().ToArray());
        Assert.Equal(Bytes(20_000, 1), back.FindPart("/WordDocument")!.GetBytes().ToArray());
        Assert.Equal(Bytes(120, 3), back.FindPart("/\u0005SummaryInformation")!.GetBytes().ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, back.FindPart("/ObjectPool/_2/Extra")!.GetBytes().ToArray());
        Assert.Null(back.FindPart("/ObjectPool/_1/Ole"));
    }

    [Fact]
    public void Bad_names_are_rejected_and_special_characters_round_trip()
    {
        using CfbPackage pkg = CfbPackage.Create();
        Assert.Throws<ArgumentException>(() => pkg.AddPart("/" + new string('a', 32), "", ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentException>(() => pkg.AddPart("/a:b", "", ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentException>(() => pkg.AddPart("/", "", ReadOnlyMemory<byte>.Empty));
        pkg.AddPart("/100%0025", "", new byte[] { 6 });
        pkg.AddPart("/\u0001CompObj", "", new byte[] { 7 });
        Assert.Throws<ArgumentException>(() => pkg.AddPart("/a%002Fb", "", ReadOnlyMemory<byte>.Empty));
        using var ms = new MemoryStream();
        pkg.WriteTo(ms);
        using CfbPackage back = CfbPackage.Open(new MemoryStream(ms.ToArray()));
        Assert.Equal(new byte[] { 6 }, back.FindPart("/100%0025")!.GetBytes().ToArray());
        Assert.Equal(new byte[] { 7 }, back.FindPart("/\u0001CompObj")!.GetBytes().ToArray());
    }

    [Fact]
    public void Names_with_a_slash_in_existing_files_survive_a_rewrite()
    {
        using var built = new MemoryStream();
        using (var w = new CompoundFileWriter(built, new CompoundFileWriterOptions { StrictNames = false }, leaveOpen: true))
        {
            w.WriteEntry(new ArchiveEntry { Name = "a%002Fb", Kind = ArchiveEntryKind.File, Size = 1 }, new byte[] { 5 });
            w.WriteEntry(new ArchiveEntry { Name = "other", Kind = ArchiveEntryKind.File, Size = 1 }, new byte[] { 1 });
            w.Finish();
        }

        using CfbPackage pkg = CfbPackage.Open(new MemoryStream(built.ToArray()));
        Assert.Equal(new byte[] { 5 }, pkg.FindPart("/a%002Fb")!.GetBytes().ToArray());
        pkg.FindPart("/other")!.SetBytes(new byte[] { 2 });
        using var ms = new MemoryStream();
        pkg.WriteTo(ms);
        using CfbPackage back = CfbPackage.Open(new MemoryStream(ms.ToArray()));
        Assert.Equal(new byte[] { 5 }, back.FindPart("/a%002Fb")!.GetBytes().ToArray());
        Assert.Equal(new byte[] { 2 }, back.FindPart("/other")!.GetBytes().ToArray());
    }

    [Fact]
    public void Save_replaces_the_file_and_stays_usable()
    {
        string path = Path.Combine(_dir, "doc.doc");
        File.WriteAllBytes(path, Sample());
        using CfbPackage pkg = CfbPackage.Open(path);
        CfbStreamPart table = pkg.Streams.Single(p => p.Name == "/1Table");
        table.SetBytes(Bytes(10, 77));
        pkg.Save();
        Assert.False(pkg.IsModified);
        Assert.Equal(Bytes(10, 77), table.GetBytes().ToArray());
        Assert.Equal(Bytes(20_000, 1), pkg.FindPart("/WordDocument")!.GetBytes().ToArray());
        Assert.Empty(Directory.GetFiles(_dir, ".*.tmp"));
        pkg.AddPart("/New", "", new byte[] { 1 });
        pkg.Save();
        using CfbPackage back = CfbPackage.Open(path);
        Assert.Equal(Bytes(10, 77), back.FindPart("/1Table")!.GetBytes().ToArray());
        Assert.NotNull(back.FindPart("/New"));
        Assert.Equal(Bytes(5000, 4), back.FindPart("/ObjectPool/_1/CONTENTS")!.GetBytes().ToArray());
    }

    [Fact]
    public void Cancelled_save_leaves_the_original()
    {
        string path = Path.Combine(_dir, "doc.doc");
        byte[] cfb = Sample();
        File.WriteAllBytes(path, cfb);
        using CfbPackage pkg = CfbPackage.Open(path);
        pkg.FindPart("/1Table")!.SetBytes(Bytes(10, 77));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => pkg.Save(cts.Token));
        Assert.Equal(Sha(cfb), Sha(File.ReadAllBytes(path)));
        Assert.Empty(Directory.GetFiles(_dir, ".*.tmp"));
        Assert.True(pkg.IsModified);
        Assert.Equal(Bytes(20_000, 1), pkg.FindPart("/WordDocument")!.GetBytes().ToArray());
    }

    [Fact]
    public void A_looping_sector_chain_is_a_typed_error()
    {
        byte[] cfb = Sample();
        uint start;
        using (var r = new CompoundFileReader(new MemoryStream(cfb)))
        {
            start = r.Find("WordDocument")!.StartSector;
        }

        // The FAT: header DIFAT entry 0 (offset 0x4C) names its sector; make the second sector of the stream point back at the first.
        uint fatSector = BinaryPrimitives.ReadUInt32LittleEndian(cfb.AsSpan(0x4C));
        int fatOffset = (int)((fatSector + 1) * 512);
        uint next = BinaryPrimitives.ReadUInt32LittleEndian(cfb.AsSpan(fatOffset + (int)start * 4));
        BinaryPrimitives.WriteUInt32LittleEndian(cfb.AsSpan(fatOffset + (int)next * 4), start);

        using CfbPackage pkg = CfbPackage.Open(new MemoryStream(cfb), Ctx(out DefectCollection defects));
        // Strict reading notices at the stream, or recovery noticed already; either way typed and with a location.
        try
        {
            byte[] data = pkg.FindPart("/WordDocument")!.GetBytes().ToArray();
            Assert.True(data.Length <= 20_000);
            Assert.NotEmpty(defects);
            Assert.Contains(defects, d => d.Code.StartsWith("packaging.cfb-", StringComparison.Ordinal));
        }
        catch (OfficeFormatException ex)
        {
            Assert.Equal("packaging.damaged", ex.Defect.Code);
            Assert.Equal("/WordDocument", ex.Location.Container);
        }
    }

    [Fact]
    public void Truncated_files_are_recovered_with_defects_or_rejected_typed()
    {
        byte[] cfb = Sample();
        int recovered = 0;
        foreach (int cut in new[] { 100, 512, 700, 4096, cfb.Length / 2, cfb.Length - 512, cfb.Length - 1 })
        {
            try
            {
                using CfbPackage pkg = CfbPackage.Open(new MemoryStream(cfb[..cut]), Ctx(out DefectCollection defects));
                recovered++;
                Assert.NotEmpty(defects);
                foreach (IPackagePart p in pkg.Parts)
                {
                    try
                    {
                        _ = p.GetBytes();
                    }
                    catch (OfficeException)
                    {
                    }
                }

                pkg.WriteTo(Stream.Null);
            }
            catch (OfficeException ex)
            {
                Assert.NotEmpty(ex.Defect.Code);
            }
        }

        Assert.True(recovered > 0);
    }

    [Fact]
    public void Not_a_compound_file_and_limits_are_typed()
    {
        Assert.Throws<OfficeFormatException>(() => CfbPackage.Open(new MemoryStream(Encoding.ASCII.GetBytes("not a compound file at all, just text"))));
        Assert.Throws<OfficeFormatException>(() => CfbPackage.Open(new MemoryStream([])));
        byte[] cfb = Sample();
        var count = Assert.Throws<OfficeLimitException>(() => CfbPackage.Open(new MemoryStream(cfb), new OpenContext(new OfficeLimits { MaxPartCount = 3 })));
        Assert.Equal(nameof(OfficeLimits.MaxPartCount), count.LimitName);
        using CfbPackage pkg = CfbPackage.Open(new MemoryStream(cfb), new OpenContext(new OfficeLimits { MaxPartSize = 1000 }));
        var big = Assert.Throws<OfficeLimitException>(() => pkg.FindPart("/WordDocument")!.GetBytes());
        Assert.Equal(nameof(OfficeLimits.MaxPartSize), big.LimitName);
        Assert.Equal("/WordDocument", big.Location.Container);
        Assert.False(CfbPackage.TryOpen(ByteSource.FromMemory(Encoding.ASCII.GetBytes("xx")), out CfbPackage? none, Ctx(out DefectCollection d)));
        Assert.Null(none);
        Assert.True(d.HasFatal);
    }

    [Fact]
    public void Mutated_files_never_throw_untyped_exceptions()
    {
        byte[] cfb = Sample();
        var rng = new Random(99);
        for (int i = 0; i < 400; i++)
        {
            byte[] m = (byte[])cfb.Clone();
            for (int k = 0; k < 1 + i % 5; k++)
            {
                // Aim at the header, the FAT and the directory more than at stream data.
                int at = rng.Next(3) == 0 ? rng.Next(m.Length) : rng.Next(Math.Min(m.Length, 2048));
                m[at] = (byte)rng.Next(256);
            }

            try
            {
                using CfbPackage p = CfbPackage.Open(new MemoryStream(m), new OpenContext(new OfficeLimits { MaxPartSize = 1 << 20 }));
                foreach (IPackagePart part in p.Parts)
                {
                    try
                    {
                        _ = part.GetBytes();
                    }
                    catch (OfficeException)
                    {
                    }
                }

                p.WriteTo(Stream.Null);
            }
            catch (OfficeException)
            {
            }
        }
    }
}
