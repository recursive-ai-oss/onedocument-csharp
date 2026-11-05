using System;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Equations;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Equations;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// Office Math: arbitrary XML read as an equation, written in linear form and laid out in text and display style. The tree reader and the
/// layout must never throw whatever the nesting, repetition or content; boxes must have finite extents.
/// </summary>
internal static class MathTarget
{
    private static FontLibrary? _fonts;

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1 || !MarkupDocument.TryParse(data[1..].ToArray(), out MarkupDocument? doc) || doc is null)
        {
            return;
        }

        MathNode? node = OmmlReader.Read(doc.Root, out _);
        if (node is null)
        {
            return;
        }

        _ = MathLinearizer.Linearize(node);
        _fonts ??= FontLibrary.CreateDefault();
        bool display = (data[0] & 1) != 0;
        MathBox box = MathLayouter.Layout(node, new MathLayoutOptions
        {
            Fonts = _fonts,
            FontSize = 10,
            Display = display,
            AvailableWidth = (data[0] & 2) != 0 ? 120 : double.PositiveInfinity,
        });
        if (!double.IsFinite(box.Width) || !double.IsFinite(box.Ascent) || !double.IsFinite(box.Descent))
        {
            throw new InvalidOperationException("non-finite equation box");
        }
    }

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        const string m = "xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"";
        string[] seeds =
        [
            "<m:oMath " + m + "><m:f><m:num><m:r><m:t>a+b</m:t></m:r></m:num><m:den><m:r><m:t>c</m:t></m:r></m:den></m:f></m:oMath>",
            "<m:oMathPara " + m + "><m:oMath><m:nary><m:naryPr><m:chr m:val=\"\u2211\"/></m:naryPr><m:sub><m:r><m:t>i=1</m:t></m:r></m:sub><m:sup><m:r><m:t>n</m:t></m:r></m:sup><m:e><m:r><m:t>i</m:t></m:r></m:e></m:nary></m:oMath></m:oMathPara>",
            "<m:oMath " + m + "><m:d><m:dPr><m:begChr m:val=\"[\"/></m:dPr><m:e><m:m><m:mr><m:e><m:r><m:t>a</m:t></m:r></m:e><m:e><m:r><m:t>b</m:t></m:r></m:e></m:mr></m:m></m:e></m:d><m:rad><m:deg><m:r><m:t>3</m:t></m:r></m:deg><m:e><m:r><m:t>x</m:t></m:r></m:e></m:rad></m:oMath>",
            "<m:oMath " + m + "><m:acc><m:e><m:r><m:t>x</m:t></m:r></m:e></m:acc><m:groupChr><m:e><m:r><m:t>abc</m:t></m:r></m:e></m:groupChr><m:eqArr><m:e><m:r><m:rPr><m:aln/></m:rPr><m:t>x=1+2+3+4+5+6+7+8+9+10+11+12</m:t></m:r></m:e></m:eqArr></m:oMath>",
        ];
        for (int i = 0; i < seeds.Length; i++)
        {
            File.WriteAllBytes(Path.Combine(directory, "seed" + i), [(byte)(i & 3), .. Encoding.UTF8.GetBytes(seeds[i])]);
        }

        return 0;
    }
}
