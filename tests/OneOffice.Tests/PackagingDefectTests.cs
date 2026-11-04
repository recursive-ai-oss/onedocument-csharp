using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using RecursiveAI.OneArchive;
using RecursiveAI.OneArchive.Cfb;
using RecursiveAI.OneArchive.Zip;
using RecursiveAI.OneOffice.Cfb;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Tests;

/// <summary>One test per defect code the packaging layers add, each against a crafted input.</summary>
public class PackagingDefectTests
{
    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var za = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                using Stream s = za.CreateEntry(name).Open();
                s.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return ms.ToArray();
    }

    private const string TypesHead = "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>";

    private static DefectCollection OpenCollect(byte[] zip, string? relsXml = null, string extraTypes = "")
    {
        var defects = new DefectCollection();
        var entries = new List<(string, string)> { ("[Content_Types].xml", TypesHead + extraTypes + "</Types>") };
        if (relsXml is not null)
        {
            entries.Add(("_rels/.rels", relsXml));
        }

        entries.Add(("a.xml", "<a/>"));
        _ = zip;
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(Zip([.. entries])), new OpenContext(null, defects));
        _ = pkg.GetRelationships();
        return defects;
    }

    private const string RelsHead = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">";

    [Fact]
    public void Content_type_entry_without_its_attributes_is_dropped()
    {
        DefectCollection d = OpenCollect([], extraTypes: "<Default Extension=\"png\"/><Override PartName=\"relative.xml\" ContentType=\"x/y\"/>");
        Assert.Equal(2, d.Count(x => x.Code == "packaging.bad-content-type-entry" && x.Severity == DefectSeverity.Dropped));
    }

    [Fact]
    public void Relationship_without_id_type_or_target_is_dropped_and_a_bad_mode_is_repaired()
    {
        DefectCollection d = OpenCollect([], RelsHead + "<Relationship Type=\"t\" Target=\"a.xml\"/><Relationship Id=\"ok\" Type=\"t\" Target=\"a.xml\" TargetMode=\"Sideways\"/></Relationships>");
        Assert.Single(d, x => x.Code == "packaging.bad-relationship" && x.Severity == DefectSeverity.Dropped && x.Location.Container == "/_rels/.rels");
        Assert.Single(d, x => x.Code == "packaging.bad-relationship-target-mode" && x.Severity == DefectSeverity.Repaired);
    }

    [Fact]
    public void Flat_opc_part_names_with_prefixed_relationships_are_read()
    {
        const string Flat = "<pkg:package xmlns:pkg=\"http://schemas.microsoft.com/office/2006/xmlPackage\"><pkg:part pkg:name=\"/_rels/.rels\" pkg:contentType=\"application/vnd.openxmlformats-package.relationships+xml\" pkg:padding=\"512\"><pkg:xmlData><rel:Relationships xmlns:rel=\"http://schemas.openxmlformats.org/package/2006/relationships\"><rel:Relationship Id=\"rId1\" Target=\"word/document.xml\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\"/></rel:Relationships></pkg:xmlData></pkg:part><pkg:part pkg:name=\"/word/document.xml\" pkg:contentType=\"application/xml\"><pkg:xmlData><w:document xmlns:w=\"x\">&amp;&lt;</w:document></pkg:xmlData></pkg:part></pkg:package>";
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(Encoding.UTF8.GetBytes(Flat)));
        Assert.Equal("rId1", Assert.Single(pkg.GetRelationships()).Id);
        Assert.Equal("<w:document xmlns:w=\"x\">&amp;&lt;</w:document>", Encoding.UTF8.GetString(pkg.FindPart("/word/document.xml")!.GetBytes().Span));
    }

    [Fact]
    public void Input_that_is_neither_zip_nor_flat_opc_has_its_own_code()
    {
        var ex = Assert.Throws<OfficeFormatException>(() => OpcPackage.Open(new MemoryStream(Encoding.ASCII.GetBytes("plain text, not a package"))));
        Assert.Equal("packaging.not-a-package", ex.Defect.Code);
        Assert.Equal(DefectKind.Packaging, ex.Defect.Kind);
    }

    [Fact]
    public void Every_compound_file_damage_kind_maps_to_its_own_repaired_defect()
    {
        var codes = new HashSet<string>();
        foreach (CfbDamageKind kind in Enum.GetValues<CfbDamageKind>())
        {
            Defect d = CfbPackage.ToDefect(new CfbDamage(kind, 1234, 8, 3, "WordDocument", "a reason"));
            Assert.StartsWith("packaging.cfb-", d.Code, StringComparison.Ordinal);
            Assert.Equal(DefectSeverity.Repaired, d.Severity);
            Assert.Equal("/WordDocument", d.Location.Container);
            Assert.Equal(1234, d.Location.Offset);
            Assert.True(codes.Add(d.Code), d.Code);
        }

        Assert.Equal(Enum.GetValues<CfbDamageKind>().Length, codes.Count);
    }

    private static byte[] TwoStreamCompoundFile()
    {
        using var ms = new MemoryStream();
        using (var w = new CompoundFileWriter(ms, leaveOpen: true))
        {
            byte[] data = new byte[20000];
            new Random(1).NextBytes(data);
            w.WriteEntry(new ArchiveEntry { Name = "WordDocument", Kind = ArchiveEntryKind.File, Size = data.Length }, data);
            w.WriteEntry(new ArchiveEntry { Name = "Small", Kind = ArchiveEntryKind.File, Size = 100 }, new byte[100]);
            w.Finish();
        }

        return ms.ToArray();
    }

    private static (CfbPackage Package, DefectCollection Defects) OpenPatched(int offset, byte xor = 0x55, int entryIndex = -1)
    {
        byte[] cfb = TwoStreamCompoundFile();
        int at = offset;
        if (entryIndex >= 0)
        {
            uint dirSector = BinaryPrimitives.ReadUInt32LittleEndian(cfb.AsSpan(0x30));
            at = (int)((dirSector + 1) * 512) + (entryIndex * 128) + offset;
        }

        cfb[at] ^= xor;
        var defects = new DefectCollection();
        return (CfbPackage.Open(new MemoryStream(cfb), new OpenContext(null, defects)), defects);
    }

    [Fact]
    public void A_damaged_mini_fat_is_recovered_and_the_short_stream_says_so()
    {
        (CfbPackage pkg, DefectCollection defects) = OpenPatched(0x3C);
        using (pkg)
        {
            Assert.Contains(defects, d => d.Code == "packaging.cfb-mini-stream-unusable");
            Assert.Contains(defects, d => d.Code == "packaging.cfb-header-inconsistent");
            Assert.Contains(defects, d => d.Code == "packaging.cfb-damaged-stream");
            Assert.All(defects, d => Assert.Equal(DefectLocationKind.Record, d.Location.Kind));
            CfbStreamPart small = pkg.Streams.Single(s => s.Name == "/Small");
            Assert.True(small.IsDamaged);
            Assert.True(small.Length < small.DeclaredLength);
            Assert.Equal(100, small.DeclaredLength);
            Assert.False(pkg.Streams.Single(s => s.Name == "/WordDocument").IsDamaged);
            Assert.Equal(20000, pkg.FindPart("/WordDocument")!.GetBytes().Length);
        }
    }

    [Fact]
    public void Broken_directory_entries_and_links_are_reported()
    {
        (CfbPackage pkg, DefectCollection defects) = OpenPatched(66, 0x77, entryIndex: 2);
        using (pkg)
        {
            Assert.Contains(defects, d => d.Code == "packaging.cfb-bad-entry");
            Assert.Contains(defects, d => d.Code == "packaging.cfb-bad-link");
            Assert.Contains(defects, d => d.Code == "packaging.cfb-orphaned-entry");
        }

        (CfbPackage dirPkg, DefectCollection dirDefects) = OpenPatched(0x30);
        using (dirPkg)
        {
            Assert.Contains(dirDefects, d => d.Code == "packaging.cfb-directory-unusable");
            Assert.Equal(2, dirPkg.Parts.Count);
        }

        (CfbPackage hdrPkg, DefectCollection hdrDefects) = OpenPatched(0x1C);
        using (hdrPkg)
        {
            Assert.Contains(hdrDefects, d => d.Code == "packaging.cfb-header-inconsistent");
        }
    }

    [Fact]
    public void A_file_whose_fat_is_cut_off_is_rejected_with_the_offset()
    {
        byte[] cfb = TwoStreamCompoundFile();
        var ex = Assert.Throws<OfficeFormatException>(() => CfbPackage.Open(new MemoryStream(cfb[..(cfb.Length - 600)])));
        Assert.Equal("packaging.damaged", ex.Defect.Code);
        Assert.True(ex.Location.Offset >= 0);
    }

    [Fact]
    public void Two_streams_whose_names_differ_only_in_case_expose_the_first()
    {
        using var ms = new MemoryStream();
        using (var w = new CompoundFileWriter(ms, leaveOpen: true))
        {
            w.WriteEntry(new ArchiveEntry { Name = "a", Kind = ArchiveEntryKind.File, Size = 1 }, new byte[] { 1 });
            w.WriteEntry(new ArchiveEntry { Name = "b", Kind = ArchiveEntryKind.File, Size = 1 }, new byte[] { 2 });
            w.Finish();
        }

        byte[] cfb = ms.ToArray();

        // Directory entries are 128 bytes, the name is UTF-16 at the start of each; rename "b" to "A".
        uint dirSector = BinaryPrimitives.ReadUInt32LittleEndian(cfb.AsSpan(0x30));
        int dir = (int)((dirSector + 1) * 512);
        int entryB = -1;
        for (int i = 1; i < 4; i++)
        {
            if (cfb[dir + (i * 128)] == (byte)'b')
            {
                entryB = dir + (i * 128);
            }
        }

        Assert.True(entryB > 0);
        cfb[entryB] = (byte)'A';
        var defects = new DefectCollection();
        using CfbPackage pkg = CfbPackage.Open(new MemoryStream(cfb), new OpenContext(null, defects));
        Assert.Single(pkg.Parts);
        Assert.Contains(defects, d => d.Code == "packaging.duplicate-stream-name" && d.Severity == DefectSeverity.Dropped);
    }

    private static string FuzzFile(string name, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "testdata", "fuzz", name);

    [Fact]
    public void Fuzz_finding_a_part_with_no_content_type_survives_the_flat_form()
    {
        byte[] data = File.ReadAllBytes(FuzzFile("opc-flat-part-count.bin"));
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(data));
        using var flat = new MemoryStream();
        pkg.WriteFlatOpcTo(flat);
        using OpcPackage back = OpcPackage.Open(new MemoryStream(flat.ToArray()));
        Assert.Equal(pkg.Parts.Count, back.Parts.Count);
    }

    [Fact]
    public void Fuzz_finding_storages_with_names_equal_ignoring_case_rewrite_cleanly()
    {
        byte[] data = File.ReadAllBytes(FuzzFile("cfb-case-duplicate-storage.bin"));
        using CfbPackage pkg = CfbPackage.Open(new MemoryStream(data));
        using var output = new MemoryStream();
        pkg.WriteTo(output);
        using CfbPackage back = CfbPackage.Open(new MemoryStream(output.ToArray()));
        Assert.Equal(pkg.Parts.Count, back.Parts.Count);
    }

    [Fact]
    public void Fuzz_finding_an_entry_with_a_NUL_name_is_dropped_and_the_package_saves()
    {
        byte[] data = File.ReadAllBytes(FuzzFile("opc-nul-entry-name.bin"));
        var defects = new DefectCollection();
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(data), new OpenContext(null, defects));
        using var output = new MemoryStream();
        pkg.WriteTo(output);
        Assert.Contains(defects, d => d.Code == "packaging.invalid-part-name");
    }

    [Fact]
    public void Untouched_parts_are_copied_raw_so_the_stored_bytes_survive_a_save()
    {
        string types = TypesHead + "<Override PartName=\"/word/document.xml\" ContentType=\"application/xml\"/></Types>";
        string body = string.Concat(Enumerable.Range(0, 2000).Select(i => "<p>line " + (i * 7919 % 1000) + "</p>"));
        byte[] input = Zip(("[Content_Types].xml", types), ("word/document.xml", body), ("word/other.xml", body + body));
        using OpcPackage pkg = OpcPackage.Open(new MemoryStream(input));
        using var output = new MemoryStream();
        pkg.WriteTo(output);

        static List<(string Name, byte[] Raw)> Stored(byte[] zip)
        {
            using var reader = new ZipReader(new MemoryStream(zip));
            var list = new List<ZipEntry>();
            while (reader.GetNextEntry() is { } e)
            {
                list.Add(e);
            }

            return list.Select(e =>
            {
                using Stream raw = reader.OpenRawEntryStream(e);
                using var ms = new MemoryStream();
                raw.CopyTo(ms);
                return (e.Name, ms.ToArray());
            }).ToList();
        }

        var before = Stored(input);
        var after = Stored(output.ToArray());
        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].Name, after[i].Name);
            Assert.True(before[i].Raw.AsSpan().SequenceEqual(after[i].Raw), before[i].Name);
        }
    }
}
