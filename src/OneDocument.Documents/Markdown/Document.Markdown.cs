using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Markdown;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    private byte[]? _mdSource;
    private bool _mdPristine;

    // Markdown read and saved without edits writes the bytes it was read from.
    private void NoteEditsForMarkdown()
    {
        if (_mdPristine && HasUncommittedChanges)
        {
            _mdPristine = false;
        }
    }

    /// <summary>
    /// Opens a Markdown file (CommonMark 0.31.2 with the GitHub Flavored Markdown extensions: tables, task lists, strike-through, autolinks
    /// and footnotes) as a document. The content is never recognised as Markdown on its own: the caller names the format by calling this.
    /// </summary>
    /// <param name="path">The file; UTF-8 (a byte-order mark is skipped) or UTF-16 with a byte-order mark.</param>
    /// <param name="options">Picture resolver and front matter choice; null for the defaults (no pictures are read from outside the text).</param>
    /// <param name="context">Limits, defect sink; null for the defaults. The size limit is <see cref="OfficeLimits.MaxPartSize"/> and the nesting limit is <see cref="OfficeLimits.MaxRecursionDepth"/>.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>
    /// The document, which the caller disposes. It saves as DOCX; <see cref="OfficeFile.Format"/> is <see cref="OfficeFormat.Markdown"/>. <see cref="ImportLoss"/> lists what
    /// Markdown has no counterpart for in the document (raw HTML until the HTML importer takes it, pictures that were not given, code languages…).
    /// </returns>
    /// <exception cref="OfficeLimitException">The file is over the size limit.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static Document OpenMarkdown(string path, MarkdownImportOptions? options = null, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return OpenMarkdown(ByteSource.FromFile(path), context ?? new OpenContext(), Path.GetFullPath(path), options, cancellationToken);
    }

    /// <summary>Opens Markdown from a stream, which is read to its end and left open. See <see cref="OpenMarkdown(string, MarkdownImportOptions?, OpenContext?, CancellationToken)"/>.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="options">Picture resolver and front matter choice; null for the defaults.</param>
    /// <param name="context">Limits, defect sink; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>The document, which the caller disposes.</returns>
    public static Document OpenMarkdown(Stream stream, MarkdownImportOptions? options = null, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return OpenMarkdown(ByteSource.FromStream(stream, leaveOpen: true), context ?? new OpenContext(), null, options, cancellationToken);
    }

    /// <summary>Reads Markdown given as text. See <see cref="OpenMarkdown(string, MarkdownImportOptions?, OpenContext?, CancellationToken)"/>.</summary>
    /// <param name="markdown">The Markdown.</param>
    /// <param name="options">Picture resolver and front matter choice; null for the defaults.</param>
    /// <param name="context">Limits, defect sink; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>The document, which the caller disposes.</returns>
    public static Document ParseMarkdown(string markdown, MarkdownImportOptions? options = null, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return OpenMarkdown(ByteSource.FromMemory(Encoding.UTF8.GetBytes(markdown)), context ?? new OpenContext(), null, options, cancellationToken);
    }

    /// <summary>
    /// Opens a file as a word-processing document when the caller says what format it is. <see cref="OfficeFormat.Markdown"/> is read as Markdown; any other
    /// format is found from the content as <see cref="Open(string, OpenContext?, CancellationToken)"/> does and must be one this class reads.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="format">The format the caller says the file is.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The document, which the caller disposes.</returns>
    public static Document Open(string path, OfficeFormat format, OpenContext? context = null, CancellationToken cancellationToken = default) =>
        format == OfficeFormat.Markdown ? OpenMarkdown(path, null, context, cancellationToken) : Open(path, context, cancellationToken);

    // The reader behind the registry entry for Markdown.
    internal static Document OpenMarkdown(IByteSource source, OpenContext context, string? filePath, MarkdownImportOptions? options, CancellationToken cancellationToken)
    {
        string location = filePath ?? "markdown";
        try
        {
            long length = source.Length;
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), context.Limits.MaxPartSize, length, DefectLocation.InRecord(location, 0));
            byte[] bytes = new byte[length];
            int read = 0;
            while (read < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int n = source.Read(read, bytes.AsSpan(read));
                if (n <= 0)
                {
                    break;
                }

                read += n;
            }

            if (read < bytes.Length)
            {
                bytes = bytes[..read];
            }

            string text = MarkdownText.Decode(bytes, context, location);
            var loss = new LossReport();
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            options ??= new MarkdownImportOptions();
            if (options.FrontMatter)
            {
                text = MarkdownText.SplitFrontMatter(text, properties, loss);
            }

            var parserOptions = new MarkdownParserOptions { MaxDepth = Math.Max(8, context.Limits.MaxRecursionDepth) };
            MarkdownParseResult parsed = MarkdownParser.Parse(text, parserOptions);
            foreach ((string code, string message, int line) in parsed.Issues)
            {
                context.Defects.Report(new Defect(DefectKind.Limit, code, DefectSeverity.Repaired, message, DefectLocation.AtText(MarkdownText.LineOffset(text, line))));
            }

            var importer = new MarkdownImporter(parsed.Root, options, context, loss, properties);
            OpcPackage package = importer.BuildPackage(cancellationToken);
            try
            {
                return new Document(package, context, "/word/document.xml", ownsPackage: true, OfficeFormat.Markdown, filePath)
                {
                    _mdSource = bytes,
                    _mdPristine = true,
                    ImportLoss = loss,
                };
            }
            catch
            {
                package.Dispose();
                throw;
            }
        }
        catch (Exception ex) when (ex is not (OfficeException or OperationCanceledException or IOException))
        {
            // Reading must not fail with anything but a typed error, whatever the text held.
            throw new OfficeFormatException(DefectKind.Structure, "markdown.damaged", "The Markdown could not be converted: " + ex.GetType().Name + ".", DefectLocation.InRecord(location, 0), ex);
        }
        finally
        {
            source.Dispose();
        }
    }

    /// <summary>
    /// Writes the document as GitHub Flavored Markdown text. Headings come from heading styles or outline levels, lists from numbering, tables, code, quotes,
    /// links, pictures (through <see cref="MarkdownExportOptions.ImageSink"/>) and footnotes; <see cref="ExportLoss"/> lists what Markdown cannot say.
    /// </summary>
    /// <param name="options">Picture sink; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The Markdown.</returns>
    public string ToMarkdown(MarkdownExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        WriteMarkdown(buffer, options, cancellationToken);
        return new UTF8Encoding(false).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Writes the document as Markdown in UTF-8 without a byte-order mark. See <see cref="ToMarkdown"/>.</summary>
    /// <param name="destination">The stream; it is left open.</param>
    /// <param name="options">Picture sink; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    public void WriteMarkdown(Stream destination, MarkdownExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CommitChanges();
        var loss = new LossReport();
        string text = MarkdownExporter.Export(this, options ?? new MarkdownExportOptions(), loss, cancellationToken);
        destination.Write(new UTF8Encoding(false).GetBytes(text));
        ExportLoss = loss;
    }

    // The writer behind SaveAs and Save for Markdown: a document read from Markdown and not edited writes the bytes it was read from.
    internal void SaveMarkdown(Stream destination, CancellationToken cancellationToken)
    {
        CommitChanges();
        if (_mdPristine && _mdSource is not null)
        {
            destination.Write(_mdSource);
            ExportLoss = new LossReport();
            return;
        }

        WriteMarkdown(destination, null, cancellationToken);
    }
}
