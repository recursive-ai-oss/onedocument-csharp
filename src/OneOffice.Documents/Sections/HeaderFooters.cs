using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Packaging;
using RecursiveAI.OneOffice.Schema;
using W = RecursiveAI.OneOffice.Schema.Wordprocessing;

namespace RecursiveAI.OneOffice.Documents;

/// <summary>Whether a story is a header or a footer.</summary>
public enum HeaderFooterKind
{
    /// <summary>A page header (<c>w:headerReference</c>, part root <c>w:hdr</c>).</summary>
    Header = 0,

    /// <summary>A page footer (<c>w:footerReference</c>, part root <c>w:ftr</c>).</summary>
    Footer = 1,
}

/// <summary>Which pages a header or footer is for (<c>w:type</c> of the reference, ECMA-376 Part 1 §17.10.5).</summary>
public enum HeaderFooterType
{
    /// <summary>Odd pages, and every page when even and odd pages are not told apart; also later pages of a section with a different first page.</summary>
    Default = 0,

    /// <summary>The first page of the section when it has <c>w:titlePg</c>.</summary>
    First = 1,

    /// <summary>Even pages when the document sets <c>w:evenAndOddHeaders</c>.</summary>
    Even = 2,
}

/// <summary>The stories of a header or footer part: a container of blocks over its part's root element.</summary>
public sealed class HeaderFooter : BlockContainer
{
    internal HeaderFooter(Document owner, MarkupRef root, HeaderFooterKind kind, string partName, string relationshipId)
        : base(owner, root)
    {
        Kind = kind;
        PartName = partName;
        RelationshipId = relationshipId;
    }

    /// <summary>Gets whether this is a header or a footer.</summary>
    public HeaderFooterKind Kind { get; }

    /// <summary>Gets the part name, for example <c>/word/header1.xml</c>.</summary>
    public string PartName { get; }

    /// <summary>Gets the relationship id the section references it by.</summary>
    public string RelationshipId { get; }

    /// <summary>Gets the typed view of the root: <c>w:hdr</c> for a header, <c>w:ftr</c> for a footer.</summary>
    public W.HeaderFooter View => W.HeaderFooter.Create(Container);

    /// <summary>Gets the text of the story's paragraphs, joined by line feeds.</summary>
    public string Text => string.Join("\n", Paragraphs().Select(p => p.Text));
}

/// <summary>A header or footer reference as the section states it (<c>w:headerReference</c> / <c>w:footerReference</c>).</summary>
/// <param name="Kind">Header or footer.</param>
/// <param name="Type">Which pages it is for.</param>
/// <param name="RelationshipId">The <c>r:id</c> value.</param>
public readonly record struct HeaderFooterReferenceInfo(HeaderFooterKind Kind, HeaderFooterType Type, string RelationshipId);

/// <summary>The outcome of resolving a header or footer for a section.</summary>
/// <param name="Story">The story, or null when the page has none (it prints blank).</param>
/// <param name="Source">The section whose references define it: this section, or an earlier one it is linked to; null with no story.</param>
/// <param name="IsLinked">True when the story comes from an earlier section (the section has no reference of its own for that type).</param>
public readonly record struct ResolvedHeaderFooter(HeaderFooter? Story, Section? Source, bool IsLinked);

public sealed partial class Section
{
    /// <summary>
    /// Lists the header and footer references the section states itself, in file order. A second reference for the same kind and type
    /// is ignored and reported (<c>docx.header-reference-duplicate</c>); an unknown <c>w:type</c> is ignored too.
    /// </summary>
    /// <returns>The references; empty when the section has none.</returns>
    public IReadOnlyList<HeaderFooterReferenceInfo> HeaderFooterReferences()
    {
        var list = new List<HeaderFooterReferenceInfo>();
        foreach (MarkupRef e in Wml.Elements(_properties))
        {
            HeaderFooterKind kind;
            if (Wml.Is(e, "headerReference"u8))
            {
                kind = HeaderFooterKind.Header;
            }
            else if (Wml.Is(e, "footerReference"u8))
            {
                kind = HeaderFooterKind.Footer;
            }
            else
            {
                continue;
            }

            HeaderFooterType? type = Wml.Attr(e, "type") switch
            {
                "default" => HeaderFooterType.Default,
                "first" => HeaderFooterType.First,
                "even" => HeaderFooterType.Even,
                _ => null,
            };
            string? id = RelationshipIdOf(e);
            if (type is null || id is null)
            {
                continue;
            }

            if (list.Exists(r => r.Kind == kind && r.Type == type))
            {
                Owner.ReportDefect("docx.header-reference-duplicate", DefectKind.Structure, DefectSeverity.Repaired,
                    "The section has two " + (kind == HeaderFooterKind.Header ? "header" : "footer") + " references of type '" + Wml.Attr(e, "type") + "'; the first is used.", e);
                continue;
            }

            list.Add(new HeaderFooterReferenceInfo(kind, type.Value, id));
        }

        return list;
    }

