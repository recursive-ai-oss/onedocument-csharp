using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Html;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    /// <summary>
    /// Reads an HTML page into a new document. The page is parsed as a browser parses it (OneHtml), its cascaded styles are mapped to paragraph and
    /// character formatting and to named styles, and the result is an ordinary document that saves as DOCX or any other format the package writes.
    /// </summary>
    /// <param name="html">The page or fragment.</param>
    /// <param name="options">What is let in and how images are found; null for the defaults (<see cref="HtmlSanitization.Strict"/>, no external images).</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>
    /// The document, which has no <see cref="OfficeFile.FilePath"/>. <see cref="ImportLoss"/> lists everything that was not carried over: scripts, event
    /// handlers, unsafe links, forms, frames, external images and the like, each with a count. The page's text is never altered by the sanitising.
    /// </returns>
    /// <exception cref="OfficeLimitException">The page is larger than <see cref="OfficeLimits.MaxPartSize"/> or the parser's limits.</exception>
    /// <exception cref="OfficeFormatException">The import failed unexpectedly (<c>html.damaged</c>); HTML itself is never rejected, because any text parses.</exception>
    public static Document FromHtml(string html, HtmlImportOptions? options = null, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        // A byte-order mark makes the parser read the text as UTF-8, whatever a <meta charset> inside it says.
        byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(html)).ToArray();
        return FromHtml(bytes, options, context, cancellationToken);
    }

    /// <summary>Reads an HTML page from its bytes; the encoding comes from a byte-order mark, a <c>meta</c> tag or defaults to UTF-8, as in a browser.</summary>
    /// <param name="html">The page.</param>
    /// <param name="options">What is let in and how images are found; null for the defaults.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>The document; see <see cref="FromHtml(string, HtmlImportOptions?, OpenContext?, CancellationToken)"/>.</returns>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="OfficeFormatException">The import failed unexpectedly (<c>html.damaged</c>).</exception>
    public static Document FromHtml(ReadOnlyMemory<byte> html, HtmlImportOptions? options = null, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        context ??= new OpenContext();
        return ImportHtml(html, options ?? new HtmlImportOptions(), context, null, cancellationToken);
    }

    // The reader behind Document.Open for HTML and the registry entry for the format.
    internal static Document OpenHtml(IByteSource source, OpenContext context, string? filePath, CancellationToken cancellationToken)
    {
        try
        {
            ReadOnlyMemory<byte> bytes = Xml2003.Xml2003Input.ReadAll(source, context.Limits);
            return ImportHtml(bytes, new HtmlImportOptions(), context, filePath, cancellationToken);
        }
        finally
        {
            source.Dispose();
        }
    }

    private static Document ImportHtml(ReadOnlyMemory<byte> bytes, HtmlImportOptions options, OpenContext context, string? filePath, CancellationToken cancellationToken)
    {
        var loss = new LossReport();
        try
        {
            var reader = new HtmlReader(options, context, loss, cancellationToken);
            HtmlContent content = reader.Read(bytes);
            OpcPackage package = new HtmlToWml(content, context).Build();
            try
            {
                return new Document(package, context, "/word/document.xml", ownsPackage: true, OfficeFormat.Html, filePath) { ImportLoss = loss };
            }
            catch
            {
                package.Dispose();
                throw;
            }
        }
        catch (Exception ex) when (ex is not (OfficeException or OperationCanceledException))
        {
            // Reading must not fail with anything but a typed error, whatever the bytes held.
            throw new OfficeFormatException(DefectKind.Structure, "html.damaged", "The HTML could not be read: " + ex.GetType().Name + ".", DefectLocation.InRecord(filePath ?? "html", 0), ex);
        }
    }

    /// <summary>
    /// Writes the document as a clean HTML5 page (UTF-8) with its styles as CSS classes, lists, tables, linked end notes and pictures as data URIs.
    /// </summary>
    /// <param name="options">A fragment instead of a page, and where pictures go; null for a self-contained page.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The HTML. <see cref="ExportLoss"/> lists what it could not carry (headers and footers, comments, tracked deletions, floating layout, charts…).</returns>
    /// <exception cref="OfficeFormatException">The export failed unexpectedly (<c>html.export-failed</c>).</exception>
    public string ToHtml(HtmlExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        WriteHtml(buffer, options, cancellationToken);
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Writes the document as HTML to a stream, as <see cref="ToHtml(HtmlExportOptions?, CancellationToken)"/> does.</summary>
    /// <param name="destination">The stream.</param>
    /// <param name="options">A fragment instead of a page, and where pictures go; null for a self-contained page.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    public void SaveAsHtml(Stream destination, HtmlExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        WriteHtml(destination, options, cancellationToken);
    }

    /// <summary>
    /// Writes the document as an HTML file with a temporary file and a rename, so a failed export leaves an existing file as it was. Use this overload to
    /// give an <see cref="HtmlExportOptions.ImageSink"/> that stores pictures beside the file.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="options">A fragment instead of a page, and where pictures go; null for a self-contained page.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    public void SaveAsHtml(string path, HtmlExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string full = Path.GetFullPath(path);
        string temp = full + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                WriteHtml(fs, options, cancellationToken);
            }

            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    // The writer behind SaveAs(.., OfficeFormat.Html).
    internal void WriteHtml(Stream destination, HtmlExportOptions? options, CancellationToken cancellationToken)
    {
        CommitChanges();
        var loss = new LossReport();
        HtmlExporter.Write(this, destination, options, loss, cancellationToken);
        ExportLoss = loss;
    }
}
