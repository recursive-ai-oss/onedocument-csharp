using System.Buffers;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;

namespace RecursiveAI.OneDocument.Odf;

/// <summary>One <c>manifest:file-entry</c>: a file of the package and its media type.</summary>
/// <param name="FullPath">The package path; <c>/</c> for the document itself, a trailing <c>/</c> for a directory.</param>
/// <param name="MediaType">The media type; empty for a directory or a file with none.</param>
/// <param name="Version">The <c>manifest:version</c> of the entry, when given (the document's version for <c>/</c>).</param>
/// <param name="IsEncrypted">True when the entry has <c>manifest:encryption-data</c>. Encrypted packages are not read.</param>
public readonly record struct OdfManifestEntry(string FullPath, string MediaType, string? Version = null, bool IsEncrypted = false);

/// <summary>
/// The package manifest, <c>META-INF/manifest.xml</c> (OpenDocument 1.3 Part 3, Packages): the list of the files in the package with their
/// media types. Reading is tolerant: an unreadable manifest is a defect and an empty list. Writing is deterministic.
/// </summary>
public sealed class OdfManifest
{
    private readonly List<OdfManifestEntry> _entries = [];

    internal OdfManifest(string version = "1.3")
    {
        Version = version;
    }

    /// <summary>Gets the <c>manifest:version</c> of the root element ("1.3" when absent).</summary>
    public string Version { get; private set; }

    /// <summary>Gets the entries in file order.</summary>
    public IReadOnlyList<OdfManifestEntry> Entries => _entries;

    /// <summary>Gets a value indicating whether an entry was added, removed or changed since the manifest was read, so the package writes a new manifest.</summary>
    public bool IsModified { get; internal set; }

    /// <summary>Finds an entry by its full path.</summary>
    /// <param name="fullPath">The path as written in the manifest.</param>
    /// <param name="entry">The entry.</param>
    /// <returns>True when found.</returns>
    public bool TryGet(string fullPath, out OdfManifestEntry entry)
    {
        foreach (OdfManifestEntry e in _entries)
        {
            if (string.Equals(e.FullPath, fullPath, StringComparison.Ordinal))
            {
                entry = e;
                return true;
            }
        }

        entry = default;
        return false;
    }

    internal void Set(string fullPath, string mediaType)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].FullPath, fullPath, StringComparison.Ordinal))
            {
                if (_entries[i].MediaType != mediaType)
                {
                    _entries[i] = _entries[i] with { MediaType = mediaType };
                    IsModified = true;
                }

                return;
            }
        }

        _entries.Add(new OdfManifestEntry(fullPath, mediaType));
        IsModified = true;
    }

    internal bool Remove(string fullPath)
    {
        int n = _entries.RemoveAll(e => string.Equals(e.FullPath, fullPath, StringComparison.Ordinal));
        if (n > 0)
        {
            IsModified = true;
        }

        return n > 0;
    }

    internal static OdfManifest Parse(ReadOnlyMemory<byte> bytes, OpenContext context, string partName)
    {
        var manifest = new OdfManifest();
        if (!MarkupDocument.TryParse(bytes, out MarkupDocument? doc, context, partName) || doc is null)
        {
            Report(context, "packaging.odf-manifest-unreadable", DefectSeverity.Repaired, "The manifest is not well-formed XML; it is treated as empty.", partName);
            return manifest;
        }

        MarkupNode root = doc.Root;
        if (root.IsNull || !root.IsElement(OdfNamespaces.Manifest, "manifest"))
        {
            Report(context, "packaging.odf-manifest-unreadable", DefectSeverity.Repaired, "The manifest's root is not manifest:manifest; it is treated as empty.", partName);
            return manifest;
        }

        manifest.Version = OdfMarkup.Attribute(root, OdfNamespaces.Manifest, "version") ?? "1.3";
        foreach (MarkupNode e in root.ChildElements)
        {
            if (!e.IsElement(OdfNamespaces.Manifest, "file-entry"))
            {
                continue;
            }

            string? path = OdfMarkup.Attribute(e, OdfNamespaces.Manifest, "full-path");
            if (path is null)
            {
                Report(context, "packaging.odf-manifest-entry", DefectSeverity.Dropped, "A manifest:file-entry has no full-path; it was left out.", partName, e.SourceStart);
                continue;
            }

            bool encrypted = !OdfMarkup.Child(e, OdfNamespaces.Manifest, "encryption-data").IsNull;
            manifest._entries.Add(new OdfManifestEntry(
                path,
                OdfMarkup.Attribute(e, OdfNamespaces.Manifest, "media-type") ?? string.Empty,
                OdfMarkup.Attribute(e, OdfNamespaces.Manifest, "version"),
                encrypted));
        }

        return manifest;
    }

    internal byte[] ToBytes(string documentMediaType)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new XmlWriter(buffer);
        w.WriteXmlDeclaration(standalone: null);
        w.WriteStartElement("manifest:manifest");
        w.WriteNamespaceDeclaration("manifest", OdfNamespaces.Manifest);
        w.WriteAttribute("manifest:version", Version);
        bool wroteRoot = false;
        foreach (OdfManifestEntry e in _entries)
        {
            if (e.FullPath == "/")
            {
                wroteRoot = true;
            }
        }

        if (!wroteRoot)
        {
            WriteEntry(w, new OdfManifestEntry("/", documentMediaType, Version));
        }

        foreach (OdfManifestEntry e in _entries)
        {
            WriteEntry(w, e);
        }

        w.WriteEndElement();
        w.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteEntry(XmlWriter w, OdfManifestEntry e)
    {
        w.WriteStartElement("manifest:file-entry");
        w.WriteAttribute("manifest:full-path", e.FullPath);
        if (e.Version is not null)
        {
            w.WriteAttribute("manifest:version", e.Version);
        }

        w.WriteAttribute("manifest:media-type", e.MediaType);
        w.WriteEndElement();
    }

    private static void Report(OpenContext context, string code, DefectSeverity severity, string message, string part, long offset = -1) =>
        context.Defects.Report(new Defect(DefectKind.Packaging, code, severity, message, DefectLocation.InPart(part, offset)));
}
