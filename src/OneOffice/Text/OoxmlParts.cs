using RecursiveAI.OneArchive;
using RecursiveAI.OneArchive.Zip;
using RecursiveAI.OneCompress;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.IO;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Markup.Xml;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.TextExtraction;

/// <summary>
/// The package access the text extractors need: forward streams of parts by name, and relationships. It reads the zip directly so
/// extraction touches only the parts it reads and builds nothing for the rest. It is the one place the extractors know how a package is
/// stored, so they can move onto the OPC package layer by replacing this class.
/// </summary>
internal sealed class OoxmlParts : IDisposable
{
    private readonly OpenContext _context;
    private readonly Stream _stream;
    private readonly ZipReader _zip;
    private readonly Dictionary<string, ZipEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public OoxmlParts(IByteSource source, OpenContext context)
    {
        _context = context;
        _stream = ByteSource.AsStream(source);
        try
        {
            _zip = new ZipReader(_stream, new ZipReaderOptions { Limits = context.Limits.ToArchiveLimits(), LazyDirectory = true }, leaveOpen: false);
            while (_zip.GetNextEntry() is { } entry)
            {
                string name = entry.Name;
                if (name.Length > 0 && name[^1] != '/')
                {
                    _entries.TryAdd(name, entry);
                }
            }
        }
        catch (CompressionException ex)
        {
            _stream.Dispose();
            throw ArchiveErrors.Translate(ex);
        }
    }

    public bool Contains(string partName) => _entries.ContainsKey(Key(partName));

    /// <summary>Opens a part for forward reading.</summary>
    public Stream Open(string partName) => _zip.OpenEntryStream(_entries[Key(partName)]);

    /// <summary>Gets the main part, named by the package relationship of type <c>officeDocument</c> (ECMA-376 Part 2 §9.3).</summary>
    public string? FindMainPart()
    {
        foreach (Relationship rel in ReadRelationships(null))
        {
            if (rel.Type.EndsWith("/officeDocument", StringComparison.Ordinal) && rel.TargetMode == RelationshipTargetMode.Internal
                && PartUri.TryResolve(null, rel.Target, out PartUri uri) && Contains(uri.Value))
            {
                return uri.Value;
            }
        }

        return null;
    }

    /// <summary>Reads the relationships of a part (or of the package, for null).</summary>
    public List<Relationship> ReadRelationships(string? sourcePart)
    {
        var result = new List<Relationship>();
        string relsName;
        if (sourcePart is null)
        {
            relsName = "/_rels/.rels";
        }
        else
        {
            int slash = sourcePart.LastIndexOf('/');
            relsName = sourcePart[..(slash + 1)] + "_rels/" + sourcePart[(slash + 1)..] + ".rels";
        }

        if (!Contains(relsName))
        {
            return result;
        }

        using Stream s = Open(relsName);
        using var r = new MarkupReader(s, _context, relsName, leaveOpen: true);
        while (r.Read())
        {
            if (r.Kind is not (XmlTokenKind.StartElement or XmlTokenKind.EmptyElement) || !r.LocalName.SequenceEqual("Relationship"u8))
            {
                continue;
            }

            string? id = null;
            string? type = null;
            string? target = null;
            bool external = false;
            foreach (XmlAttributeToken a in r.Attributes)
            {
                if (a.IsNamespaceDeclaration || a.Prefix.Length != 0)
                {
                    continue;
                }

                ReadOnlySpan<byte> n = a.LocalName;
                if (n.SequenceEqual("Id"u8))
                {
                    id = XmlText.GetString(a.RawValue, true);
                }
                else if (n.SequenceEqual("Type"u8))
                {
                    type = XmlText.GetString(a.RawValue, true);
                }
                else if (n.SequenceEqual("Target"u8))
                {
                    target = XmlText.GetString(a.RawValue, true);
                }
                else if (n.SequenceEqual("TargetMode"u8))
                {
                    external = a.RawValue.SequenceEqual("External"u8);
                }
            }

            if (id is not null && type is not null && target is not null)
            {
                result.Add(new Relationship(id, type, target, external ? RelationshipTargetMode.External : RelationshipTargetMode.Internal));
            }
        }

        return result;
    }

    /// <summary>Resolves an internal relationship target against the part that holds the relationship.</summary>
    public static string? Resolve(string? sourcePart, Relationship rel)
    {
        if (rel.TargetMode != RelationshipTargetMode.Internal)
        {
            return null;
        }

        PartUri? source = sourcePart is not null && PartUri.TryCreate(sourcePart, out PartUri s) ? s : null;
        return PartUri.TryResolve(source, rel.Target, out PartUri resolved) ? resolved.Value : null;
    }

    public void Dispose() => _zip.Dispose();

    private static string Key(string partName) => partName.Length > 0 && partName[0] == '/' ? partName[1..] : partName;
}
