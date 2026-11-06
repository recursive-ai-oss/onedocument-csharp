using RecursiveAI.OneDocument.IO;

namespace RecursiveAI.OneDocument.Formats;

/// <summary>Opens a source as a document of a given format.</summary>
/// <param name="source">The input.</param>
/// <param name="format">The format the sniffer found.</param>
/// <param name="context">Limits, defect sink and password.</param>
/// <param name="filePath">The path the source was opened from, which the file keeps for <c>Save()</c>; null when there is none.</param>
/// <param name="cancellationToken">Cancels the open.</param>
/// <returns>The opened file, which owns <paramref name="source"/>; the caller disposes it.</returns>
public delegate OfficeFile OpenHandler(IByteSource source, OfficeFormat format, OpenContext context, string? filePath, CancellationToken cancellationToken);

/// <summary>Extracts the text of a source without building the whole model, where a format can do that more cheaply.</summary>
/// <param name="source">The input.</param>
/// <param name="format">The format the sniffer found.</param>
/// <param name="context">Limits, defect sink and password.</param>
/// <param name="output">Where the text goes.</param>
/// <param name="cancellationToken">Cancels the extraction.</param>
public delegate void TextExtractionHandler(IByteSource source, OfficeFormat format, OpenContext context, TextWriter output, CancellationToken cancellationToken);

/// <summary>Writes an opened file in a given format.</summary>
/// <param name="file">The file to write; it may be of any kind the writer accepts.</param>
/// <param name="format">The format to write.</param>
/// <param name="destination">The stream to write to.</param>
/// <param name="cancellationToken">Cancels the write.</param>
public delegate void WriteHandler(OfficeFile file, OfficeFormat format, Stream destination, CancellationToken cancellationToken);

/// <summary>Finds the VBA project of a source whose format keeps it where the core package cannot look (PowerPoint 97-2003).</summary>
/// <param name="source">The input.</param>
/// <param name="context">Limits, defect sink and password.</param>
/// <param name="cancellationToken">Cancels the read.</param>
/// <returns>The project, or null when the file holds none.</returns>
public delegate Vba.VbaProject? VbaReadHandler(IByteSource source, OpenContext context, CancellationToken cancellationToken);

/// <summary>Writes a copy of a source without its VBA project.</summary>
/// <param name="source">The input.</param>
/// <param name="destination">Where the copy goes.</param>
/// <param name="context">Limits, defect sink and password.</param>
/// <param name="cancellationToken">Cancels the removal.</param>
/// <returns>What was removed and what was left.</returns>
public delegate Vba.VbaRemovalReport VbaRemoveHandler(IByteSource source, Stream destination, OpenContext context, CancellationToken cancellationToken);

