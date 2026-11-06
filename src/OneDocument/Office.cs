using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;

namespace RecursiveAI.OneDocument;

/// <summary>
/// The happy path: one-liners over the model that work on any format whose kind's package is referenced. Each takes the input
/// (a path or a source), identifies it from its content (never its extension), and hands it to the reader registered for that format
/// in <see cref="FormatRegistry.Default"/>. Where the kind's package is absent or does not implement the format yet, they raise
/// <see cref="FormatNotAvailableException"/>.
/// </summary>
/// <remarks>
/// The optional <see cref="OpenContext"/> is the same object the layers below take: limits, defect sink, password. Pass nothing for the
/// defaults (default limits, defects discarded into a private collection, no password).
/// </remarks>
public static class Office
{
    /// <summary>Gets the registry these statics dispatch through.</summary>
    public static FormatRegistry Registry => FormatRegistry.Default;

    /// <summary>Identifies a file by its content.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The format, or <see cref="OfficeFormat.Unknown"/>.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="OfficeLimitException">The container asks for more than the default limits allow.</exception>
    public static OfficeFormat Detect(string path) => FormatSniffer.Detect(path);

    /// <summary>Opens a file as the model its content calls for.</summary>
    /// <param name="path">The path.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The opened file, which the caller disposes. Its <see cref="OfficeFile.FilePath"/> is the path.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">No reader is registered for the format.</exception>
    /// <exception cref="OfficeException">Any other typed failure of the input (limits, encryption, unsupported content).</exception>
    public static OfficeFile Open(string path, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        IByteSource source = ByteSource.FromFile(path);
        try
        {
            return Open(source, context, path, cancellationToken);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>Opens a source as the model its content calls for. The returned file owns the source and disposes it.</summary>
    /// <param name="source">The input.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The opened file, which the caller disposes.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">No reader is registered for the format.</exception>
    public static OfficeFile Open(IByteSource source, OpenContext? context = null, CancellationToken cancellationToken = default) =>
        Open(source, context, null, cancellationToken);

    /// <summary>
    /// Opens a file as a format the caller names, for the formats that are never recognised from content: <see cref="OfficeFormat.Markdown"/>. Any format with a
    /// reader registered may be named; the content is not checked against it.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="format">The format to read the file as.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The opened file, which the caller disposes.</returns>
    /// <exception cref="ArgumentException"><paramref name="format"/> is <see cref="OfficeFormat.Unknown"/>.</exception>
    /// <exception cref="FormatNotAvailableException">No reader is registered for the format.</exception>
    public static OfficeFile Open(string path, OfficeFormat format, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        if (format == OfficeFormat.Unknown)
        {
            throw new ArgumentException("A format must be named.", nameof(format));
        }

        IByteSource source = ByteSource.FromFile(path);
        try
        {
            return FormatRegistry.Default.GetReader(format)(source, format, context ?? new OpenContext(), path, cancellationToken);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private static OfficeFile Open(IByteSource source, OpenContext? context, string? filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        context ??= new OpenContext();
        OfficeFormat format = FormatSniffer.Detect(source, context.Limits);
        if (format == OfficeFormat.Unknown)
        {
            throw new OfficeFormatException(DefectKind.Structure, "format.unrecognised", "The content is not a format OneDocument knows.");
        }

        return FormatRegistry.Default.GetReader(format)(source, format, context, filePath, cancellationToken);
    }

    /// <summary>Checks a file against Office's accessibility rules; nothing is edited or saved.</summary>
    /// <param name="path">The path.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The findings.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">No reader is registered for the format.</exception>
    public static Accessibility.AccessibilityReport CheckAccessibility(string path, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        using OfficeFile file = Open(path, context, cancellationToken);
        return file.CheckAccessibility(cancellationToken);
    }

    /// <summary>Extracts the text of a file, in reading order.</summary>
    /// <param name="path">The path.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the extraction.</param>
    /// <returns>The text.</returns>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">No reader or extractor is registered for the format.</exception>
    public static string ExtractText(string path, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        ExtractText(path, writer, context, cancellationToken);
        return writer.ToString();
    }

    /// <summary>Extracts the text of a file to a writer.</summary>
    /// <param name="path">The path.</param>
    /// <param name="output">Where the text goes.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the extraction.</param>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">No reader or extractor is registered for the format.</exception>
    public static void ExtractText(string path, TextWriter output, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        using IByteSource source = ByteSource.FromFile(path);
        ExtractText(source, output, context, cancellationToken);
    }

    /// <summary>Extracts the text of a source to a writer. The source is left open.</summary>
    /// <param name="source">The input.</param>
    /// <param name="output">Where the text goes.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the extraction.</param>
    /// <exception cref="OfficeFormatException">The content is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">No reader or extractor is registered for the format.</exception>
    public static void ExtractText(IByteSource source, TextWriter output, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        context ??= new OpenContext();
        OfficeFormat format = FormatSniffer.Detect(source, context.Limits);
        if (format == OfficeFormat.Unknown)
        {
            throw new OfficeFormatException(DefectKind.Structure, "format.unrecognised", "The content is not a format OneDocument knows.");
        }

        FormatRegistry registry = FormatRegistry.Default;
        if (registry.TryGetTextExtractor(format, out TextExtractionHandler? extractor))
        {
            extractor(source, format, context, output, cancellationToken);
            return;
        }

        // No cheaper path for this format: open the model and read its text. The file would own the source, so give it its own.
        if (!registry.TryGetReader(format, out OpenHandler? reader))
        {
            throw new FormatNotAvailableException(format, "extract text from");
        }

        using OfficeFile file = reader(new BorrowedByteSource(source), format, context, null, cancellationToken);
        file.ExtractText(output, cancellationToken);
    }

    /// <summary>Converts a file to another format, choosing the output format by the destination's extension.</summary>
    /// <param name="source">The input path.</param>
    /// <param name="destination">The output path; its extension names the format.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the conversion; an existing destination stays as it was.</param>
    /// <exception cref="OfficeFormatException">The input is not a recognised format, or is damaged beyond repair.</exception>
    /// <exception cref="FormatNotAvailableException">No reader is registered for the input or no writer for the output.</exception>
    /// <exception cref="ArgumentException">The destination's extension does not name one format.</exception>
    public static void Convert(string source, string destination, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        // Markdown is never recognised from content, so a source named like Markdown is read as Markdown.
        OfficeFormat named = OfficeFormats.FromExtension(source);
        using OfficeFile file = named == OfficeFormat.Markdown ? Open(source, named, context, cancellationToken) : Open(source, context, cancellationToken);
        file.SaveAs(destination, cancellationToken);
    }

    // Lets a file that "owns" its source be opened over a source the caller still owns: disposing it leaves the original open.
    internal sealed class BorrowedByteSource(IByteSource inner) : IByteSource
    {
        public long Length => inner.Length;

        public bool CanReadSync => inner.CanReadSync;

        public int Read(long offset, Span<byte> destination) => inner.Read(offset, destination);

        public ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(offset, destination, cancellationToken);

        public bool TryGetMemory(out ReadOnlyMemory<byte> memory) => inner.TryGetMemory(out memory);

        public void Dispose()
        {
        }
    }
}
