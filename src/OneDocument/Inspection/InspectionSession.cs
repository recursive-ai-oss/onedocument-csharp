using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Inspection;

/// <summary>
/// The package under inspection, with what a provider needs to read it: the parts, their relationships, parsed markup (parsed once and
/// shared) and the way to report findings. Providers hold no state of their own, so one provider serves any number of sessions.
/// </summary>
public class InspectionSession
{
    /// <summary>Tells whether a namespace URI is the relationships namespace, in its Transitional or Strict spelling (ECMA-376 Part 1 §22.8).</summary>
    /// <param name="uri">The namespace URI.</param>
    /// <returns>True when attributes in it hold relationship ids.</returns>
    public static bool IsRelationshipNamespace(string uri) =>
        uri.Contains("/officeDocument/", StringComparison.Ordinal) && uri.EndsWith("/relationships", StringComparison.Ordinal);

    private readonly Dictionary<string, MarkupDocument?> _markup = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<InspectionFinding> _findings = [];
    private readonly List<string> _tips = [];
    private readonly List<string> _notes = [];

    internal InspectionSession(IOpcPackage package, OfficeFormat format, OfficeKind kind, OpenContext context, string? mainPartName)
    {
        Package = package;
        Format = format;
        Kind = kind;
        Context = context;
        MainPartName = mainPartName;
    }

    /// <summary>Gets the package.</summary>
    public IOpcPackage Package { get; }

    /// <summary>Gets the OOXML format of the package.</summary>
    public OfficeFormat Format { get; }

    /// <summary>Gets the kind of document.</summary>
    public OfficeKind Kind { get; }

    /// <summary>Gets the limits and defect sink the package was opened with.</summary>
    public OpenContext Context { get; }

    /// <summary>Gets the name of the main part (<c>/word/document.xml</c>), or null when the package has none.</summary>
    public string? MainPartName { get; }

    internal IReadOnlyList<InspectionFinding> Findings => _findings;

    internal IReadOnlyList<string> Tips => _tips;

    internal IReadOnlyList<string> Notes => _notes;

    /// <summary>Adds a finding.</summary>
    /// <param name="category">The category.</param>
    /// <param name="location">Where it is.</param>
    /// <param name="description">What it is.</param>
    /// <param name="count">How many items it stands for; values below 1 are raised to 1.</param>
    /// <param name="detail">The value itself, where short and useful.</param>
    public void Add(InspectionCategory category, string location, string description, int count = 1, string? detail = null) =>
        _findings.Add(new InspectionFinding(category, location, description, Math.Max(1, count), detail));

    /// <summary>Adds a tip.</summary>
    /// <param name="text">The tip.</param>
    public void Tip(string text) => _tips.Add(text);

    /// <summary>Adds a note about the inspection.</summary>
    /// <param name="text">The note.</param>
    public void Note(string text) => _notes.Add(text);

    /// <summary>Finds the parts whose content type matches.</summary>
    /// <param name="match">The test on the content type.</param>
    /// <returns>The parts, in package order.</returns>
    public List<IPackagePart> PartsWhere(Func<string, bool> match)
    {
        var list = new List<IPackagePart>();
        foreach (IPackagePart p in Package.Parts)
        {
            if (match(p.ContentType))
            {
                list.Add(p);
            }
        }

        return list;
    }

    /// <summary>Finds the parts whose name matches.</summary>
    /// <param name="match">The test on the part name.</param>
    /// <returns>The parts, in package order.</returns>
    public List<IPackagePart> PartsNamed(Func<string, bool> match)
    {
        var list = new List<IPackagePart>();
        foreach (IPackagePart p in Package.Parts)
        {
            if (match(p.Name))
            {
                list.Add(p);
            }
        }

        return list;
    }

