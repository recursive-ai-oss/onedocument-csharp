using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Cli.Tests;

public class CliTextTests
{
    [Fact]
    public void ALocationIsNamedWhenThereIsOne()
    {
        Assert.Equal(" at /word/document.xml@12", CliText.At(DefectLocation.InPart("/word/document.xml", 12)));
        Assert.Equal(" at Sheet1!B7", CliText.At(DefectLocation.InCell("Sheet1", "B7")));
    }

    [Fact]
    public void ADefectWithoutALocationLeavesNoDanglingAt()
    {
        Assert.Equal(string.Empty, CliText.At(default));
        Assert.Equal(string.Empty, CliText.At(DefectLocation.None));
    }
}
