using RecursiveAI.OneDocument.Documents.Odf;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class OdfStyleNameTests
{
    [Theory]
    [InlineData("Heading 1", "Heading_20_1")]
    [InlineData("2nd Level", "_32_nd_20_Level")]
    [InlineData("-dash", "_2d_dash")]
    [InlineData(".dot", "_2e_dot")]
    [InlineData("a-b.c", "a-b.c")]
    public void StyleNamesAreXmlNamesAndDecodeBack(string display, string expected)
    {
        // ODF 1.3 Part 1: style names are NCNames, which cannot start with a digit, a hyphen or a full stop.
        string encoded = OdfUnits.EncodeStyleName(display);
        Assert.Equal(expected, encoded);
        Assert.Equal(display, OdfUnits.DecodeStyleName(encoded));
    }
}
