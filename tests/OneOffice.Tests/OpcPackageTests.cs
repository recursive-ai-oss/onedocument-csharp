using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.IO;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Tests;

public sealed class OpcPackageTests : IDisposable
{
    private const string DocType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    private const string OfficeDocRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oneoffice-opc-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);

    public OpcPackageTests() => Directory.CreateDirectory(_dir);

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

    private static byte[] Build(IEnumerable<(string Name, byte[] Content)> entries, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                ZipArchiveEntry e = zip.CreateEntry(name, level);
                using Stream s = e.Open();
                s.Write(content);
            }
        }

        return ms.ToArray();
    }

    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    private static string ContentTypesXml(string extra = "") =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
        $"<Override PartName=\"/word/document.xml\" ContentType=\"{DocType}\"/>{extra}</Types>";

    private static string Rels(params (string Id, string Type, string Target, string? Mode)[] rels)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        foreach ((string id, string type, string target, string? mode) in rels)
        {
            sb.Append($"<Relationship Id=\"{id}\" Type=\"{type}\" Target=\"{target}\"" + (mode is null ? "" : $" TargetMode=\"{mode}\"") + "/>");
        }

        return sb.Append("</Relationships>").ToString();
    }

    private static List<(string Name, byte[] Content)> Docx(int mediaCount = 3)
    {
        var parts = new List<(string, byte[])>
        {
            ("[Content_Types].xml", U(ContentTypesXml())),
            ("_rels/.rels", U(Rels(("rId1", OfficeDocRel, "word/document.xml", null)))),
            ("word/document.xml", U("<?xml version=\"1.0\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Hello</w:t></w:r></w:p></w:body></w:document>")),
            ("word/_rels/document.xml.rels", U(Rels(("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "media/image1.png", null), ("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink", "https://example.com/", "External")))),
        };
        for (int i = 1; i <= mediaCount; i++)
        {
            byte[] data = new byte[2000 + i];
            new Random(i).NextBytes(data);
            parts.Add(($"word/media/image{i}.png", data));
        }

        return parts;
    }

    private static Dictionary<string, byte[]> ReadZip(byte[] zip)
    {
        var d = new Dictionary<string, byte[]>();
        using var za = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        foreach (ZipArchiveEntry e in za.Entries)
        {
            using var ms = new MemoryStream();
            using (Stream s = e.Open())
            {
                s.CopyTo(ms);
            }

            d[e.FullName] = ms.ToArray();
        }

        return d;
    }

    private static string[] Order(byte[] zip)
    {
        using var za = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return [.. za.Entries.Select(e => e.FullName)];
    }

    private static OpenContext Ctx(out DefectCollection defects, OfficeLimits? limits = null)
    {
        defects = new DefectCollection();
        return new OpenContext(limits, defects);
    }

    private static string[] Codes(DefectCollection d) => [.. d.Select(x => x.Code)];

    [Fact]
    public void Opens_parts_content_types_and_relationships()
    {
        byte[] zip = Build(Docx());
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(zip), Ctx(out DefectCollection defects));
        Assert.Empty(defects);
        Assert.Equal(PackageKind.Opc, pkg.Kind);
        Assert.False(pkg.IsModified);
        Assert.Equal(["/word/document.xml", "/word/media/image1.png", "/word/media/image2.png", "/word/media/image3.png"], pkg.Parts.Select(p => p.Name));
        Assert.Equal(DocType, pkg.FindPart("/WORD/document.xml")!.ContentType);
        Assert.Equal("image/png", pkg.FindPart("/word/media/image2.png")!.ContentType);
        Assert.Equal("word/document.xml", Assert.Single(pkg.GetRelationships()).Target);
        IReadOnlyList<Relationship> rels = pkg.GetRelationships("/word/document.xml");
        Assert.Equal(2, rels.Count);
        Assert.Equal(RelationshipTargetMode.External, rels[1].TargetMode);
        Assert.True(OpcPackage.TryResolveTarget("/word/document.xml", rels[0], out PartUri target));
        Assert.Equal("/word/media/image1.png", target.Value);
        Assert.False(OpcPackage.TryResolveTarget("/word/document.xml", rels[1], out _));
        Assert.Empty(pkg.GetRelationships("/nothing.xml"));
    }

    [Fact]
    public void Open_reads_only_the_directory_content_types_and_package_relationships()
    {
        byte[] zip = Build(Docx(mediaCount: 200), CompressionLevel.Fastest);
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(zip));
        Assert.Equal(2, pkg.LoadedPartCount);
        Assert.Equal(201, pkg.Parts.Count);
        _ = pkg.GetRelationships("/word/document.xml");
        Assert.Equal(3, pkg.LoadedPartCount);
        _ = pkg.FindPart("/word/media/image7.png")!.GetBytes();
        Assert.Equal(4, pkg.LoadedPartCount);
        _ = pkg.FindPart("/word/media/image7.png")!.GetBytes();
        Assert.Equal(4, pkg.LoadedPartCount);
    }

    [Fact]
    public void Untouched_package_round_trips_every_part_and_keeps_order()
    {
        byte[] zip = Build(Docx());
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(zip));
        using var output = new MemoryStream();
        pkg.WriteTo(output);
        byte[] again = output.ToArray();
        Assert.Equal(Order(zip), Order(again));
        Dictionary<string, byte[]> a = ReadZip(zip), b = ReadZip(again);
        foreach (string name in a.Keys)
        {
            Assert.Equal(a[name], b[name]);
        }

        Assert.False(pkg.IsModified);
    }

    [Fact]
    public void Editing_one_part_changes_only_that_part()
    {
        byte[] zip = Build(Docx());
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(zip));
        IPackagePart doc = pkg.FindPart("/word/document.xml")!;
        doc.SetBytes(U("<w:document xmlns:w=\"x\"/>"));
        Assert.True(pkg.IsModified);
        using var output = new MemoryStream();
        pkg.WriteTo(output);
        Dictionary<string, byte[]> a = ReadZip(zip), b = ReadZip(output.ToArray());
        Assert.Equal(a.Keys.Order(), b.Keys.Order());
        foreach (string name in a.Keys)
        {
            if (name == "word/document.xml")
            {
                Assert.Equal(U("<w:document xmlns:w=\"x\"/>"), b[name]);
            }
            else
            {
                Assert.Equal(a[name], b[name]);
            }
        }
    }

    [Fact]
    public void Adding_and_removing_parts_updates_content_types_and_relationships()
    {
        byte[] zip = Build(Docx());
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(zip));
        pkg.AddPart("/word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml", U("<w:styles xmlns:w=\"x\"/>"));
        pkg.AddPart("/word/media/photo.JPG", "image/jpeg", new byte[] { 1, 2, 3 });
        Assert.True(pkg.RemovePart("/word/media/image2.png"));
        pkg.SetRelationships("/word/document.xml", [.. pkg.GetRelationships("/word/document.xml"), new Relationship("rId9", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml")]);
        using var output = new MemoryStream();
        pkg.WriteTo(output);

        using OpcPackage back = OpcPackage.Open(new MemoryStream(output.ToArray()), Ctx(out DefectCollection defects));
        Assert.Empty(defects);
        Assert.Null(back.FindPart("/word/media/image2.png"));
        Assert.Equal("image/jpeg", back.FindPart("/word/media/photo.jpg")!.ContentType);
        Assert.Contains("styles+xml", back.FindPart("/word/styles.xml")!.ContentType);
        Assert.Equal(3, back.GetRelationships("/word/document.xml").Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, back.FindPart("/word/media/photo.jpg")!.GetBytes().ToArray());
        Assert.Equal(ReadZip(zip)["word/media/image3.png"], back.FindPart("/word/media/image3.png")!.GetBytes().ToArray());
        Assert.Throws<ArgumentException>(() => back.AddPart("/WORD/styles.xml", "x/y", ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentException>(() => back.AddPart("/_rels/.rels", "x/y", ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void Created_package_round_trips()
    {
        using OpcPackage pkg = OpcPackage.Create();
        Assert.True(pkg.IsModified);
        pkg.AddPart("/word/document.xml", DocType, U("<w:document xmlns:w=\"x\"/>"));
        pkg.SetRelationships(null, [new Relationship("rId1", OfficeDocRel, "word/document.xml")]);
        string path = Path.Combine(_dir, "new.docx");
        pkg.SaveAs(path);
        string[] order = Order(File.ReadAllBytes(path));
        Assert.Equal(["[Content_Types].xml", "_rels/.rels", "word/document.xml"], order);
        using OpcPackage back = OpcPackage.Open(path);
        Assert.Equal(DocType, back.FindPart("/word/document.xml")!.ContentType);
        Assert.Equal("word/document.xml", back.GetRelationships()[0].Target);
    }

    [Fact]
    public void Repairs_are_reported_with_locations()
    {
        var entries = new List<(string, byte[])>
        {
            ("[Content_Types].xml", U(ContentTypesXml("<Override PartName=\"/word/document.xml\" ContentType=\"dup/dup\"/><Default Extension=\"png\" ContentType=\"image/x\"/>"))),
            ("_rels/.rels", U(Rels(("rId1", OfficeDocRel, "word/document.xml", null), ("rId1", "t", "again.xml", null), ("rId2", "t", "missing.xml", null), ("rId3", "t", "../../up.xml", null)))),
            ("word/document.xml", U("<a/>")),
            ("WORD/Document.xml", U("<b/>")),
            ("word/document.xml", U("<c/>")),
            ("notype.zzz", U("zz")),
            ("dir/", []),
            ("a\\b.xml", U("x")),
            ("spine.xml/[0].piece", U("p")),
        };
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(Build(entries)), Ctx(out DefectCollection defects));
        string[] codes = Codes(defects);
        Assert.Contains("packaging.duplicate-content-type", codes);
        Assert.Contains("packaging.part-name-case-collision", codes);
        Assert.Contains("packaging.duplicate-part-name", codes);
        Assert.Contains("packaging.missing-content-type", codes);
        Assert.Contains("packaging.invalid-part-name", codes);
        Assert.Contains("packaging.interleaved-part", codes);
        Assert.Contains("packaging.duplicate-relationship-id", codes);
        Assert.Contains("packaging.dangling-relationship", codes);
        Assert.Contains("packaging.bad-relationship-target", codes);
        Defect missing = defects.First(d => d.Code == "packaging.missing-content-type");
        Assert.Equal("/notype.zzz", missing.Location.Container);
        Assert.Equal(DocType, pkg.FindPart("/word/document.xml")!.ContentType);
        Assert.Equal("<a/>", Encoding.UTF8.GetString(pkg.FindPart("/word/document.xml")!.GetBytes().Span));
        Assert.Equal(string.Empty, pkg.FindPart("/notype.zzz")!.ContentType);

        // The items that are not parts are still written back.
        using var output = new MemoryStream();
        pkg.WriteTo(output);
        Assert.Equal(Order(Build(entries)), Order(output.ToArray()));
    }

    [Fact]
    public void Missing_content_types_is_a_typed_fatal_error()
    {
        byte[] zip = Build([("word/document.xml", U("<a/>"))]);
        var ex = Assert.Throws<OfficeFormatException>(() => OpcPackage.Open(new MemoryStream(zip)));
        Assert.Equal("packaging.missing-content-types", ex.Defect.Code);
        Assert.Equal(DefectSeverity.Fatal, ex.Defect.Severity);
        Assert.False(OpcPackage.TryOpen(ByteSource.FromMemory(zip), out OpcPackage? none, Ctx(out DefectCollection d)));
        Assert.Null(none);
        Assert.True(d.HasFatal);
    }

    [Fact]
    public void Garbage_content_types_and_dtd_are_refused_typed()
    {
        byte[] bad = Build([("[Content_Types].xml", U("<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY a \"b\">]><Types/>")), ("a.xml", U("<a/>"))]);
        var ex = Assert.Throws<OfficeFormatException>(() => OpcPackage.Open(new MemoryStream(bad)));
        Assert.Equal("packaging.dtd-refused", ex.Defect.Code);
        byte[] bad2 = Build([("[Content_Types].xml", U("this is not xml")), ("a.xml", U("<a/>"))]);
        Assert.Equal("packaging.bad-content-types", Assert.Throws<OfficeFormatException>(() => OpcPackage.Open(new MemoryStream(bad2))).Defect.Code);
    }

    [Fact]
    public void Damaged_relationships_keep_what_was_readable()
    {
        string rels = Rels(("rId1", OfficeDocRel, "word/document.xml", null))[..^16] + "<Relationship Id=\"rId2\" Type=\"t\" Target=\"x\"";
        var entries = Docx();
        entries[1] = ("_rels/.rels", U(rels));
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(Build(entries)), Ctx(out DefectCollection defects));
        Assert.Equal("rId1", Assert.Single(pkg.GetRelationships()).Id);
        Defect d = Assert.Single(defects, x => x.Code == "packaging.bad-relationships-part");
        Assert.Equal("/_rels/.rels", d.Location.Container);
        Assert.True(d.Location.Offset > 0);
    }

    [Fact]
    public void Truncated_and_garbage_zips_raise_typed_exceptions()
    {
        byte[] zip = Build(Docx());
        foreach (int cut in new[] { 4, 30, zip.Length / 3, zip.Length - 10, zip.Length - 40 })
        {
            try
            {
                using OpcPackage p = OpcPackage.Open(new MemoryStream(zip[..cut]), Ctx(out DefectCollection defects));
                // Recovery may have found it usable; whatever opened must be readable or fail typed.
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
            }
            catch (OfficeException)
            {
            }
        }

        Assert.Throws<OfficeFormatException>(() => OpcPackage.Open(new MemoryStream(U("hello world"))));
        Assert.Throws<OfficeFormatException>(() => OpcPackage.Open(new MemoryStream([])));
        byte[] damaged = (byte[])zip.Clone();
        damaged[damaged.Length - 30] ^= 0xFF;
        try
        {
            using OpcPackage p = OpcPackage.Open(new MemoryStream(damaged));
        }
        catch (OfficeException)
        {
        }
    }

    [Fact]
    public void Damaged_zip_directory_is_recovered_with_a_defect()
    {
        byte[] zip = Build(Docx());
        // Cut off the central directory and end record: only local headers remain.
        int eocd = zip.AsSpan().LastIndexOf(new byte[] { 0x50, 0x4B, 0x05, 0x06 });
        int cd = zip.AsSpan().IndexOf(new byte[] { 0x50, 0x4B, 0x01, 0x02 });
        Assert.True(cd > 0 && eocd > cd);
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(zip[..cd]), Ctx(out DefectCollection defects));
        Assert.Contains("packaging.zip-damaged", Codes(defects));
        Assert.Equal(4, pkg.Parts.Count);
        Assert.Equal(ReadZip(zip)["word/media/image1.png"], pkg.FindPart("/word/media/image1.png")!.GetBytes().ToArray());
        using var output = new MemoryStream();
        pkg.WriteTo(output);
        Assert.Equal(ReadZip(zip)["word/document.xml"], ReadZip(output.ToArray())["word/document.xml"]);
    }

    [Fact]
    public void Zip_bomb_hits_the_expansion_limit()
    {
        byte[] huge = new byte[64 << 20];
        var entries = Docx();
        entries.Add(("word/bomb.bin", huge));
        byte[] zip = Build(entries, CompressionLevel.SmallestSize);
        Assert.True(zip.Length < 1 << 20);
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(zip), new OpenContext(new OfficeLimits { MaxExpansionRatio = 50 }));
        var ex = Assert.Throws<OfficeLimitException>(() => pkg.FindPart("/word/bomb.bin")!.GetBytes());
        Assert.Equal(nameof(OfficeLimits.MaxExpansionRatio), ex.LimitName);
    }

    [Fact]
    public void Part_count_total_size_and_part_size_limits_are_typed()
    {
        byte[] zip = Build(Docx(mediaCount: 20));
        var count = Assert.Throws<OfficeLimitException>(() => OpcPackage.Open(new MemoryStream(zip), new OpenContext(new OfficeLimits { MaxPartCount = 10 })));
        Assert.Equal(nameof(OfficeLimits.MaxPartCount), count.LimitName);
        var total = Assert.Throws<OfficeLimitException>(() => OpcPackage.Open(new MemoryStream(zip), new OpenContext(new OfficeLimits { MaxTotalUncompressedSize = 10_000 })));
        Assert.Equal(nameof(OfficeLimits.MaxTotalUncompressedSize), total.LimitName);
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(zip), new OpenContext(new OfficeLimits { MaxPartSize = 1000 }));
        var big = Assert.Throws<OfficeLimitException>(() => pkg.FindPart("/word/media/image1.png")!.GetBytes());
        Assert.Equal(nameof(OfficeLimits.MaxPartSize), big.LimitName);
        Assert.Equal("/word/media/image1.png", big.Location.Container);
    }

    [Fact]
    public void Deep_xml_in_content_types_hits_the_depth_limit()
    {
        string deep = "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" + string.Concat(Enumerable.Repeat("<a>", 300)) + string.Concat(Enumerable.Repeat("</a>", 300)) + "</Types>";
        byte[] zip = Build([("[Content_Types].xml", U(deep)), ("a.xml", U("<a/>"))]);
        var ex = Assert.Throws<OfficeLimitException>(() => OpcPackage.Open(new MemoryStream(zip)));
        Assert.Equal(nameof(OfficeLimits.MaxXmlDepth), ex.LimitName);
    }

    [Fact]
    public void Save_replaces_the_file_atomically_and_the_package_stays_usable()
    {
        string path = Path.Combine(_dir, "doc.docx");
        File.WriteAllBytes(path, Build(Docx()));
        using OpcPackage pkg = OpcPackage.Open(path);
        IPackagePart doc = pkg.FindPart("/word/document.xml")!;
        IPackagePart img = pkg.FindPart("/word/media/image1.png")!;
        doc.SetBytes(U("<w:document xmlns:w=\"x\">new</w:document>"));
        pkg.Save();
        Assert.False(pkg.IsModified);
        Assert.True(doc.IsUntouched);
        Assert.Equal(ReadZip(Build(Docx()))["word/media/image1.png"], img.GetBytes().ToArray());
        Assert.Equal(ReadZip(Build(Docx()))["word/media/image3.png"], pkg.FindPart("/word/media/image3.png")!.GetBytes().ToArray());
        Assert.Empty(Directory.GetFiles(_dir, ".*.tmp"));
        Dictionary<string, byte[]> saved = ReadZip(File.ReadAllBytes(path));
        Assert.Equal(U("<w:document xmlns:w=\"x\">new</w:document>"), saved["word/document.xml"]);

        // A second edit and save works on the rebased file.
        pkg.AddPart("/word/extra.xml", "application/xml", U("<e/>"));
        pkg.Save();
        using OpcPackage back = OpcPackage.Open(path);
        Assert.NotNull(back.FindPart("/word/extra.xml"));
        Assert.Equal(ReadZip(Build(Docx()))["word/media/image2.png"], back.FindPart("/word/media/image2.png")!.GetBytes().ToArray());
    }

    [Fact]
    public void An_interrupted_save_leaves_the_original_intact()
    {
        string path = Path.Combine(_dir, "doc.docx");
        byte[] original = Build(Docx());
        File.WriteAllBytes(path, original);
        string before = Convert.ToHexString(SHA256.HashData(original));

        // 1. Cancelled before it starts.
        using (OpcPackage pkg = OpcPackage.Open(path))
        {
            pkg.FindPart("/word/document.xml")!.SetBytes(U("<changed/>"));
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => pkg.Save(cts.Token));
        }

        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        Assert.Empty(Directory.GetFiles(_dir, ".*.tmp"));

        // 2. A part with damaged stored data: an untouched part is copied as stored, so a save does not fail and does not "repair" it;
        //    reading the part still reports the damage as a typed failure, and the original file is untouched by a failed attempt.
        string damagedPath = Path.Combine(_dir, "damaged.docx");
        byte[] damaged = Build(Docx(), CompressionLevel.NoCompression);
        int at = damaged.AsSpan().IndexOf(U("word/media/image3.png")) + "word/media/image3.png".Length + 100;
        damaged[at] ^= 0xFF;
        File.WriteAllBytes(damagedPath, damaged);
        string damagedHash = Convert.ToHexString(SHA256.HashData(damaged));
        using (OpcPackage pkg = OpcPackage.Open(damagedPath))
        {
            pkg.FindPart("/word/document.xml")!.SetBytes(U("<changed/>"));
            Assert.Throws<OfficeFormatException>(() => pkg.FindPart("/word/media/image3.png")!.GetBytes());
            using var copy = new MemoryStream();
            pkg.WriteTo(copy);
            Assert.True(copy.ToArray().AsSpan().IndexOf(damaged.AsSpan(at - 8, 16)) >= 0);
            Assert.True(pkg.IsModified);
        }

        Assert.Equal(damagedHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(damagedPath))));
        Assert.Empty(Directory.GetFiles(_dir, ".*.tmp"));

        // 3. The destination stream throws.
        using OpcPackage p3 = OpcPackage.Open(path);
        Assert.Throws<IOException>(() => p3.WriteTo(new ThrowingStream(300)));
    }

    private sealed class ThrowingStream(int limit) : Stream
    {
        private int _written;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _written += count;
            if (_written > limit)
            {
                throw new IOException("disk full");
            }
        }
    }

    [Fact]
    public void Flat_opc_round_trips_through_zip_and_back()
    {
        byte[] zip = Build(Docx());
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(zip));
        using var flat = new MemoryStream();
        pkg.WriteFlatOpcTo(flat);
        string text = Encoding.UTF8.GetString(flat.ToArray());
        Assert.Contains("<pkg:package", text);
        Assert.Contains("mso-application progid=\"Word.Document\"", text);
        Assert.Contains("<pkg:binaryData>", text);

        using OpcPackage fromFlat = OpcPackage.Open(new MemoryStream(flat.ToArray()), Ctx(out DefectCollection defects));
        Assert.Empty(defects);
        Assert.True(fromFlat.IsFlatOpc);
        Assert.False(fromFlat.IsModified);
        Dictionary<string, byte[]> expect = ReadZip(zip);
        Assert.Equal(pkg.Parts.Select(p => p.Name), fromFlat.Parts.Select(p => p.Name));
        foreach (IPackagePart p in fromFlat.Parts)
        {
            ReadOnlySpan<byte> got = p.GetBytes().Span;
            byte[] want = expect[p.Name[1..]];
            if (p.Name.EndsWith(".xml", StringComparison.Ordinal))
            {
                // The XML declaration cannot live inside the wrapper; everything after it is identical.
                string w = Encoding.UTF8.GetString(want);
                int end = w.StartsWith("<?xml", StringComparison.Ordinal) ? w.IndexOf("?>", StringComparison.Ordinal) + 2 : 0;
                Assert.Equal(w[end..].TrimStart(), Encoding.UTF8.GetString(got));
            }
            else
            {
                Assert.True(got.SequenceEqual(want), p.Name);
            }
        }

        Assert.Equal(DocType, fromFlat.FindPart("/word/document.xml")!.ContentType);
        Assert.Equal(2, fromFlat.GetRelationships("/word/document.xml").Count);

        // Flat to zip, and a flat save keeps the form.
        using var back = new MemoryStream();
        fromFlat.WriteZipTo(back);
        using OpcPackage reread = OpcPackage.Open(new MemoryStream(back.ToArray()));
        Assert.Equal(DocType, reread.FindPart("/word/document.xml")!.ContentType);
        Assert.Equal(expect["word/media/image2.png"], reread.FindPart("/word/media/image2.png")!.GetBytes().ToArray());
        string flatPath = Path.Combine(_dir, "doc.xml");
        File.WriteAllBytes(flatPath, flat.ToArray());
        using (OpcPackage fp = OpcPackage.Open(flatPath))
        {
            fp.AddPart("/word/x.xml", "application/xml", U("<x/>"));
            fp.Save();
        }

        Assert.StartsWith("<?xml", File.ReadAllText(flatPath), StringComparison.Ordinal);
        using OpcPackage fp2 = OpcPackage.Open(flatPath);
        Assert.True(fp2.IsFlatOpc);
        Assert.NotNull(fp2.FindPart("/word/x.xml"));
    }

    [Fact]
    public void Flat_opc_hostile_inputs_are_typed()
    {
        Assert.Equal("packaging.dtd-refused", Assert.Throws<OfficeFormatException>(() => OpcPackage.Open(new MemoryStream(U("<!DOCTYPE a [<!ENTITY x 'y'>]><a/>")))).Defect.Code);
        Assert.Equal("packaging.bad-flat-opc", Assert.Throws<OfficeFormatException>(() => OpcPackage.Open(new MemoryStream(U("<a><b/></a>")))).Defect.Code);
        Assert.Equal("packaging.bad-flat-opc", Assert.Throws<OfficeFormatException>(() => OpcPackage.Open(new MemoryStream(U("<pkg:package xmlns:pkg=\"x\"><pkg:part")))).Defect.Code);
        string withBad = "<pkg:package xmlns:pkg=\"http://schemas.microsoft.com/office/2006/xmlPackage\"><pkg:part pkg:name=\"/a.bin\" pkg:contentType=\"application/octet-stream\"><pkg:binaryData>!!notbase64</pkg:binaryData></pkg:part><pkg:part pkg:name=\"/b.xml\" pkg:contentType=\"application/xml\"><pkg:xmlData><b/></pkg:xmlData></pkg:part></pkg:package>";
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(U(withBad)), Ctx(out DefectCollection defects));
        Assert.Equal("/b.xml", Assert.Single(pkg.Parts).Name);
        Assert.Contains("packaging.bad-flat-part", Codes(defects));
    }

    [Fact]
    public void Non_ascii_part_names_map_to_percent_encoded_zip_items()
    {
        var entries = Docx();
        entries.Add(("word/media/caf%C3%A9.png", [1, 2]));
        entries.Add(("word/media/na\u00efve.png", [3]));
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(Build(entries)), Ctx(out DefectCollection defects));
        Assert.Empty(defects);
        Assert.Equal(new byte[] { 1, 2 }, pkg.FindPart("/word/media/caf\u00e9.png")!.GetBytes().ToArray());
        Assert.Equal(new byte[] { 1, 2 }, pkg.FindPart("/word/media/caf%C3%a9.png")!.GetBytes().ToArray());
        Assert.NotNull(pkg.FindPart("/word/media/na\u00efve.png"));
        pkg.AddPart("/word/media/\u00fcber.png", "image/png", new byte[] { 9 });
        using var output = new MemoryStream();
        pkg.WriteTo(output);
        Assert.Contains("word/media/%C3%BCber.png", Order(output.ToArray()));
        Assert.True(OpcPackage.TryResolveTarget("/word/document.xml", new Relationship("r", "t", "media/caf%C3%A9.png#frag"), out PartUri p));
        Assert.Equal("/word/media/caf\u00e9.png", p.Value);
    }

    [Fact]
    public void Parts_can_be_read_from_many_threads_and_interleaved_streams_stay_correct()
    {
        var entries = Docx(mediaCount: 6);
        byte[] big = new byte[12 << 20];
        new Random(7).NextBytes(big.AsSpan(0, 1 << 20));
        entries.Add(("word/media/big1.bin", big));
        var big2 = (byte[])big.Clone();
        big2[5] = 0x55;
        entries.Add(("word/media/big2.bin", big2));
        byte[] zip = Build(entries, CompressionLevel.Fastest);
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(zip));
        Dictionary<string, byte[]> expect = ReadZip(zip);
        Parallel.ForEach(pkg.Parts, p =>
        {
            Assert.Equal(expect[p.Name[1..]], p.GetBytes().ToArray());
        });

        using var pkg2 = OpcPackage.Open(new MemoryStream(zip));
        using Stream a = pkg2.FindPart("/word/media/big1.bin")!.OpenRead();
        using Stream b = pkg2.FindPart("/word/media/big2.bin")!.OpenRead();
        byte[] ba = new byte[big.Length], bb = new byte[big.Length];
        int ra = 0, rb = 0;
        while (ra < ba.Length || rb < bb.Length)
        {
            if (ra < ba.Length)
            {
                ra += Math.Max(0, a.Read(ba, ra, Math.Min(100_000, ba.Length - ra)));
            }

            if (rb < bb.Length)
            {
                rb += Math.Max(0, b.Read(bb, rb, Math.Min(77_777, bb.Length - rb)));
            }
        }

        Assert.Equal(big, ba);
        Assert.Equal(big2, bb);
    }

    [Fact]
    public void Relationship_changes_write_a_new_part_and_others_stay_raw()
    {
        byte[] zip = Build(Docx());
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(zip));
        pkg.SetRelationships(null, [.. pkg.GetRelationships(), new Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml")]);
        pkg.SetRelationships("/word/media/image1.png", [new Relationship("a", "t", "../x.xml?q=1&r=<2>")]);
        using var output = new MemoryStream();
        pkg.WriteTo(output);
        Dictionary<string, byte[]> a = ReadZip(zip), b = ReadZip(output.ToArray());
        Assert.NotEqual(a["_rels/.rels"], b["_rels/.rels"]);
        Assert.Equal(a["word/_rels/document.xml.rels"], b["word/_rels/document.xml.rels"]);
        Assert.Equal(a["[Content_Types].xml"], b["[Content_Types].xml"]);
        Assert.True(b.ContainsKey("word/media/_rels/image1.png.rels"));
        using OpcPackage back = OpcPackage.Open(new MemoryStream(output.ToArray()));
        Assert.Equal(2, back.GetRelationships().Count);
        Assert.Equal("../x.xml?q=1&r=<2>", back.GetRelationships("/word/media/image1.png")[0].Target);
        Assert.Throws<ArgumentException>(() => back.SetRelationships(null, [new Relationship("x", "t", "a"), new Relationship("x", "t", "b")]));
    }

    [Theory]
    [InlineData("/_rels/.rels", true, null)]
    [InlineData("/word/_rels/document.xml.rels", true, "/word/document.xml")]
    [InlineData("/_rels/doc.xml.rels", true, "/doc.xml")]
    [InlineData("/word/_RELS/a.RELS", true, "/word/a")]
    [InlineData("/word/rels/a.rels", false, null)]
    [InlineData("/word/_rels/.rels", false, null)]
    [InlineData("/a.rels", false, null)]
    public void Relationship_part_names_map_to_their_source(string name, bool isRels, string? source)
    {
        Assert.Equal(isRels, OpcRelationships.IsRelationshipsPart(name, out string? got));
        Assert.Equal(source, got);
        if (isRels)
        {
            Assert.Equal(name.ToUpperInvariant(), OpcRelationships.PartNameFor(source).ToUpperInvariant());
        }
    }

    [Fact]
    public void Interrupted_open_never_throws_untyped_for_mutated_packages()
    {
        byte[] zip = Build(Docx());
        var rng = new Random(1234);
        for (int i = 0; i < 400; i++)
        {
            byte[] m = (byte[])zip.Clone();
            for (int k = 0; k < 1 + i % 4; k++)
            {
                m[rng.Next(m.Length)] = (byte)rng.Next(256);
            }

            try
            {
                using OpcPackage p = OpcPackage.Open(new MemoryStream(m), new OpenContext(new OfficeLimits { MaxPartSize = 1 << 20 }));
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

                _ = p.GetRelationships("/word/document.xml");
                p.WriteTo(Stream.Null);
            }
            catch (OfficeException)
            {
            }
        }
    }
}
