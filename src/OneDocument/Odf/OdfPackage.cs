using System.Buffers;
using System.Text;
using RecursiveAI.OneArchive;
using RecursiveAI.OneArchive.Zip;
using RecursiveAI.OneCompress;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Odf;

/// <summary>One file of an <see cref="OdfPackage"/>.</summary>
/// <param name="Name">The package path, <c>/</c>-separated and relative (<c>content.xml</c>, <c>Pictures/image1.png</c>); a directory ends with <c>/</c>.</param>
/// <param name="MediaType">The media type the manifest gives it; empty when it names none.</param>
/// <param name="Length">The uncompressed size in bytes, or -1 when it is not known.</param>
/// <param name="IsDirectory">True for a directory entry.</param>
public readonly record struct OdfEntry(string Name, string MediaType, long Length, bool IsDirectory);

/// <summary>
/// An OpenDocument file: the zip package (OpenDocument 1.3 Part 3) or a flat XML document (<c>office:document</c>, Part 1), with
/// <c>content.xml</c>, <c>styles.xml</c>, <c>meta.xml</c> and <c>settings.xml</c> as lossless markup trees and every other file kept
/// as bytes. It serves text, spreadsheet and presentation documents alike.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lossless.</b> A file read and written without edits writes each entry's stored bytes again, copied raw (not decompressed and
/// recompressed). A part whose tree was edited is written from the tree, which keeps unknown elements, attributes, prefixes and
/// order. The manifest is rewritten only when an entry was added, removed or retyped.
/// </para>
/// <para>
/// <b>Written packages</b> start with an uncompressed <c>mimetype</c> entry holding the media type with no extra field, as Part 3
/// requires for the type to be found without reading the zip, then the remaining entries in their order.
/// </para>
/// <para>
/// <b>Tolerant reading.</b> A missing or misplaced <c>mimetype</c>, a missing manifest, a manifest naming a file that is not there
/// and a damaged zip directory are defects (<c>packaging.odf-*</c>, <c>packaging.zip-damaged</c>); the package opens anyway. A part that is not
/// well-formed XML raises its <c>xml.*</c> error when first used. Encrypted packages are refused with <see cref="OfficeUnsupportedException"/>.
/// </para>
/// <para>Not safe for concurrent mutation; concurrent reads are safe once the parts are loaded.</para>
/// </remarks>
public sealed class OdfPackage : IDisposable
{
    /// <summary>The name of the media type file.</summary>
    public const string MimetypeName = "mimetype";

    /// <summary>The name of the manifest.</summary>
    public const string ManifestName = "META-INF/manifest.xml";

    /// <summary>The name of the content part.</summary>
    public const string ContentName = "content.xml";

    /// <summary>The name of the styles part.</summary>
    public const string StylesName = "styles.xml";

    /// <summary>The name of the metadata part.</summary>
    public const string MetaName = "meta.xml";

    /// <summary>The name of the settings part.</summary>
    public const string SettingsName = "settings.xml";

    private readonly object _sync = new();
    private readonly List<Item> _items = [];
    private readonly Dictionary<string, Item> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MarkupDocument?> _markup = new(StringComparer.Ordinal);
    private ZipHandle? _zip;
    private MarkupDocument? _flat;
    private OdfManifest _manifest = new();
    private OdfStyleSet? _styles;
    private OdfMetadata? _metadata;
    private bool _disposed;
    private bool _structureChanged;

    private OdfPackage(OpenContext context, string? filePath, bool flat)
    {
        Context = context;
        FilePath = filePath;
        IsFlat = flat;
        MimeType = string.Empty;
        Version = "1.3";
    }

    /// <summary>Gets the limits and defect sink the package was opened with.</summary>
    public OpenContext Context { get; }

    /// <summary>Gets the full path the package was read from, or null.</summary>
    public string? FilePath { get; }

    /// <summary>Gets a value indicating whether the file is a flat XML document rather than a zip package.</summary>
    public bool IsFlat { get; }

    /// <summary>Gets the media type of the document, for example <c>application/vnd.oasis.opendocument.text</c>; empty when the file names none and none could be inferred.</summary>
    public string MimeType { get; private set; }