    /// <summary>Gets the relationships of a part or, for null, of the package, whose type ends with a segment (<c>/comments</c>).</summary>
    /// <param name="source">The owner part name, or null for the package.</param>
    /// <param name="typeSuffix">The end of the relationship type.</param>
    /// <returns>The matching relationships.</returns>
    public List<Relationship> RelationshipsOf(string? source, string typeSuffix)
    {
        var list = new List<Relationship>();
        foreach (Relationship r in Package.GetRelationships(source))
        {
            if (r.Type.EndsWith(typeSuffix, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(r);
            }
        }

        return list;
    }

    /// <summary>Resolves an internal relationship to the part it targets.</summary>
    /// <param name="source">The owner part name, or null for the package.</param>
    /// <param name="relationship">The relationship.</param>
    /// <returns>The part, or null when the target is external or missing.</returns>
    public IPackagePart? Target(string? source, Relationship relationship) =>
        OpcPackage.TryResolveTarget(source, relationship, out PartUri uri) ? Package.FindPart(uri.Value) : null;

    /// <summary>Gets the parts a part targets through relationships of a type.</summary>
    /// <param name="source">The owner part name, or null for the package.</param>
    /// <param name="typeSuffix">The end of the relationship type.</param>
    /// <returns>The target parts that exist.</returns>
    public List<IPackagePart> TargetsOf(string? source, string typeSuffix)
    {
        var list = new List<IPackagePart>();
        foreach (Relationship r in RelationshipsOf(source, typeSuffix))
        {
            IPackagePart? t = Target(source, r);
            if (t is not null)
            {
                list.Add(t);
            }
        }

        return list;
    }

    /// <summary>Gets the parsed markup of a part, parsed once. A part that is not well-formed is noted and gives null.</summary>
    /// <param name="part">The part.</param>
    /// <returns>The markup, or null.</returns>
    public MarkupDocument? Markup(IPackagePart part)
    {
        if (_markup.TryGetValue(part.Name, out MarkupDocument? cached))
        {
            return cached;
        }

        MarkupDocument? doc = null;
        try
        {
            doc = MarkupDocument.Parse(part.GetBytes(), Context, part.Name);
        }
        catch (OfficeException ex)
        {
            _notes.Add(part.Name + " could not be read as XML: " + ex.Defect.Code);
        }

        _markup[part.Name] = doc;
        return doc;
    }

    /// <summary>Gets the parsed markup of a part by name.</summary>
    /// <param name="name">The part name.</param>
    /// <returns>The markup, or null when the part is missing or not well-formed.</returns>
    public MarkupDocument? Markup(string name)
    {
        IPackagePart? p = Package.FindPart(name);
        return p is null ? null : Markup(p);
    }

    /// <summary>Forgets every parsed part, after something else changed the package (the hub models write their edits into the package).</summary>
    protected void DropMarkupCache() => _markup.Clear();

    /// <summary>Gets the parts that were parsed through this session, for writing back.</summary>
    internal IEnumerable<KeyValuePair<string, MarkupDocument?>> ParsedParts => _markup;
}

/// <summary>
/// An <see cref="InspectionSession"/> that also edits: removing parts, relationships and markup, and recording what it did. Edits to
/// parsed markup are written back when the session is flushed; parts a provider does not touch stay byte-identical.
/// </summary>
public sealed class SanitizeSession : InspectionSession
{
    private readonly HashSet<InspectionCategory> _selected;
    private readonly List<RemovedItem> _removed = [];
    private readonly List<NotRemovedItem> _notRemoved = [];
    private readonly HashSet<string> _pruneCandidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirty = new(StringComparer.OrdinalIgnoreCase);

    internal SanitizeSession(IOpcPackage package, OfficeFormat format, OfficeKind kind, OpenContext context, string? mainPartName, IEnumerable<InspectionCategory> selected)
        : base(package, format, kind, context, mainPartName)
    {
        _selected = [.. selected];
    }

    internal IReadOnlyList<RemovedItem> RemovedItems => _removed;

    internal IReadOnlyList<NotRemovedItem> NotRemovedItems => _notRemoved;

    /// <summary>Tells whether the caller asked for a category to be removed.</summary>
    /// <param name="category">The category.</param>
    /// <returns>True when selected.</returns>
    public bool Selected(InspectionCategory category) => _selected.Contains(category);

    /// <summary>Records that something was removed.</summary>
    /// <param name="category">The category.</param>
    /// <param name="location">Where it was.</param>
    /// <param name="description">What it was.</param>
    /// <param name="count">How many items.</param>
    public void Removed(InspectionCategory category, string location, string description, int count = 1) =>
        _removed.Add(new RemovedItem(category, location, description, Math.Max(1, count)));

    /// <summary>Records that something in a selected category stays, and why.</summary>
    /// <param name="category">The category.</param>
    /// <param name="code">A <c>sanitize.*</c> code.</param>
    /// <param name="location">Where it is.</param>
    /// <param name="reason">Why it stays.</param>
    public void NotRemoved(InspectionCategory category, string code, string location, string reason) =>
        _notRemoved.Add(new NotRemovedItem(category, code, location, reason));

    /// <summary>Marks a part's parsed markup as edited, so that it is written back on flush.</summary>
    /// <param name="partName">The part name.</param>
    public void MarkEdited(string partName) => _dirty.Add(partName);

