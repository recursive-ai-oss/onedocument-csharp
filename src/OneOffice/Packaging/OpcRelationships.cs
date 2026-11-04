using System.Text;
using RecursiveAI.OneOffice.Diagnostics;

namespace RecursiveAI.OneOffice.Packaging;

/// <summary>Reading and writing Relationships parts (ECMA-376 Part 2 §6.5).</summary>
internal static class OpcRelationships
{
    public const string Namespace = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// Reads a Relationships part tolerantly. A relationship without an <c>Id</c>, <c>Type</c> or <c>Target</c>, or whose
    /// <c>Id</c> repeats an earlier one, is dropped (§6.5.3.4: the Id is required and unique within the part); a target that does
    /// not name a part is kept as written and reported.
    /// </summary>
    /// <param name="xml">The part's bytes.</param>
    /// <param name="relsName">The Relationships part's own name, for locations.</param>
    /// <param name="source">The source part's name, or null for the package.</param>
    /// <param name="limits">The limits.</param>
    /// <param name="defects">Where repairs are reported.</param>
    /// <param name="partExists">Tells whether a part of the given name is in the package.</param>
    public static List<Relationship> Parse(ReadOnlyMemory<byte> xml, string relsName, string? source, OfficeLimits limits, IDefectSink defects, Func<string, bool> partExists)
    {
        var list = new List<Relationship>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var reader = new OpcXmlReader(xml, limits, relsName);
        try
        {
            bool sawRoot = false;
            while (reader.Read())
            {
                if (reader.Token != OpcXmlToken.StartElement)
                {
                    continue;
                }

                if (reader.Depth == 1)
                {
                    if (reader.LocalName != "Relationships")
                    {
                        throw new OpcXmlException("The root element is not Relationships.", reader.TokenStart);
                    }

                    sawRoot = true;
                    continue;
                }

                if (reader.Depth != 2 || !sawRoot || reader.LocalName != "Relationship")
                {
                    continue;
                }

                string? id = reader.GetAttribute("Id");
                string? type = reader.GetAttribute("Type");
                string? target = reader.GetAttribute("Target");
                string? mode = reader.GetAttribute("TargetMode");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(type) || target is null)
                {
                    ContentTypes.Report(defects, "packaging.bad-relationship", DefectSeverity.Dropped, "A relationship lacks its Id, Type or Target.", relsName, reader.TokenStart);
                    continue;
                }

                if (!ids.Add(id))
                {
                    ContentTypes.Report(defects, "packaging.duplicate-relationship-id", DefectSeverity.Dropped, $"The relationship id '{id}' is used twice; the first is kept.", relsName, reader.TokenStart);
                    continue;
                }

                RelationshipTargetMode targetMode = RelationshipTargetMode.Internal;
                if (mode is not null)
                {
                    if (string.Equals(mode, "External", StringComparison.OrdinalIgnoreCase))
                    {
                        targetMode = RelationshipTargetMode.External;
                    }
                    else if (!string.Equals(mode, "Internal", StringComparison.OrdinalIgnoreCase))
                    {
                        ContentTypes.Report(defects, "packaging.bad-relationship-target-mode", DefectSeverity.Repaired, $"The TargetMode '{mode}' is not Internal or External; Internal is used.", relsName, reader.TokenStart);
                    }
                }

                if (targetMode == RelationshipTargetMode.Internal)
                {
                    if (!TryResolve(source, target, out PartUri resolved))
                    {
                        ContentTypes.Report(defects, "packaging.bad-relationship-target", DefectSeverity.Repaired, $"The target '{target}' of relationship '{id}' is not a valid part name; it is kept as written.", relsName, reader.TokenStart);
                    }
                    else if (!partExists(resolved.Value))
                    {
                        ContentTypes.Report(defects, "packaging.dangling-relationship", DefectSeverity.Repaired, $"The target '{target}' of relationship '{id}' is not a part of the package; it is kept as written.", relsName, reader.TokenStart);
                    }
                }

                list.Add(new Relationship(id, type, target, targetMode));
            }

            if (!sawRoot)
            {
                throw new OpcXmlException("There is no Relationships element.", 0);
            }
        }
        catch (OpcXmlException ex)
        {
            // What was read before the damage is kept; the rest of the part is lost.
            ContentTypes.Report(defects, ex.IsDtd ? "packaging.dtd-refused" : "packaging.bad-relationships-part", DefectSeverity.Dropped, ex.Message + " The relationships after it are lost.", relsName, ex.Offset);
        }

        return list;
    }

    /// <summary>
    /// Resolves an internal target to a part name: the fragment and query are not part of the name, non-ASCII percent-escapes are
    /// decoded as for zip item names, and the reference is resolved against the source part's directory (§6.4, §6.5.2.3).
    /// </summary>
    public static bool TryResolve(string? source, string target, out PartUri resolved)
    {
        int cut = target.AsSpan().IndexOfAny('#', '?');
        string path = cut >= 0 ? target[..cut] : target;
        path = OpcNames.UnescapeNonAscii(path);
        PartUri? owner = source is not null && PartUri.TryCreate(source, out PartUri o) ? o : null;
        return PartUri.TryResolve(owner, path, out resolved);
    }

    /// <summary>Writes a Relationships part.</summary>
    public static byte[] ToXml(IReadOnlyList<Relationship> relationships)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        sb.Append("<Relationships xmlns=\"").Append(Namespace).Append("\">");
        foreach (Relationship r in relationships)
        {
            sb.Append("<Relationship Id=\"").Append(OpcXmlWriter.Escape(r.Id))
                .Append("\" Type=\"").Append(OpcXmlWriter.Escape(r.Type))
                .Append("\" Target=\"").Append(OpcXmlWriter.Escape(r.Target)).Append('"');
            if (r.TargetMode == RelationshipTargetMode.External)
            {
                sb.Append(" TargetMode=\"External\"");
            }

            sb.Append("/>");
        }

        sb.Append("</Relationships>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Tells whether a part name is a Relationships part and, if so, names its source (§6.5.2.2, §6.5.2.3): <c>/_rels/.rels</c> has
    /// the package as source (null), and <c>/dir/_rels/name.rels</c> has the part <c>/dir/name</c>.
    /// </summary>
    public static bool IsRelationshipsPart(string partName, out string? source)
    {
        source = null;
        int slash = partName.LastIndexOf('/');
        if (slash < 0)
        {
            return false;
        }

        string file = partName[(slash + 1)..];
        string dir = partName[..slash];
        if (!file.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) || !(dir + "/").EndsWith("/_rels/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string parent = dir.Length >= 6 ? dir[..^6] : string.Empty; // Removes "/_rels"; for "/_rels" itself that leaves "".
        if (file.Equals(".rels", StringComparison.OrdinalIgnoreCase))
        {
            if (parent.Length != 0)
            {
                return false;
            }

            source = null;
            return true;
        }

        if (file.Length <= 5)
        {
            return false;
        }

        source = parent + "/" + file[..^5];
        return true;
    }

    /// <summary>The name of the Relationships part of a source part, or of the package when <paramref name="source"/> is null.</summary>
    public static string PartNameFor(string? source)
    {
        if (source is null)
        {
            return "/_rels/.rels";
        }

        int slash = source.LastIndexOf('/');
        return source[..(slash + 1)] + "_rels/" + source[(slash + 1)..] + ".rels";
    }
}
