using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>Relationship type matching that accepts the Transitional and the Strict spelling of a type.</summary>
internal static class RelationshipTypes
{
    private const string Transitional = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Strict = "http://purl.oclc.org/ooxml/officeDocument/relationships/";

    /// <summary>True when <paramref name="type"/> is the officeDocument-family relationship <paramref name="name"/> in either spelling.</summary>
    public static bool Is(string type, string name) =>
        (type.StartsWith(Transitional, StringComparison.Ordinal) && string.CompareOrdinal(type, Transitional.Length, name, 0, name.Length) == 0 && type.Length == Transitional.Length + name.Length)
        || (type.StartsWith(Strict, StringComparison.Ordinal) && string.CompareOrdinal(type, Strict.Length, name, 0, name.Length) == 0 && type.Length == Strict.Length + name.Length);
}

/// <summary>
/// The markup trees of the parts a document has read, by part name. A part is parsed on first use and stays in the cache, so every
/// hub object over it shares one tree; <see cref="Flush"/> writes the edited ones back to the package.
/// </summary>
internal sealed class PartStore
{
    private readonly IOpcPackage _package;
    private readonly OpenContext _context;
    private readonly Dictionary<string, MarkupDocument?> _documents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Guards first-use initialisation of everything the document reads lazily, so concurrent readers share one tree and one hub
    /// object per part. It is re-entrant: a lazy object may load parts while it is being built.
    /// </summary>
    public object Gate { get; } = new();

    public PartStore(IOpcPackage package, OpenContext context)
    {
        _package = package;
        _context = context;
    }

    public bool HasEdits
    {
        get
        {
            lock (Gate)
            {
                foreach (MarkupDocument? d in _documents.Values)
                {
                    if (d is { IsModified: true })
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <summary>The part name a parsed tree was read from, or null for a tree this store does not hold.</summary>
    public string? NameOf(MarkupDocument document)
    {
        lock (Gate)
        foreach (KeyValuePair<string, MarkupDocument?> kv in _documents)
        {
            if (ReferenceEquals(kv.Value, document))
            {
                return kv.Key;
            }
        }

        return null;
    }

    /// <summary>Finds a part related to <paramref name="sourcePart"/> by a relationship of the officeDocument family and parses it.</summary>
    public MarkupDocument? LoadRelated(string sourcePart, string relationshipName, out string? partName)
    {
        partName = null;
        foreach (Relationship r in _package.GetRelationships(sourcePart))
        {
            if (r.TargetMode == RelationshipTargetMode.Internal && RelationshipTypes.Is(r.Type, relationshipName)
                && PartUri.TryResolve(new PartUri(sourcePart), r.Target, out PartUri uri) && _package.FindPart(uri.Value) is { } part)
            {
                partName = part.Name;
                return Load(part.Name, required: false);
            }
        }

        return null;
    }

    /// <summary>Parses a part once. An unreadable optional part is reported as a defect and reads as absent.</summary>
    public MarkupDocument? Load(string partName, bool required)
    {
        lock (Gate)
        {
            return LoadLocked(partName, required);
        }
    }

    private MarkupDocument? LoadLocked(string partName, bool required)
    {
        if (_documents.TryGetValue(partName, out MarkupDocument? cached))
        {
            return cached;
        }

        IPackagePart? part = _package.FindPart(partName);
        MarkupDocument? document = null;
        if (part is not null)
        {
            try
            {
                document = MarkupDocument.Parse(part.GetBytes(), _context, part.Name);
            }
            catch (OfficeException ex) when (!required && ex is not OfficeLimitException)
            {
                _context.Defects.Report(new Defect(
                    DefectKind.Packaging,
                    "docx.part-unreadable",
                    DefectSeverity.Dropped,
                    "The part could not be read and is treated as absent: " + ex.Defect.Message,
                    DefectLocation.InPart(part.Name, ex.Location.Offset)));
            }
        }
        else if (required)
        {
            throw new OfficeFormatException(DefectKind.Packaging, "docx.part-missing", "The part " + partName + " does not exist.", DefectLocation.InPart(partName));
        }

        _documents[partName] = document;
        return document;
    }

    /// <summary>Finds the part a node was parsed from; null for a node of a tree this store did not load.</summary>
    public string? PartNameOf(MarkupNode node)
    {
        lock (Gate)
        foreach (KeyValuePair<string, MarkupDocument?> kv in _documents)
        {
            if (kv.Value is not null && ReferenceEquals(kv.Value, node.Document))
            {
                return kv.Key;
            }
        }

        return null;
    }

    /// <summary>Forgets every parsed tree, so the next use reads the part again from the package (after the package was changed underneath).</summary>
    public void Clear()
    {
        lock (Gate)
        {
            _documents.Clear();
        }
    }

    public void Flush()
    {
        lock (Gate)
        foreach (KeyValuePair<string, MarkupDocument?> kv in _documents)
        {
            if (kv.Value is { IsModified: true } doc && _package.FindPart(kv.Key) is { } part)
            {
                part.SetBytes(doc.ToArray());
            }
        }
    }
}