/// <summary>
/// The explicit table of what can be read, text-extracted and written, per <see cref="OfficeFormat"/>. Each kind's package fills in its
/// formats by calling <c>Register…</c> from a method it exposes (for example <c>DocumentFormats.Register(registry)</c>); nothing is found by
/// scanning assemblies or by reflection, so trimming and NativeAOT keep exactly what is registered. packages that are
/// not referenced simply leave their formats unregistered, and the operations that need them raise <see cref="FormatNotAvailableException"/>.
/// </summary>
/// <remarks>
/// The registry is safe for concurrent use. Registering a format that is already registered replaces the earlier handler, so a
/// caller can override a built-in. <see cref="Default"/> is what <see cref="Office"/> and the hub types use.
/// </remarks>
public sealed class FormatRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<OfficeFormat, OpenHandler> _readers = [];
    private readonly Dictionary<OfficeFormat, TextExtractionHandler> _extractors = [];
    private readonly Dictionary<OfficeFormat, WriteHandler> _writers = [];
    private readonly Dictionary<OfficeFormat, Templating.ITemplateBinder> _binders = [];
    private readonly Dictionary<OfficeFormat, VbaReadHandler> _vbaReaders = [];
    private readonly Dictionary<OfficeFormat, VbaRemoveHandler> _vbaRemovers = [];

    /// <summary>Gets the registry the <see cref="Office"/> statics and the hub types use.</summary>
    public static FormatRegistry Default { get; } = CreateDefault();

    /// <summary>Registers a reader for formats.</summary>
    /// <param name="handler">Opens a source as a document.</param>
    /// <param name="formats">The formats it opens.</param>
    /// <exception cref="ArgumentException">A format is <see cref="OfficeFormat.Unknown"/>.</exception>
    public void RegisterReader(OpenHandler handler, params OfficeFormat[] formats) => Register(_readers, handler, formats);

    /// <summary>Registers a text extractor for formats.</summary>
    /// <param name="handler">Extracts text from a source.</param>
    /// <param name="formats">The formats it handles.</param>
    /// <exception cref="ArgumentException">A format is <see cref="OfficeFormat.Unknown"/>.</exception>
    public void RegisterTextExtractor(TextExtractionHandler handler, params OfficeFormat[] formats) => Register(_extractors, handler, formats);

    /// <summary>Registers a writer for formats.</summary>
    /// <param name="handler">Writes a file.</param>
    /// <param name="formats">The formats it writes.</param>
    /// <exception cref="ArgumentException">A format is <see cref="OfficeFormat.Unknown"/>.</exception>
    public void RegisterWriter(WriteHandler handler, params OfficeFormat[] formats) => Register(_writers, handler, formats);

    /// <summary>Registers the template binder for formats; <see cref="Template"/> dispatches through it.</summary>
    /// <param name="binder">The binder.</param>
    /// <param name="formats">The formats it binds.</param>
    /// <exception cref="ArgumentException">A format is <see cref="OfficeFormat.Unknown"/>.</exception>
    public void RegisterTemplateBinder(Templating.ITemplateBinder binder, params OfficeFormat[] formats)
    {
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentNullException.ThrowIfNull(formats);
        if (Array.IndexOf(formats, OfficeFormat.Unknown) >= 0)
        {
            throw new ArgumentException("Unknown is not a format that can be registered.", nameof(formats));
        }

        lock (_gate)
        {
            foreach (OfficeFormat f in formats)
            {
                _binders[f] = binder;
            }
        }
    }

    /// <summary>Gets the template binder for a format.</summary>
    /// <param name="format">The format.</param>
    /// <param name="binder">The binder.</param>
    /// <returns>True if registered.</returns>
    public bool TryGetTemplateBinder(OfficeFormat format, out Templating.ITemplateBinder binder)
    {
        lock (_gate)
        {
            return _binders.TryGetValue(format, out binder!);
        }
    }

    /// <summary>Gets the template binder for a format or raises <see cref="FormatNotAvailableException"/>.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The binder.</returns>
    /// <exception cref="FormatNotAvailableException">No binder is registered.</exception>
    public Templating.ITemplateBinder GetTemplateBinder(OfficeFormat format) =>
        TryGetTemplateBinder(format, out Templating.ITemplateBinder? b) ? b : throw new FormatNotAvailableException(format, "render a template from");

    /// <summary>Registers how to find and how to remove the VBA project of formats the core package cannot look into itself.</summary>
    /// <param name="read">Finds the project.</param>
    /// <param name="remove">Writes a copy without it.</param>
    /// <param name="formats">The formats they handle.</param>
    /// <exception cref="ArgumentException">A format is <see cref="OfficeFormat.Unknown"/>.</exception>
    public void RegisterVba(VbaReadHandler read, VbaRemoveHandler remove, params OfficeFormat[] formats)
    {
        Register(_vbaReaders, read, formats);
        Register(_vbaRemovers, remove, formats);
    }

    /// <summary>Gets the VBA project finder for a format.</summary>
    /// <param name="format">The format.</param>
    /// <param name="handler">The finder.</param>
    /// <returns>True if registered.</returns>
    public bool TryGetVbaReader(OfficeFormat format, out VbaReadHandler handler) => TryGet(_vbaReaders, format, out handler);

    /// <summary>Gets the VBA project remover for a format.</summary>
    /// <param name="format">The format.</param>
    /// <param name="handler">The remover.</param>
    /// <returns>True if registered.</returns>
    public bool TryGetVbaRemover(OfficeFormat format, out VbaRemoveHandler handler) => TryGet(_vbaRemovers, format, out handler);

    /// <summary>Gets the reader for a format.</summary>
    /// <param name="format">The format.</param>
    /// <param name="handler">The reader.</param>
    /// <returns>True if registered.</returns>
    public bool TryGetReader(OfficeFormat format, out OpenHandler handler) => TryGet(_readers, format, out handler);

    /// <summary>Gets the text extractor for a format.</summary>
    /// <param name="format">The format.</param>
    /// <param name="handler">The extractor.</param>
    /// <returns>True if registered.</returns>
    public bool TryGetTextExtractor(OfficeFormat format, out TextExtractionHandler handler) => TryGet(_extractors, format, out handler);

    /// <summary>Gets the writer for a format.</summary>
    /// <param name="format">The format.</param>
    /// <param name="handler">The writer.</param>
    /// <returns>True if registered.</returns>
    public bool TryGetWriter(OfficeFormat format, out WriteHandler handler) => TryGet(_writers, format, out handler);

    /// <summary>Gets the formats that can be opened, in format order.</summary>
    /// <returns>A snapshot.</returns>
    public IReadOnlyList<OfficeFormat> ReadableFormats() => Snapshot(_readers);

    /// <summary>Gets the formats that can be written, in format order.</summary>
    /// <returns>A snapshot.</returns>
    public IReadOnlyList<OfficeFormat> WritableFormats() => Snapshot(_writers);

    /// <summary>Gets the reader for a format or raises <see cref="FormatNotAvailableException"/>.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The reader.</returns>
    /// <exception cref="FormatNotAvailableException">No reader is registered.</exception>
    public OpenHandler GetReader(OfficeFormat format) =>
        TryGetReader(format, out OpenHandler? h) ? h : throw new FormatNotAvailableException(format, "read");

    /// <summary>Gets the writer for a format or raises <see cref="FormatNotAvailableException"/>.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The writer.</returns>
    /// <exception cref="FormatNotAvailableException">No writer is registered.</exception>
    public WriteHandler GetWriter(OfficeFormat format) =>
        TryGetWriter(format, out WriteHandler? h) ? h : throw new FormatNotAvailableException(format, "write");

    // What the core package itself can do, so the default registry works before any kind package is registered.
    private static FormatRegistry CreateDefault()
    {
        var registry = new FormatRegistry();
        TextExtraction.OoxmlTextExtractor.Register(registry);
        Encryption.EncryptedPackageReader.Register(registry);
        return registry;
    }

    private void Register<T>(Dictionary<OfficeFormat, T> table, T handler, OfficeFormat[] formats)
        where T : Delegate
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(formats);
        foreach (OfficeFormat f in formats)
        {
            if (f == OfficeFormat.Unknown)
            {
                throw new ArgumentException("Unknown is not a format that can be registered.", nameof(formats));
            }
        }

        lock (_gate)
        {
            foreach (OfficeFormat f in formats)
            {
                table[f] = handler;
            }
        }
    }

    private bool TryGet<T>(Dictionary<OfficeFormat, T> table, OfficeFormat format, out T handler)
        where T : Delegate
    {
        lock (_gate)
        {
            if (table.TryGetValue(format, out T? found))
            {
                handler = found;
                return true;
            }
        }

        handler = null!;
        return false;
    }

    private OfficeFormat[] Snapshot<T>(Dictionary<OfficeFormat, T> table)
        where T : Delegate
    {
        lock (_gate)
        {
            OfficeFormat[] keys = [.. table.Keys];
            Array.Sort(keys);
            return keys;
        }
    }
}
