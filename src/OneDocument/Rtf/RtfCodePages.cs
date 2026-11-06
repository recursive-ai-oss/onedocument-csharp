using System.Text;

namespace RecursiveAI.OneDocument.Rtf;

// Code pages as RTF names them: \fcharsetN on a font and \ansicpgN for the document, mapped to Windows code pages.
/// <summary>Code pages as RTF names them: <c>\fcharsetN</c> on a font and <c>\ansicpgN</c> for the document, mapped to Windows code pages.</summary>
public static class RtfCodePages
{
    // RTF 1.9.1 \fcharsetN values (the Windows character-set constants) and the code page each implies.
    /// <summary>The Windows code page that an RTF <c>\fcharsetN</c> value implies.</summary>
    /// <param name="charset">The charset number.</param>
    /// <returns>The code page, or 0 when the charset implies none (the document code page applies).</returns>
    public static int FromCharset(int charset) => charset switch
    {
        0 => 1252,
        77 => 10000,
        128 => 932,
        129 => 949,
        130 => 1361,
        134 => 936,
        136 => 950,
        161 => 1253,
        162 => 1254,
        163 => 1258,
        177 => 1255,
        178 => 1256,
        186 => 1257,
        204 => 1251,
        222 => 874,
        238 => 1250,
        254 or 255 => 437,
        _ => 0,
    };

    // A decoder that replaces undecodable bytes with U+FFFD, or null when the platform has no such code page.
    /// <summary>A decoder for a code page that replaces undecodable bytes with U+FFFD.</summary>
    /// <param name="codePage">The Windows code page.</param>
    /// <returns>The encoding, or null when the platform has no such code page.</returns>
    public static Encoding? Decoder(int codePage)
    {
        var decoderFallback = new DecoderReplacementFallback("\uFFFD");
        try
        {
            switch (codePage)
            {
                case 65001:
                case 1200:
                case 1201:
                case 12000:
                case 12001:
                case 20127:
                case 28591:
                    return Encoding.GetEncoding(codePage, EncoderFallback.ReplacementFallback, decoderFallback);
                default:
                    return CodePagesEncodingProvider.Instance.GetEncoding(codePage, EncoderFallback.ReplacementFallback, decoderFallback);
            }
        }
        catch (ArgumentException)
        {
            return null; // no such code page on this platform
        }
    }
}
