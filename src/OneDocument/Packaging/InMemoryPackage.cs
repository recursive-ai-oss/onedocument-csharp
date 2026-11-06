namespace RecursiveAI.OneDocument.Packaging;

/// <summary>
/// A minimal <see cref="IOpcPackage"/> held in memory. It exists to pin down the contract (lazy loading, the untouched state, the
/// atomic save, limits on part size) for tests and for code that builds a package before any real container exists. It is
/// not a file container itself: <see cref="WriteTo"/> writes the parts and relationships as an Open Packaging Conventions zip.
/// The zip-based and compound-file packages implement the same interfaces over real containers.
/// </summary>
public sealed class InMemoryPackage : IOpcPackage
{
    private readonly List<Part> _parts = [];
    private readonly Dictionary<string, Part> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Relationship>> _relationships = new(StringComparer.OrdinalIgnoreCase);
    private bool _removed;
    private bool _relationshipsChanged;
    private bool _disposed;

    /// <summary>Creates an empty package.</summary>
    /// <param name="limits">The limits to apply; null for <see cref="OfficeLimits.Default"/>.</param>
    /// <param name="filePath">The file <see cref="Save"/> replaces, or null for none.</param>
    public InMemoryPackage(OfficeLimits? limits = null, string? filePath = null)
    {
        Limits = limits ?? OfficeLimits.Default;
        FilePath = filePath;
    }

    /// <inheritdoc />
    public PackageKind Kind => PackageKind.InMemory;

    /// <inheritdoc />
    public OfficeLimits Limits { get; }

    /// <inheritdoc />
    public string? FilePath { get; }

    /// <inheritdoc />
    public IReadOnlyList<IPackagePart> Parts => _parts;

    /// <inheritdoc />
    public bool IsModified
    {
        get
        {
            if (_removed || _relationshipsChanged)
            {
                return true;
            }

            foreach (Part p in _parts)
            {
                if (!p.IsUntouched)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Adds a part as if it had been loaded from a file: it starts untouched, and its content is produced by
    /// <paramref name="load"/> the first time it is asked for (and not before).
    /// </summary>
    /// <param name="name">The part name, a valid <see cref="PartUri"/>.</param>
    /// <param name="contentType">The content type.</param>
    /// <param name="length">The uncompressed length, or -1 if not known.</param>
    /// <param name="load">Produces the content.</param>
    /// <returns>The part.</returns>
    /// <exception cref="ArgumentException">The name is invalid or already used.</exception>
    public IPackagePart AddLoadedPart(string name, string contentType, long length, Func<ReadOnlyMemory<byte>> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        return Add(new Part(this, name, contentType, length, load, untouched: true));
    }

    /// <inheritdoc />
    public IPackagePart? FindPart(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.GetValueOrDefault(name);
    }

    /// <inheritdoc />
    public IPackagePart AddPart(string name, string contentType, ReadOnlyMemory<byte> content)
    {
        var part = new Part(this, name, contentType, content.Length, null, untouched: false);
        part.Content = content;
        return Add(part);
    }

    /// <inheritdoc />
    public bool RemovePart(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_byName.Remove(name, out Part? part))
        {
            return false;
        }

        _parts.Remove(part);
        _removed = true;
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<Relationship> GetRelationships(string? sourcePartName = null) =>
        _relationships.TryGetValue(sourcePartName ?? string.Empty, out List<Relationship>? list) ? list : [];

    /// <inheritdoc />
    public void SetRelationships(string? sourcePartName, IReadOnlyList<Relationship> relationships)
    {
        ArgumentNullException.ThrowIfNull(relationships);
        _relationships[sourcePartName ?? string.Empty] = [.. relationships];
        _relationshipsChanged = true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Writes an Open Packaging Conventions zip with the parts, their content types and the relationships, so the result can be
    /// opened again with <see cref="OpcPackage.Open(Stream, OpenContext?, bool)"/> or sniffed like any other package file. A part
    /// without a content type is written as <c>application/octet-stream</c>.
    /// </remarks>
    public void WriteTo(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        using OpcPackage zip = OpcPackage.Create(Limits);
        foreach (Part p in _parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            zip.AddPart(p.Name, p.ContentType.Length == 0 ? "application/octet-stream" : p.ContentType, p.GetBytes());
        }

        foreach (KeyValuePair<string, List<Relationship>> r in _relationships)
        {
            if (r.Value.Count > 0)
            {
                zip.SetRelationships(r.Key.Length == 0 ? null : r.Key, r.Value);
            }
        }

        zip.WriteTo(destination, cancellationToken);
    }

    /// <inheritdoc />
    public void Save(CancellationToken cancellationToken = default)
    {
        if (FilePath is null)
        {
            throw new InvalidOperationException("The package was not opened from a file; use SaveAs.");
        }

        SaveAs(FilePath, cancellationToken);
    }

    /// <inheritdoc />
    public void SaveAs(string path, CancellationToken cancellationToken = default) =>
        IO.AtomicFile.Write(path, s => WriteTo(s, cancellationToken));

    /// <inheritdoc />
    public void Dispose() => _disposed = true;

    private Part Add(Part part)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!PartUri.IsValid(part.Name))
        {
            throw new ArgumentException($"'{part.Name}' is not a valid part name.", nameof(part));
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartCount), Limits.MaxPartCount, _parts.Count + 1);
        if (!_byName.TryAdd(part.Name, part))
        {
            throw new ArgumentException($"A part named '{part.Name}' already exists.", nameof(part));
        }

        _parts.Add(part);
        return part;
    }

    private sealed class Part : IPackagePart
    {
        private readonly InMemoryPackage _owner;
        private Func<ReadOnlyMemory<byte>>? _load;
        private ReadOnlyMemory<byte>? _content;

        public Part(InMemoryPackage owner, string name, string contentType, long length, Func<ReadOnlyMemory<byte>>? load, bool untouched)
        {
            _owner = owner;
            Name = name;
            ContentType = contentType;
            Length = length;
            _load = load;
            IsUntouched = untouched;
        }

        public string Name { get; }

        public string ContentType { get; }

        public long Length { get; private set; }

        public bool IsUntouched { get; private set; }

        public ReadOnlyMemory<byte> Content
        {
            set => _content = value;
        }

        public Stream OpenRead() => new MemoryStream(GetBytes().ToArray(), writable: false);

        public ReadOnlyMemory<byte> GetBytes()
        {
            if (_content is { } done)
            {
                return done;
            }

            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), _owner.Limits.MaxPartSize, Length, DefectLocationOf());
            ReadOnlyMemory<byte> loaded = _load!();
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), _owner.Limits.MaxPartSize, loaded.Length, DefectLocationOf());
            _content = loaded;
            Length = loaded.Length;
            _load = null;
            return loaded;
        }

        public void SetBytes(ReadOnlyMemory<byte> content)
        {
            _content = content;
            _load = null;
            Length = content.Length;
            IsUntouched = false;
        }

        public Stream OpenWrite()
        {
            IsUntouched = false;
            return new CommitStream(this);
        }

        private Diagnostics.DefectLocation DefectLocationOf() => Diagnostics.DefectLocation.InPart(Name);

        private sealed class CommitStream(Part part) : MemoryStream
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
}