    /// <summary>Gets the <c>office:version</c> of the document root ("1.3" when absent or unreadable).</summary>
    public string Version { get; private set; }

    /// <summary>Gets the manifest. A flat document has an empty one.</summary>
    public OdfManifest Manifest => _manifest;

    /// <summary>Gets the files of the package in zip order. A flat document has none.</summary>
    public IReadOnlyList<OdfEntry> Entries
    {
        get
        {
            var list = new List<OdfEntry>(_items.Count);
            foreach (Item i in _items)
            {
                string mt = _manifest.TryGet(i.Name, out OdfManifestEntry m) ? m.MediaType : string.Empty;
                list.Add(new OdfEntry(i.Name, mt, i.Length, i.IsDirectory));
            }

            return list;
        }
    }

    /// <summary>Gets a value indicating whether anything has been changed since the package was read: an entry set or removed, or a part's tree edited.</summary>
    public bool IsModified
    {
        get
        {
            if (_structureChanged || _manifest.IsModified)
            {
                return true;
            }

            if (_flat is { IsModified: true })
            {
                return true;
            }

            foreach (MarkupDocument? d in _markup.Values)
            {
                if (d is { IsModified: true })
                {
                    return true;
                }
            }

            return false;
        }
    }

    // ---- opening ---------------------------------------------------------------------------------------------------------------

    /// <summary>Creates a new, empty package of a document type, with minimal <c>content.xml</c>, <c>styles.xml</c> and <c>meta.xml</c>.</summary>
    /// <param name="mimeType">The media type, for example <see cref="OdfMediaTypes.Text"/>.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="version">The ODF version written into the root elements and the manifest; "1.3" by default.</param>
    /// <returns>The package. Add a body to <see cref="ContentRoot"/> and entries with <see cref="SetEntry"/>.</returns>
    public static OdfPackage Create(string mimeType, OpenContext? context = null, string version = "1.3")
    {
        ArgumentException.ThrowIfNullOrEmpty(mimeType);
        var pkg = new OdfPackage(context ?? new OpenContext(), null, flat: false) { MimeType = mimeType, Version = version };
        pkg._manifest = new OdfManifest(version) { IsModified = true };
        pkg._structureChanged = true;
        string body = OdfMediaTypes.BodyElement(mimeType);
        pkg.AddNew(ContentName, Encoding.UTF8.GetBytes(
            Declaration + "<office:document-content" + RootNamespaces + " office:version=\"" + version + "\"><office:body><office:" + body + "/></office:body></office:document-content>"));
        pkg.AddNew(StylesName, Encoding.UTF8.GetBytes(
            Declaration + "<office:document-styles" + RootNamespaces + " office:version=\"" + version + "\"/>"));
        pkg.AddNew(MetaName, Encoding.UTF8.GetBytes(
            Declaration + "<office:document-meta" + RootNamespaces + " office:version=\"" + version + "\"><office:meta/></office:document-meta>"));
        foreach (string n in new[] { ContentName, StylesName, MetaName })
        {
            pkg._manifest.Set(n, "text/xml");
        }

        return pkg;
    }

    private const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";

    private const string RootNamespaces =
        " xmlns:office=\"" + OdfNamespaces.Office + "\" xmlns:style=\"" + OdfNamespaces.Style + "\" xmlns:text=\"" + OdfNamespaces.Text +
        "\" xmlns:table=\"" + OdfNamespaces.Table + "\" xmlns:draw=\"" + OdfNamespaces.Draw + "\" xmlns:fo=\"" + OdfNamespaces.Fo +
        "\" xmlns:xlink=\"" + OdfNamespaces.XLink + "\" xmlns:dc=\"" + OdfNamespaces.DublinCore + "\" xmlns:meta=\"" + OdfNamespaces.Meta +
        "\" xmlns:number=\"" + OdfNamespaces.Number + "\" xmlns:svg=\"" + OdfNamespaces.Svg + "\"";

