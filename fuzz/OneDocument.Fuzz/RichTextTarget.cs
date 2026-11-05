using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The HTML and Markdown converters behind the document template's <c>html</c> and <c>markdown</c> tags. The first byte picks the tag; the rest
/// is the value. Rendering must end with a document that saves and opens again, and only a typed <see cref="OfficeException"/> may escape.
/// </summary>
internal static class RichTextTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
        {
            return;
        }

        DocumentFormats.Register(FormatRegistry.Default);
        string tag = (data[0] & 1) == 0 ? "html" : "markdown";
        string value = Encoding.UTF8.GetString(data[1..]);
        try
        {
            using Document doc = Document.Create();
            doc.Body.AddParagraph("{% " + tag + " v %}");
            Template.Render(doc, new Dictionary<string, object?> { ["v"] = value });
            using var ms = new MemoryStream();
            doc.SaveAs(ms, OfficeFormat.Docx);
            ms.Position = 0;
            using Document again = Document.Open(ms);
            _ = again.ExtractText();
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
            "<h1>Title</h1><p>Hi <b>there</b> <i>x</i> <u>u</u> <a href='https://example.com'>l</a></p><ul><li>a</li><li>b<ol><li>c</li></ol></li></ul>",
            "<p onclick='x()'>t<script>alert(1)</script><a href='javascript:x'>n</a><table><tr><td>c</td></tr></table><img src=x>",
            "# Title\n\nSome **bold** and *italic* [link](https://example.com) `code`\n\n- one\n- two\n  - nested\n\n1. first\n2. second\n\n> quote\n\n```\ncode\n```",
            "***a** b* __c__ ![i](x) <b>raw</b> [a](<b c>) \\* * * *\n---\n====",
        ];
        for (int i = 0; i < seeds.Length; i++)
        {
            File.WriteAllBytes(Path.Combine(directory, "seed" + i), [(byte)(i < 2 ? 0 : 1), .. Encoding.UTF8.GetBytes(seeds[i])]);
        }

        return 0;
    }
}
