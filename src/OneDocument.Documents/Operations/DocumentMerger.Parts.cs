using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents;

internal sealed partial class DocumentMerger
{
    private readonly Dictionary<(string Source, string Id, string Destination), string> _relationshipMap = [];
    private readonly Dictionary<string, string> _partMap = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string>? _leafIndex;

    // Brings the part a relationship points at into the destination and relates it to the destination part; returns the new relationship id.
    // ECMA-376 Part 2 §9.3 (relationships): ids are unique within the source part, so they are renumbered for the destination part.
    private string MapRelationship(string srcPart, string id, string dstPart)
    {
        var key = (srcPart, id, dstPart);
        if (_relationshipMap.TryGetValue(key, out string? known))
        {
            return known;
        }

        Relationship? rel = _src.Package.GetRelationships(srcPart).FirstOrDefault(r => r.Id == id);
        if (rel is null)
        {
            _r.Problem("merge.relationship-missing", "The copied content refers to relationship '" + id + "', which the source part does not have; the reference was kept as written.",
                DefectLocation.InPart(srcPart));
            return id;
        }

        string newId;
        if (rel.TargetMode == RelationshipTargetMode.External)
        {
            newId = AddRelationship(dstPart, rel.Type, rel.Target, RelationshipTargetMode.External);
            _r.Count("external-links");
        }
        else if (PartUri.TryResolve(new PartUri(srcPart), rel.Target, out PartUri uri) && _src.Package.FindPart(uri.Value) is { } part)
        {
            string name = CopyPart(part, rel.Type);
            newId = AddRelationship(dstPart, rel.Type, RelativeTarget(dstPart, name), RelationshipTargetMode.Internal);
        }
        else
        {
            _r.Problem("merge.part-missing", "Relationship '" + id + "' points at a part the source does not have; the reference was kept as written.", DefectLocation.InPart(srcPart));
            return id;
        }

        _relationshipMap[key] = newId;
        return newId;
    }

    private string CopyPart(IPackagePart part, string relationshipType)
    {
        if (_partMap.TryGetValue(part.Name, out string? done))
        {
            return done;
        }

        if (_same)
        {
            _partMap[part.Name] = part.Name;
            return part.Name;
        }

        if (RelationshipTypes.Is(relationshipType, "header") || RelationshipTypes.Is(relationshipType, "footer"))
        {
            return CopyStoryPart(part, RelationshipTypes.Is(relationshipType, "header") ? "headers" : "footers");
        }

        IReadOnlyList<Relationship> children = _src.Package.GetRelationships(part.Name);
        ReadOnlyMemory<byte> bytes = part.GetBytes();
        bool leaf = children.Count == 0 && IsBinary(part.ContentType);
        string hash = string.Empty;
        if (leaf)
        {
            // Pictures and other binary parts are shared by content: the same bytes are stored once.
            hash = Convert.ToHexString(SHA256.HashData(bytes.Span)) + "|" + part.ContentType;
            _leafIndex ??= BuildLeafIndex();
            if (_leafIndex.TryGetValue(hash, out string? existing))
            {
                _partMap[part.Name] = existing;
                _r.Count("parts-shared");
                return existing;
            }
        }

        string name = UniquePartName(part.Name);
        _dst.Package.AddPart(name, part.ContentType, bytes);
        _partMap[part.Name] = name;
        if (leaf)
        {
            _leafIndex![hash] = name;
        }

        _r.Count(part.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? "images" : "parts");
        if (children.Count > 0)
        {
            var copied = new List<Relationship>();
            foreach (Relationship c in children)
            {
                if (c.TargetMode == RelationshipTargetMode.External)
                {
                    copied.Add(c);
                }
                else if (PartUri.TryResolve(new PartUri(part.Name), c.Target, out PartUri u) && _src.Package.FindPart(u.Value) is { } child)
                {
                    string childName = CopyPart(child, c.Type);
                    copied.Add(c with { Target = RelativeTarget(name, childName) });
                }
            }

            _dst.Package.SetRelationships(name, copied);
        }

        return name;
    }

    private string CopyStoryPart(IPackagePart part, string countName)
    {
        string name = UniquePartName(part.Name);
        _dst.Package.AddPart(name, part.ContentType, "<x/>"u8.ToArray());
        _partMap[part.Name] = name;
        MarkupDocument? doc = _src.Parts.Load(part.Name, required: false);
        byte[] bytes;
        if (doc is null)
        {
            bytes = part.GetBytes().ToArray();
        }
        else
        {
            byte[] xml = MarkupFragment.Serialize(doc.Root, default, MakeOptions(part.Name, name));
            bytes = [.. "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"u8, .. xml];
        }

        _dst.Package.FindPart(name)!.SetBytes(bytes);
        _r.Count(countName);
        return name;
    }

    private static bool IsBinary(string contentType) =>
        !(contentType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("text/xml", StringComparison.OrdinalIgnoreCase));

    private Dictionary<string, string> BuildLeafIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (IPackagePart p in _dst.Package.Parts)
        {
            if (IsBinary(p.ContentType) && _dst.Package.GetRelationships(p.Name).Count == 0)
            {
                index.TryAdd(Convert.ToHexString(SHA256.HashData(p.GetBytes().Span)) + "|" + p.ContentType, p.Name);
            }
        }

        return index;
    }

    private string UniquePartName(string preferred)
    {
        if (_dst.Package.FindPart(preferred) is null)
        {
            return preferred;
        }

        int dot = preferred.LastIndexOf('.');
        string stem = dot < 0 ? preferred : preferred[..dot];
        string ext = dot < 0 ? string.Empty : preferred[dot..];
        int end = stem.Length;
        while (end > 0 && char.IsAsciiDigit(stem[end - 1]))
        {
            end--;
        }

        stem = stem[..end];
        for (int i = 1; ; i++)
        {
            string candidate = stem + Num(i) + ext;
            if (_dst.Package.FindPart(candidate) is null)
            {
                return candidate;
            }
        }
    }

    private string AddRelationship(string dstPart, string type, string target, RelationshipTargetMode mode)
    {
        var rels = new List<Relationship>(_dst.Package.GetRelationships(dstPart));
        Relationship? same = rels.FirstOrDefault(r => r.Type == type && r.Target == target && r.TargetMode == mode);
        if (same is not null)
        {
            return same.Id;
        }

        int n = 1;
        while (rels.Exists(r => r.Id == "rId" + Num(n)))
        {
            n++;
        }

        string id = "rId" + Num(n);
        rels.Add(new Relationship(id, type, target, mode));
        _dst.Package.SetRelationships(dstPart, rels);
        return id;
    }

    // The target as a relative reference from the source part's folder (ECMA-376 Part 2 §9.3.4).
    internal static string RelativeTarget(string fromPart, string toPart)
    {
        string[] from = fromPart.TrimStart('/').Split('/');
        string[] to = toPart.TrimStart('/').Split('/');
        int common = 0;
        while (common < from.Length - 1 && common < to.Length - 1 && string.Equals(from[common], to[common], StringComparison.OrdinalIgnoreCase))
        {
            common++;
        }

        var sb = new StringBuilder();
        for (int i = common; i < from.Length - 1; i++)
        {
            sb.Append("../");
        }

        sb.Append(string.Join('/', to[common..]));
        return sb.ToString();
    }
}
