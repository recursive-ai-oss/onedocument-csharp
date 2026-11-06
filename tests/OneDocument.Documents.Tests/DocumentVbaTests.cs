using RecursiveAI.OneDocument.Tests;
using RecursiveAI.OneDocument.Vba;

namespace RecursiveAI.OneDocument.Documents.Tests;

public sealed class DocumentVbaTests : IDisposable
{
    private readonly string _dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "doc-vba-" + Guid.NewGuid().ToString("N"));

    public DocumentVbaTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string Fixture = "lo-roundtrip.docm";
    private const string ProjectPart = "word/vbaProject.bin";

    [Fact]
    public void Open_ReadsTheProjectAsData()
    {
        using Document file = Document.Open(VbaHubFixtures.Path(Fixture));
        Assert.True(file.HasVbaProject);
        VbaProject? p = file.GetVbaProject();
        Assert.NotNull(p);
        Assert.Equal(4, p.Modules.Count);
        Assert.Contains("Sub Run()", p.FindModule("Module1")!.Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenAndSaveUntouched_KeepsTheProjectAndItsSignatureByteIdentical(bool withSignature)
    {
        string source = withSignature ? VbaHubFixtures.Signed(Fixture, _dir, "/" + ProjectPart) : VbaHubFixtures.Path(Fixture);
        string target = Path.Combine(_dir, "saved.docm");
        using (Document file = Document.Open(source))
        {
            file.SaveAs(target);
        }

        Dictionary<string, byte[]> before = VbaHubFixtures.Entries(source);
        Dictionary<string, byte[]> after = VbaHubFixtures.Entries(target);
        Assert.Equal(before[ProjectPart], after[ProjectPart]);
        if (withSignature)
        {
            string sig = ProjectPart[..ProjectPart.LastIndexOf('/')] + "/vbaProjectSignature.bin";
            Assert.Equal(before[sig], after[sig]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Remove_ThenSaveAsMacroFree_LeavesAPlainFileWithTheSameContent(bool withSignature)
    {
        string source = withSignature ? VbaHubFixtures.Signed(Fixture, _dir, "/" + ProjectPart) : VbaHubFixtures.Path(Fixture);
        string target = Path.Combine(_dir, "clean.docx");
        string textBefore = Office.ExtractText(source);
        using (Document file = Document.Open(source))
        {
            VbaRemovalReport report = file.RemoveVbaProject();
            Assert.True(report.HadProject);
            Assert.Equal(4, report.ModulesRemoved);
            Assert.Equal(withSignature ? VbaSignatureKinds.Legacy : VbaSignatureKinds.None, report.SignaturesRemoved);
            Assert.False(file.HasVbaProject);
            Assert.Null(file.GetVbaProject());
            file.SaveAs(target);
        }

        Dictionary<string, byte[]> entries = VbaHubFixtures.Entries(target);
        Assert.DoesNotContain(entries.Keys, k => k.Contains("vba", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(RecursiveAI.OneDocument.OfficeFormat.Docx, Formats.FormatSniffer.Detect(target));
        Assert.Equal(textBefore, Office.ExtractText(target));
        using (Document again = Document.Open(target))
        {
            Assert.False(again.HasVbaProject);
        }
    }

    [Fact]
    public void RemoveFromAFileWithoutAProject_ChangesNothing()
    {
        string clean = Path.Combine(_dir, "clean.docx");
        using (Document file = Document.Open(VbaHubFixtures.Path(Fixture)))
        {
            file.RemoveVbaProject();
            file.SaveAs(clean);
        }

        using (Document again = Document.Open(clean))
        {
            VbaRemovalReport report = again.RemoveVbaProject();
            Assert.False(report.HadProject);
            Assert.Empty(report.Removed);
        }
    }
}
