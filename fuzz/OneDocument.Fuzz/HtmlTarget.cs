using System;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Documents;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// HTML: the bytes are imported as a page with small limits, the document is written as DOCX and as HTML (page and fragment), and the HTML is read
/// again. Only a typed <see cref="OfficeException"/> may come out.
/// </summary>
internal static class HtmlTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        var context = new OpenContext(new OfficeLimits { MaxXmlDepth = 64, MaxRecordCount = 100_000, MaxPartSize = 4 << 20, MaxAllocationBytes = 64 << 20 });
        try
        {
            using Document doc = Document.FromHtml(data.ToArray(), null, context);
            _ = doc.ExtractText();
            using var docx = new MemoryStream();
            doc.SaveAs(docx, OfficeFormat.Docx);
            string html = doc.ToHtml();
            _ = doc.ToHtml(new HtmlExportOptions { Fragment = true });
            using Document back = Document.FromHtml(html, new HtmlImportOptions { Sanitization = HtmlSanitization.KeepInert }, context);
            _ = back.ExtractText();
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
            "<!DOCTYPE html><html lang=en><head><title>T</title><style>.a{color:red;font-weight:bold}</style></head><body><h1 class=a>Head</h1><p style='margin-left:2em'>Para <b>b</b><i>i</i><u>u</u> <a href='#x'>l</a></p><p id=x>t</p></body></html>",
            "<ul><li>a<ol start=3 type=a><li>b</li></ol></li><li>c</li></ul>",
            "<table border=1><thead><tr><th colspan=2>H</th></tr></thead><tr><td rowspan=2>a</td><td>b</td></tr><tr><td>c</td></tr></table>",
            "<p onclick=x()>hi<script>alert(1)</script><a href='javascript:x'>j</a><img src=data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg== width=5></p>",
            "<pre>x  y\n\tz</pre><blockquote>q<br>r</blockquote><hr><div dir=rtl>\u05E9</div><form><input value=v></form><iframe>f</iframe>",
        ];
        for (int i = 0; i < seeds.Length; i++)
        {
            File.WriteAllBytes(Path.Combine(directory, "seed" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)), Encoding.UTF8.GetBytes(seeds[i]));
        }

        return 0;
    }
}
