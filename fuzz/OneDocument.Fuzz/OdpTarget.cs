using System;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Presentations;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// OpenDocument presentations. <c>odp</c> opens the input as a presentation, walks the slides and their shapes, extracts the text and saves it as
/// ODP, flat ODP and PPTX, then opens the ODP again. Only a typed <see cref="OfficeException"/> may escape.
/// </summary>
internal static class OdpTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        PresentationFormats.Register(FormatRegistry.Default);
        byte[] bytes = data.ToArray();
        try
        {
            using Presentation deck = Presentation.Open(new MemoryStream(bytes));
            _ = deck.ExtractText();
            foreach (Slide slide in deck.Slides)
            {
                foreach (Shape shape in slide.AllShapes())
                {
                    _ = shape.Text;
                    _ = shape.ResolveBounds();
                }

                _ = slide.Notes?.NotesText;
            }

            using var odp = new MemoryStream();
            deck.SaveAs(odp, OfficeFormat.Odp);
            odp.Position = 0;
            using Presentation again = Presentation.Open(odp);
            _ = again.ExtractText();
            using var flat = new MemoryStream();
            again.SaveAs(flat, OfficeFormat.Fodp);
            using var pptx = new MemoryStream();
            deck.SaveAs(pptx, OfficeFormat.Pptx);
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
            "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" xmlns:presentation=\"urn:oasis:names:tc:opendocument:xmlns:presentation:1.0\" " +
            "office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.presentation\">";
        const string styles = "<office:automatic-styles><style:page-layout style:name=\"PM1\"><style:page-layout-properties fo:page-width=\"28cm\" fo:page-height=\"15.75cm\"/></style:page-layout>" +
            "<style:style style:name=\"gr1\" style:family=\"graphic\"><style:graphic-properties draw:fill=\"solid\" draw:fill-color=\"#4472c4\" draw:stroke=\"none\"/></style:style>" +
            "<style:style style:name=\"T1\" style:family=\"text\"><style:text-properties fo:font-weight=\"bold\" fo:font-size=\"24pt\" fo:color=\"#ff0000\"/></style:style>" +
            "<text:list-style style:name=\"L1\"><text:list-level-style-bullet text:level=\"1\" text:bullet-char=\"-\"/></text:list-style></office:automatic-styles>" +
            "<office:master-styles><style:master-page style:name=\"M\" style:page-layout-name=\"PM1\"/></office:master-styles>";
        string[] bodies =
        [
            "<draw:page draw:name=\"One\" draw:master-page-name=\"M\"><draw:frame presentation:class=\"title\" svg:x=\"1cm\" svg:y=\"1cm\" svg:width=\"20cm\" svg:height=\"3cm\"><draw:text-box><text:p>Title <text:span text:style-name=\"T1\">bold</text:span></text:p></draw:text-box></draw:frame>" +
            "<draw:frame presentation:class=\"outline\" svg:x=\"1cm\" svg:y=\"5cm\" svg:width=\"20cm\" svg:height=\"8cm\"><draw:text-box><text:list text:style-name=\"L1\"><text:list-item><text:p>point</text:p><text:list><text:list-item><text:p>sub</text:p></text:list-item></text:list></text:list-item></text:list></draw:text-box></draw:frame></draw:page>",
            "<draw:page draw:name=\"Two\" draw:master-page-name=\"M\"><draw:custom-shape draw:style-name=\"gr1\" svg:x=\"2cm\" svg:y=\"2cm\" svg:width=\"5cm\" svg:height=\"3cm\" draw:transform=\"rotate (0.5) translate (3cm 3cm)\"><text:p>shape</text:p><draw:enhanced-geometry draw:type=\"ooxml-rightArrow\" draw:modifiers=\"50000 50000\"/></draw:custom-shape>" +
            "<draw:path svg:d=\"M0 0 L10 0 C10 5 5 10 0 10 Z\" svg:viewBox=\"0 0 10 10\" svg:x=\"8cm\" svg:y=\"2cm\" svg:width=\"3cm\" svg:height=\"3cm\"/><draw:line svg:x1=\"1cm\" svg:y1=\"1cm\" svg:x2=\"9cm\" svg:y2=\"9cm\"/>" +
            "<draw:g><draw:rect svg:x=\"1cm\" svg:y=\"8cm\" svg:width=\"2cm\" svg:height=\"2cm\"/><draw:ellipse svg:x=\"4cm\" svg:y=\"8cm\" svg:width=\"2cm\" svg:height=\"2cm\"/></draw:g>" +
            "<draw:frame svg:x=\"12cm\" svg:y=\"6cm\" svg:width=\"9cm\" svg:height=\"3cm\"><table:table><table:table-column table:number-columns-repeated=\"2\"/><table:table-row><table:table-cell table:number-columns-spanned=\"2\"><text:p>c</text:p></table:table-cell><table:covered-table-cell/></table:table-row></table:table></draw:frame>" +
            "<presentation:notes><draw:frame presentation:class=\"notes\" svg:x=\"1cm\" svg:y=\"1cm\" svg:width=\"5cm\" svg:height=\"2cm\"><draw:text-box><text:p>note</text:p></draw:text-box></draw:frame></presentation:notes></draw:page>",
        ];
        int n = 0;
        foreach (string body in bodies)
        {
            File.WriteAllBytes(Path.Combine(directory, "flat" + n++ + ".fodp"), Encoding.UTF8.GetBytes(head + styles + "<office:body><office:presentation>" + body + "</office:presentation></office:body></office:document>"));
        }

        return 0;
    }
}
