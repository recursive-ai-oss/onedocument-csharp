using RecursiveAI.OneDocument.Encryption;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    // ECMA-376 Part 1 §11.3 and [MS-DOCX]: the main part's content type tells the four word-processing formats apart.
    private const string MainDocx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    private const string MainDocm = "application/vnd.ms-word.document.macroEnabled.main+xml";
    private const string MainDotx = "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml";
    private const string MainDotm = "application/vnd.ms-word.template.macroEnabledTemplate.main+xml";

    // [MS-OFFMACRO2]: the VBA project is a part of this content type, related to the main part.
    private const string VbaProjectContentType = "application/vnd.ms-office.vbaProject";

    /// <inheritdoc />
    public override OfficeKind Kind => OfficeKind.Document;

    /// <summary>
    /// Opens a word-processing document from a file. The format is found from the content, never the extension: DOCX, DOCM, DOTX, DOTM
    /// and Flat OPC are accepted.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The document, which the caller disposes. Only the package structure is read; the main part and every other part are read when first used, so a damaged main part raises at that first use, not here. Opening changes nothing.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">The content is another format (a workbook, a legacy binary file…), named in the exception.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static Document Open(string path, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return OpenSource(ByteSource.FromFile(path), context, Path.GetFullPath(path), cancellationToken);
    }

    /// <summary>Opens a word-processing document from a seekable stream.</summary>
    /// <param name="stream">The stream; parts are read from it lazily, so it must stay readable until the document is disposed.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="leaveOpen">True (the default) to leave the stream open when the document is disposed.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The document, which the caller disposes. It has no <see cref="OfficeFile.FilePath"/>, so use <c>SaveAs</c>.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">The content is another format, named in the exception.</exception>
    public static Document Open(Stream stream, OpenContext? context = null, bool leaveOpen = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return OpenSource(ByteSource.FromStream(stream, leaveOpen), context, null, cancellationToken);
    }

    /// <summary>Opens a word-processing document from a byte source, which the document then owns and disposes.</summary>
    /// <param name="source">The bytes; it must support synchronous reads (<see cref="IByteSource.CanReadSync"/>).</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The document, which the caller disposes.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">The content is another format, named in the exception.</exception>
    public static Document Open(IByteSource source, OpenContext? context = null, CancellationToken cancellationToken = default) =>
        OpenSource(source, context, null, cancellationToken);

    /// <summary>
    /// Creates a minimal valid document: one empty paragraph, a Normal style, and Letter-size pages with one-inch margins. The bytes
    /// written for it are the same on every run.
    /// </summary>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <returns>The document, which the caller disposes. It saves as DOCX and has no <see cref="OfficeFile.FilePath"/>.</returns>
    public static Document Create(OpenContext? context = null)
    {
        context ??= new OpenContext();
        OpcPackage package = OpcPackage.Create(context.Limits);
        try
        {
            package.SetRelationships(null,
            [
                new Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "word/document.xml"),
            ]);
            package.AddPart("/word/document.xml", MainDocx, Encoding.UTF8.GetBytes(NewDocumentXml));
            package.AddPart("/word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml", Encoding.UTF8.GetBytes(NewStylesXml));
            package.SetRelationships("/word/document.xml",
            [
                new Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml"),
            ]);
            return FromPackage(package, context, ownsPackage: true);
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    // ECMA-376 Part 1 §17.6.13 (pgSz) and §17.6.11 (pgMar): twentieths of a point; Letter is 8.5 x 11 in, margins one inch.
    private const string NewDocumentXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
        "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p/>" +
        "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>" +
        "</w:body></w:document>";

    private const string NewStylesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
        "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
        "<w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val=\"22\"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"160\" w:line=\"259\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>" +
        "</w:styles>";

    /// <summary>Gets the paragraphs of the body, in order (those inside tables and content controls included).</summary>
    public IEnumerable<Paragraph> Paragraphs => Body.Paragraphs();

    /// <summary>Gets the tables of the body, in order.</summary>
    public IEnumerable<Table> Tables
    {
        get
        {
            foreach (BlockElement block in Body.Blocks())
            {
                if (block is Table table)
                {
                    yield return table;
                }
            }
        }
    }

    /// <summary>Gets a value indicating whether the document carries a VBA project. It is never read or run.</summary>
    public bool HasVbaProject
    {
        get
        {
            foreach (IPackagePart part in Package.Parts)
            {
                if (string.Equals(part.ContentType, VbaProjectContentType, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Saves to the file the document was opened from, atomically: the new bytes are written to a temporary file beside it and renamed
    /// over it, so a failed or interrupted save leaves the original as it was. Edited parts are written, the others are copied as stored.
    /// </summary>
    /// <param name="cancellationToken">Cancels the save; the original stays as it was.</param>
    /// <exception cref="InvalidOperationException">The document was not opened from a path.</exception>
    /// <exception cref="OfficeException">The package cannot be written.</exception>
    public override void Save(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (FilePath is null)
        {
            base.Save(cancellationToken);
            return;
        }

        CommitChanges();
        if (Package is OpcPackage { FilePath: not null } opc)
        {
            // The package replaces its own file and goes on reading from the new one.
            opc.Save(cancellationToken);
            return;
        }

        base.Save(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks><c>.docx</c>, <c>.docm</c>, <c>.dotx</c> and <c>.dotm</c> name those formats and <c>.xml</c> names Flat OPC.</remarks>
    protected override OfficeFormat ResolveOutputFormat(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase)
            ? OfficeFormat.FlatOpcDocument
            : base.ResolveOutputFormat(path);
    }

    /// <inheritdoc />
    protected override void ExtractTextCore(TextWriter output, CancellationToken cancellationToken)
    {
        // The text of the document as it is now, edits included: write it out and read it back through the streaming extractor, so
        // the model and Office.ExtractText agree on every file.
        CommitChanges();
        // A document read from RTF, HTML or Markdown is a word-processing package in memory, so its text comes from the same extractor.
        OfficeFormat textFormat = Format is OfficeFormat.Rtf or OfficeFormat.Html or OfficeFormat.Markdown ? OfficeFormat.Docx : Format;
        if (!FormatRegistry.Default.TryGetTextExtractor(textFormat, out TextExtractionHandler? extractor))
        {
            foreach (Paragraph paragraph in Paragraphs)
            {
                output.WriteLine(paragraph.Text);
            }

            return;
        }

        using var buffer = new MemoryStream();
        Package.WriteTo(buffer, cancellationToken);
        using IByteSource source = ByteSource.FromMemory(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
        extractor(source, textFormat, Context, output, cancellationToken);
    }

    /// <inheritdoc />
    protected override void WriteCore(OfficeFormat format, Stream destination, CancellationToken cancellationToken)
    {
        if (IsWordFormat(format))
        {
            Write(format, destination, cancellationToken);
        }
        else if (format == OfficeFormat.Rtf)
        {
            WriteRtf(destination, cancellationToken);
        }
        else if (format == OfficeFormat.Html)
        {
            WriteHtml(destination, null, cancellationToken);
        }
        else if (format == OfficeFormat.Markdown)
        {
            SaveMarkdown(destination, cancellationToken);
        }
        else
        {
            base.WriteCore(format, destination, cancellationToken);
        }
    }

    // The writer behind SaveAs(path|stream, format).
    internal void Write(OfficeFormat format, Stream destination, CancellationToken cancellationToken)
    {
        CommitChanges();
        if (format == OfficeFormat.FlatOpcDocument)
        {
            if (Package is not OpcPackage flat)
            {
                throw new FormatNotAvailableException(format, "write this package as");
            }

            flat.WriteFlatOpcTo(destination, cancellationToken);
            return;
        }

        string target = MainContentTypeOf(format);
        if (HasVbaProject && format is OfficeFormat.Docx or OfficeFormat.Dotx)
        {
            throw new OfficeUnsupportedException(
                "Saving a document that has a VBA project as " + format + " (the macro-free format would silently drop the project; use Docm or Dotm)",
                DefectLocation.InPart(MainPartName));
        }

        IPackagePart main = Package.FindPart(MainPartName)!;
        if (Package is not OpcPackage opc)
        {
            Package.WriteTo(destination, cancellationToken);
            return;
        }

        // A copy in another word-processing format differs from this document only in the main part's content type; set it for the
        // write and put it back, so saving a copy never changes the document.
        string original = main.ContentType;
        bool retype = !string.Equals(original, target, StringComparison.OrdinalIgnoreCase);
        try
        {
            if (retype)
            {
                opc.SetPartContentType(MainPartName, target);
            }

            opc.WriteZipTo(destination, cancellationToken);
        }
        finally
        {
            if (retype)
            {
                opc.SetPartContentType(MainPartName, original);
            }
        }
    }

    private static string MainContentTypeOf(OfficeFormat format) => format switch
    {
        OfficeFormat.Docx => MainDocx,
        OfficeFormat.Docm => MainDocm,
        OfficeFormat.Dotx => MainDotx,
        OfficeFormat.Dotm => MainDotm,
        _ => throw new FormatNotAvailableException(format, "write a word-processing document as"),
    };

    private static OfficeFormat FormatOf(IOpcPackage package, string mainPartName)
    {
        if (package is OpcPackage { IsFlatOpc: true })
        {
            return OfficeFormat.FlatOpcDocument;
        }

        string type = package.FindPart(mainPartName)?.ContentType ?? string.Empty;
        if (string.Equals(type, MainDocm, StringComparison.OrdinalIgnoreCase))
        {
            return OfficeFormat.Docm;
        }

        if (string.Equals(type, MainDotx, StringComparison.OrdinalIgnoreCase))
        {
            return OfficeFormat.Dotx;
        }

        return string.Equals(type, MainDotm, StringComparison.OrdinalIgnoreCase) ? OfficeFormat.Dotm : OfficeFormat.Docx;
    }

    internal static bool IsWordFormat(OfficeFormat format) =>
        format is OfficeFormat.Docx or OfficeFormat.Docm or OfficeFormat.Dotx or OfficeFormat.Dotm or OfficeFormat.FlatOpcDocument;

    // The reader registered for the word-processing formats; also what Document.Open runs.
    internal static Document OpenSource(IByteSource source, OpenContext? context, string? filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        context ??= new OpenContext();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            OfficeFormat sniffed = FormatSniffer.Detect(source, context.Limits);
            if (sniffed == OfficeFormat.Unknown)
            {
                throw new OfficeFormatException(DefectKind.Structure, "format.unrecognised", "The content is not a format OneDocument knows.");
            }

            if (sniffed == OfficeFormat.EncryptedPackage)
            {
                // Decrypt to the package inside and open that as whatever it is. It has no file path, so Save() is not available.
                IByteSource plain = ByteSource.FromMemory(PackageEncryption.DecryptCompoundFile(source, context));
                source.Dispose();
                return OpenSource(plain, context, null, cancellationToken);
            }

            if (sniffed == OfficeFormat.Word2003Xml)
            {
                // Converted at once into an in-memory DOCX package, so the file itself is not needed afterwards.
                Document converted = Xml2003.WordMl2003.Open(source, context, cancellationToken);
                source.Dispose();
                return converted;
            }

            if (sniffed == OfficeFormat.Doc)
            {
                return OpenBinary(source, context);
            }

            if (sniffed == OfficeFormat.Rtf)
            {
                return OpenRtf(source, context, filePath, cancellationToken);
            }

            if (sniffed == OfficeFormat.Html)
            {
                return OpenHtml(source, context, filePath, cancellationToken);
            }

            if (!IsWordFormat(sniffed))
            {
                throw new FormatNotAvailableException(sniffed, "open as a word-processing document:");
            }

            // The package owns the source from here on, and disposes it if the open fails.
            OpcPackage package = filePath is null ? OpcPackage.Open(source, context) : OpcPackage.Open(source, filePath, context);
            try
            {
                return FromPackage(package, context, ownsPackage: true);
            }
            catch
            {
                package.Dispose();
                throw;
            }
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }
}
