using System;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Odf;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// OpenDocument. <c>odfpkg</c> opens the input as a package (zip or flat), walks its parts, styles and metadata and writes it again as a zip and as flat XML.
/// <c>odt</c> opens it as a text document, extracts the text and saves it as ODT and DOCX. Only a typed <see cref="OfficeException"/> may escape.
/// </summary>
internal static class OdfTarget
{
    public static void RunPackage(ReadOnlySpan<byte> data)
    {
        try
        {
            using OdfPackage pkg = OdfPackage.Open(ByteSource.FromMemory(data.ToArray()));
            _ = pkg.MimeType;
            _ = pkg.Manifest.Entries.Count;
            foreach (OdfEntry e in pkg.Entries)
            {
                if (!e.IsDirectory && pkg.TryReadEntry(e.Name, out ReadOnlyMemory<byte> bytes))
                {
                    _ = bytes.Length;
                }
            }

            _ = pkg.Metadata.Title;
            foreach (OdfStyle s in pkg.Styles.All)
            {
                foreach (OdfStyle _ in pkg.Styles.Chain(s))
                {
                }
            }

            if (!pkg.IsFlat)
            {
                using var zip = new MemoryStream();
                pkg.WriteTo(zip);
            }

            using var flat = new MemoryStream();
            pkg.WriteFlatTo(flat);
        }
        catch (OfficeException)
        {
        }
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        DocumentFormats.Register(FormatRegistry.Default);
        byte[] bytes = data.ToArray();
        try
        {
            using Document doc = Document.Open(new MemoryStream(bytes));
            _ = doc.ExtractText();
            using var odt = new MemoryStream();
            doc.SaveAs(odt, OfficeFormat.Odt);
            odt.Position = 0;
            using Document again = Document.Open(odt);
            _ = again.ExtractText();
            using var docx = new MemoryStream();
            doc.SaveAs(docx, OfficeFormat.Docx);
        }
        catch (OfficeException)
        {
        }
    }

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        const string head = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
            "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
            "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" " +
            "xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" " +
            "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.text\">";
        string[] bodies =
        [
            "<office:body><office:text><text:h text:outline-level=\"1\">Title</text:h><text:p>Hello <text:span text:style-name=\"T1\">world</text:span></text:p></office:text></office:body>",
            "<office:automatic-styles><style:style style:name=\"T1\" style:family=\"text\"><style:text-properties fo:font-weight=\"bold\"/></style:style></office:automatic-styles><office:body><office:text><text:p>A<text:tab/>B<text:s text:c=\"3\"/>C</text:p><text:list><text:list-item><text:p>x</text:p></text:list-item></text:list></office:text></office:body>",
            "<office:body><office:text><table:table><table:table-column table:number-columns-repeated=\"2\"/><table:table-row><table:table-cell table:number-columns-spanned=\"2\"><text:p>c</text:p></table:table-cell><table:covered-table-cell/></table:table-row></table:table><text:p><text:note text:note-class=\"footnote\"><text:note-body><text:p>n</text:p></text:note-body></text:note></text:p></office:text></office:body>",
        ];
        int n = 0;
        foreach (string body in bodies)
        {
            File.WriteAllBytes(Path.Combine(directory, "flat" + n++ + ".fodt"), Encoding.UTF8.GetBytes(head + body + "</office:document>"));
        }

        using OdfPackage pkg = OdfPackage.Create(OdfMediaTypes.Text);
        using var ms = new MemoryStream();
        pkg.WriteTo(ms);
        File.WriteAllBytes(Path.Combine(directory, "empty.odt"), ms.ToArray());
        return 0;
    }
}
