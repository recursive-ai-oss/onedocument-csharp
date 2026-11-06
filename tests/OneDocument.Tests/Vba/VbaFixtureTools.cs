using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Tests;

/// <summary>
/// Writes the inputs of the black-box oracle runs: a generated VBA project, and macro-enabled packages made by putting it into plain
/// packages another program wrote. Runs only when ONEDOCUMENT_VBA_FIXTURES names a directory holding <c>base/in.docx</c>, <c>in.xlsx</c>
/// and <c>in.pptx</c>; otherwise it is skipped.
/// </summary>
public sealed class VbaFixtureTools
{
    [Fact]
    public void WriteOracleInputs()
    {
        string? dir = Environment.GetEnvironmentVariable("ONEDOCUMENT_VBA_FIXTURES");
        Assert.SkipWhen(string.IsNullOrEmpty(dir), "ONEDOCUMENT_VBA_FIXTURES is not set.");
        byte[] project = VbaTestProject.Typical().Build();
        File.WriteAllBytes(Path.Combine(dir!, "typical.bin"), project);
        Inject(dir!, "in.docx", "in.docm", "word/document.xml", "application/vnd.ms-word.document.macroEnabled.main+xml", project);
        Inject(dir!, "in.xlsx", "in.xlsm", "xl/workbook.xml", "application/vnd.ms-excel.sheet.macroEnabled.main+xml", project);
        Inject(dir!, "in.pptx", "in.pptm", "ppt/presentation.xml", "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml", project);
    }

    private static void Inject(string dir, string from, string to, string main, string macroType, byte[] project)
    {
        string source = Path.Combine(dir, "base", from);
        string target = Path.Combine(dir, to);
        using OpcPackage package = OpcPackage.Open(source);
        string mainDir = main[..main.LastIndexOf('/')];
        package.SetPartContentType("/" + main, macroType);
        package.AddPart("/" + mainDir + "/vbaProject.bin", "application/vnd.ms-office.vbaProject", project);
        var rels = new List<Relationship>(package.GetRelationships("/" + main))
        {
            new("rIdVba1", "http://schemas.microsoft.com/office/2006/relationships/vbaProject", "vbaProject.bin"),
        };
        package.SetRelationships("/" + main, rels);
        package.SaveAs(target);
    }
}