    /// <summary>
    /// Opens an OpenDocument file: a zip package or a flat XML document, told apart by content. The source is read on demand and kept
    /// until the package is disposed.
    /// </summary>
    /// <param name="source">The bytes; the package owns and disposes it.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="filePath">The path the bytes came from, for locations and <see cref="FilePath"/>; may be null.</param>
    /// <returns>The package.</returns>
    /// <exception cref="OfficeFormatException">The content is neither an OpenDocument zip package nor a flat document (<c>packaging.not-odf</c>), the zip is damaged beyond repair (<c>packaging.damaged</c>), or a flat document is not well-formed XML (<c>xml.*</c>).</exception>
    /// <exception cref="OfficeUnsupportedException">The package is encrypted.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static OdfPackage Open(IByteSource source, OpenContext? context = null, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        OpenContext ctx = context ?? new OpenContext();
        try
        {
            byte[] head = new byte[8];
            int n = source.Read(0, head);
            if (n >= 2 && head[0] == (byte)'P' && head[1] == (byte)'K')
            {
                return OpenZip(source, filePath, ctx);
            }

            return OpenFlat(source, filePath, ctx);
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

    private static OdfPackage OpenFlat(IByteSource source, string? filePath, OpenContext ctx)
    {
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), ctx.Limits.MaxPartSize, source.Length, DefectLocation.InPart(filePath ?? "(flat)", 0));
        byte[] all;
        if (source.TryGetMemory(out ReadOnlyMemory<byte> mem))
        {
            all = mem.ToArray();
        }
        else
        {
            all = new byte[source.Length];
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

            if (read < all.Length)
            {
                all = all[..read];
            }
        }

        source.Dispose();
        MarkupDocument doc = MarkupDocument.Parse(all, ctx, filePath ?? "(flat)");
        MarkupNode root = doc.Root;
        if (root.IsNull || !root.IsElement(OdfNamespaces.Office, "document"))
        {
            throw new OfficeFormatException(DefectKind.Packaging, "packaging.not-odf", "The XML document is not an OpenDocument flat document (its root is not office:document).", DefectLocation.InPart(filePath ?? "(flat)", Math.Max(0, root.IsNull ? 0 : root.SourceStart)));
        }

        var pkg = new OdfPackage(ctx, filePath, flat: true) { _flat = doc };
        pkg.MimeType = OdfMarkup.Attribute(root, OdfNamespaces.Office, "mimetype") ?? string.Empty;
        pkg.Version = OdfMarkup.Attribute(root, OdfNamespaces.Office, "version") ?? "1.3";
        if (pkg.MimeType.Length == 0)
        {
            pkg.MimeType = OdfMediaTypes.FromBody(OdfMarkup.Child(root, OdfNamespaces.Office, "body"));
            if (pkg.MimeType.Length > 0)
            {
                Report(ctx, "packaging.odf-no-mimetype", DefectSeverity.Repaired, "The flat document has no office:mimetype; the type was taken from the body.", filePath ?? "(flat)");
            }
        }

