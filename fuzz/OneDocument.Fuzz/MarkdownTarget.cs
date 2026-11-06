using System;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Markdown;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// Markdown: the bytes are read as text and parsed (nothing may throw), then opened as a document with small limits, written back as Markdown and as DOCX, and read again.
/// Only a typed <see cref="OfficeException"/> may escape. The parser is also run with the CommonMark options, which do not know the extensions.
/// </summary>
internal static class MarkdownTarget
{
    public static void RunParse(ReadOnlySpan<byte> data)
    {
        string text = new UTF8Encoding(false, false).GetString(data);
        MarkdownParseResult gfm = MarkdownParser.Parse(text, new MarkdownParserOptions { MaxDepth = 32 });
        MarkdownParseResult plain = MarkdownParser.Parse(text, new MarkdownParserOptions { Tables = false, Strikethrough = false, TaskLists = false, Autolinks = false, Footnotes = false, MaxDepth = 32 });
        if (gfm.Root.Kind != MdKind.Document || plain.Root.Kind != MdKind.Document)
        {
            throw new InvalidOperationException("no document node");
        }
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        var context = new OpenContext(new OfficeLimits { MaxRecursionDepth = 32, MaxPartSize = 4 << 20, MaxAllocationBytes = 64 << 20 });
        try
        {
            using Document doc = Document.OpenMarkdown(new MemoryStream(data.ToArray()), null, context);
            _ = doc.ExtractText();
            string md = doc.ToMarkdown();
            using var docx = new MemoryStream();
            doc.SaveAs(docx, OfficeFormat.Docx);
            using Document again = Document.OpenMarkdown(new MemoryStream(Encoding.UTF8.GetBytes(md)), null, context);
            _ = again.ToMarkdown();
            using Document viaDocx = Document.Open(new MemoryStream(docx.ToArray()));
            _ = viaDocx.ToMarkdown();
        }
        catch (OfficeException)
        {
        }
    }

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        string[] seeds =
        [
            "# Title\n\nA *b* **c** ~~d~~ `e` [f](https://g.h \"t\") ![i](data:image/png;base64,AAAA) <https://j.k> www.l.com\n",
            "- a\n  - b\n    - c\n\n1. x\n2. y\n\n- [ ] t\n- [x] u\n\n> q\n> > r\n",
            "| a | b |\n|:-|-:|\n| 1 | 2 \\| 3 |\n| 4 |\n",
            "```cs\nx\n```\n\n    indented\n\n---\n\n***\n\n<div>\nraw\n</div>\n\n<!-- c -->\n",
            "[a]: /u \"t\"\n\n[a] [b][a] [c][] ![d][a]\n\nfoo[^1]\n\n[^1]: note\n    more\n",
            "---\ntitle: T\nauthor: A\n---\n\nbody\n",
            "Setext\n===\n\nSetext 2\n---\n\n&amp; &#35; &#x1F600; &nosuch; \\* \\\n",
        ];
        for (int i = 0; i < seeds.Length; i++)
        {
            File.WriteAllText(Path.Combine(directory, "seed" + i), seeds[i]);
        }

        return 0;
    }
}
