using System.Linq;
using RecursiveAI.OneArchive;
using RecursiveAI.OneArchive.Cfb;
using RecursiveAI.OneCompress;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Cfb;

/// <summary>A storage (a directory) of a compound file, with the metadata a rewrite keeps.</summary>
/// <param name="Path">The storage's path, <c>/</c>-separated with a leading slash, in the form <see cref="CfbPackage"/> names streams (<c>/ObjectPool</c>).</param>
/// <param name="ClassId">The storage's CLSID ([MS-CFB] 2.6.1); <see cref="Guid.Empty"/> when it has none.</param>
/// <param name="StateBits">The user-defined state bits.</param>
/// <param name="CreationFileTime">The creation time as a raw FILETIME, 0 when not recorded.</param>
/// <param name="ModifiedFileTime">The modification time as a raw FILETIME, 0 when not recorded.</param>
public sealed record CfbStorage(string Path, Guid ClassId, uint StateBits, ulong CreationFileTime, ulong ModifiedFileTime);

/// <summary>A stream of a compound file, presented as a part of a <see cref="CfbPackage"/>.</summary>
public sealed class CfbStreamPart : IPackagePart
{
    private readonly CfbPackage _owner;
    private ReadOnlyMemory<byte>? _content;
    private long _length;

    internal CfbStreamPart(CfbPackage owner, string name, CfbEntry? source, long length, bool untouched)
    {
        _owner = owner;
        Name = name;
        Source = source;
        _length = length;
        IsUntouched = untouched;
        DeclaredLength = source?.DeclaredSize ?? length;
        IsDamaged = source?.IsDamaged ?? false;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string ContentType => string.Empty;

    /// <inheritdoc />
    public long Length => _content?.Length ?? _length;

    /// <inheritdoc />
    public bool IsUntouched { get; internal set; }

    /// <summary>Gets the length the directory entry declares. It differs from <see cref="Length"/> only when recovery shortened a damaged stream.</summary>
    public long DeclaredLength { get; }

    /// <summary>Gets a value indicating whether recovery found the stream damaged and returned less than it should hold (its sector chain loops, leaves the file or is cut short).</summary>
    public bool IsDamaged { get; }

    internal CfbEntry? Source { get; set; }

    internal bool IsCached => _content is not null;

    internal bool TryGetCached(out ReadOnlyMemory<byte> bytes)
    {
        if (_content is { } c)
        {
            bytes = c;
            return true;
        }

        bytes = default;
        return false;
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> GetBytes()
    {
        if (_content is { } c)
        {
            return c;
        }

        byte[] loaded = _owner.ReadStream(this);
        _content = loaded;
        _length = loaded.Length;
        return loaded;
    }

    /// <inheritdoc />
    public Stream OpenRead() => _content is { } c ? new MemoryReadStream(c) : _owner.OpenSourceStreamLocked(this);

    /// <inheritdoc />
    public void SetBytes(ReadOnlyMemory<byte> content)
    {
        _content = content;
        _length = content.Length;
        IsUntouched = false;
    }

    /// <inheritdoc />
    public Stream OpenWrite()
    {
        IsUntouched = false;
        return new CommitStream(this);
    }

    private sealed class CommitStream(CfbStreamPart part) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                part.SetBytes(ToArray());
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>
/// A compound file ([MS-CFB]) presented as a package: every stream is a part, named by its path (<c>/WordDocument</c>,
/// <c>/ObjectPool/_1234/CONTENTS</c>). It is the container under DOC, XLS and PPT, VBA projects and encrypted OOXML, behind the
/// same part and package contract as the zip-based package.
/// </summary>
/// <remarks>
/// <para>
/// Names are the stored names joined by <c>/</c>, so <c>\u0005SummaryInformation</c> keeps its control character. A stored name that
/// contains <c>/</c> or <c>%</c> has those two characters written as <c>%002F</c> and <c>%0025</c>. Lookups compare exactly first and then
/// ignoring case, because [MS-CFB] §2.6.4 treats names as case-insensitive.
/// </para>
/// <para>
/// Opening reads the header, the allocation tables and the directory; a stream's bytes are read when first asked for. Saving an
/// unmodified package copies the original file; once anything changes, the file is rewritten by the compound-file writer, so
/// the streams' contents are identical but the sector layout is the writer's. Damage that the reader's recovery mode can work
/// around is reported as defects (<c>packaging.cfb-*</c>) with the stream path and file offset.
/// </para>
/// <para>Packages are not safe for concurrent mutation; after open, reading parts from several threads is safe.</para>
/// </remarks>
public sealed class CfbPackage : IPackage
{
    private readonly object _sync = new();
    private readonly OpenContext _context;
    private readonly List<CfbStreamPart> _parts = [];
    private readonly Dictionary<string, CfbStreamPart> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CfbStreamPart> _folded = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CfbStorage> _storages = [];
    private readonly HashSet<string> _storageNames = new(StringComparer.OrdinalIgnoreCase);
    private IByteSource? _bytes;
    private CompoundFileReader? _reader;
    private CfbEntry? _rootEntry;
    private Dictionary<string, CfbEntry> _entriesByPath = new(StringComparer.Ordinal);
    private int _majorVersion = 3;
    private bool _removed;
    private bool _disposed;
    private int _loadedParts;

    private CfbPackage(OpenContext context, string? filePath)
    {
        _context = context;
        FilePath = filePath;
    }

    /// <inheritdoc />
    public PackageKind Kind => PackageKind.CompoundFile;

    /// <inheritdoc />
    public OfficeLimits Limits => _context.Limits;

    /// <inheritdoc />
    public string? FilePath { get; }

    /// <summary>Gets where the package reports defects found while reading.</summary>
    public IDefectSink Defects => _context.Defects;

    /// <inheritdoc />
    public IReadOnlyList<IPackagePart> Parts => _parts;

    /// <summary>Gets the streams, typed.</summary>
    public IReadOnlyList<CfbStreamPart> Streams => _parts;

    /// <summary>Gets the storages other than the root, in the order the directory lists them.</summary>
    public IReadOnlyList<CfbStorage> Storages => _storages;

    /// <summary>Gets or sets the CLSID of the root storage ([MS-CFB] §2.6.1); it names the application that wrote the file in some formats.</summary>
    public Guid RootClassId { get; set; }

    /// <summary>Gets how many streams' contents have been read from the file so far. Opening reads none.</summary>
    public int LoadedPartCount => Volatile.Read(ref _loadedParts);

    /// <inheritdoc />
    public bool IsModified
    {
        get
        {
            if (_removed || (_rootEntry is not null && RootClassId != _rootEntry.ClassId) || (_rootEntry is null && (_parts.Count > 0 || RootClassId != Guid.Empty)))
            {
                return true;
            }

            foreach (CfbStreamPart p in _parts)
            {
                if (!p.IsUntouched)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Creates an empty compound file in memory.</summary>
    /// <param name="limits">The limits; null for the defaults.</param>
    /// <returns>The new, empty package.</returns>
    public static CfbPackage Create(OfficeLimits? limits = null) => new(new OpenContext(limits), null);

    /// <summary>Opens a compound file from a path.</summary>
    /// <param name="path">The file.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <returns>The open package. Dispose it to release the file.</returns>
    /// <exception cref="OfficeException">The file is not a compound file, is damaged beyond repair, or exceeds a limit.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static CfbPackage Open(string path, OpenContext? context = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return OpenOwned(ByteSource.FromFile(path), Path.GetFullPath(path), context);
    }

    /// <summary>Opens a compound file from a seekable stream.</summary>
    /// <param name="stream">The stream, which must stay readable for the life of the package.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="leaveOpen">True to leave the stream open when the package is disposed.</param>
    /// <returns>The open package.</returns>
    /// <exception cref="OfficeException">The stream is not a compound file, is damaged beyond repair, or exceeds a limit.</exception>
    public static CfbPackage Open(Stream stream, OpenContext? context = null, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return OpenOwned(ByteSource.FromStream(stream, leaveOpen), null, context);
    }

    /// <summary>Opens a compound file from a byte source, which the package then owns and disposes with itself.</summary>
    /// <param name="source">The bytes; it must support synchronous reads.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <returns>The open package.</returns>
    /// <exception cref="OfficeException">The source is not a compound file, is damaged beyond repair, or exceeds a limit.</exception>
    public static CfbPackage Open(IByteSource source, OpenContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return OpenOwned(source, null, context);
    }

    /// <summary>Opens a compound file without raising for input the library rejects; the fatal defect goes to the context's sink.</summary>
    /// <param name="source">The bytes; disposed when this returns false.</param>
    /// <param name="package">The package, or null.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <returns>True if the source opened.</returns>
    public static bool TryOpen(IByteSource source, out CfbPackage? package, OpenContext? context = null)
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
        return _exact.TryGetValue(name, out CfbStreamPart? p) ? p : _folded.GetValueOrDefault(name);
    }

    /// <inheritdoc />
    /// <remarks>Parent storages are created as needed. The name is a path of stored names (see the type's remarks); each is 1 to 31 UTF-16 code units without NUL, <c>/</c>, <c>\</c>, <c>:</c> or <c>!</c> ([MS-CFB] §2.6.1), unique ignoring case within its storage.</remarks>
    public IPackagePart AddPart(string name, string contentType, ReadOnlyMemory<byte> content)
    {
        ArgumentNullException.ThrowIfNull(name);
        ObjectDisposedException.ThrowIf(_disposed, this);
        string[] components = Split(name);
        if (components.Length == 0)
        {
            throw new ArgumentException("A stream needs a name.", nameof(name));
        }

        foreach (string c in components)
        {
            if (c.Length is 0 or > 31 || c.AsSpan().IndexOfAny("\0/\\:!") >= 0)
            {
                throw new ArgumentException($"'{c}' in '{name}' is not a valid compound-file name ([MS-CFB] 2.6.1).", nameof(name));
            }
        }

        string full = "/" + string.Join('/', components.Select(Escape));
        if (_folded.ContainsKey(full))
        {
            throw new ArgumentException($"A stream named '{name}' already exists.", nameof(name));
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartCount), Limits.MaxPartCount, _parts.Count + 1L);
        var part = new CfbStreamPart(this, full, null, content.Length, untouched: false);
        part.SetBytes(content);
        Register(part);
        return part;
    }

    /// <inheritdoc />
    public bool RemovePart(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (FindPart(name) is not CfbStreamPart p)
        {
            return false;
        }

        _parts.Remove(p);
        _exact.Remove(p.Name);
        _folded.Remove(p.Name);
        _removed = true;
        return true;
    }

    /// <inheritdoc />
    /// <remarks>An unmodified package copies the original file byte for byte.</remarks>
    public void WriteTo(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            if (!IsModified && _bytes is not null && _reader is not null && !_reader.IsRecovered)
            {
                CopyOriginal(destination, cancellationToken);
                return;
            }

            WriteRebuilt(destination, cancellationToken);
        }
    }

    /// <inheritdoc />
    public void Save(CancellationToken cancellationToken = default)
    {
        if (FilePath is null)
        {
            throw new InvalidOperationException("The package was not opened from a file; use SaveAs.");
        }

        SaveCore(FilePath, rebase: true, cancellationToken);
    }

    /// <inheritdoc />
    public void SaveAs(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        bool same = FilePath is not null && string.Equals(Path.GetFullPath(path), FilePath, StringComparison.Ordinal);
        SaveCore(path, same, cancellationToken);
    }

    /// <summary>Releases the file or stream the package reads from.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_sync)
        {
            CloseSource();
        }
    }

    // ---- reading ------------------------------------------------------------------------------------------------------------

    private static CfbPackage OpenOwned(IByteSource source, string? filePath, OpenContext? context)
    {
        OpenContext ctx = context ?? new OpenContext();
        var pkg = new CfbPackage(ctx, filePath);
        try
        {
            pkg.Load(source, recovery: false);
        }
        catch (CompressionFormatException first)
        {
            pkg._reader?.Dispose();
            pkg._reader = null;
            try
            {
                pkg.ResetModel();
                pkg.Load(source, recovery: true);
            }
            catch (CompressionException)
            {
                source.Dispose();
                throw ArchiveErrors.Translate(first, DefectLocation.InRecord(string.Empty, first.Offset));
            }
            catch
            {
                source.Dispose();
                throw;
            }
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

        return pkg;
    }

    private void ResetModel()
    {
        _parts.Clear();
        _exact.Clear();
        _folded.Clear();
        _storages.Clear();
        _storageNames.Clear();
        _entriesByPath = new Dictionary<string, CfbEntry>(StringComparer.Ordinal);
    }

    // Reads the directory and builds the parts. The reader is kept so stream bytes can be read later.
    private void Load(IByteSource source, bool recovery)
    {
        _bytes = source;
        var options = new CompoundFileReaderOptions { Limits = Limits.ToArchiveLimits(), Recovery = recovery };
        _reader = source.TryGetMemory(out ReadOnlyMemory<byte> memory)
            ? new CompoundFileReader(memory, options)
            : new CompoundFileReader(ByteSource.AsStream(source), options);
        _majorVersion = _reader.Header.MajorVersion;
        _rootEntry = _reader.Root;
        RootClassId = _rootEntry.ClassId;
        foreach (CfbDamage d in _reader.Damage)
        {
            Report(d);
        }

        long total = 0;
        int count = 0;
        Walk(_reader.Root, string.Empty, 0, ref count, ref total);
    }

    private void Walk(CfbEntry storage, string path, int depth, ref int count, ref long total)
    {
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecursionDepth), Limits.MaxRecursionDepth, depth + 1L, DefectLocation.InRecord(path.Length == 0 ? "/" : path));
        foreach (CfbEntry child in _reader!.GetChildren(storage))
        {
            string name = path + "/" + Escape(child.RawName);
            count++;
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartCount), Limits.MaxPartCount, count, DefectLocation.InRecord(name));
            if (child.ObjectType == CfbObjectType.Storage)
            {
                // [MS-CFB] 2.6.4: sibling names are unique ignoring case; a storage whose name repeats an earlier storage or stream is dropped with its subtree.
                if (!_storageNames.Add(name) || _folded.ContainsKey(name))
                {
                    ContentTypesReport("packaging.duplicate-stream-name", DefectSeverity.Dropped, $"The storage '{name}' has the same name, ignoring case, as an earlier entry; it is not exposed.", name, -1);
                    continue;
                }

                _storages.Add(new CfbStorage(name, child.ClassId, child.StateBits, child.CreationFileTime, child.ModifiedFileTime));
                Walk(child, name, depth + 1, ref count, ref total);
                continue;
            }

