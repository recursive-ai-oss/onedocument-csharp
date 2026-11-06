using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Vba;

namespace RecursiveAI.OneDocument.Tests;

/// <summary>The VBA reader and remover on projects another program wrote (testdata/vba/README.md).</summary>
public sealed class VbaOracleTests
{
    private static string Fixture(string name, [CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "testdata", "vba", name));

    [Fact]
    public void GeneratedProject_ReadsBackEveryModuleExactly()
    {
        VbaProject p = VbaProject.FromProjectStorage(File.ReadAllBytes(Fixture("generated-project.bin")));
        VbaTestProject t = VbaTestProject.Typical();
        Assert.Equal(t.Modules.Count, p.Modules.Count);
        foreach (VbaTestProject.Module m in t.Modules)
        {
            Assert.Equal(m.Source, p.FindModule(m.Name)!.Source);
        }
    }

    [Theory]
    [InlineData("lo-roundtrip.docm", "/word/vbaProject.bin", OfficeFormat.Docm)]
    [InlineData("lo-roundtrip.xlsm", "/xl/vbaProject.bin", OfficeFormat.Xlsm)]
    [InlineData("lo-roundtrip.pptm", "/ppt/vbaProject.bin", OfficeFormat.Pptm)]
    [InlineData("lo-roundtrip.xls", "/_VBA_PROJECT_CUR", OfficeFormat.Xls)]
    public void LibreOfficeWrittenProjects_ReadWithAllSourceAndReferences(string file, string container, OfficeFormat format)
    {
        Assert.Equal(format, Formats.FormatSniffer.Detect(Fixture(file)));
        var defects = new DefectCollection();
        VbaProject? p = VbaProject.Open(Fixture(file), new OpenContext(defects: defects));
        Assert.NotNull(p);
        Assert.DoesNotContain(defects, d => d.Code.StartsWith("vba.", StringComparison.Ordinal));
        Assert.Equal(container, p.Container);
        Assert.Equal(4, p.Modules.Count);
        Assert.All(p.Modules, m => Assert.True(m.SourceAvailable));
        Assert.Contains("Sub Run()", p.FindModule("Module1")!.Source, StringComparison.Ordinal);
        Assert.Contains("Debug.Print \"é€\"", p.FindModule("Module1")!.Source, StringComparison.Ordinal);
        Assert.Contains("Private Sub Document_Open()", p.FindModule("ThisDocument")!.Source, StringComparison.Ordinal);
        Assert.Equal(VbaModuleKind.Document, p.FindModule("ThisDocument")!.Kind);
        Assert.Equal(VbaModuleKind.Standard, p.FindModule("Module1")!.Kind);
        Assert.Equal(1252, p.CodePage);
        Assert.Contains(p.References, r => r.Kind == VbaReferenceKind.Registered && r.Name == "stdole");
        Assert.False(p.IsLocked);
        Assert.False(p.IsSigned);
    }

    [Theory]
    [InlineData("lo-roundtrip.docm", OfficeFormat.Docx)]
    [InlineData("lo-roundtrip.xlsm", OfficeFormat.Xlsx)]
    [InlineData("lo-roundtrip.pptm", OfficeFormat.Pptx)]
    [InlineData("lo-roundtrip.xls", OfficeFormat.Xls)]
    public void LibreOfficeWrittenProjects_RemoveToTheMacroFreeFormat(string file, OfficeFormat expected)
    {
        string path = Path.Combine(Path.GetTempPath(), "vba-oracle-" + Environment.ProcessId + "-" + file + ".out");
        VbaRemovalReport report = VbaRemoval.Remove(Fixture(file), path);
        try
        {
            Assert.True(report.HadProject);
            Assert.Equal(4, report.ModulesRemoved);
            Assert.Equal(expected, report.OutputFormat);
            Assert.Equal(expected, Formats.FormatSniffer.Detect(path));
            Assert.Null(VbaProject.Open(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LibreOfficeXls_AfterRemoval_KeepsEveryOtherStream()
    {
        string path = Path.Combine(Path.GetTempPath(), "vba-oracle-xls-" + Environment.ProcessId + ".xls");
        try
        {
            VbaRemoval.Remove(Fixture("lo-roundtrip.xls"), path);
            using CfbPackage before = CfbPackage.Open(Fixture("lo-roundtrip.xls"));
            using CfbPackage after = CfbPackage.Open(path);
            foreach (CfbStreamPart s in before.Streams.Where(x => !x.Name.StartsWith("/_VBA_PROJECT_CUR", StringComparison.OrdinalIgnoreCase) && x.Name != "/Workbook"))
            {
                Assert.Equal(s.GetBytes().ToArray(), after.FindPart(s.Name)!.GetBytes().ToArray());
            }

            Assert.Equal(before.FindPart("/Workbook")!.Length - 4, after.FindPart("/Workbook")!.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
