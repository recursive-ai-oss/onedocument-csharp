using RecursiveAI.OneDocument.Ole;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Tests;

public class OleEditTimeTests
{
    [Fact]
    public void EditTimeBeyondTheSchemaIntegerIsReportedAsLostNotWritten()
    {
        // A legacy file whose edit-time field holds a stray timestamp: the extended-properties schema types TotalTime as xsd:int.
        using var p1 = new InMemoryPackage();
        var src = new DocumentProperties(p1);
        src.Extended.TotalTimeMinutes = 23_054_499_219;
        (byte[]? summary, byte[]? docSummary) = OleDocumentProperties.Create(src);

        using var p2 = new InMemoryPackage();
        var back = new DocumentProperties(p2);
        IReadOnlyList<string> lost = OleDocumentProperties.Read(summary, docSummary, back, new OpenContext());
        Assert.Null(back.Extended.TotalTimeMinutes);
        Assert.NotEmpty(lost);
    }

    [Fact]
    public void EditTimeWithinTheSchemaIntegerIsKept()
    {
        using var p1 = new InMemoryPackage();
        var src = new DocumentProperties(p1);
        src.Extended.TotalTimeMinutes = 2_000_000_000;
        (byte[]? summary, byte[]? docSummary) = OleDocumentProperties.Create(src);

        using var p2 = new InMemoryPackage();
        var back = new DocumentProperties(p2);
        OleDocumentProperties.Read(summary, docSummary, back, new OpenContext());
        Assert.Equal(2_000_000_000, back.Extended.TotalTimeMinutes);
    }
}