            if (child.ObjectType != CfbObjectType.Stream)
            {
                continue;
            }

            total += child.Size;
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxTotalUncompressedSize), Limits.MaxTotalUncompressedSize, total, DefectLocation.InRecord(name));
            if (_folded.ContainsKey(name) || _storageNames.Contains(name))
            {
                ContentTypesReport("packaging.duplicate-stream-name", DefectSeverity.Dropped, $"The stream '{name}' has the same name, ignoring case, as an earlier one; it is not exposed.", name, -1);
                continue;
            }

            var part = new CfbStreamPart(this, name, child, child.Size, untouched: true);
            Register(part);
            _entriesByPath[name] = child;
        }
    }

    private void Register(CfbStreamPart part)
    {
        _parts.Add(part);
        _exact[part.Name] = part;
        _folded[part.Name] = part;
    }

    private void Report(CfbDamage d) => _context.Defects.Report(ToDefect(d));

    internal static Defect ToDefect(CfbDamage d)
    {
        (string code, string what) = d.Kind switch
        {
            CfbDamageKind.HeaderInconsistent => ("packaging.cfb-header-inconsistent", "a header field disagrees with the file"),
            CfbDamageKind.Truncated => ("packaging.cfb-truncated", "the file ends inside a structure"),
            CfbDamageKind.AllocationTableUnusable => ("packaging.cfb-allocation-table-unusable", "the FAT or DIFAT is damaged"),
            CfbDamageKind.MiniStreamUnusable => ("packaging.cfb-mini-stream-unusable", "the mini FAT or mini stream is damaged"),
            CfbDamageKind.DirectoryUnusable => ("packaging.cfb-directory-unusable", "the directory chain is damaged"),
            CfbDamageKind.BadEntry => ("packaging.cfb-bad-entry", "a directory entry has invalid fields"),
            CfbDamageKind.BadLink => ("packaging.cfb-bad-link", "a sibling or child link is invalid"),
            CfbDamageKind.OrphanedEntry => ("packaging.cfb-orphaned-entry", "an entry is not reachable from the root"),
            _ => ("packaging.cfb-damaged-stream", "a stream's sector chain is damaged"),
        };
        string where = d.EntryName is null ? string.Empty : "/" + Escape(d.EntryName);
        return new Defect(
            DefectKind.Packaging,
            code,
            DefectSeverity.Repaired,
            $"{what} ({d.Reason}); file offset {d.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture)}. Recovery used what was readable.",
            DefectLocation.InRecord(where, d.Offset));
    }

    private void ContentTypesReport(string code, DefectSeverity severity, string message, string name, long offset) =>
        _context.Defects.Report(new Defect(DefectKind.Packaging, code, severity, message, DefectLocation.InRecord(name, offset)));

    // ---- stream content -----------------------------------------------------------------------------------------------------

    internal byte[] ReadStream(CfbStreamPart part)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_reader is null, this);
            DefectLocation where = DefectLocation.InRecord(part.Name);
            long max = Limits.MaxPartSize;
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), max, part.Length, where);
            try
            {
                using Stream s = _reader.OpenEntryStream(part.Source ?? throw new InvalidOperationException("The stream has no source."));
                byte[] buffer = new byte[(int)Math.Min(Math.Max(part.Length, 0), Array.MaxLength)];
                int n = 0;
                while (n < buffer.Length)
                {
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

    internal Stream OpenSourceStreamLocked(CfbStreamPart part)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_reader is null, this);
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), Limits.MaxPartSize, part.Length, DefectLocation.InRecord(part.Name));
            try
            {
                return new LockedStream(_sync, _reader.OpenEntryStream(part.Source ?? throw new InvalidOperationException("The stream has no source.")));
            }
            catch (CompressionException ex)
            {
                throw ArchiveErrors.Translate(ex, DefectLocation.InRecord(part.Name));
            }
        }
    }

    // ---- writing ------------------------------------------------------------------------------------------------------------

    private void CopyOriginal(Stream destination, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1 << 16];
        long offset = 0;
        while (offset < _bytes!.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int n = _bytes.Read(offset, buffer);
            if (n <= 0)
            {
                break;
            }

            destination.Write(buffer, 0, n);
            offset += n;
        }
    }

    private void WriteRebuilt(Stream destination, CancellationToken cancellationToken)
    {
        try
        {
            using var w = new CompoundFileWriter(destination, new CompoundFileWriterOptions { MajorVersion = _majorVersion, StrictNames = false }, leaveOpen: true);
            w.WriteEntry(new CfbEntry
            {
                Name = string.Empty,
                RawName = string.Empty,
                Kind = ArchiveEntryKind.Directory,
                ClassId = RootClassId,
                StateBits = _rootEntry?.StateBits ?? 0,
                CreationFileTime = _rootEntry?.CreationFileTime ?? 0,
                ModifiedFileTime = _rootEntry?.ModifiedFileTime ?? 0,
            });
            foreach (CfbStorage s in _storages)
            {
                w.WriteEntry(new CfbEntry
                {
                    Name = ToWriterPath(s.Path),
                    RawName = string.Empty,
                    Kind = ArchiveEntryKind.Directory,
                    ClassId = s.ClassId,
                    StateBits = s.StateBits,
                    CreationFileTime = s.CreationFileTime,
                    ModifiedFileTime = s.ModifiedFileTime,
                });
            }

            foreach (CfbStreamPart p in _parts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadOnlyMemory<byte> bytes = p.GetBytes();
                CfbEntry? src = p.Source;
                w.WriteEntry(
                    new CfbEntry
                    {
                        Name = ToWriterPath(p.Name),
                        RawName = string.Empty,
                        Kind = ArchiveEntryKind.File,
                        Size = bytes.Length,
                        ClassId = src?.ClassId ?? Guid.Empty,
                        StateBits = src?.StateBits ?? 0,
                        CreationFileTime = src?.CreationFileTime ?? 0,
                        ModifiedFileTime = src?.ModifiedFileTime ?? 0,
                    },
                    bytes);
            }

            w.Finish();
        }
        catch (CompressionException ex)
        {
            throw ArchiveErrors.Translate(ex);
        }
    }

    private void SaveCore(string path, bool rebase, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!rebase)
        {
            AtomicFile.Write(path, s => WriteTo(s, cancellationToken));
            return;
        }

        bool released = false;
        try
        {
            AtomicFile.Write(path, s =>
            {
                lock (_sync)
                {
                    // Everything is read into memory by the rebuild, so the file being replaced can be released before the
                    // rename (platforms that cannot rename over an open file need that).
                    foreach (CfbStreamPart p in _parts)
                    {
                        _ = p.GetBytes();
                    }

                    WriteTo(s, cancellationToken);
                    CloseSource();
                    released = true;
                }
            });
        }
        catch
        {
            if (released)
            {
                lock (_sync)
                {
                    Reattach(path);
                }
            }

            throw;
        }

        lock (_sync)
        {
            Reattach(path);
            foreach (CfbStreamPart p in _parts)
            {
                p.IsUntouched = true;
            }

            _removed = false;
        }
    }

    // Opens the file again and points each stream at its entry there, by name; streams already in memory keep their bytes.
    private void Reattach(string path)
    {
        IByteSource src = ByteSource.FromFile(path);
        var options = new CompoundFileReaderOptions { Limits = Limits.ToArchiveLimits() };
        var reader = new CompoundFileReader(ByteSource.AsStream(src), options);
        _bytes = src;
        _reader = reader;
        _rootEntry = reader.Root;
        var map = new Dictionary<string, CfbEntry>(StringComparer.Ordinal);
        MapEntries(reader.Root, string.Empty, map);
        foreach (CfbStreamPart p in _parts)
        {
            p.Source = map.GetValueOrDefault(p.Name);
        }
    }

    private void MapEntries(CfbEntry storage, string path, Dictionary<string, CfbEntry> map)
    {
        foreach (CfbEntry child in _reader!.GetChildren(storage))
        {
            string name = path + "/" + Escape(child.RawName);
            if (child.ObjectType == CfbObjectType.Storage)
            {
                MapEntries(child, name, map);
            }
            else
            {
                map[name] = child;
            }
        }
    }

    private void CloseSource()
    {
        _reader?.Dispose();
        _reader = null;
        _bytes?.Dispose();
        _bytes = null;
    }

    // ---- names --------------------------------------------------------------------------------------------------------------

    // The compound-file writer takes paths in the reader's escaped form, one component at a time.
    private static string ToWriterPath(string path) => string.Join('/', Split(path).Select(CompoundFileReader.SanitizeName));

    private static string[] Split(string name) => name.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Unescape).ToArray();

    // A stored name may hold '/' or '%', which the path form cannot; they are written as %002F and %0025.
    internal static string Escape(string stored) =>
        stored.AsSpan().IndexOfAny('/', '%') < 0 ? stored : stored.Replace("%", "%0025", StringComparison.Ordinal).Replace("/", "%002F", StringComparison.Ordinal);

    internal static string Unescape(string path)
    {
        if (!path.Contains('%', StringComparison.Ordinal))
        {
            return path;
        }

        // Component by component so a path keeps its separators.
        return string.Join('/', path.Split('/').Select(c => c.Replace("%002F", "/", StringComparison.Ordinal).Replace("%0025", "%", StringComparison.Ordinal)));
    }
}
