using Xunit;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

/// <summary>Inputs the fuzz campaign found.</summary>
public class FuzzRegressionTests
{
    // A legacy file can hold a lone UTF-16 surrogate in a string; the shared string part must escape it, not fail on write.
    [Fact]
    public void SharedStringEncodingEscapesLoneSurrogates()
    {
        Assert.Equal("a_xD800_b", XString.Encode("a\uD800b"));
        Assert.Equal("_xDC00_", XString.Encode("\uDC00"));
        Assert.Equal("\U0001F600", XString.Encode("\U0001F600"));
    }
}
