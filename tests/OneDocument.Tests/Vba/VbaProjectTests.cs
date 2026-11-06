using System.IO.Compression;
using System.Text;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Vba;

namespace RecursiveAI.OneDocument.Tests;

public sealed class VbaProjectTests
{
    private static string Hex(string s) => s.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static byte[] FromHex(string s) => Convert.FromHexString(Hex(s));

    // ---- [MS-OVBA] §3.2 compression examples --------------------------------------------------------------------------------

    [Fact]
    public void Decompress_NoCompressionExample()
    {
        byte[] input = FromHex("01 19 B0 00 61 62 63 64 65 66 67 68 00 69 6A 6B 6C 6D 6E 6F 70 00 71 72 73 74 75 76 2E");
        Assert.True(VbaCompression.TryDecompress(input, 1 << 20, out byte[] output, out string? problem), problem);
        Assert.Equal("abcdefghijklmnopqrstuv.", Encoding.ASCII.GetString(output));
    }

    [Fact]
    public void Decompress_NormalCompressionExample()
    {
        byte[] input = FromHex("01 2F B0 00 23 61 61 61 62 63 64 65 82 66 00 70 61 67 68 69 6A 01 38 08 61 6B 6C 00 20 6D 6E 6F 70 06 71 02 70 04 00 72 73 74 75 76 10 77 78 79 7A 00 2C");
        Assert.True(VbaCompression.TryDecompress(input, 1 << 20, out byte[] output, out string? problem), problem);
        Assert.Equal("#aaabcdefaaaaghijaaaaaklaaamnopqaaaaaaaaaaaarstuvwxyzaaa", Encoding.ASCII.GetString(output));
    }

    [Fact]
    public void Decompress_MaximumCompressionExample()
    {
        byte[] input = FromHex("01 03 B0 02 61 45 00");
        Assert.True(VbaCompression.TryDecompress(input, 1 << 20, out byte[] output, out string? problem), problem);
        Assert.Equal(new string('a', 73), Encoding.ASCII.GetString(output));
    }

    [Fact]
    public void Decompress_ManyChunks_RoundTripsLiteralContainers()
    {
        byte[] data = new byte[40_000];
        new Random(5).NextBytes(data);
        Assert.True(VbaCompression.TryDecompress(VbaTestProject.Compress(data), 1 << 20, out byte[] output, out _));
        Assert.Equal(data, output);
    }