        return pkg;
    }

    private static OdfPackage OpenZip(IByteSource source, string? filePath, OpenContext ctx)
    {
        var pkg = new OdfPackage(ctx, filePath, flat: false);
        ZipHandle? zip = null;
        try
        {
            try
            {
                zip = ZipHandle.Enumerate(source, ctx.Limits, recovery: false);
            }
            catch (CompressionFormatException first)
            {
                // The directory or an entry header is damaged: scan local headers instead and say what had to be skipped.
                try
                {
                    zip = ZipHandle.Enumerate(source, ctx.Limits, recovery: true);
                    Report(ctx, "packaging.zip-damaged", DefectSeverity.Repaired, "The zip structure is damaged (" + first.Message + "); the entries were recovered.", string.Empty, first.Offset);
                }
                catch (CompressionException)
                {
                    throw first;
                }
            }

            pkg._zip = zip;
            foreach (ZipDamage d in zip.Damage)
            {
                Report(ctx, "packaging.zip-damaged", DefectSeverity.Repaired, $"{d.Kind}: {d.Reason}", d.EntryName ?? string.Empty, d.Offset);
            }

            pkg.Classify(zip);
            pkg.ReadMimetypeAndManifest();
            return pkg;
        }
        catch
        {
            (zip ?? pkg._zip)?.Dispose();
            pkg._zip = null;
            throw;
        }
    }

    private void Classify(ZipHandle zip)
    {
        for (int i = 0; i < zip.Entries.Count; i++)
        {
            ZipEntry z = zip.Entries[i];
            string name = z.Name;
            bool dir = z.Kind == ArchiveEntryKind.Directory || name.EndsWith('/');
            if (name.Length == 0 || name.Contains('\0') || name.StartsWith('/') || name.Contains('\\') || HasDotSegment(name))
            {
                Report(Context, "packaging.invalid-part-name", DefectSeverity.Dropped, "A stored item has an empty, absolute or relative-segment name; it is left out.", name.Replace('\0', '?'));
                continue;
            }

            if (_byName.ContainsKey(name))
            {
                Report(Context, "packaging.duplicate-part-name", DefectSeverity.Dropped, "The package stores '" + name + "' twice; the first is used.", name);
                continue;
            }

            zip.Preloaded.TryGetValue(i, out byte[]? pre);
            var item = new Item(name)
            {
                SourceIndex = pre is null ? i : -1,
                Bytes = pre,
                IsDirectory = dir,
                Modified = z.ModifiedTime,
                Length = pre?.Length ?? (z.IsSizeKnown ? z.Size : -1),
                Position = _items.Count,
            };
            _items.Add(item);
            _byName[name] = item;
        }
    }

    private static bool HasDotSegment(string name)
    {
        foreach (string seg in name.Split('/'))
        {
            if (seg == "..")
            {
                return true;
            }
        }

        return false;
    }

    private void ReadMimetypeAndManifest()
    {
        if (_byName.TryGetValue(MimetypeName, out Item? mt) && !mt.IsDirectory)
        {
            MimeType = Encoding.UTF8.GetString(ReadItem(mt).Span).Trim();
            if (mt.Position != 0)
            {
                Report(Context, "packaging.odf-mimetype-not-first", DefectSeverity.Repaired, "The mimetype entry is not the first entry; it is written first when the package is saved.", MimetypeName);
            }
            else if (mt.SourceIndex >= 0 && _zip!.Entries[mt.SourceIndex].Method != ZipMethod.Store)
            {
                Report(Context, "packaging.odf-mimetype-compressed", DefectSeverity.Repaired, "The mimetype entry is compressed; it is written uncompressed when the package is saved.", MimetypeName);
            }
        }
        else
        {
            Report(Context, "packaging.odf-no-mimetype", DefectSeverity.Repaired, "The package has no mimetype entry; the type is taken from the manifest or the document.", MimetypeName);
        }

        if (_byName.TryGetValue(ManifestName, out Item? man))
        {
            _manifest = OdfManifest.Parse(ReadItem(man), Context, ManifestName);
            foreach (OdfManifestEntry e in _manifest.Entries)
            {
                if (e.IsEncrypted)
                {
                    throw new OfficeUnsupportedException("encrypted OpenDocument package", DefectLocation.InPart(ManifestName));
                }

                if (e.FullPath != "/" && !_byName.ContainsKey(e.FullPath) && !_byName.ContainsKey(e.FullPath.TrimEnd('/') + "/") && !_byName.ContainsKey(e.FullPath.TrimEnd('/')))
                {
                    Report(Context, "packaging.odf-manifest-missing-entry", DefectSeverity.Repaired, "The manifest lists '" + e.FullPath + "', which the package does not hold.", ManifestName);
                }
            }

            if (_manifest.TryGet("/", out OdfManifestEntry root))
            {
                if (MimeType.Length == 0)
                {
                    MimeType = root.MediaType;
                }

                if (root.Version is not null)
                {
                    Version = root.Version;
                }
            }
        }
        else
        {
            Report(Context, "packaging.odf-no-manifest", DefectSeverity.Repaired, "The package has no META-INF/manifest.xml; one is written when the package is saved.", ManifestName);
            _manifest.IsModified = true;
        }

        // A damaged content part is reported when it is first used; probing it for the type and version must not make opening fail.
        MarkupNode contentRoot = default;
        try
        {
            contentRoot = ContentRoot;
        }
        catch (OfficeFormatException)
        {
            // Left for the first real use of the part.
        }

        if (MimeType.Length == 0)
        {
            MimeType = OdfMediaTypes.FromBody(OdfMarkup.Child(contentRoot, OdfNamespaces.Office, "body"));
        }

        if (MimeType.Length == 0 && contentRoot.IsNull)
        {
            throw new OfficeFormatException(DefectKind.Packaging, "packaging.not-odf", "The zip package is not an OpenDocument file: it has no usable mimetype and no content.xml.", DefectLocation.InPart(MimetypeName, 0));
        }

        // The document root's own version wins over the manifest's: it is what consumers see.
        string? v = OdfMarkup.Attribute(contentRoot, OdfNamespaces.Office, "version");
        if (v is not null)
        {
            Version = v;
        }

        if (!_manifest.IsModified && _manifest.Entries.Count == 0)
        {
            _manifest.IsModified = false;
        }
    }

    // ---- entries ---------------------------------------------------------------------------------------------------------------

    /// <summary>Gets a value indicating whether the package holds a file.</summary>
    /// <param name="name">The package path.</param>
    /// <returns>True when present.</returns>
    public bool Contains(string name) => name is not null && _byName.ContainsKey(name);

    /// <summary>Reads a file's bytes (decompressed). The array is cached; do not modify it.</summary>
    /// <param name="name">The package path.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="KeyNotFoundException">There is no such file.</exception>
    /// <exception cref="OfficeLimitException">The file is larger than <see cref="OfficeLimits.MaxPartSize"/>.</exception>
    /// <exception cref="OfficeFormatException">The stored data is damaged.</exception>
    public ReadOnlyMemory<byte> ReadEntry(string name)
    {
        if (!TryReadEntry(name, out ReadOnlyMemory<byte> bytes))
        {
            throw new KeyNotFoundException("The package has no entry '" + name + "'.");
        }

        return bytes;
    }

    /// <summary>Reads a file's bytes if it exists.</summary>
    /// <param name="name">The package path.</param>
    /// <param name="bytes">The bytes.</param>
    /// <returns>True when the file exists.</returns>
    public bool TryReadEntry(string name, out ReadOnlyMemory<byte> bytes)
    {
        if (name is not null && _byName.TryGetValue(name, out Item? item) && !item.IsDirectory)
        {
            bytes = ReadItem(item);
            return true;
        }

        bytes = default;
        return false;
    }

    /// <summary>Adds or replaces a file and gives it a manifest entry.</summary>
    /// <param name="name">The package path (<c>/</c>-separated, relative, no <c>..</c> segment).</param>
    /// <param name="bytes">The content.</param>
    /// <param name="mediaType">The media type for the manifest; empty for none.</param>
    /// <exception cref="ArgumentException">The name is empty, absolute or has a <c>..</c> segment, or the package is flat.</exception>
    public void SetEntry(string name, ReadOnlyMemory<byte> bytes, string mediaType = "")
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (IsFlat)
        {
            throw new InvalidOperationException("A flat OpenDocument file has no entries.");
        }

        if (name.StartsWith('/') || name.Contains('\\') || name.Contains('\0') || HasDotSegment(name))
        {
            throw new ArgumentException("The name must be a relative, '/'-separated package path.", nameof(name));
        }

        lock (_sync)
        {
            _markup.Remove(name);
            if (_byName.TryGetValue(name, out Item? existing))
            {
                existing.Bytes = bytes.ToArray();
                existing.Length = bytes.Length;
                existing.SourceIndex = -1;
            }
            else
            {
                AddNew(name, bytes.ToArray());
            }

            if (name is not (MimetypeName or ManifestName))
            {
                _manifest.Set(name, mediaType);
            }

            _structureChanged = true;
            if (name is ContentName or StylesName)
            {
                _styles = null;
            }

            if (name == MetaName)
            {
                _metadata = null;
            }
        }
    }

    /// <summary>Removes a file and its manifest entry.</summary>
    /// <param name="name">The package path.</param>
    /// <returns>True when the file existed.</returns>
    public bool RemoveEntry(string name)
    {
        lock (_sync)
        {
            if (name is null || !_byName.Remove(name, out Item? item))
            {
                return false;
            }

            _items.Remove(item);
            _markup.Remove(name);
            _manifest.Remove(name);
            _structureChanged = true;
            return true;
        }
    }

    private void AddNew(string name, byte[] bytes)
    {
        var item = new Item(name) { Bytes = bytes, Length = bytes.Length, Position = _items.Count };
        _items.Add(item);
        _byName[name] = item;
    }

    private ReadOnlyMemory<byte> ReadItem(Item item)
    {
        lock (_sync)
        {
            if (item.Bytes is { } cached)
            {
                return cached;
            }

            ObjectDisposedException.ThrowIf(_zip is null, this);
            DefectLocation where = DefectLocation.InPart(item.Name);
            long max = Context.Limits.MaxPartSize;
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), max, item.Length, where);
            try
            {
                using Stream s = _zip.Reader.OpenEntryStream(_zip.Entries[item.SourceIndex]);
                using var ms = new MemoryStream(item.Length is > 0 and < int.MaxValue ? (int)item.Length : 4096);
                byte[] chunk = ArrayPool<byte>.Shared.Rent(81920);
                try
                {
                    int got;
                    while ((got = s.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), max, ms.Length + got, where);
                        ms.Write(chunk, 0, got);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(chunk);
                }

                item.Bytes = ms.ToArray();
                item.Length = item.Bytes.Length;
                return item.Bytes;
            }
            catch (CompressionException ex)
            {
                throw ArchiveErrors.Translate(ex, where);
            }
        }
    }

    // ---- parts as markup -------------------------------------------------------------------------------------------------------

    /// <summary>Gets the content part as a markup tree (the flat document itself for a flat file), or null when the package has none. Parsed on first use.</summary>
    /// <exception cref="OfficeFormatException">The part is not well-formed XML (<c>xml.*</c>), or its root is not <c>office:document-content</c> (<c>packaging.odf-wrong-root</c>).</exception>
    public MarkupDocument? ContentDocument => Part(ContentName, "document-content");

    /// <summary>Gets the styles part as a markup tree (the flat document itself for a flat file), or null when absent.</summary>
    /// <exception cref="OfficeFormatException">As for <see cref="ContentDocument"/>.</exception>
    public MarkupDocument? StylesDocument => Part(StylesName, "document-styles");

    /// <summary>Gets the metadata part as a markup tree (the flat document itself for a flat file), or null when absent.</summary>
    /// <exception cref="OfficeFormatException">As for <see cref="ContentDocument"/>.</exception>
    public MarkupDocument? MetaDocument => Part(MetaName, "document-meta");

    /// <summary>Gets the settings part as a markup tree (the flat document itself for a flat file), or null when absent.</summary>
    /// <exception cref="OfficeFormatException">As for <see cref="ContentDocument"/>.</exception>
    public MarkupDocument? SettingsDocument => Part(SettingsName, "document-settings");

    /// <summary>
    /// Gets the element that holds <c>office:body</c>, <c>office:automatic-styles</c> and <c>office:font-face-decls</c> of the content: the root of
    /// <c>content.xml</c>, or the <c>office:document</c> root of a flat file. A null node when there is no content.
    /// </summary>
    public MarkupNode ContentRoot => ContentDocument?.Root ?? default;

    /// <summary>Gets the element that holds <c>office:styles</c>, <c>office:automatic-styles</c> and <c>office:master-styles</c>: the root of <c>styles.xml</c>, or the flat root. Null when absent.</summary>
    public MarkupNode StylesRoot => StylesDocument?.Root ?? default;

    /// <summary>Gets the element that holds <c>office:meta</c>: the root of <c>meta.xml</c>, or the flat root. Null when absent.</summary>
    public MarkupNode MetaRoot => MetaDocument?.Root ?? default;

    /// <summary>Gets the element that holds <c>office:settings</c>: the root of <c>settings.xml</c>, or the flat root. Null when absent.</summary>
    public MarkupNode SettingsRoot => SettingsDocument?.Root ?? default;

    /// <summary>Gets the <c>office:body</c> element, or a null node.</summary>
    public MarkupNode Body => OdfMarkup.Child(ContentRoot, OdfNamespaces.Office, "body");

    /// <summary>Gets the style families of the document, built once from <c>styles.xml</c> and <c>content.xml</c> (or the flat root).</summary>
    public OdfStyleSet Styles => _styles ??= OdfStyleSet.Load(this);

    /// <summary>Gets the document properties of <c>meta.xml</c>.</summary>
    public OdfMetadata Metadata => _metadata ??= new OdfMetadata(MetaRoot);

    private MarkupDocument? Part(string name, string rootLocal)
    {
        if (IsFlat)
        {
            return _flat;
        }

        lock (_sync)
        {
            if (_markup.TryGetValue(name, out MarkupDocument? cached))
            {
                return cached;
            }

            if (!_byName.TryGetValue(name, out Item? item) || item.IsDirectory)
            {
                _markup[name] = null;
                return null;
            }

            MarkupDocument doc = MarkupDocument.Parse(ReadItem(item), Context, name);
            if (doc.Root.IsNull || !doc.Root.IsElement(OdfNamespaces.Office, rootLocal))
            {
                throw new OfficeFormatException(DefectKind.Structure, "packaging.odf-wrong-root", name + " does not start with office:" + rootLocal + ".", DefectLocation.InPart(name, Math.Max(0, doc.Root.IsNull ? 0 : doc.Root.SourceStart)));
            }

            _markup[name] = doc;
            return doc;
        }
    }

    // ---- writing ---------------------------------------------------------------------------------------------------------------

    /// <summary>Writes the package as an OpenDocument zip. Untouched entries are copied raw; edited parts are written from their trees.</summary>
    /// <param name="destination">The stream.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="OfficeUnsupportedException">The package is a flat document (use <see cref="WriteFlatTo"/>).</exception>
    /// <exception cref="OfficeFormatException">A stored entry is damaged.</exception>
    public void WriteTo(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsFlat)
        {
            throw new OfficeUnsupportedException("writing a flat OpenDocument file as a zip package", DefectLocation.None);
        }

        lock (_sync)
        {
            try
            {
                using var zw = new ZipWriter(destination, new ZipWriterOptions { Method = ZipMethod.Deflate }, leaveOpen: true);
                byte[] mime = Encoding.UTF8.GetBytes(MimeType);
                WriteStored(zw, MimetypeName, mime);

                bool manifestWritten = false;
                foreach (Item item in _items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.Name is MimetypeName)
                    {
                        continue;
                    }

                    if (item.Name == ManifestName)
                    {
                        WriteManifest(zw);
                        manifestWritten = true;
                        continue;
                    }

                    WriteItem(zw, item);
                }

                if (!manifestWritten)
                {
                    WriteManifest(zw);
                }

                zw.Finish();
            }
            catch (CompressionException ex)
            {
                throw ArchiveErrors.Translate(ex);
            }
        }
    }

    private void WriteManifest(ZipWriter zw)
    {
        if (!_manifest.IsModified && _byName.TryGetValue(ManifestName, out Item? m))
        {
            WriteItem(zw, m);
            return;
        }

        byte[] bytes = _manifest.ToBytes(MimeType);
        zw.WriteEntry(new ZipEntry { Name = ManifestName, Size = bytes.Length }, bytes);
    }

    private void WriteItem(ZipWriter zw, Item item)
    {
        if (item.IsDirectory)
        {
            zw.WriteEntry(new ZipEntry { Name = item.Name, Kind = ArchiveEntryKind.Directory, ModifiedTime = item.Modified });
            return;
        }

        byte[]? edited = null;
        if (_markup.TryGetValue(item.Name, out MarkupDocument? doc) && doc is { IsModified: true })
        {
            edited = doc.ToArray();
        }

        if (edited is null && item.Bytes is null && item.SourceIndex >= 0 && _zip is { CanCopyRaw: true } zip && zip.Entries[item.SourceIndex].IsSizeKnown)
        {
            try
            {
                ZipEntry source = zip.Entries[item.SourceIndex];
                using Stream raw = zip.Reader.OpenRawEntryStream(source);
                zw.WriteRawEntry(ZipRawEntry.FromEntry(source), raw);
                return;
            }
            catch (ArchiveUnsupportedException)
            {
                // An entry the raw API cannot take is recompressed below; its bytes read back the same.
            }
        }

        ReadOnlyMemory<byte> bytes = edited ?? ReadItem(item);
        if (item.Name == MimetypeName)
        {
            WriteStored(zw, item.Name, bytes.Span);
            return;
        }

        zw.WriteEntry(new ZipEntry { Name = item.Name, Size = bytes.Length, ModifiedTime = item.Modified }, bytes);
    }

    // Part 3 asks for the mimetype entry to be stored, with no extra field: the stored header carries the type at a fixed place.
    private static void WriteStored(ZipWriter zw, string name, ReadOnlySpan<byte> bytes)
    {
        var header = new ZipRawEntry
        {
            RawName = Encoding.ASCII.GetBytes(name),
            Method = ZipMethod.Store,
            Crc32 = Crc32(bytes),
            CompressedSize = bytes.Length,
            UncompressedSize = bytes.Length,
        };
        using var content = new MemoryStream(bytes.ToArray(), writable: false);
        zw.WriteRawEntry(header, content);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static readonly uint[] CrcTable = MakeCrcTable();

    private static uint[] MakeCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            t[n] = c;
        }

        return t;
    }

    /// <summary>
    /// Writes the document as a flat XML document (<c>office:document</c>): the metadata, settings, fonts, styles and body in one file, with
    /// packaged pictures inlined as <c>office:binary-data</c>. A flat package writes its own tree.
    /// </summary>
    /// <param name="destination">The stream.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public void WriteFlatTo(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsFlat)
        {
            _flat!.WriteTo(destination);
            return;
        }

        OdfFlat.Write(this, destination, cancellationToken);
    }

    /// <summary>Saves the package to a path as a zip (or, for a flat package, as flat XML), by writing a temporary file next to it and renaming it over the target.</summary>
    /// <param name="path">The target file.</param>
    /// <param name="cancellationToken">Cancels the save; an existing file at the path stays as it was.</param>
    public void SaveAs(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string full = Path.GetFullPath(path);
        string temp = full + ".tmp" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (IsFlat)
                {
                    WriteFlatTo(fs, cancellationToken);
                }
                else
                {
                    WriteTo(fs, cancellationToken);
                }
            }

            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // The temporary file is left behind only when the disk refuses the delete too.
            }

            throw;
        }
    }

    /// <summary>Releases the source the package reads from.</summary>
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

    private static void Report(OpenContext ctx, string code, DefectSeverity severity, string message, string part, long offset = -1) =>
        ctx.Defects.Report(new Defect(DefectKind.Packaging, code, severity, message, DefectLocation.InPart(part, offset)));

    private sealed class Item(string name)
    {
        public string Name { get; } = name;

        public int SourceIndex { get; set; } = -1;

        public byte[]? Bytes { get; set; }

        public bool IsDirectory { get; init; }

        public DateTimeOffset? Modified { get; init; }

        public long Length { get; set; } = -1;

        public int Position { get; set; }
    }

    private sealed class ZipHandle : IDisposable
    {
        private readonly IByteSource _bytes;

        private ZipHandle(IByteSource bytes, ZipReader reader, List<ZipEntry> entries, Dictionary<int, byte[]> preloaded, List<ZipDamage> damage, bool canCopyRaw)
        {
            _bytes = bytes;
            Reader = reader;
            Entries = entries;
            Preloaded = preloaded;
            Damage = damage;
            CanCopyRaw = canCopyRaw;
        }

        public ZipReader Reader { get; }

        public List<ZipEntry> Entries { get; }

        public Dictionary<int, byte[]> Preloaded { get; }

        public List<ZipDamage> Damage { get; }

        public bool CanCopyRaw { get; }

        public static ZipHandle Enumerate(IByteSource bytes, OfficeLimits limits, bool recovery)
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
                        // Recovery without a usable directory reads forward only, so each file is read as it is met.
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

            return new ZipHandle(bytes, reader, entries, preloaded, [.. reader.Damage], !recovery && reader.SupportsRandomAccess);
        }

        public void Dispose()
        {
            Reader.Dispose();
            _bytes.Dispose();
        }
    }
}
