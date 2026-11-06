using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Presentations.Odf;

/// <summary>
/// Reads OpenDocument presentations (ODP, OTP and flat FODP) into the presentation hub. The file is converted to an in-memory PPTX package, which
/// <see cref="Presentation"/> wraps like any other, and everything that did not map is reported to a <see cref="LossReport"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mapped: master pages (with their drawing content, background and text styles) to slide masters, one layout for each master page and page
/// layout a slide uses, title, subtitle, outline, date, footer and page-number frames to placeholders, slides with their names, hidden state and
/// notes pages, frames with text boxes, pictures and tables, rectangles, ellipses, lines, connectors, polygons, paths and custom shapes (as a preset
/// geometry where one is named, with its adjust values), groups, fills (solid, gradient, bitmap), lines, shadows, paragraphs with lists, bullets
/// and numbering, character formatting, hyperlinks, slide-number and date fields, and the document properties.
/// </para>
/// <para>
/// Reported as losses: slide transitions and animations, charts and other embedded objects (their preview picture is kept), formula-driven custom
/// geometries (drawn as rectangles), hatch fills, picture cropping, comments and links to other slides. Nothing is fetched: linked pictures are
/// dropped and a hyperlink target is only recorded.
/// </para>
/// <para>Repairs are defects with the codes <c>odp.*</c>, <c>odf.*</c> and <c>packaging.*</c> (see the defect catalogue).</para>
/// </remarks>
public static class OpenDocumentPresentation
{
    /// <summary>Converts an OpenDocument presentation to an in-memory PPTX package.</summary>
    /// <param name="source">The file's bytes; read at once and left open.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="loss">Receives what could not be carried over; null to discard it.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>A package that <see cref="Presentation.FromPackage(IOpcPackage, OpenContext?, bool)"/> accepts. The caller disposes it.</returns>
    /// <exception cref="OfficeFormatException">The file is not an OpenDocument package or flat document, or not a presentation (<c>odf.not-presentation</c>).</exception>
    /// <exception cref="OfficeUnsupportedException">The package is encrypted.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static OpcPackage ToPackage(IByteSource source, OpenContext? context = null, ILossSink? loss = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        context ??= new OpenContext();
        byte[] bytes = new byte[source.Length];
        int read = 0;
        while (read < bytes.Length)
        {
            int n = source.Read(read, bytes.AsSpan(read));
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        using var odf = RecursiveAI.OneDocument.Odf.OdfPackage.Open(ByteSource.FromMemory(bytes.AsMemory(0, read)), context);
        return new OdpImporter(odf, context, loss ?? new LossReport(), template: odf.MimeType == RecursiveAI.OneDocument.Odf.OdfMediaTypes.PresentationTemplate, cancellationToken).Convert();
    }
}