    [Fact]
    public void Decompress_ContainerWithNoChunks_IsEmpty()
    {
        Assert.True(VbaCompression.TryDecompress(new byte[] { 0x01 }, 1 << 20, out byte[] output, out _));
        Assert.Empty(output);
        Assert.Equal(new byte[] { 0x01 }, VbaTestProject.Compress(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Decompress_RawChunks_AreCopied()
    {
        byte[] data = new byte[8192];
        new Random(9).NextBytes(data);
        Assert.True(VbaCompression.TryDecompress(VbaTestProject.CompressRaw(data), 1 << 20, out byte[] output, out string? problem), problem);
        Assert.Equal(data, output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("02 19 B0 00")]
    [InlineData("01 19")]
    [InlineData("01 19 B0 00 61 62")]
    [InlineData("01 19 A0 00 61 62 63 64 65 66 67 68")]
    [InlineData("01 03 B0 02 61 FF FF")]
    [InlineData("01 03 B0 01 01 00")]
    public void Decompress_DamagedInput_NeverThrowsAndSaysWhy(string hex)
    {
        Assert.False(VbaCompression.TryDecompress(FromHex(hex), 1 << 20, out _, out string? problem));
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact]
    public void Decompress_StopsAtTheOutputLimit()
    {
        byte[] input = FromHex("01 03 B0 02 61 45 00");
        Assert.False(VbaCompression.TryDecompress(input, 10, out byte[] output, out string? problem));
        Assert.Contains("limit", problem, StringComparison.Ordinal);
        Assert.True(output.Length <= 10);
    }

    // ---- protection ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Protection_SpecificationValuesDecode()
    {
        // [MS-OVBA] §3.1.6: a project with no protection, no password and visible.
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, VbaDataEncryption.Decode("0705D8E3D8EDDBF1DBF1DBF1DBF1"));
        Assert.Equal(new byte[] { 0 }, VbaDataEncryption.Decode("0E0CD1ECDFF4E7F5E7F5E7"));
        Assert.Equal(new byte[] { 0xFF }, VbaDataEncryption.Decode("1517CAF1D6F9D7F9D706"));
        VbaProtection p = VbaDataEncryption.Interpret("0705D8E3D8EDDBF1DBF1DBF1DBF1", "0E0CD1ECDFF4E7F5E7F5E7", "1517CAF1D6F9D7F9D706", out bool unreadable);
        Assert.False(unreadable);
        Assert.False(p.IsLocked);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ZZ")]
    [InlineData("0705D8E3")]
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFF")]
    public void Protection_GarbageIsNull(string hex) => Assert.Null(VbaDataEncryption.Decode(hex));

    // ---- reading a project --------------------------------------------------------------------------------------------------

    private static VbaProject Read(byte[] bin, out DefectCollection defects, string root = "")
    {
        defects = new DefectCollection();
        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(bin), new OpenContext(defects: defects));
        return VbaProject.FromStorage(cfb, root, new OpenContext(defects: defects));
    }

    [Fact]
    public void Read_TypicalProject_ModulesSourceAndInformation()
    {
        VbaTestProject t = VbaTestProject.Typical();
        VbaProject p = Read(t.Build(), out DefectCollection defects);
        Assert.DoesNotContain(defects, d => d.Code.StartsWith("vba.", StringComparison.Ordinal));
        Assert.Equal("VBAProject", p.Name);
        Assert.Equal(1252, p.CodePage);
        Assert.Equal(VbaSysKind.Win32, p.SysKind);
        Assert.Equal(0x409, p.Lcid);
        Assert.Equal(1355u, p.VersionMajor);
        Assert.Equal((ushort)7, p.VersionMinor);
        Assert.Equal(4, p.Modules.Count);
        foreach (VbaTestProject.Module m in t.Modules)
        {
            VbaModule got = Assert.IsType<VbaModule>(p.FindModule(m.Name));
            Assert.Equal(m.Source, got.Source);
            Assert.True(got.SourceAvailable);
            Assert.False(got.HasPerformanceCache);
        }

        Assert.Equal(VbaModuleKind.Document, p.FindModule("ThisDocument")!.Kind);
        Assert.Equal(VbaModuleKind.Standard, p.FindModule("module1")!.Kind);
        Assert.Equal(VbaModuleKind.Class, p.FindModule("Class1")!.Kind);
        Assert.Equal(VbaModuleKind.Designer, p.FindModule("UserForm1")!.Kind);
        Assert.Equal(["UserForm1"], p.DesignerStorages);
        VbaReference r = Assert.Single(p.References);
        Assert.Equal(VbaReferenceKind.Registered, r.Kind);
        Assert.Equal("stdole", r.Name);
        Assert.StartsWith("*\\G{00020430-0000-0000-C000-000000000046}#2.0#0#", r.Libid, StringComparison.Ordinal);
        Assert.Contains("Document=ThisDocument/&H00000000", p.ProjectText, StringComparison.Ordinal);
        Assert.False(p.IsLocked);
        Assert.False(p.IsSigned);
        Assert.False(p.HasPCode);
        Assert.False(p.SourceAndPCodeMayDisagree);
        Assert.Equal((ushort)0xFFFF, p.PerformanceCacheVersion);
        Assert.Empty(p.UnlistedStreams);
    }

    [Fact]
    public void Read_ProjectUnderAStorage_ForDocAndXls()
    {
        VbaProject p = Read(VbaTestProject.Typical().Build("/Macros"), out _, "/Macros");
        Assert.Equal(4, p.Modules.Count);
    }

    [Fact]
    public void Read_NonLatinCodePage_DecodesSourceAndNames()
    {
        var t = new VbaTestProject { CodePage = 1251, ProjectName = "Проект" };
        t.Modules.Add(new VbaTestProject.Module("Модуль1", "Module", "Attribute VB_Name = \"Модуль1\"\r\nSub Привет()\r\n  MsgBox \"Мир\"\r\nEnd Sub\r\n"));
        VbaProject p = Read(t.Build(), out DefectCollection defects);
        Assert.DoesNotContain(defects, d => d.Code.StartsWith("vba.", StringComparison.Ordinal));
        Assert.Equal(1251, p.CodePage);
        Assert.Equal("Проект", p.Name);
        Assert.Equal(t.Modules[0].Source, p.FindModule("Модуль1")!.Source);
    }

    [Fact]
    public void Read_Protection_PasswordHiddenAndProtectedAreReportedNeverOpened()
    {
        var t = VbaTestProject.Typical();
        t.Cmg = true;
        t.Password = "secret";
        t.Hidden = true;
        VbaProject p = Read(t.Build(), out _);
        Assert.True(p.IsLocked);
        Assert.True(p.Protection.UserProtected);
        Assert.True(p.Protection.HostProtected);
        Assert.False(p.Protection.EditorProtected);
        Assert.True(p.Protection.HasPassword);
        Assert.False(p.Protection.Visible);
        Assert.NotNull(p.Protection.PasswordRaw);
        Assert.Equal(4, p.Modules.Count); // the lock does not stop reading the source
    }

    [Fact]
    public void Read_CorruptPasswordValue_CountsAsLockedWithADefect()
    {
        var t = VbaTestProject.Typical();
        t.CorruptDpb = true;
        VbaProject p = Read(t.Build(), out DefectCollection defects);
        Assert.True(p.IsLocked);
        Assert.True(p.Protection.Unreadable);
        Assert.Contains(defects, d => d.Code == "vba.protection-unreadable");
    }

    [Fact]
    public void Read_SignatureStream_IsLocatedNotVerified()
    {
        var t = VbaTestProject.Typical();
        t.Signature = true;
        VbaProject p = Read(t.Build(), out _);
        Assert.True(p.IsSigned);
        Assert.Equal(VbaSignatureKinds.Legacy, p.Signatures);
        Assert.Equal(VbaSignatureKinds.Agile, VbaSignatures.OfStreamName("/\u0005DigitalSignatureEx"));
        Assert.Equal(VbaSignatureKinds.V3, VbaSignatures.OfStreamName("/x/\u0005DigitalSignatureExt"));
        Assert.Equal(VbaSignatureKinds.None, VbaSignatures.OfStreamName("/\u0005DigitalSignatureOther"));
    }

    [Fact]
    public void Read_PCodeWithLiveVersionAndNoSource_IsFlaggedAsPossiblyStomped()
    {
        var t = new VbaTestProject { VbaVersion = 0x00B3, VbaProjectCache = new byte[40] };
        // A module whose stream is only its performance cache: the source was blanked.
        t.Modules.Add(new VbaTestProject.Module("Module1", "Module", string.Empty, Cache: new byte[200]) { });
        VbaProject p = Read(t.Build(), out _);
        VbaModule m = p.Modules[0];
        Assert.True(m.HasPerformanceCache);
        Assert.Empty(m.SourceBytes.ToArray()); // an empty source container: the text was blanked, the cache kept
        Assert.True(p.HasPCode);
        Assert.Equal((ushort)0x00B3, p.PerformanceCacheVersion);
        Assert.True(p.SourceAndPCodeMayDisagree);
    }

    [Fact]
    public void Read_PCodeWithSource_OrIgnoredCache_IsNotFlagged()
    {
        var live = new VbaTestProject { VbaVersion = 0x00B3 };
        live.Modules.Add(new VbaTestProject.Module("Module1", "Module", "Attribute VB_Name = \"Module1\"\r\n", Cache: new byte[200]));
        VbaProject p = Read(live.Build(), out _);
        Assert.True(p.Modules[0].HasPerformanceCache);
        Assert.Equal("Attribute VB_Name = \"Module1\"\r\n", p.Modules[0].Source);
        Assert.False(p.SourceAndPCodeMayDisagree);

        var ignored = new VbaTestProject { VbaVersion = 0xFFFF };
        ignored.Modules.Add(new VbaTestProject.Module("Module1", "Module", string.Empty, Cache: new byte[200]));
        Assert.False(Read(ignored.Build(), out _).SourceAndPCodeMayDisagree);
    }

    [Fact]
    public void Read_UnlistedModuleStream_WithLiveCache_IsFlagged()
    {
        var t = VbaTestProject.Typical();
        t.VbaVersion = 0x00B3;
        t.ExtraVbaStreams.Add("Hidden1");
        t.ExtraVbaStreams.Add("__SRP_0");
        VbaProject p = Read(t.Build(), out _);
        Assert.Equal(["Hidden1"], p.UnlistedStreams);
        Assert.True(p.SourceAndPCodeMayDisagree);
    }

    // ---- damage -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Read_MissingDir_IsATypedDefectAndAnEmptyProject()
    {
        var t = VbaTestProject.Typical();
        t.OmitDir = true;
        VbaProject p = Read(t.Build(), out DefectCollection defects);
        Assert.Empty(p.Modules);
        Defect d = Assert.Single(defects, x => x.Code == "vba.dir-missing");
        Assert.Equal(DefectSeverity.Dropped, d.Severity);
        Assert.Contains("/VBA/dir", d.Location.ToString(), StringComparison.Ordinal);
        Assert.NotNull(p.ProjectText);
    }

    [Fact]
    public void Read_ModuleStreamMissingOrOffsetPastEnd_AreLocatedDefects()
    {
        var t = VbaTestProject.Typical();
        t.Modules.Add(new VbaTestProject.Module("Ghost", "Module", "x", StreamName: "GhostStream"));
        byte[] bin = t.Build();
        // Remove the ghost's stream and make Module1's offset (in dir) lie past its end by cutting its stream down.
        using (CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(bin)))
        {
            cfb.RemovePart("/VBA/GhostStream");
            cfb.FindPart("/VBA/Module1")!.SetBytes(Array.Empty<byte>());
            using var ms = new MemoryStream();
            cfb.WriteTo(ms);
            bin = ms.ToArray();
        }

        VbaProject p = Read(bin, out DefectCollection defects);
        Assert.Contains(defects, d => d.Code == "vba.module-stream-missing");
        Assert.False(p.FindModule("Ghost")!.SourceAvailable);
        Assert.False(p.FindModule("Module1")!.SourceAvailable);
        Assert.Equal(5, p.Modules.Count);
    }

    [Fact]
    public void Read_DamagedSource_KeepsWhatDecodedAndReports()
    {
        var t = new VbaTestProject();
        string text = new string('a', 6000);
        t.Modules.Add(new VbaTestProject.Module("Module1", "Module", text));
        byte[] bin = t.Build();
        using (CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(bin)))
        {
            CfbStreamPart s = (CfbStreamPart)cfb.FindPart("/VBA/Module1")!;
            byte[] b = s.GetBytes().ToArray();
            s.SetBytes(b.AsMemory(0, b.Length - 100)); // the second chunk now runs past the end
            using var ms = new MemoryStream();
            cfb.WriteTo(ms);
            bin = ms.ToArray();
        }

        VbaProject p = Read(bin, out DefectCollection defects);
        VbaModule m = p.Modules[0];
        Assert.True(m.SourceDamaged);
        Assert.StartsWith("aaaa", m.Source, StringComparison.Ordinal);
        Assert.True(m.Source.Length >= 4096);
        Defect d = Assert.Single(defects, x => x.Code == "vba.module-source-damaged");
        Assert.Contains("VBA/Module1", d.Location.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Read_TruncatedDirRecords_StopCleanly()
    {
        // A dir stream cut inside the module list: the modules read so far stay, and the damage is a defect, not an exception.
        var t = VbaTestProject.Typical();
        Encoding mbcs = Encoding.GetEncoding(1252);
        byte[] dir = t.BuildDir(mbcs);
        byte[] bin = t.Build();
        using (CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(bin)))
        {
            cfb.FindPart("/VBA/dir")!.SetBytes(VbaTestProject.Compress(dir.AsSpan(0, dir.Length - 120)));
            using var ms = new MemoryStream();
            cfb.WriteTo(ms);
            bin = ms.ToArray();
        }

        VbaProject p = Read(bin, out DefectCollection defects);
        Assert.InRange(p.Modules.Count, 1, 3);
        Assert.Contains(defects, d => d.Code == "vba.dir-record");
    }

    [Fact]
    public void Read_ModuleCountOverTheRecordLimit_IsALimitError()
    {
        var t = VbaTestProject.Typical();
        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(t.Build()));
        var ctx = new OpenContext(new OfficeLimits { MaxRecordCount = 2 });
        OfficeLimitException ex = Assert.Throws<OfficeLimitException>(() => VbaProject.FromStorage(cfb, string.Empty, ctx));
        Assert.Equal(nameof(OfficeLimits.MaxRecordCount), ex.LimitName);
    }

    // ---- extraction ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Export_WritesBasClsAndFrmWithOfficesHeaders()
    {
        VbaProject p = Read(VbaTestProject.Typical().Build(), out _);
        Assert.StartsWith("Attribute VB_Name = \"Module1\"", p.FindModule("Module1")!.ExportText(), StringComparison.Ordinal);
        Assert.StartsWith("VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Class1\"", p.FindModule("Class1")!.ExportText(), StringComparison.Ordinal);
        Assert.StartsWith("VERSION 5.00\r\nBegin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} UserForm1\r\nEnd\r\n", p.FindModule("UserForm1")!.ExportText(), StringComparison.Ordinal);
        Assert.Equal([".cls", ".bas", ".cls", ".frm"], p.Modules.Select(m => m.FileExtension));

        string dir = Path.Combine(Path.GetTempPath(), "vba-export-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            IReadOnlyList<string> files = p.ExportModules(dir);
            Assert.Equal(4, files.Count);
            Assert.Equal(["Class1.cls", "Module1.bas", "ThisDocument.cls", "UserForm1.frm"], files.Select(Path.GetFileName).Order(StringComparer.Ordinal));
            Assert.Equal(p.FindModule("Module1")!.ExportText(), File.ReadAllText(Path.Combine(dir, "Module1.bas"), Encoding.GetEncoding(1252)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        IReadOnlyDictionary<string, string> sources = p.ExtractSources();
        Assert.Equal(4, sources.Count);
        Assert.Contains("MsgBox", sources["ThisDocument"], StringComparison.Ordinal);
    }

    // ---- OOXML: open from a macro-enabled package, remove ------------------------------------------------------------------

    internal static byte[] MacroPackage(string mainPart, string mainType, byte[] project, bool signed, string? extraRelsOnMain = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] data)
            {
                ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Fastest);
                using Stream s = e.Open();
                s.Write(data);
            }

            void AddText(string name, string text) => Add(name, Encoding.UTF8.GetBytes(text));

            string dir = mainPart[..mainPart.LastIndexOf('/')];
            string file = mainPart[(mainPart.LastIndexOf('/') + 1)..];
            string types = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Default Extension=\"bin\" ContentType=\"application/vnd.ms-office.vbaProject\"/>" +
                "<Override PartName=\"/" + mainPart + "\" ContentType=\"" + mainType + "\"/>" +
                (signed ? "<Override PartName=\"/" + dir + "/vbaProjectSignature.bin\" ContentType=\"application/vnd.ms-office.vbaProjectSignature\"/>" : string.Empty) +
                "<Override PartName=\"/" + dir + "/other.xml\" ContentType=\"application/vnd.test+xml\"/></Types>";
            AddText("[Content_Types].xml", types);
            AddText("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"" + mainPart + "\"/></Relationships>");
            AddText(mainPart, "<root xmlns:z=\"urn:z\"><z:keep a=\"1\"/></root>");
            AddText(dir + "/other.xml", "<other/>");
            AddText(dir + "/_rels/" + file + ".rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.microsoft.com/office/2006/relationships/vbaProject\" Target=\"vbaProject.bin\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://example.test/other\" Target=\"other.xml\"/>" + extraRelsOnMain + "</Relationships>");
            Add(dir + "/vbaProject.bin", project);
            if (signed)
            {
                AddText(dir + "/_rels/vbaProject.bin.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.microsoft.com/office/2006/relationships/vbaProjectSignature\" Target=\"vbaProjectSignature.bin\"/></Relationships>");
                Add(dir + "/vbaProjectSignature.bin", [0x30, 0x03, 1, 2, 3]);
            }
        }

        return ms.ToArray();
    }