    /// <summary>Gets a part's markup for editing. It is written back when flushed.</summary>
    /// <param name="part">The part.</param>
    /// <returns>The markup, or null when the part is not well-formed XML.</returns>
    public MarkupDocument? Edit(IPackagePart part)
    {
        MarkupDocument? doc = Markup(part);
        if (doc is not null)
        {
            _dirty.Add(part.Name);
        }

        return doc;
    }

    /// <summary>
    /// Removes a part and queues the parts it pointed at to be removed too if nothing else points at them. Relationships from other parts
    /// that target it are not changed here: use <see cref="Unlink"/> for those.
    /// </summary>
    /// <param name="name">The part name.</param>
    /// <returns>True when the part existed.</returns>
    public bool RemovePart(string name)
    {
        foreach (Relationship r in Package.GetRelationships(name))
        {
            if (OpcPackage.TryResolveTarget(name, r, out PartUri t))
            {
                _pruneCandidates.Add(t.Value);
            }
        }

        _dirty.Remove(name);
        return Package.RemovePart(name);
    }

    /// <summary>
    /// Removes relationships of a part (or of the package, for null) and the markup in the source part that uses their ids, then queues
    /// their targets to be removed if nothing else points at them.
    /// </summary>
    /// <param name="source">The owner part name, or null for the package.</param>
    /// <param name="relationshipIds">The ids to remove.</param>
    /// <returns>The number of markup elements removed from the source part.</returns>
    public int Unlink(string? source, IReadOnlyCollection<string> relationshipIds)
    {
        if (relationshipIds.Count == 0)
        {
            return 0;
        }

        var keep = new List<Relationship>();
        foreach (Relationship r in Package.GetRelationships(source))
        {
            if (relationshipIds.Contains(r.Id))
            {
                if (OpcPackage.TryResolveTarget(source, r, out PartUri t))
                {
                    _pruneCandidates.Add(t.Value);
                }
            }
            else
            {
                keep.Add(r);
            }
        }

        Package.SetRelationships(source, keep);
        int removed = 0;
        IPackagePart? part = source is null ? null : Package.FindPart(source);
        if (part is not null && part.ContentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            MarkupDocument? doc = Edit(part);
            if (doc is not null)
            {
                removed = RemoveReferences(doc, relationshipIds);
            }
        }

        return removed;
    }

    /// <summary>Removes the relationships of a part that target a given part, and the markup that uses them.</summary>
    /// <param name="source">The owner part name, or null for the package.</param>
    /// <param name="targetName">The target part name.</param>
    /// <returns>The ids removed.</returns>
    public List<string> UnlinkTarget(string? source, string targetName)
    {
        var ids = new List<string>();
        foreach (Relationship r in Package.GetRelationships(source))
        {
            if (OpcPackage.TryResolveTarget(source, r, out PartUri t) && string.Equals(t.Value, targetName, StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(r.Id);
            }
        }

        Unlink(source, ids);
        return ids;
    }

    /// <summary>Removes a part, the relationships of every other part (and of the package) that target it, and the markup that uses them.</summary>
    /// <param name="name">The part name.</param>
    /// <returns>True when the part existed.</returns>
    public bool RemoveEverywhere(string name)
    {
        foreach (string? owner in OwnersOf(name))
        {
            UnlinkTarget(owner, name);
        }

        return RemovePart(name);
    }

    /// <summary>
    /// Removes an element of a part together with the relationships only it used: the ids found in its subtree that nothing else in the part
    /// uses any more are unlinked.
    /// </summary>
    /// <param name="partName">The part the element is in.</param>
    /// <param name="element">The element; it must belong to markup obtained from this session.</param>
    public void RemoveSubtree(string partName, MarkupNode element)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (MarkupNode e in InspectionXml.Descendants(element).Prepend(element))
        {
            foreach (MarkupAttributeRef a in e.Attributes)
            {
                if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0 && IsRelationshipNamespace(a.NamespaceUri))
                {
                    ids.Add(a.Value);
                }
            }
        }

        MarkupDocument? doc = element.Document;
        InspectionXml.RemoveWithWrappers(element);
        MarkEdited(partName);
        if (ids.Count == 0 || doc is null)
        {
            return;
        }

        foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
        {
            foreach (MarkupAttributeRef a in e.Attributes)
            {
                if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0 && IsRelationshipNamespace(a.NamespaceUri))
                {
                    ids.Remove(a.Value);
                }
            }
        }

        var keep = new List<Relationship>();
        foreach (Relationship r in Package.GetRelationships(partName))
        {
            if (ids.Contains(r.Id))
            {
                if (OpcPackage.TryResolveTarget(partName, r, out PartUri t))
                {
                    _pruneCandidates.Add(t.Value);
                }
            }
            else
            {
                keep.Add(r);
            }
        }

