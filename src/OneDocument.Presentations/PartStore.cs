using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>
/// The markup trees of the parts a presentation has read, by part name. A part is parsed on first use and stays in the cache, so
/// every hub object over it shares one tree; <see cref="Flush"/> writes the edited ones back to the package.
/// </summary>
internal sealed class PartStore
{
    private readonly IOpcPackage _package;
    private readonly OpenContext _context;
    private readonly Dictionary<string, MarkupDocument?> _documents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Guards first-use initialisation of everything read lazily; re-entrant, because a lazy object may load parts while it is built.</summary>
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

    /// <summary>Parses a part once. A required part that is missing or not well-formed raises; an optional one is reported as a defect and reads as absent.</summary>
    public MarkupDocument? Load(string partName, bool required)
    {
        lock (Gate)
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
                        "pptx.part-unreadable",
                        DefectSeverity.Dropped,
                        "The part could not be read and is treated as absent: " + ex.Defect.Message,
                        DefectLocation.InPart(part.Name, ex.Location.Offset)));
                }
            }
            else if (required)
            {
                throw new OfficeFormatException(DefectKind.Packaging, "pptx.part-missing", "The part " + partName + " does not exist.", DefectLocation.InPart(partName));
            }

            _documents[partName] = document;
            return document;
        }
    }

    /// <summary>Drops the cached tree of a part that was removed or replaced, so a later part of the same name is read afresh.</summary>
    public void Forget(string partName)
    {
        lock (Gate)
        {
            _documents.Remove(partName);
        }
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
        {
            foreach (KeyValuePair<string, MarkupDocument?> kv in _documents)
            {
                if (kv.Value is { IsModified: true } doc && _package.FindPart(kv.Key) is { } part)
                {
                    part.SetBytes(doc.ToArray());
                }
            }
        }
    }
}