    private const string DocmType = "application/vnd.ms-word.document.macroEnabled.main+xml";

    [Fact]
    public void Open_Docm_FindsTheProjectAndItsSignatureParts()
    {
        byte[] file = MacroPackage("word/document.xml", DocmType, VbaTestProject.Typical().Build(), signed: true);
        using var ms = new MemoryStream(file);
        VbaProject? p = VbaProject.Open(ms);
        Assert.NotNull(p);
        Assert.Equal("/word/vbaProject.bin", p.Container);
        Assert.Equal(4, p.Modules.Count);
        Assert.Equal(VbaSignatureKinds.Legacy, p.Signatures);
    }

    [Fact]
    public void Open_PackageWithoutAProject_IsNull()
    {
        using var ms = new MemoryStream(Samples.Opc("word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"));
        Assert.Null(VbaProject.Open(ms));
    }

    [Fact]
    public void Remove_DocmToDocx_DropsProjectSignatureRelationshipsAndTypesOnly()
    {
        byte[] file = MacroPackage("word/document.xml", DocmType, VbaTestProject.Typical().Build(), signed: true);
        using var output = new MemoryStream();
        VbaRemovalReport report = VbaRemoval.Remove(new MemoryStream(file), output);
        Assert.True(report.HadProject);
        Assert.Equal(OfficeFormat.Docm, report.SourceFormat);
        Assert.Equal(OfficeFormat.Docx, report.OutputFormat);
        Assert.Equal(4, report.ModulesRemoved);
        Assert.Equal(VbaSignatureKinds.Legacy, report.SignaturesRemoved);
        Assert.Contains(report.Removed, i => i.Kind == VbaRemovedKind.Project && i.Location == "/word/vbaProject.bin" && i.Bytes > 0);
        Assert.Contains(report.Removed, i => i.Kind == VbaRemovedKind.Signature && i.Location == "/word/vbaProjectSignature.bin");
        Assert.Contains(report.Removed, i => i.Kind == VbaRemovedKind.ContentType);
        Assert.Empty(report.NotDone);

        using var zip = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToHashSet();
        Assert.DoesNotContain("word/vbaProject.bin", names);
        Assert.DoesNotContain("word/vbaProjectSignature.bin", names);
        Assert.DoesNotContain("word/_rels/vbaProject.bin.rels", names);
        string types = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open()).ReadToEnd();
        Assert.DoesNotContain("vbaProject", types, StringComparison.Ordinal);
        Assert.DoesNotContain("macroEnabled", types, StringComparison.Ordinal);
        Assert.Contains("wordprocessingml.document.main+xml", types, StringComparison.Ordinal);
        string rels = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open()).ReadToEnd();
        Assert.DoesNotContain("vbaProject", rels, StringComparison.Ordinal);
        Assert.Contains("other.xml", rels, StringComparison.Ordinal);
        // The parts that are not the project's keep their bytes.
        using var original = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        foreach (string keep in new[] { "word/document.xml", "word/other.xml", "_rels/.rels" })
        {
            Assert.Equal(Read(original.GetEntry(keep)!), Read(zip.GetEntry(keep)!));
        }

        // The output opens as a plain document with no project.
        using var again = new MemoryStream(output.ToArray());
        Assert.Null(VbaProject.Open(again));
        Assert.Equal(OfficeFormat.Docx, Formats.FormatSniffer.Detect(output.ToArray()));
    }

    private static byte[] Read(ZipArchiveEntry e)
    {
        using var ms = new MemoryStream();
        using Stream s = e.Open();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [Theory]
    [InlineData("xl/workbook.xml", "application/vnd.ms-excel.sheet.macroEnabled.main+xml", OfficeFormat.Xlsm, OfficeFormat.Xlsx, "spreadsheetml.sheet.main+xml")]
    [InlineData("xl/workbook.xml", "application/vnd.ms-excel.template.macroEnabled.main+xml", OfficeFormat.Xltm, OfficeFormat.Xltx, "spreadsheetml.template.main+xml")]
    [InlineData("ppt/presentation.xml", "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml", OfficeFormat.Pptm, OfficeFormat.Pptx, "presentationml.presentation.main+xml")]
    [InlineData("ppt/presentation.xml", "application/vnd.ms-powerpoint.slideshow.macroEnabled.main+xml", OfficeFormat.Ppsm, OfficeFormat.Ppsx, "presentationml.slideshow.main+xml")]
    [InlineData("word/document.xml", "application/vnd.ms-word.template.macroEnabledTemplate.main+xml", OfficeFormat.Dotm, OfficeFormat.Dotx, "wordprocessingml.template.main+xml")]
    public void Remove_MacroEnabledKinds_BecomeTheirMacroFreeCounterparts(string main, string type, OfficeFormat from, OfficeFormat to, string expected)
    {
        byte[] file = MacroPackage(main, type, VbaTestProject.Typical().Build(), signed: false);
        using var output = new MemoryStream();
        VbaRemovalReport report = VbaRemoval.Remove(new MemoryStream(file), output);
        Assert.Equal(from, report.SourceFormat);
        Assert.Equal(to, report.OutputFormat);
        Assert.Equal(to, Formats.FormatSniffer.Detect(output.ToArray()));
        using var zip = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        Assert.Contains(expected, new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open()).ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_AddIn_KeepsItsFormatAndSaysSo()
    {
        byte[] file = MacroPackage("xl/workbook.xml", "application/vnd.ms-excel.addin.macroEnabled.main+xml", VbaTestProject.Typical().Build(), signed: false);
        using var output = new MemoryStream();
        VbaRemovalReport report = VbaRemoval.Remove(new MemoryStream(file), output);
        Assert.Equal(OfficeFormat.Xlam, report.OutputFormat);
        Assert.Contains(report.NotDone, n => n.Code == "vba.no-macro-free-type");
    }

    [Fact]
    public void Remove_FileWithoutAProject_IsACopyAndSaysSo()
    {
        byte[] file = Samples.Opc("word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml");
        using var output = new MemoryStream();
        VbaRemovalReport report = VbaRemoval.Remove(new MemoryStream(file), output);
        Assert.False(report.HadProject);
        Assert.Empty(report.Removed);
        Assert.Equal(OfficeFormat.Docx, report.OutputFormat);
    }

    [Fact]
    public void Remove_ToAFile_IsNewAndAtomic()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vba-rm-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            string src = Path.Combine(dir, "a.docm");
            string dst = Path.Combine(dir, "a.docx");
            File.WriteAllBytes(src, MacroPackage("word/document.xml", DocmType, VbaTestProject.Typical().Build(), signed: false));
            VbaRemovalReport report = VbaRemoval.Remove(src, dst);
            Assert.True(report.HadProject);
            Assert.True(File.Exists(dst));
            Assert.Null(VbaProject.Open(dst));
            Assert.NotNull(VbaProject.Open(src));
            Assert.Throws<ArgumentException>(() => VbaRemoval.Remove(src, src));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- Word 97-2003: the Macros storage ----------------------------------------------------------------------------------

    private static byte[] DocWithMacros(VbaTestProject? project)
    {
        var cfb = CfbPackage.Create();
        byte[] word = new byte[1024];
        new Random(3).NextBytes(word);
        word[0x0A] = 0; word[0x0B] = 0; // fEncrypted clear
        cfb.AddPart("/WordDocument", string.Empty, word);
        cfb.AddPart("/1Table", string.Empty, new byte[600]);
        if (project is not null)
        {
            byte[] bin = project.Build();
            using CfbPackage inner = CfbPackage.Open(ByteSource.FromMemory(bin));
            foreach (CfbStreamPart s in inner.Streams)
            {
                cfb.AddPart("/Macros" + s.Name, string.Empty, s.GetBytes());
            }
        }

        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Doc_ReadsTheMacrosStorage_AndRemovalLeavesTheOtherStreams()
    {
        byte[] doc = DocWithMacros(VbaTestProject.Typical());
        using (var ms = new MemoryStream(doc))
        {
            Assert.Equal(OfficeFormat.Doc, Formats.FormatSniffer.Detect(doc));
            VbaProject? p = VbaProject.Open(ms);
            Assert.NotNull(p);
            Assert.Equal("/Macros", p.Container);
            Assert.Equal(4, p.Modules.Count);
        }

        using var output = new MemoryStream();
        VbaRemovalReport report = VbaRemoval.Remove(new MemoryStream(doc), output);
        Assert.True(report.HadProject);
        Assert.Equal(OfficeFormat.Doc, report.OutputFormat);
        Assert.Equal(4, report.ModulesRemoved);
        Assert.Contains(report.Removed, i => i.Kind == VbaRemovedKind.Project && i.Location == "/Macros");
        Assert.Contains(report.NotDone, n => n.Code == "vba.doc-customizations-kept");

        using CfbPackage back = CfbPackage.Open(ByteSource.FromMemory(output.ToArray()));
        Assert.DoesNotContain(back.Streams, s => s.Name.StartsWith("/Macros", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(back.Storages, s => s.Path.StartsWith("/Macros", StringComparison.OrdinalIgnoreCase));
        using CfbPackage before = CfbPackage.Open(ByteSource.FromMemory(doc));
        Assert.Equal(before.FindPart("/WordDocument")!.GetBytes().ToArray(), back.FindPart("/WordDocument")!.GetBytes().ToArray());
        Assert.Equal(before.FindPart("/1Table")!.GetBytes().ToArray(), back.FindPart("/1Table")!.GetBytes().ToArray());
    }

    [Fact]
    public void Doc_WithoutMacros_IsACopy()
    {
        byte[] doc = DocWithMacros(null);
        using var output = new MemoryStream();
        VbaRemovalReport report = VbaRemoval.Remove(new MemoryStream(doc), output);
        Assert.False(report.HadProject);
        Assert.Equal(doc, output.ToArray());
        Assert.Null(VbaProject.Open(new MemoryStream(doc)));
    }

    [Fact]
    public void Doc_Encrypted_IsATypedUnsupportedError()
    {
        var cfb = CfbPackage.Create();
        byte[] word = new byte[512];
        word[0x0A] = 0x00; word[0x0B] = 0x01; // fEncrypted
        cfb.AddPart("/WordDocument", string.Empty, word);
        cfb.AddPart("/Macros/VBA/dir", string.Empty, VbaTestProject.Compress(new byte[4]));
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        OfficeUnsupportedException ex = Assert.Throws<OfficeUnsupportedException>(() => VbaRemoval.Remove(new MemoryStream(ms.ToArray()), new MemoryStream()));
        Assert.Contains("encrypted", ex.Feature, StringComparison.Ordinal);
    }

    // ---- Excel 97-2003: the project storage and the ObProj record --------------------------------------------------------------

    private static byte[] Rec(ushort type, byte[] data)
    {
        byte[] r = new byte[4 + data.Length];
        BitConverter.GetBytes(type).CopyTo(r, 0);
        BitConverter.GetBytes((ushort)data.Length).CopyTo(r, 2);
        data.CopyTo(r, 4);
        return r;
    }

    // Globals: BOF, ObProj, BoundSheet8 (position of the sheet), SST, ExtSST, EOF; then one sheet: BOF, Index (DBCell position), DBCell, EOF.
    private static byte[] WorkbookStream(bool withObProj, out int sheetBof)
    {
        const int bof = 20, bound = 17, sst = 16, extSst = 14, eof = 4;
        int sstAt = bof + (withObProj ? 4 : 0) + bound;
        sheetBof = sstAt + sst + extSst + eof;
        int dbcellAt = sheetBof + bof + 24;
        var all = new List<byte>();
        all.AddRange(Rec(0x0809, new byte[16]));
        if (withObProj)
        {
            all.AddRange(Rec(0x00D3, []));
        }

        all.AddRange(Rec(0x0085, [.. BitConverter.GetBytes((uint)sheetBof), 0, 0, 5, 0, (byte)'S', (byte)'h', (byte)'e', (byte)'e', (byte)'t']));
        all.AddRange(Rec(0x00FC, new byte[12]));
        all.AddRange(Rec(0x00FF, [.. BitConverter.GetBytes((ushort)8), .. BitConverter.GetBytes((uint)sstAt), .. BitConverter.GetBytes((ushort)12), 0, 0]));
        all.AddRange(Rec(0x000A, []));
        Assert.Equal(sheetBof, all.Count);
        all.AddRange(Rec(0x0809, new byte[16]));
        all.AddRange(Rec(0x020B, [.. new byte[12], .. BitConverter.GetBytes((uint)dbcellAt), .. BitConverter.GetBytes((uint)dbcellAt)]));
        all.AddRange(Rec(0x00D7, new byte[8]));
        all.AddRange(Rec(0x000A, []));
        return [.. all];
    }

    private static byte[] Xls(bool withProject, bool withObProj)
    {
        var cfb = CfbPackage.Create();
        cfb.AddPart("/Workbook", string.Empty, WorkbookStream(withObProj, out _));
        cfb.AddPart("/\u0005SummaryInformation", string.Empty, new byte[64]);
        if (withProject)
        {
            byte[] bin = VbaTestProject.Typical().Build();
            using CfbPackage inner = CfbPackage.Open(ByteSource.FromMemory(bin));
            foreach (CfbStreamPart s in inner.Streams)
            {
                cfb.AddPart("/_VBA_PROJECT_CUR" + s.Name, string.Empty, s.GetBytes());
            }
        }

        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Xls_RemovalDropsTheStorageAndTheProjectRecordAndFixesPositions()
    {
        byte[] xls = Xls(withProject: true, withObProj: true);
        Assert.Equal(OfficeFormat.Xls, Formats.FormatSniffer.Detect(xls));
        VbaProject? p = VbaProject.Open(new MemoryStream(xls));
        Assert.NotNull(p);
        Assert.Equal("/_VBA_PROJECT_CUR", p.Container);

        using var output = new MemoryStream();
        VbaRemovalReport report = VbaRemoval.Remove(new MemoryStream(xls), output);
        Assert.True(report.HadProject);
        Assert.Equal(4, report.ModulesRemoved);
        Assert.Contains(report.Removed, i => i.Kind == VbaRemovedKind.Project && i.Location == "/_VBA_PROJECT_CUR");
        Assert.Contains(report.Removed, i => i.Kind == VbaRemovedKind.Marker);

        using CfbPackage back = CfbPackage.Open(ByteSource.FromMemory(output.ToArray()));
        Assert.DoesNotContain(back.Streams, s => s.Name.StartsWith("/_VBA_PROJECT_CUR", StringComparison.OrdinalIgnoreCase));
        byte[] now = back.FindPart("/Workbook")!.GetBytes().ToArray();
        byte[] was = WorkbookStream(withObProj: true, out int oldSheet);
        Assert.Equal(was.Length - 4, now.Length);
        // Walk the records of the output: no ObProj, and every stored position points at the record it did before.
        var offsets = new Dictionary<int, ushort>();
        for (int pos = 0; pos + 4 <= now.Length; pos += 4 + BitConverter.ToUInt16(now, pos + 2))
        {
            offsets[pos] = BitConverter.ToUInt16(now, pos);
        }

        Assert.DoesNotContain(offsets.Values, t => t == 0x00D3);
        int boundAt = offsets.First(kv => kv.Value == 0x0085).Key;
        uint sheetPos = BitConverter.ToUInt32(now, boundAt + 4);
        Assert.Equal(oldSheet - 4, (int)sheetPos);
        Assert.Equal((ushort)0x0809, offsets[(int)sheetPos]);
        int indexAt = offsets.First(kv => kv.Value == 0x020B).Key;
        Assert.Equal((ushort)0x00D7, offsets[(int)BitConverter.ToUInt32(now, indexAt + 4 + 12)]);
        Assert.Equal((ushort)0x00D7, offsets[(int)BitConverter.ToUInt32(now, indexAt + 4 + 16)]);
        int extAt = offsets.First(kv => kv.Value == 0x00FF).Key;
        Assert.Equal((ushort)0x00FC, offsets[(int)BitConverter.ToUInt32(now, extAt + 6)]);
        Assert.Equal(new byte[64], back.FindPart("/\u0005SummaryInformation")!.GetBytes().ToArray());
    }

    [Fact]
    public void Xls_WithoutAProject_IsACopy()
    {
        byte[] xls = Xls(withProject: false, withObProj: false);
        using var output = new MemoryStream();
        VbaRemovalReport report = VbaRemoval.Remove(new MemoryStream(xls), output);
        Assert.False(report.HadProject);
        Assert.Equal(xls, output.ToArray());
    }

    [Fact]
    public void NotAVbaFormat_IsATypedError()
    {
        using var ms = new MemoryStream(Samples.Text("{\\rtf1 hello}"));
        Assert.Throws<FormatNotAvailableException>(() => VbaRemoval.Remove(ms, new MemoryStream()));
        Assert.Null(VbaProject.Open(new MemoryStream(Samples.Text("{\\rtf1 hello}"))));
    }
}