    internal static string? RelationshipIdOf(MarkupRef e)
    {
        foreach (MarkupAttributeRef a in e.Node.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0 && Wml.AsciiEquals(a.LocalName, "id")
                && Encoding.UTF8.GetString(a.NamespaceUriBytes).EndsWith("/relationships", StringComparison.Ordinal))
            {
                return a.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the story this section names itself for a kind and type, without following links to earlier sections.
    /// </summary>
    /// <param name="kind">Header or footer.</param>
    /// <param name="type">Which pages.</param>
    /// <returns>The story; null when the section has no such reference, or its relationship does not lead to a part (<c>docx.header-missing</c>, the reference is then ignored).</returns>
    public HeaderFooter? GetOwn(HeaderFooterKind kind, HeaderFooterType type)
    {
        foreach (HeaderFooterReferenceInfo r in HeaderFooterReferences())
        {
            if (r.Kind == kind && r.Type == type)
            {
                return Owner.LoadHeaderFooter(r.RelationshipId, kind, _properties);
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a header or footer with linked-to-previous behaviour (ECMA-376 Part 1 §17.10.5): a section without its own reference of a
    /// kind and type uses the one of the nearest earlier section that has it; the first section with none leaves the page blank. There
    /// is no fallback between types: a missing <c>first</c> or <c>even</c> story is blank, not the default one.
    /// </summary>
    /// <param name="kind">Header or footer.</param>
    /// <param name="type">Which pages.</param>
    /// <returns>The story and where it came from.</returns>
    public ResolvedHeaderFooter Resolve(HeaderFooterKind kind, HeaderFooterType type)
    {
        IReadOnlyList<Section> all = Owner.Sections;
        for (int i = Math.Min(Index, all.Count - 1); i >= 0; i--)
        {
            if (all[i].GetOwn(kind, type) is { } story)
            {
                return new ResolvedHeaderFooter(story, all[i], i != Index);
            }
        }

        return new ResolvedHeaderFooter(null, null, false);
    }

    /// <summary>
    /// Chooses the story for a page. With <see cref="TitlePage"/> the first page of the section uses the <c>first</c> story; otherwise,
    /// when the document sets <see cref="Document.EvenAndOddHeaders"/>, an even page number uses the <c>even</c> story; every other page
    /// uses the <c>default</c> one.
    /// </summary>
    /// <param name="kind">Header or footer.</param>
    /// <param name="isFirstPageOfSection">True for the first page the section prints.</param>
    /// <param name="pageNumber">The page number as printed (after any restart), used for odd/even.</param>
    /// <returns>The resolved story.</returns>
    public ResolvedHeaderFooter ForPage(HeaderFooterKind kind, bool isFirstPageOfSection, long pageNumber)
    {
        HeaderFooterType type = TitlePage && isFirstPageOfSection ? HeaderFooterType.First
            : Owner.EvenAndOddHeaders && pageNumber % 2 == 0 ? HeaderFooterType.Even
            : HeaderFooterType.Default;
        return Resolve(kind, type);
    }

    /// <summary>
    /// Gives the section its own story for a kind and type: a new empty part with one empty paragraph, its relationship from the main
    /// part, and the reference in the section properties. An existing own reference of that kind and type is returned unchanged.
    /// </summary>
    /// <param name="kind">Header or footer.</param>
    /// <param name="type">Which pages.</param>
    /// <returns>The story, ready for its content.</returns>
    public HeaderFooter AddOwn(HeaderFooterKind kind, HeaderFooterType type)
    {
        if (GetOwn(kind, type) is { } existing)
        {
            return existing;
        }

        bool header = kind == HeaderFooterKind.Header;
        MarkupDocument part = Owner.CreateRelatedPart(
            header ? "header" : "footer",
            header ? "/word/header1.xml" : "/word/footer1.xml",
            header ? "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml" : "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml",
            header ? "hdr" : "ftr",
            out string partName);
        part.Root.AppendXml(Encoding.UTF8.GetBytes("<" + WmlEdit.Qualified(new MarkupRef(part.Root), "p") + "/>"));

        string? id = null;
        foreach (Relationship r in Owner.Package.GetRelationships(Owner.MainPartName))
        {
            if (PartUri.TryResolve(new PartUri(Owner.MainPartName), r.Target, out PartUri uri) && string.Equals(uri.Value, partName, StringComparison.OrdinalIgnoreCase))
            {
                id = r.Id;
            }
        }

        W.SectionProperties sp = Ensure();
        W.HeaderFooterReference reference = header ? sp.AddHeaderReference() : sp.AddFooterReference();
        WmlEdit.SetAttr(reference.Markup, "type", type switch { HeaderFooterType.First => "first", HeaderFooterType.Even => "even", _ => "default" });
        string relNs = Owner.IsStrict ? "http://purl.oclc.org/ooxml/officeDocument/relationships" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        reference.Markup.Node.SetAttribute(relNs, "id", id!, "r");
        return Owner.LoadHeaderFooter(id!, kind, _properties)!;
    }
}

public sealed partial class Document
{
    /// <summary>
    /// Gets or sets whether odd and even pages have different headers and footers (<c>w:evenAndOddHeaders</c> in the settings part).
    /// Setting it creates the settings part when the document has none.
    /// </summary>
    public bool EvenAndOddHeaders
    {
        get
        {
            MarkupDocument? settings = Parts.LoadRelated(MainPartName, "settings", out _);
            return settings is not null && Wml.Child(new MarkupRef(settings.Root), "evenAndOddHeaders"u8) is { } e && Wml.OnOffElement(e);
        }
        set
        {
            MarkupDocument? settings = Parts.LoadRelated(MainPartName, "settings", out _);
            if (settings is null)
            {
                if (!value)
                {
                    return;
                }

                settings = CreateRelatedPart("settings", "/word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml", "settings", out _);
            }

            var view = W.Settings.Create(new MarkupRef(settings.Root));
            if (value)
            {
                view.GetOrAddEvenAndOddHeaders();
            }
            else
            {
                view.RemoveEvenAndOddHeaders();
            }
        }
    }

    /// <summary>
    /// Gets the sections in order. A section ends at the paragraph whose properties hold a <c>w:sectPr</c>; the last one ends at the end of the
    /// body and takes the body's <c>w:sectPr</c> (absent: all defaults). The list is built from the markup on each call.
    /// </summary>
    public IReadOnlyList<Section> Sections
    {
        get
        {
            var ends = new List<(MarkupRef Properties, int Block)>();
            int index = 0;
            foreach (BlockElement block in Body.Blocks())
            {
                foreach (MarkupRef sect in SectionBreaksIn(block))
                {
                    ends.Add((sect, index));
                }

                index++;
            }

            var result = new List<Section>();
            int first = 0;
            foreach ((MarkupRef props, int block) in ends)
            {
                result.Add(new Section(this, result.Count, props, first, block, false));
                first = block + 1;
            }

            result.Add(new Section(this, result.Count, Body.SectionProperties ?? default, first, Math.Max(index - 1, first), true));
            return result;
        }
    }

    private static IEnumerable<MarkupRef> SectionBreaksIn(BlockElement block)
    {
        switch (block)
        {
            case Paragraph p:
                if (Wml.Child(Wml.Child(p.Markup, "pPr"u8) ?? default, "sectPr"u8) is { } s)
                {
                    yield return s;
                }

                break;
            case BlockContentControl sdt:
                foreach (BlockElement inner in sdt.Content.Blocks())
                {
                    foreach (MarkupRef s2 in SectionBreaksIn(inner))
                    {
                        yield return s2;
                    }
                }

                break;
        }
    }

    internal HeaderFooter? LoadHeaderFooter(string relationshipId, HeaderFooterKind kind, MarkupRef at)
    {
        foreach (Relationship r in Package.GetRelationships(MainPartName))
        {
            if (r.Id == relationshipId && r.TargetMode == RelationshipTargetMode.Internal
                && PartUri.TryResolve(new PartUri(MainPartName), r.Target, out PartUri uri) && Package.FindPart(uri.Value) is { } part)
            {
                MarkupDocument? doc = Parts.Load(part.Name, required: false);
                if (doc is not null && !doc.Root.IsNull)
                {
                    return new HeaderFooter(this, new MarkupRef(doc.Root), kind, part.Name, relationshipId);
                }

                break;
            }
        }

        ReportDefect("docx.header-missing", DefectKind.Reference, DefectSeverity.Repaired,
            "A header or footer reference (" + relationshipId + ") does not lead to a readable part; it is ignored.", at);
        return null;
    }
}
