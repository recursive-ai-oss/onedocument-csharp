using System.Text;
using RecursiveAI.OneArchive;
using RecursiveAI.OneArchive.Zip;
using RecursiveAI.OneCompress;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;

namespace RecursiveAI.OneDocument.Packaging;

/// <summary>
/// An Open Packaging Conventions package (ECMA-376 Part 2): the container of every OOXML file, held as a zip or as Flat OPC.
/// Opening reads the zip directory, <c>[Content_Types].xml</c> and the package relationships; a part's bytes are read the first
/// time they are asked for. A part nobody changes is written back with the bytes it had (see <see cref="IPackagePart.IsUntouched"/>),
/// in the order and under the names it was stored with.
/// </summary>
/// <remarks>
/// <para>
/// Reading is tolerant and every repair is reported to <see cref="OpenContext.Defects"/> with a part name and offset: duplicate
/// or case-colliding part names, parts with no content type, malformed or dangling relationships, interleaved (piece) parts, zip
/// damage that recovery mode works around. What cannot be repaired (no <c>[Content_Types].xml</c>, a limit exceeded, a damaged
/// directory that recovery cannot use) raises an <see cref="OfficeException"/>.
/// </para>
/// <para>
/// The relationships of a part are read when first asked for (<see cref="GetRelationships"/>), except the package relationships,
/// which are read on open. <c>[Content_Types].xml</c> and the <c>.rels</c> parts are not parts of the abstract package, so they
/// are not in <see cref="Parts"/>: they are exposed as <see cref="ContentTypes"/> and as relationships.
/// </para>
/// <para>Packages are not safe for concurrent mutation; after open, reading parts and relationships from several threads is safe.</para>
/// </remarks>
public sealed class OpcPackage : IOpcPackage
{
    private const string ContentTypesZipName = "[Content_Types].xml";

    private readonly object _sync = new();
    private readonly List<Entry> _all = [];
    private readonly List<Entry> _parts = [];
    private readonly Dictionary<string, Entry> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RelState> _rels = new(StringComparer.Ordinal);
    private readonly ContentTypes _contentTypes = new();
    private readonly OpenContext _context;
    private ZipSource? _zip;
    private Entry? _contentTypesEntry;
    private bool _removed;
    private bool _disposed;
    private int _loadedParts;

    private OpcPackage(OpenContext context, string? filePath, bool flat)
    {
        _context = context;
        FilePath = filePath;
        IsFlatOpc = flat;
    }

    /// <inheritdoc />
    public PackageKind Kind => PackageKind.Opc;

    /// <inheritdoc />
    public OfficeLimits Limits => _context.Limits;

    /// <inheritdoc />
    public string? FilePath { get; private set; }

    /// <summary>Gets a value indicating whether the package was opened from Flat OPC (one XML document); <see cref="WriteTo"/> and <see cref="Save"/> then write that form again.</summary>
    public bool IsFlatOpc { get; private set; }

    /// <summary>Gets the content types of the package. Read-only: content types change when parts are added or removed.</summary>
    public ContentTypes ContentTypes => _contentTypes;

    /// <summary>Gets where the package reports defects found while reading.</summary>
    public IDefectSink Defects => _context.Defects;

    /// <inheritdoc />
    public IReadOnlyList<IPackagePart> Parts => _parts;

    /// <summary>
    /// Gets how many parts' contents have been read from the container so far, including the ones read to open (content types and
    /// package relationships). Opening a large package keeps this at two or three.
    /// </summary>
    public int LoadedPartCount => Volatile.Read(ref _loadedParts);

