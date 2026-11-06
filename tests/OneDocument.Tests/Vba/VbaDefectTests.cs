using System.Text;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Vba;

namespace RecursiveAI.OneDocument.Tests;

/// <summary>One crafted input per `vba.` code of the defect catalogue.</summary>
public sealed class VbaDefectTests
{
    private static byte[] Edit(byte[] bin, Action<CfbPackage> edit)
    {
        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(bin));
        edit(cfb);
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        return ms.ToArray();
    }

    private static (VbaProject Project, DefectCollection Defects) Read(byte[] bin)
    {
        var defects = new DefectCollection();
        VbaProject p = VbaProject.FromProjectStorage(bin, new OpenContext(defects: defects));
        return (p, defects);
    }

    private static Defect One(DefectCollection defects, string code, DefectSeverity severity)
    {
        Defect d = Assert.Single(defects, x => x.Code == code);
        Assert.Equal(severity, d.Severity);
        return d;
    }

    [Fact]
    public void ProjectStreamMissing()
    {
        byte[] bin = Edit(VbaTestProject.Typical().Build(), c => c.RemovePart("/PROJECT"));
        (VbaProject p, DefectCollection d) = Read(bin);
        One(d, "vba.project-stream-missing", DefectSeverity.Repaired);
        Assert.Equal(4, p.Modules.Count);
        Assert.Null(p.ProjectText);
        // Without the PROJECT stream the kinds come from the module type records alone.
        Assert.Equal(VbaModuleKind.Class, p.FindModule("ThisDocument")!.Kind);
        Assert.Equal(VbaModuleKind.Standard, p.FindModule("Module1")!.Kind);
    }

    [Fact]
    public void DirDamaged_KeepsTheRecordsBeforeTheDamage()
    {
        byte[] bin = Edit(VbaTestProject.Typical().Build(), c =>
        {
            byte[] dir = c.FindPart("/VBA/dir")!.GetBytes().ToArray();
            c.FindPart("/VBA/dir")!.SetBytes((byte[])[.. dir, 0x00, 0x20]); // a chunk header with a bad signature after the last chunk
        });
        (VbaProject p, DefectCollection d) = Read(bin);
        Defect defect = One(d, "vba.dir-damaged", DefectSeverity.Repaired);
        Assert.Contains("/VBA/dir", defect.Location.ToString(), StringComparison.Ordinal);
        Assert.Equal(4, p.Modules.Count);
    }

    [Fact]
    public void DirRecordUnknown_IsSkippedBySize()
    {
        var t = VbaTestProject.Typical();
        t.DirTransform = dir => [.. dir[..10], 0x77, 0x77, 4, 0, 0, 0, 1, 2, 3, 4, .. dir[10..]]; // after PROJECTSYSKIND
        (VbaProject p, DefectCollection d) = Read(t.Build());
        One(d, "vba.dir-record-unknown", DefectSeverity.Repaired);
        Assert.Equal(4, p.Modules.Count);
    }

    [Fact]
    public void DirRecord_ASizeFieldThatIsWrong()
    {
        var t = VbaTestProject.Typical();
        t.DirTransform = dir =>
        {
            byte[] copy = (byte[])dir.Clone();
            copy[12] = 9; // the size of PROJECTLCID (the second record) is 4
            return copy;
        };
        (VbaProject p, DefectCollection d) = Read(t.Build());
        One(d, "vba.dir-record", DefectSeverity.Repaired);
        Assert.Empty(p.Modules);
    }

    [Fact]
    public void CodePageUnsupported_FallsBackToLatin1()
    {
        var t = VbaTestProject.Typical();
        t.DirTransform = dir =>
        {
            byte[] copy = (byte[])dir.Clone();
            copy[36] = 0x5D; // PROJECTCODEPAGE value: 0xDE5D is no Windows code page
            copy[37] = 0xDE;
            return copy;
        };
        (VbaProject p, DefectCollection d) = Read(t.Build());
        One(d, "vba.codepage-unsupported", DefectSeverity.Repaired);
        Assert.Equal(0xDE5D, p.CodePage);
        Assert.Equal(4, p.Modules.Count);
    }

    [Fact]
    public void ModuleListShort()
    {
        var t = VbaTestProject.Typical();
        t.DeclaredModuleCount = 6;
        (VbaProject p, DefectCollection d) = Read(t.Build());
        One(d, "vba.module-list-short", DefectSeverity.Repaired);
        Assert.Equal(4, p.Modules.Count);
    }

    [Fact]
    public void ModuleStreamMissing()
    {
        byte[] bin = Edit(VbaTestProject.Typical().Build(), c => c.RemovePart("/VBA/Class1"));
        (VbaProject p, DefectCollection d) = Read(bin);
        Defect defect = One(d, "vba.module-stream-missing", DefectSeverity.Dropped);
        Assert.Equal(DefectKind.Reference, defect.Kind);
        Assert.False(p.FindModule("Class1")!.SourceAvailable);
        Assert.Equal(-1, p.FindModule("Class1")!.StreamLength);
    }

    [Fact]
    public void ModuleOffsetInvalid()
    {
        var t = new VbaTestProject();
        t.Modules.Add(new VbaTestProject.Module("Module1", "Module", "x", Cache: new byte[500]));
        byte[] bin = Edit(t.Build(), c => c.FindPart("/VBA/Module1")!.SetBytes(new byte[40]));
        (VbaProject p, DefectCollection d) = Read(bin);
        Defect defect = One(d, "vba.module-offset-invalid", DefectSeverity.Dropped);
        Assert.Equal(500, defect.Location.Offset);
        Assert.False(p.Modules[0].SourceAvailable);
    }

    [Fact]
    public void ModuleSourceDamaged()
    {
        var t = new VbaTestProject();
        t.Modules.Add(new VbaTestProject.Module("Module1", "Module", "Attribute VB_Name = \"Module1\"\r\n"));
        byte[] bin = Edit(t.Build(), c =>
        {
            byte[] b = c.FindPart("/VBA/Module1")!.GetBytes().ToArray();
            b[0] = 0x02; // the container signature byte
            c.FindPart("/VBA/Module1")!.SetBytes(b);
        });
        (VbaProject p, DefectCollection d) = Read(bin);
        One(d, "vba.module-source-damaged", DefectSeverity.Repaired);
        Assert.True(p.Modules[0].SourceDamaged);
    }

    [Fact]
    public void VersionStreamMissingAndShort()
    {
        (VbaProject _, DefectCollection d) = Read(Edit(VbaTestProject.Typical().Build(), c => c.RemovePart("/VBA/_VBA_PROJECT")));
        One(d, "vba.version-stream-missing", DefectSeverity.Repaired);
        (VbaProject p, DefectCollection d2) = Read(Edit(VbaTestProject.Typical().Build(), c => c.FindPart("/VBA/_VBA_PROJECT")!.SetBytes(new byte[] { 0xCC, 0x61, 0xFF })));
        One(d2, "vba.version-stream-short", DefectSeverity.Repaired);
        Assert.Null(p.PerformanceCacheVersion);
    }

    [Fact]
    public void ProjectPartThatIsNotACompoundFile()
    {
        byte[] docm = VbaProjectTests.MacroPackage("word/document.xml", "application/vnd.ms-word.document.macroEnabled.main+xml", Encoding.ASCII.GetBytes("this is not a compound file"), signed: false);
        var defects = new DefectCollection();
        VbaProject? p = VbaProject.Open(new MemoryStream(docm), new OpenContext(defects: defects));
        Assert.NotNull(p);
        Assert.Empty(p.Modules);
        One(defects, "vba.project-not-compound", DefectSeverity.Dropped);
        Assert.Contains("/word/vbaProject.bin", defects[0].Location.ToString(), StringComparison.Ordinal);

        using var output = new MemoryStream();
        VbaRemovalReport report = VbaRemoval.Remove(new MemoryStream(docm), output);
        Assert.True(report.HadProject);
        Assert.Equal(0, report.ModulesRemoved);
        Assert.Null(VbaProject.Open(new MemoryStream(output.ToArray())));
    }

    [Fact]
    public void RemovalNote_ProjectUnreadable_WhenTheModuleLimitStopsTheCount()
    {
        byte[] docm = VbaProjectTests.MacroPackage("word/document.xml", "application/vnd.ms-word.document.macroEnabled.main+xml", VbaTestProject.Typical().Build(), signed: false);
        using var output = new MemoryStream();
        VbaRemovalReport report = VbaRemoval.Remove(new MemoryStream(docm), output, new OpenContext(new OfficeLimits { MaxRecordCount = 2 }));
        Assert.True(report.HadProject);
        VbaRemovalNote note = Assert.Single(report.NotDone, n => n.Code == "vba.project-unreadable");
        Assert.Contains("record", note.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(report.Removed, i => i.Kind == VbaRemovedKind.Project);
    }

    [Fact]
    public void RemovalNote_OutputNotEncrypted()
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Where())!, "..", "..", "..", "testdata", "encryption", "secret-basic.docx"));
        using var output = new MemoryStream();
        using var input = new MemoryStream(File.ReadAllBytes(path));
        VbaRemovalReport report = VbaRemoval.Remove(input, output, new OpenContext(password: "secret"));
        Assert.False(report.HadProject);
        Assert.Contains(report.NotDone, n => n.Code == "vba.output-not-encrypted");
        Assert.Equal(OfficeFormat.Docx, Formats.FormatSniffer.Detect(output.ToArray()));
        Assert.Throws<OfficeEncryptedException>(() => VbaRemoval.Remove(new MemoryStream(File.ReadAllBytes(path)), new MemoryStream()));
    }

    private static string Where([System.Runtime.CompilerServices.CallerFilePath] string p = "") => p;

    [Fact]
    public void WorkbookStreamMissing_IsAFatalLocatedDefect()
    {
        var cfb = CfbPackage.Create();
        cfb.AddPart("/_VBA_PROJECT_CUR/PROJECT", string.Empty, new byte[] { 1 });
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => LegacyVba.RemoveFromXls(ByteSource.FromMemory(ms.ToArray()), new MemoryStream(), new OpenContext(), default));
        Assert.Equal("vba.workbook-stream-missing", ex.Defect.Code);
    }

    [Fact]
    public void InvalidDataInTheInput_IsATypedError()
    {
        // A zip central directory that lies about an entry: the container reports it, and the removal types it.
        byte[] zip = Samples.Opc("word/document.xml", "application/vnd.ms-word.document.macroEnabled.main+xml");
        zip[^5] ^= 0xFF;
        OfficeException ex = Assert.ThrowsAny<OfficeException>(() => VbaRemoval.Remove(new MemoryStream(zip), new MemoryStream()));
        Assert.False(string.IsNullOrEmpty(ex.Defect.Code));
    }
}
