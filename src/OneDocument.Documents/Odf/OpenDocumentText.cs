using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Odf;

/// <summary>
/// Reads OpenDocument text (ODT, OTT and flat FODT) into the document hub. The file is converted to an in-memory DOCX package, which
/// <see cref="Document"/> wraps like any other, and everything that did not map is reported to a <see cref="LossReport"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mapped: paragraphs and headings with outline levels, character and paragraph formatting, common styles with inheritance (automatic styles
/// become direct formatting on their nearest common style), lists with their numbering, tables with spans, header rows and cell formatting,
/// page layouts and master pages as sections with headers and footers, footnotes and endnotes, bookmarks, hyperlinks, page-number and other
/// fields, the table of contents, comments, tracked insertions and deletions, pictures and text boxes, and the document properties.
/// </para>
/// <para>
/// Reported as losses: drawing shapes other than pictures and text boxes, embedded objects (their preview picture is kept), form controls,
/// ruby text, formatting revisions, page backgrounds, column breaks and page breaks after a paragraph. Nothing is fetched: linked pictures
/// are dropped and a hyperlink target is only recorded.
/// </para>
/// <para>Repairs are defects with the codes <c>odf.*</c> and <c>packaging.*</c> (see the defect catalogue).</para>
/// </remarks>
public static class OpenDocumentText
{
    /// <summary>Converts an OpenDocument text file to an in-memory DOCX package.</summary>
    /// <param name="source">The file's bytes; read at once and left open.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="loss">Receives what could not be carried over; null to discard it.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>A package that <see cref="Document.FromPackage(IOpcPackage, OpenContext?, bool)"/> accepts. The caller disposes it.</returns>
    /// <exception cref="OfficeFormatException">The file is not an OpenDocument package or flat document, or not a text document (<c>odf.not-text</c>).</exception>
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
        return new OdtImporter(odf, context, loss ?? new LossReport(), template: odf.MimeType == RecursiveAI.OneDocument.Odf.OdfMediaTypes.TextTemplate, cancellationToken).Convert();
    }
}