    /// <inheritdoc />
    public bool IsModified
    {
        get
        {
            if (_removed || _contentTypes.Dirty)
            {
                return true;
            }

            foreach (Entry e in _all)
            {
                // The content-types item follows the map, which only changes together with the parts.
                if (!e.IsUntouched && e.Kind != EntryKind.ContentTypes)
                {
                    return true;
                }
            }

            foreach (RelState r in _rels.Values)
            {
                if (r.Changed)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Creates an empty package: no parts, no relationships, and the usual defaults for <c>.rels</c> and <c>.xml</c> content types.</summary>
    /// <param name="limits">The limits; null for the defaults.</param>
    /// <returns>The new package, which is modified from the start.</returns>
    public static OpcPackage Create(OfficeLimits? limits = null)
    {
        var pkg = new OpcPackage(new OpenContext(limits), null, flat: false);
        pkg._contentTypes.AddDefault("rels", ContentTypes.RelationshipsContentType);
        pkg._contentTypes.AddDefault("xml", "application/xml");
        pkg._contentTypes.MarkDirty();
        return pkg;
    }

    /// <summary>Opens a package from a file, as a zip or as Flat OPC (decided from the content, never the extension).</summary>
    /// <param name="path">The file.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <returns>The open package. Dispose it to release the file.</returns>
    /// <exception cref="OfficeException">The file is not a package, is damaged beyond repair, or exceeds a limit.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static OpcPackage Open(string path, OpenContext? context = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        IByteSource source = ByteSource.FromFile(path);
        return OpenOwned(source, Path.GetFullPath(path), context);
    }

    /// <summary>Opens a package from a seekable stream.</summary>
    /// <param name="stream">The stream; it must stay readable for the life of the package (parts load lazily).</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="leaveOpen">True to leave the stream open when the package is disposed.</param>
    /// <returns>The open package.</returns>
    /// <exception cref="OfficeException">The stream is not a package, is damaged beyond repair, or exceeds a limit.</exception>
    public static OpcPackage Open(Stream stream, OpenContext? context = null, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return OpenOwned(ByteSource.FromStream(stream, leaveOpen), null, context);
    }

    /// <summary>Opens a package from a byte source, which the package then owns and disposes with itself.</summary>
    /// <param name="source">The bytes; it must support synchronous reads (<see cref="IByteSource.CanReadSync"/>).</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <returns>The open package.</returns>
    /// <exception cref="OfficeException">The source is not a package, is damaged beyond repair, or exceeds a limit.</exception>
    public static OpcPackage Open(IByteSource source, OpenContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return OpenOwned(source, null, context);
    }

    /// <summary>Opens a package from a byte source that was read from a file, which the package then owns; <see cref="Save"/> replaces that file.</summary>
    /// <param name="source">The bytes of the file; it must support synchronous reads (<see cref="IByteSource.CanReadSync"/>).</param>
    /// <param name="filePath">The path the bytes came from.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <returns>The open package.</returns>
    /// <exception cref="OfficeException">The source is not a package, is damaged beyond repair, or exceeds a limit.</exception>
    public static OpcPackage Open(IByteSource source, string filePath, OpenContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        return OpenOwned(source, Path.GetFullPath(filePath), context);
    }

    /// <summary>Opens a package from a source that may be remote, buffering it first when it cannot be read synchronously.</summary>
    /// <param name="source">The bytes; the package owns and disposes it.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the buffering.</param>
    /// <returns>The open package.</returns>
    /// <exception cref="OfficeException">The source is not a package, is damaged beyond repair, or exceeds a limit.</exception>
    public static async ValueTask<OpcPackage> OpenAsync(IByteSource source, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        OpenContext ctx = context ?? new OpenContext();
        if (!source.CanReadSync)
        {
            source = await ByteSource.ToMemoryAsync(source, ctx.Limits.MaxTotalUncompressedSize, cancellationToken).ConfigureAwait(false);
        }

        return OpenOwned(source, null, ctx);
    }

    /// <summary>Opens a package from a file without raising for input the library rejects.</summary>
    /// <param name="path">The file.</param>
    /// <param name="package">The package, or null.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults. The fatal defect is reported to its sink.</param>
    /// <returns>True if the file opened.</returns>
    public static bool TryOpen(string path, out OpcPackage? package, OpenContext? context = null)
    {
        OpenContext ctx = context ?? new OpenContext();
        try
        {
            package = Open(path, ctx);
            return true;
        }
        catch (OfficeException ex)
        {
            ctx.Defects.Report(ex.Defect);
            package = null;
            return false;
        }
    }

    /// <summary>Opens a package from a byte source without raising for input the library rejects; the source is disposed when this returns false.</summary>
    /// <param name="source">The bytes.</param>
    /// <param name="package">The package, or null.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults. The fatal defect is reported to its sink.</param>
    /// <returns>True if the source opened.</returns>
    public static bool TryOpen(IByteSource source, out OpcPackage? package, OpenContext? context = null)
    {
        OpenContext ctx = context ?? new OpenContext();
        try
        {
            package = Open(source, ctx);
            return true;
        }
        catch (OfficeException ex)
        {
            ctx.Defects.Report(ex.Defect);
            package = null;
            return false;
        }
    }

    /// <inheritdoc />
    public IPackagePart? FindPart(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byKey.TryGetValue(OpcNames.Key(name), out Entry? e) && e.Kind == EntryKind.Part ? e : null;
    }

    /// <inheritdoc />
    /// <remarks>The content type is added to <see cref="ContentTypes"/> as a default or an override as needed (ECMA-376 Part 2 §7.2.3.4). Relationships parts and the content-types item cannot be added as parts.</remarks>
    public IPackagePart AddPart(string name, string contentType, ReadOnlyMemory<byte> content)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrEmpty(contentType);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!PartUri.IsValid(name))
        {
            throw new ArgumentException($"'{name}' is not a valid part name.", nameof(name));
        }

        if (OpcRelationships.IsRelationshipsPart(name, out _) || OpcNames.Key(name) == OpcNames.Key("/" + ContentTypesZipName))
        {
            throw new ArgumentException($"'{name}' is reserved for relationships or content types; use SetRelationships.", nameof(name));
        }

        string key = OpcNames.Key(name);
        if (_byKey.ContainsKey(key))
        {
            throw new ArgumentException($"A part named '{name}' already exists.", nameof(name));
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartCount), Limits.MaxPartCount, _parts.Count + 1L);
        var e = new Entry(this, EntryKind.Part, name, OpcNames.ToZipName(name), contentType)
        {
            SourceIndex = -1,
            IsUntouched = false,
        };
        e.Set(content);
        _all.Add(e);
        _parts.Add(e);
        _byKey[key] = e;
        _contentTypes.Ensure(name, contentType);
        return e;
    }

    /// <summary>Changes the content type of an existing part (for example the main part of a document when it is saved as a template).</summary>
    /// <param name="name">The part name.</param>
    /// <param name="contentType">The new content type.</param>
    /// <returns>False when there is no such part.</returns>
    /// <remarks>The part's bytes are not touched, so an untouched part stays raw-copyable. An override is written to the content-types item.</remarks>
    public bool SetPartContentType(string name, string contentType)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrEmpty(contentType);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (FindPart(name) is not Entry e)
        {
            return false;
        }

        e.ContentType = contentType;
        _contentTypes.Ensure(name, contentType);
        return true;
    }

    /// <inheritdoc />
    /// <remarks>The part's relationships go with it, and so does its content-type override. Relationships of other parts that target it are left as they are.</remarks>
    public bool RemovePart(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string key = OpcNames.Key(name);
        if (!_byKey.TryGetValue(key, out Entry? e) || e.Kind != EntryKind.Part)
        {
            return false;
        }

        _byKey.Remove(key);
        _parts.Remove(e);
        _all.Remove(e);
        _removed = true;
        _contentTypes.RemoveOverride(e.Name);
        if (_rels.ContainsKey(OpcNames.Key(e.Name)))
        {
            SetRelationships(e.Name, []);
        }

        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<Relationship> GetRelationships(string? sourcePartName = null)
    {
        lock (_sync)
        {
            if (!_rels.TryGetValue(RelKey(sourcePartName), out RelState? state))
            {
                return [];
            }

            if (state.List is null)
            {
                ReadOnlyMemory<byte> xml = state.Entry!.GetBytes();
                state.List = OpcRelationships.Parse(xml, state.Entry.Name, sourcePartName, Limits, Defects, n => _byKey.TryGetValue(OpcNames.Key(n), out Entry? t) && t.Kind == EntryKind.Part);
            }

            return state.List.AsReadOnly();
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">An id is empty or used twice, or a type or target is missing.</exception>
    public void SetRelationships(string? sourcePartName, IReadOnlyList<Relationship> relationships)
    {
        ArgumentNullException.ThrowIfNull(relationships);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Relationship r in relationships)
        {
            if (r is null || string.IsNullOrEmpty(r.Id) || string.IsNullOrEmpty(r.Type) || r.Target is null)
            {
                throw new ArgumentException("A relationship needs an id, a type and a target.", nameof(relationships));
            }

            if (!ids.Add(r.Id))
            {
                throw new ArgumentException($"The relationship id '{r.Id}' is used twice.", nameof(relationships));
            }
        }

        lock (_sync)
        {
            string key = RelKey(sourcePartName);
            if (!_rels.TryGetValue(key, out RelState? state))
            {
                state = new RelState { Source = sourcePartName };
                _rels[key] = state;
            }

            state.List = [.. relationships];
            state.Changed = true;
            if (state.Entry is not null)
            {
                state.Entry.IsUntouched = false;
            }
        }
    }

    /// <summary>Resolves an internal relationship's target to a part name (ECMA-376 Part 2 §6.4, §6.5.2.3): relative to the owner part, without fragment or query.</summary>
    /// <param name="sourcePartName">The owner part's name, or null for a package relationship.</param>
    /// <param name="relationship">The relationship.</param>
    /// <param name="target">The part name, when the target is internal and names a valid part.</param>
    /// <returns>True when resolved. External targets and targets that climb above the root give false.</returns>
    public static bool TryResolveTarget(string? sourcePartName, Relationship relationship, out PartUri target)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        target = default;
        return relationship.TargetMode == RelationshipTargetMode.Internal && OpcRelationships.TryResolve(sourcePartName, relationship.Target, out target);
    }

    /// <inheritdoc />
    /// <remarks>Writes a zip, or Flat OPC for a package opened as Flat OPC. The container library does not yet copy a stored entry without recompressing, so untouched parts are decompressed and compressed again; their bytes are identical when read back.</remarks>
    public void WriteTo(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (IsFlatOpc)
        {
            WriteFlatOpcTo(destination, cancellationToken);
        }
        else
        {
            WriteZipCore(destination, cancellationToken, out _);
        }
    }

    /// <summary>Writes the package as Flat OPC (one XML document), whatever form it was opened in.</summary>
    /// <param name="destination">The stream.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public void WriteFlatOpcTo(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            Materialize();
            IEnumerable<(string, string, Func<ReadOnlyMemory<byte>>)> Items()
            {
                foreach (Entry e in _all)
                {
                    if (e.Kind is EntryKind.Part or EntryKind.Rels)
                    {
                        Entry captured = e;
                        yield return (e.Name, e.Kind == EntryKind.Rels ? ContentTypes.RelationshipsContentType : e.ContentType, () => captured.GetBytes());
                    }
                }
            }

            FlatOpc.Write(destination, Items(), ProgIdOfMainPart(), Limits, cancellationToken);
        }
    }

    /// <inheritdoc />
    public void Save(CancellationToken cancellationToken = default)
    {
        if (FilePath is null)
        {
            throw new InvalidOperationException("The package was not opened from a file; use SaveAs.");
        }

        SaveCore(FilePath, IsFlatOpc, rebase: true, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>Saving over the file the package was opened from is the same as <see cref="Save"/>.</remarks>
    public void SaveAs(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        bool same = FilePath is not null && string.Equals(Path.GetFullPath(path), FilePath, StringComparison.Ordinal);
        SaveCore(path, IsFlatOpc, rebase: same, cancellationToken);
    }

    /// <summary>Saves a copy as Flat OPC to a path, atomically; the package itself stays as it is.</summary>
    /// <param name="path">The target file.</param>
    /// <param name="cancellationToken">Cancels the save; an existing file at the path stays as it was.</param>
    public void SaveAsFlatOpc(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        SaveCore(path, flat: true, rebase: false, cancellationToken);
    }

    /// <summary>Releases the file or stream the package reads from. Parts not yet read can no longer be read.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_sync)
        {
            _zip?.Dispose();
            _zip = null;
        }
    }

    // ---- reading ------------------------------------------------------------------------------------------------------------

    private static OpcPackage OpenOwned(IByteSource source, string? filePath, OpenContext? context)
    {
        OpenContext ctx = context ?? new OpenContext();
        try
        {
            byte[] head = new byte[256];
            int n = source.Read(0, head);
            bool zip = n >= 2 && head[0] == (byte)'P' && head[1] == (byte)'K';

            if (!zip)
            {
                if (!FlatOpc.LooksFlat(head.AsSpan(0, n)))
                {
                    throw new OfficeFormatException(DefectKind.Packaging, "packaging.not-a-package", "The input is neither a zip package nor a Flat OPC document.", DefectLocation.InPart(string.Empty, 0));
                }

                return OpenFlat(source, filePath, ctx);
            }

            return OpenZip(source, filePath, ctx);
        }
        catch (CompressionException ex)
        {
            source.Dispose();
            throw ArchiveErrors.Translate(ex);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private static OpcPackage OpenFlat(IByteSource source, string? filePath, OpenContext ctx)
    {
        ReadOnlyMemory<byte> bytes;
        if (!source.TryGetMemory(out bytes))
        {
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxTotalUncompressedSize), ctx.Limits.MaxTotalUncompressedSize, source.Length);
            byte[] all = new byte[source.Length];
            int read = 0;
            while (read < all.Length)
            {
                int got = source.Read(read, all.AsSpan(read));
                if (got <= 0)
                {
                    break;
                }

                read += got;
            }

            bytes = all.AsMemory(0, read);
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxTotalUncompressedSize), ctx.Limits.MaxTotalUncompressedSize, bytes.Length);
        List<FlatOpc.FlatPart> flat = FlatOpc.Read(bytes, ctx.Limits, ctx.Defects, filePath ?? "flat-opc");
        source.Dispose(); // Every part is a slice of the buffer read above; nothing reads from the source again.
        var pkg = new OpcPackage(ctx, filePath, flat: true);
        long total = 0;
        foreach (FlatOpc.FlatPart fp in flat)
        {
            total += fp.Content.Length;
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxTotalUncompressedSize), ctx.Limits.MaxTotalUncompressedSize, total, DefectLocation.InPart(fp.Name));
            pkg.Classify(fp.Name, OpcNames.ToZipName(fp.Name), fp.ContentType, -1, fp.Content, fp.Content.Length, null, fromFlat: true);
        }

        pkg._contentTypes.AddDefault("rels", ContentTypes.RelationshipsContentType);
        foreach (Entry e in pkg._parts)
        {
            pkg._contentTypes.AddOverride(e.Name, e.ContentType);
        }

        pkg._contentTypes.MarkClean();
        return pkg;
    }

    private static OpcPackage OpenZip(IByteSource source, string? filePath, OpenContext ctx)
    {
        var pkg = new OpcPackage(ctx, filePath, flat: false);
        ZipSource? zip = null;
        try
        {
            try
            {
                zip = ZipSource.Enumerate(source, ctx.Limits, recovery: false);
            }
            catch (CompressionFormatException first)
            {
                // The directory or an entry header is damaged: try again in recovery mode, which scans local headers, and
                // report what it had to skip. If that fails too, the original error is the more useful one.
                try
                {
                    zip = ZipSource.Enumerate(source, ctx.Limits, recovery: true);
                    ContentTypes.Report(ctx.Defects, "packaging.zip-damaged", DefectSeverity.Repaired, "The zip structure is damaged (" + first.Message + "); the entries were recovered.", string.Empty, first.Offset);
                }
                catch (CompressionException)
                {
                    throw first;
                }
            }

            pkg._zip = zip;
            foreach (ZipDamage d in zip.Damage)
            {
                ContentTypes.Report(ctx.Defects, "packaging.zip-damaged", DefectSeverity.Repaired, $"{d.Kind}: {d.Reason}", d.EntryName ?? string.Empty, d.Offset);
            }

            pkg.ClassifyZipEntries(zip);
            pkg.ReadContentTypesAndAssign();
            pkg.ReadPackageRelationships();
            return pkg;
        }
        catch
        {
            (zip ?? pkg._zip)?.Dispose();
            pkg._zip = null;
            throw;
        }
    }

    private void ClassifyZipEntries(ZipSource zip)
    {
        for (int i = 0; i < zip.Entries.Count; i++)
        {
            ZipEntry z = zip.Entries[i];
            if (z.Name.Length == 0 || z.Name.Contains('\0'))
            {
                // The zip writer cannot store such a name, so the item is not carried into a saved package.
                ContentTypes.Report(Defects, "packaging.invalid-part-name", DefectSeverity.Dropped, "A stored item has an empty name or a NUL in its name; it is left out.", z.Name.Replace('\0', '?'), -1);
                continue;
            }

            zip.Preloaded.TryGetValue(i, out byte[]? preloaded);
            Classify(null, z.Name, string.Empty, preloaded is null ? i : -1, preloaded is null ? null : (ReadOnlyMemory<byte>?)preloaded, z.IsSizeKnown ? z.Size : -1, z.ModifiedTime, fromFlat: false, isDirectory: z.Kind == ArchiveEntryKind.Directory || z.Name.EndsWith('/'));
        }
    }

    // Sorts one stored item into content types, relationships part, part, or "foreign" (kept and written back, but not a part).
    private void Classify(string? logicalName, string zipName, string contentType, int sourceIndex, ReadOnlyMemory<byte>? content, long length, DateTimeOffset? modified, bool fromFlat, bool isDirectory = false)
    {
        string name = logicalName ?? OpcNames.FromZipName(zipName);
        EntryKind kind;
        string? relsOwner = null;
        if (isDirectory)
        {
            kind = EntryKind.Foreign;
        }
        else if (!fromFlat && string.Equals(zipName, ContentTypesZipName, StringComparison.OrdinalIgnoreCase))
        {
            if (_contentTypesEntry is null)
            {
                kind = EntryKind.ContentTypes;
                name = ContentTypesZipName;
            }
            else
            {
                kind = EntryKind.Foreign;
                ContentTypes.Report(Defects, "packaging.duplicate-part-name", DefectSeverity.Dropped, "[Content_Types].xml is stored twice; the first is used.", zipName, -1);
            }
        }
        else if (!PartUri.IsValid(name))
        {
            kind = EntryKind.Foreign;
            if (IsPiece(zipName))
            {
                ContentTypes.Report(Defects, "packaging.interleaved-part", DefectSeverity.Dropped, "An interleaved part piece (ECMA-376 Part 2 §7.2.4) is kept as stored but is not reassembled into a part.", zipName, -1);
            }
            else
            {
                ContentTypes.Report(Defects, "packaging.invalid-part-name", DefectSeverity.Dropped, $"The item '{zipName}' is not a valid part name; it is kept as stored but is not a part.", zipName, -1);
            }
        }
        else if (IsPiece(zipName))
        {
            kind = EntryKind.Foreign;
            ContentTypes.Report(Defects, "packaging.interleaved-part", DefectSeverity.Dropped, "An interleaved part piece (ECMA-376 Part 2 §7.2.4) is kept as stored but is not reassembled into a part.", zipName, -1);
        }
        else if (OpcRelationships.IsRelationshipsPart(name, out relsOwner))
        {
            kind = EntryKind.Rels;
        }
        else
        {
            kind = EntryKind.Part;
        }

        if (kind is EntryKind.Part or EntryKind.Rels)
        {
            string key = OpcNames.Key(name);
            if (_byKey.TryGetValue(key, out Entry? existing))
            {
                bool exact = existing is not null && string.Equals(existing.Name, name, StringComparison.Ordinal);
                ContentTypes.Report(
                    Defects,
                    exact ? "packaging.duplicate-part-name" : "packaging.part-name-case-collision",
                    DefectSeverity.Dropped,
                    exact ? $"The part '{name}' is stored twice; the first is used." : $"The part names '{existing?.Name}' and '{name}' differ only in case; the first is used.",
                    name,
                    -1);
                kind = EntryKind.Foreign;
            }
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartCount), Limits.MaxPartCount, _all.Count + 1L);
        var e = new Entry(this, kind, name, zipName, contentType)
        {
            SourceIndex = sourceIndex,
            IsUntouched = true,
            IsDirectory = isDirectory,
            Modified = modified,
        };
        e.Initialise(content, length);
        _all.Add(e);
        switch (kind)
        {
            case EntryKind.ContentTypes:
                _contentTypesEntry = e;
                break;
            case EntryKind.Rels:
                _rels[RelKey(relsOwner)] = new RelState { Entry = e, Source = relsOwner };
                _byKey[OpcNames.Key(name)] = e;
                break;
            case EntryKind.Part:
                _parts.Add(e);
                _byKey[OpcNames.Key(name)] = e;
                break;
            default:
                break;
        }
    }

    private static bool IsPiece(string zipName)
    {
        // ECMA-376 Part 2 §7.2.5.2: a piece's name ends with "/[n].piece" or "/[n].last.piece".
        int slash = zipName.LastIndexOf('/');
        string last = slash < 0 ? zipName : zipName[(slash + 1)..];
        return last.Length > 8 && last[0] == '[' && last.EndsWith(".piece", StringComparison.OrdinalIgnoreCase) && last.AsSpan(1).IndexOf(']') >= 1;
    }

    private void ReadContentTypesAndAssign()
    {
        if (_contentTypesEntry is null)
        {
            throw new OfficeFormatException(DefectKind.Packaging, "packaging.missing-content-types", "The package has no [Content_Types].xml (ECMA-376 Part 2 §7.3.7).", DefectLocation.InPart(ContentTypesZipName, 0));
        }

        ContentTypes parsed = ContentTypes.Parse(_contentTypesEntry.GetBytes(), ContentTypesZipName, Limits, Defects);
        foreach (ContentTypeDefault d in parsed.Defaults)
        {
            _contentTypes.AddDefault(d.Extension, d.ContentType);
        }

        foreach (ContentTypeOverride o in parsed.Overrides)
        {
            _contentTypes.AddOverride(o.PartName, o.ContentType);
        }

        _contentTypes.MarkClean();
        foreach (Entry e in _parts)
        {
            string? type = _contentTypes.Resolve(e.Name);
            if (type is null)
            {
                // ECMA-376 Part 2 §7.2.3.2: every part other than a Relationships part has a content type. The part is kept, untyped.
                ContentTypes.Report(Defects, "packaging.missing-content-type", DefectSeverity.Repaired, $"The part '{e.Name}' has no content type; it is kept with an empty one.", e.Name, -1);
                type = string.Empty;
            }

            e.ContentType = type;
        }
    }

    private void ReadPackageRelationships()
    {
        if (_rels.ContainsKey(RelKey(null)))
        {
            _ = GetRelationships(null);
        }
    }

    private static string RelKey(string? source) => source is null ? string.Empty : OpcNames.Key(source);

    private string? ProgIdOfMainPart()
    {
        // The instruction Word and PowerPoint put before pkg:package so the file opens in the right application.
        foreach (Relationship r in GetRelationships(null))
        {
            if (r.Type.EndsWith("/officeDocument", StringComparison.Ordinal) && TryResolveTarget(null, r, out PartUri main) && FindPart(main.Value) is { } part)
            {
                if (part.ContentType.Contains("wordprocessingml", StringComparison.Ordinal))
                {
                    return "Word.Document";
                }

                if (part.ContentType.Contains("presentationml", StringComparison.Ordinal))
                {
                    return "PowerPoint.Show";
                }

                if (part.ContentType.Contains("spreadsheetml", StringComparison.Ordinal))
                {
                    return "Excel.Sheet";
                }
            }
        }

        return null;
    }

    // ---- writing ------------------------------------------------------------------------------------------------------------

    // Turns what changed in the content-type map and in the relationships into the content of their items, so the writer sees
    // ordinary entries. A package that never changed keeps the items it was opened with, byte for byte.
    private void Materialize()
    {
        if (_contentTypesEntry is null)
        {
            _contentTypesEntry = new Entry(this, EntryKind.ContentTypes, ContentTypesZipName, ContentTypesZipName, "application/xml") { SourceIndex = -1 };
            _contentTypesEntry.Set(_contentTypes.ToXml());
            _all.Insert(0, _contentTypesEntry);
            _contentTypes.MarkClean();
        }
        else if (_contentTypes.Dirty)
        {
            _contentTypesEntry.Set(_contentTypes.ToXml());
            _contentTypes.MarkClean();
        }

        foreach (RelState state in _rels.Values)
        {
            if (!state.Changed)
            {
                continue;
            }

            string? source = state.Source;
            if (state.List is null || state.List.Count == 0)
            {
                if (state.Entry is not null)
                {
                    _all.Remove(state.Entry);
                    _byKey.Remove(OpcNames.Key(state.Entry.Name));
                    state.Entry = null;
                }
            }
            else
            {
                if (state.Entry is null)
                {
                    string relsName = OpcRelationships.PartNameFor(source);
                    state.Entry = new Entry(this, EntryKind.Rels, relsName, OpcNames.ToZipName(relsName), ContentTypes.RelationshipsContentType) { SourceIndex = -1 };
                    int at = source is null ? Math.Min(1, _all.Count) : _all.Count;
                    _all.Insert(at, state.Entry);
                    _byKey[OpcNames.Key(relsName)] = state.Entry;
                }

                state.Entry.Set(OpcRelationships.ToXml(state.List));
            }

            state.Changed = false;
        }
    }

    private void SaveCore(string path, bool flat, bool rebase, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!rebase)
        {
            AtomicFile.Write(path, s => WriteForm(s, flat, cancellationToken, out _));
            return;
        }

        List<Entry>? written = null;
        bool released = false;
        try
        {
            AtomicFile.Write(path, s =>
            {
                WriteForm(s, flat, cancellationToken, out written);

                // The temporary file is complete. Release the file being replaced now: some platforms cannot rename over a
                // file that is open. Reopened below, whichever way the rename goes.
                lock (_sync)
                {
                    if (_zip is not null)
                    {
                        _zip.Dispose();
                        _zip = null;
                        released = true;
                    }
                }
            });
        }
        catch
        {
            if (released)
            {
                // The rename failed; the original file is intact. Go back to reading it so the package stays usable.
                lock (_sync)
                {
                    _zip = ZipSource.Enumerate(ByteSource.FromFile(path), Limits, recovery: false);
                }
            }

            throw;
        }

        lock (_sync)
        {
            RebaseOnSavedFile(path, written);
        }
    }

    private void WriteForm(Stream s, bool flat, CancellationToken cancellationToken, out List<Entry>? written)
    {
        written = null;
        if (flat)
        {
            WriteFlatOpcTo(s, cancellationToken);
        }
        else
        {
            WriteZipCore(s, cancellationToken, out written);
        }
    }

    // After an in-place save every item is stored in the file just written, in the order written: the entries point at it, and
    // whatever was edited is untouched again relative to it.
    private void RebaseOnSavedFile(string path, List<Entry>? written)
    {
        if (written is null)
        {
            _removed = false;
            foreach (Entry e in _all)
            {
                e.IsUntouched = true;
            }

            return;
        }

        ZipSource fresh = ZipSource.Enumerate(ByteSource.FromFile(path), Limits, recovery: false);
        if (fresh.Entries.Count != written.Count)
        {
            fresh.Dispose();
            throw new OfficeFormatException(DefectKind.Packaging, "packaging.damaged", "The saved package does not hold the items that were written.", DefectLocation.InPart(path));
        }

        for (int i = 0; i < written.Count; i++)
        {
            written[i].SourceIndex = i;
            written[i].IsUntouched = true;
        }

        _zip = fresh;
        _removed = false;
    }

    /// <summary>Writes the package as a zip, whatever form it was opened in.</summary>
    /// <param name="destination">The stream.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public void WriteZipTo(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        WriteZipCore(destination, cancellationToken, out _);
    }

    private void WriteZipCore(Stream destination, CancellationToken cancellationToken, out List<Entry> written)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            Materialize();
            written = [.. _all];
            try
            {
                using var zw = new ZipWriter(destination, new ZipWriterOptions { Method = ZipMethod.Deflate }, leaveOpen: true);
                foreach (Entry e in written)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (e.IsDirectory)
                    {
                        zw.WriteEntry(new ZipEntry { Name = e.ZipName, Kind = ArchiveEntryKind.Directory, ModifiedTime = e.Modified });
                    }
                    else
                    {
                        CopyOrWrite(zw, e);
                    }
                }

                zw.Finish();
            }
            catch (CompressionException ex)
            {
                throw ArchiveErrors.Translate(ex);
            }
        }
    }

    // The one place that decides how an item gets into the output zip. An untouched item whose source is a seekable zip read
    // without recovery is copied raw: the stored bytes, CRC, method, flags, extra fields and DOS time pass through unchanged,
    // so nothing is decompressed or recompressed. The fallbacks recompress, and the bytes read back are still the input's:
    //  - a source that cannot seek (recovery that scans local headers forward, or Flat OPC input) has no random access to the
    //    stored bytes, and a recovered archive may hold damaged data that must not be carried into the output;
    //  - an item that was edited, or created in memory, has no stored form to copy.
    private void CopyOrWrite(ZipWriter zw, Entry e)
    {
        if (e.IsUntouched && e.SourceIndex >= 0 && _zip is { CanCopyRaw: true } zip && !zip.Preloaded.ContainsKey(e.SourceIndex))
        {
            ZipEntry source = zip.Entries[e.SourceIndex];
            if (source.IsSizeKnown)
            {
                try
                {
                    using Stream raw = zip.Reader.OpenRawEntryStream(source);
                    zw.WriteRawEntry(ZipRawEntry.FromEntry(source), raw);
                    return;
                }
                catch (ArchiveUnsupportedException)
                {
                    // An entry the raw API cannot take (for example an unsupported layout) is recompressed below.
                }
            }
        }

        if (e.TryGetCached(out ReadOnlyMemory<byte> bytes))
        {
            zw.WriteEntry(new ZipEntry { Name = e.ZipName, Size = bytes.Length, ModifiedTime = e.Modified }, bytes);
            return;
        }

        using Stream source2 = OpenSourceStream(e);
        zw.WriteEntry(new ZipEntry { Name = e.ZipName, Size = e.Length, ModifiedTime = e.Modified }, source2);
    }

    // ---- part content -------------------------------------------------------------------------------------------------------

    private Stream OpenSourceStream(Entry e)
    {
        ZipSource zip = _zip ?? throw new ObjectDisposedException(nameof(OpcPackage));
        try
        {
            return zip.Reader.OpenEntryStream(zip.Entries[e.SourceIndex]);
        }
        catch (CompressionException ex)
        {
            throw ArchiveErrors.Translate(ex, DefectLocation.InPart(e.Name));
        }
    }

    private byte[] ReadEntry(Entry e)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_zip is null, this);

            long max = Limits.MaxPartSize;
            DefectLocation where = DefectLocation.InPart(e.Name);
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), max, e.Length, where);
            if (e.Length > Array.MaxLength)
            {
                throw new OfficeLimitException(nameof(OfficeLimits.MaxPartSize), Array.MaxLength, e.Length, where);
            }

            try
            {
                using Stream s = OpenSourceStream(e);
                byte[] buffer = new byte[e.Length > 0 ? (int)e.Length : 4096];
                int n = 0;
                while (true)
                {
                    if (n == buffer.Length)
                    {
                        int probe = s.ReadByte();
                        if (probe < 0)
                        {
                            break;
                        }

                        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), max, (long)n + 1, where);
                        int next = (int)Math.Min(Math.Min((long)buffer.Length * 2, max), Array.MaxLength);
                        if (next <= n)
                        {
                            throw new OfficeLimitException(nameof(OfficeLimits.MaxPartSize), max, (long)n + 1, where);
                        }

                        Array.Resize(ref buffer, next);
                        buffer[n++] = (byte)probe;
                        continue;
                    }

                    int got = s.Read(buffer, n, buffer.Length - n);
                    if (got <= 0)
                    {
                        break;
                    }

                    n += got;
                }

                Interlocked.Increment(ref _loadedParts);
                if (n != buffer.Length)
                {
                    Array.Resize(ref buffer, n);
                }

                return buffer;
            }
            catch (CompressionException ex)
            {
                throw ArchiveErrors.Translate(ex, where);
            }
        }
    }

    // ---- types --------------------------------------------------------------------------------------------------------------

    private enum EntryKind
    {
        Part,
        ContentTypes,
        Rels,
        Foreign,
    }

    private sealed class RelState
    {
        public Entry? Entry;
        public string? Source;
        public List<Relationship>? List;
        public bool Changed;
    }

    // The zip as opened: the reader, the entries of its directory, and (recovery without a usable directory) content read ahead.
    private sealed class ZipSource : IDisposable
    {
        private readonly IByteSource _bytes;

        private ZipSource(IByteSource bytes, ZipReader reader, List<ZipEntry> entries, Dictionary<int, byte[]> preloaded, List<ZipDamage> damage)
        {
            _bytes = bytes;
            Reader = reader;
            Entries = entries;
            Preloaded = preloaded;
            Damage = damage;
        }

        public ZipReader Reader { get; }

        public bool CanCopyRaw { get; init; }

        public List<ZipEntry> Entries { get; }

        public Dictionary<int, byte[]> Preloaded { get; }

        public List<ZipDamage> Damage { get; }

        public static ZipSource Enumerate(IByteSource bytes, OfficeLimits limits, bool recovery)
        {
            Stream stream = ByteSource.AsStream(bytes);
            var options = new ZipReaderOptions { Limits = limits.ToArchiveLimits(), LazyDirectory = !recovery, Recovery = recovery };
            var reader = new ZipReader(stream, options, leaveOpen: true);
            var entries = new List<ZipEntry>();
            var preloaded = new Dictionary<int, byte[]>();
            try
            {
                while (reader.GetNextEntry() is { } entry)
                {
                    if (!reader.SupportsRandomAccess && entry.Kind != ArchiveEntryKind.Directory)
                    {
                        // Recovery without a usable directory reads forward only, so each item is read as it is met.
                        try
                        {
                            using Stream s = reader.OpenEntryStream();
                            using var ms = new MemoryStream();
                            s.CopyTo(ms);
                            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), limits.MaxPartSize, ms.Length, DefectLocation.InPart(entry.Name));
                            preloaded[entries.Count] = ms.ToArray();
                        }
                        catch (CompressionFormatException)
                        {
                            continue;
                        }
                    }

                    entries.Add(entry);
                    OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartCount), limits.MaxPartCount, entries.Count);
                }
            }
            catch
            {
                reader.Dispose();
                throw;
            }

            return new ZipSource(bytes, reader, entries, preloaded, [.. reader.Damage]) { CanCopyRaw = !recovery && reader.SupportsRandomAccess };
        }

        public void Dispose()
        {
            Reader.Dispose();
            _bytes.Dispose();
        }
    }

    private sealed class Entry : IPackagePart
    {
        private readonly OpcPackage _owner;
        private ReadOnlyMemory<byte>? _content;
        private long _length = -1;

        public Entry(OpcPackage owner, EntryKind kind, string name, string zipName, string contentType)
        {
            _owner = owner;
            Kind = kind;
            Name = name;
            ZipName = zipName;
            ContentType = contentType;
        }

        public EntryKind Kind { get; }

        public string Name { get; }

        public string ZipName { get; }

        public string ContentType { get; set; }

        public int SourceIndex { get; set; } = -1;

        public bool IsDirectory { get; init; }

        public DateTimeOffset? Modified { get; init; }

        public long Length => _content?.Length ?? _length;

        public bool IsUntouched { get; set; }

        public void Initialise(ReadOnlyMemory<byte>? content, long length)
        {
            _content = content;
            _length = content?.Length ?? length;
        }

        public bool TryGetCached(out ReadOnlyMemory<byte> bytes)
        {
            if (_content is { } c)
            {
                bytes = c;
                return true;
            }

            bytes = default;
            return false;
        }

        public ReadOnlyMemory<byte> GetBytes()
        {
            if (_content is { } c)
            {
                return c;
            }

            lock (_owner._sync)
            {
                if (_content is { } again)
                {
                    return again;
                }

                byte[] loaded = _owner.ReadEntry(this);
                _content = loaded;
                _length = loaded.Length;
                return loaded;
            }
        }

        public Stream OpenRead()
        {
            if (_content is { } c)
            {
                return new MemoryReadStream(c);
            }

            // A part too big to want in memory is read straight from the container, each read taking the package's lock.
            if (IsUntouched && SourceIndex >= 0 && _length > StreamingThreshold)
            {
                OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxTotalUncompressedSize), _owner.Limits.MaxTotalUncompressedSize, _length, DefectLocation.InPart(Name));
                lock (_owner._sync)
                {
                    return new LockedStream(_owner._sync, _owner.OpenSourceStream(this));
                }
            }

            return new MemoryReadStream(GetBytes());
        }

        public void SetBytes(ReadOnlyMemory<byte> content)
        {
            Set(content);
        }

        public Stream OpenWrite()
        {
            IsUntouched = false;
            return new CommitStream(this);
        }

        public void Set(ReadOnlyMemory<byte> content)
        {
            _content = content;
            _length = content.Length;
            IsUntouched = false;
            SourceIndex = -1;
        }

        private const long StreamingThreshold = 8L << 20;

        private sealed class CommitStream(Entry part) : MemoryStream
        {
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    part.Set(new ReadOnlyMemory<byte>(GetBuffer(), 0, (int)Length)); // the buffer is ours alone once disposed; no second copy of a large part
                }

                base.Dispose(disposing);
            }
        }
    }
}
