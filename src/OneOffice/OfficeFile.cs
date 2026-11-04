using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Formats;
using RecursiveAI.OneOffice.IO;

namespace RecursiveAI.OneOffice;

/// <summary>
/// What <c>Document</c>, <c>Workbook</c> and <c>Presentation</c> share: they are opened from a source with one
/// <see cref="OpenContext"/>, collect the defects found on the way, save to a file or stream in a chosen format, extract their
/// text, and are disposed. The kind packages derive from it; the open dispatch and the format registry are written against it.
/// </summary>
/// <remarks>
/// An <see cref="OfficeFile"/> is not safe for concurrent mutation; read-only use from several threads is safe once it is loaded.
/// Saving never changes the file object's own <see cref="Format"/> or <see cref="FilePath"/>: <c>SaveAs</c> writes a copy.
/// </remarks>
public abstract class OfficeFile : IDisposable
{
    private readonly OpenContext _context;
    private bool _disposed;

    /// <summary>Initializes the base state.</summary>
    /// <param name="format">The format the file was opened from (or, for a new file, the format <see cref="Save"/> writes).</param>
    /// <param name="context">The context it was opened with.</param>
    /// <param name="filePath">The path it was opened from, or null.</param>
    protected OfficeFile(OfficeFormat format, OpenContext context, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        Format = format;
        _context = context;
        FilePath = filePath;
    }

    /// <summary>Gets the format the file was opened from, which <see cref="Save"/> writes back.</summary>
    public OfficeFormat Format { get; }

    /// <summary>Gets the kind of document: the hub this is.</summary>
    public abstract OfficeKind Kind { get; }

    /// <summary>Gets the limits it was opened with; later operations (save, recalculation, layout) apply the same ones.</summary>
    public OfficeLimits Limits => _context.Limits;

    /// <summary>Gets the file it was opened from, which <see cref="Save"/> replaces; null when opened from a stream or created in memory.</summary>
    public string? FilePath { get; }

    /// <summary>
    /// Gets the defects found while opening and since, in order. Empty when the sink given at open was not a
    /// <see cref="DefectCollection"/> (the caller's own sink holds them then).
    /// </summary>
    public IReadOnlyList<Defect> Defects => _context.Defects as DefectCollection ?? (IReadOnlyList<Defect>)[];

    /// <summary>Gets the context, for derived types to pass to the layers below.</summary>
    protected OpenContext Context => _context;

    /// <summary>
    /// Saves to <see cref="FilePath"/> in <see cref="Format"/>, atomically: written to a temporary file beside it and renamed over
    /// the original, so a failed or interrupted save leaves the original intact.
    /// </summary>
    /// <param name="cancellationToken">Cancels the save; the original stays as it was.</param>
    /// <exception cref="InvalidOperationException">The file has no <see cref="FilePath"/>.</exception>
    /// <exception cref="FormatNotAvailableException">There is no writer for <see cref="Format"/> (a read-only format such as DOC).</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    public virtual void Save(CancellationToken cancellationToken = default)
    {
        if (FilePath is null)
        {
            throw new InvalidOperationException("The file was not opened from a path; use SaveAs.");
        }

        SaveAs(FilePath, Format, cancellationToken);
    }

    /// <summary>Saves a copy, choosing the format by the path's extension (which only names an output; see <see cref="OfficeFormats.FromExtension"/>).</summary>
    /// <param name="path">The target path.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <exception cref="ArgumentException">The extension does not name one format; use the overload that takes a format.</exception>
    /// <exception cref="FormatNotAvailableException">There is no writer for the format.</exception>
    public void SaveAs(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        OfficeFormat format = ResolveOutputFormat(path);
        if (format == OfficeFormat.Unknown)
        {
            throw new ArgumentException($"The extension of '{path}' does not name one format; pass the format explicitly.", nameof(path));
        }

        SaveAs(path, format, cancellationToken);
    }

    /// <summary>
    /// Chooses the format a path's extension names for this kind of file. The default is <see cref="OfficeFormats.FromExtension"/>;
    /// a kind overrides it to settle an extension that is ambiguous across kinds (<c>.xml</c> names Flat OPC for every kind).
    /// </summary>
    /// <param name="path">The target path.</param>
    /// <returns>The format, or <see cref="OfficeFormat.Unknown"/> when the extension does not name one.</returns>
    protected virtual OfficeFormat ResolveOutputFormat(string path) => OfficeFormats.FromExtension(path);

    /// <summary>Saves a copy in an explicit format, atomically (see <see cref="Save"/>).</summary>
    /// <param name="path">The target path.</param>
    /// <param name="format">The format to write.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <exception cref="FormatNotAvailableException">There is no writer for the format.</exception>
    public void SaveAs(string path, OfficeFormat format, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ThrowIfDisposed();
        AtomicFile.Write(path, stream => WriteCore(format, stream, cancellationToken));
    }

    /// <summary>Writes a copy to a stream in an explicit format.</summary>
    /// <param name="destination">The stream; it is left open.</param>
    /// <param name="format">The format to write.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="FormatNotAvailableException">There is no writer for the format.</exception>
    public void SaveAs(Stream destination, OfficeFormat format, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ThrowIfDisposed();
        WriteCore(format, destination, cancellationToken);
    }

    /// <summary>
    /// Writes the file in a format. The default looks the writer up in <see cref="FormatRegistry.Default"/>; a kind overrides it for the
    /// formats it writes itself, so saving its own formats needs no registration.
    /// </summary>
    /// <param name="format">The format to write.</param>
    /// <param name="destination">The stream to write to.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="FormatNotAvailableException">There is no writer for the format.</exception>
    protected virtual void WriteCore(OfficeFormat format, Stream destination, CancellationToken cancellationToken) =>
        FormatRegistry.Default.GetWriter(format)(this, format, destination, cancellationToken);

    /// <summary>Gets the document's text as a string; see <see cref="ExtractText(TextWriter, CancellationToken)"/>.</summary>
    /// <param name="cancellationToken">Cancels the extraction.</param>
    /// <returns>The text.</returns>
    public string ExtractText(CancellationToken cancellationToken = default)
    {
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        ExtractText(writer, cancellationToken);
        return writer.ToString();
    }

    /// <summary>Writes the document's text, in reading order, to a writer.</summary>
    /// <param name="output">Where the text goes.</param>
    /// <param name="cancellationToken">Cancels the extraction.</param>
    public void ExtractText(TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ThrowIfDisposed();
        ExtractTextCore(output, cancellationToken);
    }

    /// <summary>Releases the document and what it holds open (the source it reads lazily from).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Writes the text of the document; called by <see cref="ExtractText(TextWriter, CancellationToken)"/> after the checks.</summary>
    /// <param name="output">Where the text goes.</param>
    /// <param name="cancellationToken">Cancels the extraction.</param>
    protected abstract void ExtractTextCore(TextWriter output, CancellationToken cancellationToken);

    /// <summary>Releases resources. Called once.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
    }

    /// <summary>Raises <see cref="ObjectDisposedException"/> if the file is disposed.</summary>
    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
