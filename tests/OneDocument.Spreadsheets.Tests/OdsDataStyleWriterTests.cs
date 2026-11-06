using System.Text;
using RecursiveAI.OneDocument.Spreadsheets.Odf;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class OdsDataStyleWriterTests
{
    [Fact]
    public void ACodeWithSeveralDigitGroupsWritesOneNumberElement()
    {
        var xml = new StringBuilder();
        var issues = new List<string>();
        Assert.True(OdsDataStyleWriter.Write("(0) 0-0", "N6", xml, issues));

        // The ODF 1.3 schema allows one number element in a number style; Jing rejected the second.
        string text = xml.ToString();
        Assert.Equal(1, text.Split("<number:number ").Length - 1);
        Assert.Contains("multiple-digit-groups", issues);

        // The separators left next to each other are one text element.
        Assert.DoesNotContain("</number:text><number:text>", text, StringComparison.Ordinal);
    }
}