        if (keep.Count != Package.GetRelationships(partName).Count)
        {
            Package.SetRelationships(partName, keep);
        }
    }

    /// <summary>Finds every part with an internal relationship to a part.</summary>
    /// <param name="targetName">The target part name.</param>
    /// <returns>The owners: part names, with null standing for the package.</returns>
    public List<string?> OwnersOf(string targetName)
    {
        var owners = new List<string?>();
        foreach (Relationship r in Package.GetRelationships(null))
        {
            if (OpcPackage.TryResolveTarget(null, r, out PartUri t) && string.Equals(t.Value, targetName, StringComparison.OrdinalIgnoreCase))
            {
                owners.Add(null);
                break;
            }
        }

        foreach (IPackagePart p in Package.Parts)
        {
            foreach (Relationship r in Package.GetRelationships(p.Name))
            {
                if (OpcPackage.TryResolveTarget(p.Name, r, out PartUri t) && string.Equals(t.Value, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    owners.Add(p.Name);
                    break;
                }
            }
        }

        return owners;
    }

    /// <summary>Writes the markup that was edited back into its parts and forgets the parsed copies, so that a hub model reading the package sees the edits.</summary>
    public void Flush()
    {
        foreach (KeyValuePair<string, MarkupDocument?> kv in ParsedParts.ToList())
        {
            if (kv.Value is not null && _dirty.Contains(kv.Key) && kv.Value.IsModified)
            {
                IPackagePart? p = Package.FindPart(kv.Key);
                p?.SetBytes(kv.Value.ToArray());
            }
        }

        _dirty.Clear();
        DropMarkupCache();
    }

    /// <summary>Removes the parts that only removed parts pointed at, repeatedly, and then writes edited markup back.</summary>
    internal void Finish()
    {
        Flush();
        var queue = new Queue<string>(_pruneCandidates);
        _pruneCandidates.Clear();
        if (queue.Count == 0)
        {
            return;
        }

        var referenced = ReferencedTargets();
        while (queue.Count > 0)
        {
            string name = queue.Dequeue();
            IPackagePart? part = Package.FindPart(name);
            if (part is null || referenced.Contains(name) || IsRoot(name))
            {
                continue;
            }

            foreach (Relationship r in Package.GetRelationships(name))
            {
                if (OpcPackage.TryResolveTarget(name, r, out PartUri t))
                {
                    queue.Enqueue(t.Value);
                }
            }

            Package.RemovePart(name);
            referenced = ReferencedTargets();
        }
    }

    private bool IsRoot(string name) => string.Equals(name, MainPartName, StringComparison.OrdinalIgnoreCase);

    private HashSet<string> ReferencedTargets()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Relationship r in Package.GetRelationships(null))
        {
            if (OpcPackage.TryResolveTarget(null, r, out PartUri t))
            {
                set.Add(t.Value);
            }
        }

        foreach (IPackagePart p in Package.Parts)
        {
            foreach (Relationship r in Package.GetRelationships(p.Name))
            {
                if (OpcPackage.TryResolveTarget(p.Name, r, out PartUri t))
                {
                    set.Add(t.Value);
                }
            }
        }

        return set;
    }

    // ECMA-376 Part 1 §22.8 (relationships namespace): an attribute in it holds a relationship id of its element's part. Removing the
    // relationship leaves the attribute dangling, which Office repairs, so the element that carries it goes too. A few elements carry
    // the id as an optional attribute of something that must stay (the worksheet's page setup); those lose only the attribute.
    private static int RemoveReferences(MarkupDocument doc, IReadOnlyCollection<string> ids)
    {
        int removed = 0;
        foreach (MarkupNode e in InspectionXml.Descendants(doc.DocumentNode))
        {
            string? hit = null;
            byte[]? rawName = null;
            foreach (MarkupAttributeRef a in e.Attributes)
            {
                if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0 && IsRelationshipNamespace(a.NamespaceUri) && ids.Contains(a.Value))
                {
                    hit = a.Value;
                    rawName = a.RawName.ToArray();
                    break;
                }
            }

            if (hit is null)
            {
                continue;
            }

            if (e.LocalName.SequenceEqual("pageSetup"u8))
            {
                e.RemoveAttribute(System.Text.Encoding.UTF8.GetString(rawName!));
            }
            else
            {
                InspectionXml.RemoveWithWrappers(e);
            }

            removed++;
        }

        return removed;
    }
}
