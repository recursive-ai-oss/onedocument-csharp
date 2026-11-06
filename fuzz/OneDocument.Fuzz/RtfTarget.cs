using System;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Rtf;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// RTF: the bytes are read as a group tree (nothing may throw), then opened as a document with small limits, its text extracted, and the
/// document written as DOCX and as RTF and read again. Only a typed <see cref="OfficeException"/> may come out.
/// </summary>
internal static class RtfTarget
{
    public static void RunTree(ReadOnlySpan<byte> data)
    {
        byte[] bytes = data.ToArray();
        RtfTree tree = RtfTree.Parse(bytes, new RtfReaderOptions { MaxGroupDepth = 64 }, 100_000);
        if (tree.Root.End < tree.Root.Offset || tree.Root.End > bytes.Length)
        {
            throw new InvalidOperationException("group extent outside the input");
        }
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        var context = new OpenContext(new OfficeLimits { MaxRecursionDepth = 64, MaxRecordCount = 100_000, MaxPartSize = 4 << 20, MaxAllocationBytes = 64 << 20 });
        try
        {
            using Document doc = Document.Open(new MemoryStream(data.ToArray()), context);
            _ = doc.ExtractText();
            using var docx = new MemoryStream();
            doc.SaveAs(docx, OfficeFormat.Docx);
            using var rtf = new MemoryStream();
            doc.SaveAs(rtf, OfficeFormat.Rtf);
            using var again = new MemoryStream();
            using Document asDocx = Document.Open(new MemoryStream(docx.ToArray()));
            asDocx.SaveAs(again, OfficeFormat.Rtf); // the exporter, which an untouched RTF document does not use
            using Document back = Document.Open(new MemoryStream(again.ToArray()));
            _ = back.ExtractText();
        }
        catch (OfficeException)
        {
        }
    }

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        const string head = "{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\froman Times New Roman;}{\\f1\\fnil\\fcharset2 Symbol;}}{\\colortbl;\\red255\\green0\\blue0;}" +
            "{\\stylesheet{\\s0\\snext0 Normal;}{\\s1\\sbasedon0\\snext0\\b\\fs32 heading 1;}}";
        string[] seeds =
        [
            head + "\\pard\\plain\\s1 Title\\par\\pard Plain {\\b bold \\i both}{\\cf1\\ul red} caf\\'e9 \\u8364?\\par}",
            head + "\\trowd\\trgaph108\\clvmgf\\cellx3000\\clmgf\\cellx6000\\clmrg\\cellx9000\\pard\\intbl A\\cell B\\cell \\cell\\row\\pard x\\par}",
            head + "\\pard\\intbl\\itap2 in\\nestcell{\\*\\nesttableprops\\trowd\\cellx4000\\nestrow}{\\nonesttables\\par}\\cell\\row}",
            head + "{\\*\\listtable{\\list\\listtemplateid1{\\listlevel\\levelnfc0\\levelstartat1{\\leveltext\\'02\\'00.;}{\\levelnumbers\\'01;}\\fi-360\\li720}\\listid7}}" +
                "{\\*\\listoverridetable{\\listoverride\\listid7\\listoverridecount0\\ls1}}\\pard\\ls1\\ilvl0 one\\par}",
            head + "\\sectd{\\header \\pard h\\par}{\\footer \\pard f\\par}\\pard text{\\super\\chftn {\\footnote \\pard {\\super\\chftn } note\\par}}\\sect\\sectd\\pard two\\par}",
            head + "\\pard {\\field{\\*\\fldinst {HYPERLINK \"http://example.com/\"}}{\\fldrslt {\\ul link}}}{\\*\\bkmkstart b}x{\\*\\bkmkend b}" +
                "{\\*\\atrfstart 1}t{\\*\\atrfend 1}{\\*\\atnid A}{\\*\\atnauthor B}\\chatn{\\*\\annotation{\\*\\atnref 1}\\pard c\\par}\\par}",
            head + "\\pard {\\pict\\pngblip\\picw1\\pich1\\picwgoal100\\pichgoal100 89504e470d0a1a0a}{\\shp{\\*\\shpinst\\shpleft0{\\sp{\\sn pib}{\\sv {\\pict\\jpegblip ffd8ffd9}}}{\\shptxt \\pard in box\\par}}}\\par}",
            head + "\\pard {\\*\\revtbl{Unknown;}{A;}}{\\revised\\revauth1\\revdttm1234567890 new}{\\deleted\\revauthdel1 old}\\par}",
            head + "\\pard {\\mmath{\\*\\moMath\\f0{\\mf{\\mfPr{\\mctrlPr}{\\mtype bar}}{\\mnum{\\mr a}}{\\mden{\\mr b}}}}} \\par}",
            head + "\\pard {\\bin5 ab{}c}x\\upr{\\ud{\\uc0 \\u1234}}\\par}",
        ];
        for (int i = 0; i < seeds.Length; i++)
        {
            File.WriteAllBytes(Path.Combine(directory, "seed" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)), Encoding.Latin1.GetBytes(seeds[i]));
        }

        return 0;
    }
}
