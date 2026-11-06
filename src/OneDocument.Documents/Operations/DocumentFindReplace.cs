using RecursiveAI.OneDocument.Documents.Revisions;
using Rev = RecursiveAI.OneDocument.Documents.Revisions;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>The stories of a document a search covers.</summary>
[Flags]
public enum DocumentStories
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The main document part: paragraphs in the body, in tables and in content controls (text boxes excluded; see <see cref="TextBoxes"/>).</summary>
    Body = 1,

    /// <summary>Every header and footer part.</summary>
    HeadersFooters = 2,

    /// <summary>Footnotes and endnotes.</summary>
    Notes = 4,

    /// <summary>Comments.</summary>
    Comments = 8,

    /// <summary>The text of text boxes anchored in any of the parts searched (nested boxes included).</summary>
    TextBoxes = 16,

    /// <summary>Everything above.</summary>
    All = Body | HeadersFooters | Notes | Comments | TextBoxes,
}

/// <summary>What <see cref="Document.Find(string, DocumentFindOptions?)"/> searches and how.</summary>
public class DocumentFindOptions
{
    /// <summary>Gets how the pattern is read. Default: literal, case-insensitive.</summary>
    public SearchOptions Search { get; init; } = new();

    /// <summary>Gets the stories searched. Default <see cref="DocumentStories.All"/>.</summary>
    public DocumentStories Stories { get; init; } = DocumentStories.All;

    /// <summary>
    /// Gets a value indicating whether field instructions (<c>w:instrText</c> and the <c>w:instr</c> attribute of simple fields) are searched too,
    /// each instruction text on its own. Default false: a search sees what the reader sees, the field results.
    /// </summary>
    public bool IncludeFieldCodes { get; init; }
}

/// <summary>What <see cref="Document.Replace(string, string, DocumentReplaceOptions?)"/> does besides finding.</summary>
public sealed class DocumentReplaceOptions : DocumentFindOptions
{
    /// <summary>
    /// Gets the author name that turns the replacement into a tracked change: each match becomes a <c>w:del</c> of the old text and a
    /// <c>w:ins</c> of the new. Null (the default) replaces without tracking.
    /// </summary>
    public string? TrackedAuthor { get; init; }

    /// <summary>Gets the date written on tracked changes; null leaves the date out. The library never reads the clock.</summary>
    public DateTimeOffset? TrackedDate { get; init; }

    /// <summary>Gets how tracked-change ids are chosen; null continues after the largest id in the document's parts.</summary>
    public RevisionIds? TrackedIds { get; init; }

    /// <summary>
    /// Gets run property elements to apply to the new text, such as <c>&lt;w:b/&gt;&lt;w:color w:val="FF0000"/&gt;</c> (the part's prefix for the main
    /// namespace, normally <c>w</c>). An element replaces the one of the same name in the properties of the first replaced character's
    /// run, which the new text otherwise keeps. Null keeps the formatting as it is.
    /// </summary>
    public string? RunPropertiesXml { get; init; }
}

public sealed partial class Document
{
    /// <summary>
    /// Finds text as a reader sees it: the paragraph's current text, which joins the runs of a paragraph (and so matches across run
    /// boundaries, hyperlinks, bookmarks, content controls, field results and other authors' insertions) and leaves out text inside
    /// tracked deletions. A match never crosses a paragraph.
    /// </summary>
    /// <param name="pattern">The text, or the regular expression when <see cref="SearchOptions.UseRegex"/> is set.</param>
    /// <param name="options">What to search; null searches every story with a literal, case-insensitive pattern.</param>
    /// <returns>The hits in document order, parts in the order body, headers and footers, footnotes, endnotes, comments; text boxes follow the paragraphs of the part that anchors them.</returns>
    /// <exception cref="ArgumentException">The pattern is empty or not a valid regular expression.</exception>
    /// <exception cref="OfficeLimitException">A regular expression ran past <see cref="OfficeLimits.RegexMatchTimeoutMilliseconds"/>.</exception>
    public IReadOnlyList<TextHit> Find(string pattern, DocumentFindOptions? options = null)
    {
        options ??= new DocumentFindOptions();
        TextSearch search = TextSearch.Create(pattern, options.Search, Context.Limits);
        var hits = new List<TextHit>();
        foreach (SearchUnit unit in SearchUnits(options.Stories))
        {
            int index = 0;
            foreach (MarkupNode p in unit.Paragraphs)
            {
                string text = RevisionAuthor.CurrentText(p);
                foreach (TextMatch m in search.Find(text))
                {
                    hits.Add(new TextHit(new TextLocation(unit.Part, unit.Story, index, m.Index), m.Value));
                }

                if (options.IncludeFieldCodes)
                {
                    foreach ((_, string instruction) in FieldInstructions(p))
                    {
                        foreach (TextMatch m in search.Find(instruction))
                        {
                            hits.Add(new TextHit(new TextLocation(unit.Part, "field-code", index, m.Index), m.Value));
                        }
                    }
                }

                index++;
            }
        }

        return hits;
    }

    /// <summary>
    /// Replaces every match of a pattern, keeping the formatting of the first matched run (or applying the run properties given). The
    /// edit is made in the paragraph's runs: they are split at the edges of the match, the matched text goes, and the new text is one run
    /// with the first matched character's properties. Bookmarks, fields, drawings and every element the replacement does not touch stay
    /// where they are; parts without a match are not modified. With <see cref="DocumentReplaceOptions.TrackedAuthor"/> the replacement is
    /// written as <c>w:del</c> and <c>w:ins</c>, so rejecting the changes restores the text.
    /// </summary>
    /// <param name="pattern">The text, or the regular expression when <see cref="SearchOptions.UseRegex"/> is set.</param>
    /// <param name="replacement">The new text. For a regular-expression search <c>$1</c> and <c>${name}</c> insert groups. A tab character becomes <c>w:tab</c> and a line feed <c>w:br</c>; with <see cref="SearchOptions.SpecialCodes"/> <c>^t</c> and <c>^l</c> do the same.</param>
    /// <param name="options">What to search and whether to track; null replaces untracked in every story.</param>
    /// <returns>Every replacement with its location, and every match left alone with the reason (none today besides markup the editor cannot handle, <c>markup-unsupported</c>, and field instructions in tracked mode).</returns>
    /// <exception cref="ArgumentException">The pattern is empty or invalid, or the replacement has a character XML cannot carry or uses <c>^p</c>.</exception>
    /// <exception cref="OfficeLimitException">A regular expression ran past <see cref="OfficeLimits.RegexMatchTimeoutMilliseconds"/>. Nothing has been changed when this is raised for the first match of a paragraph; earlier paragraphs keep their replacements.</exception>
    public ReplaceReport Replace(string pattern, string replacement, DocumentReplaceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        options ??= new DocumentReplaceOptions();
        TextSearch search = TextSearch.Create(pattern, options.Search, Context.Limits);
        bool tracked = options.TrackedAuthor is not null;
        RevisionAuthor? author = tracked ? Revisions.CreateAuthor(options.TrackedAuthor!, options.TrackedDate, options.TrackedIds ?? RevisionIds.ContinueAfterMax) : null;
        var report = new ReplaceReport();

        // The units are collected first: text boxes are registered with the document before any edit, so their alternate copies
        // are brought up to date when the changes are committed.
        List<SearchUnit> units = SearchUnits(options.Stories);
        foreach (SearchUnit unit in units)
        {
            int index = 0;
            foreach (MarkupNode p in unit.Paragraphs)
            {
                ReplaceInParagraph(p, unit, index, search, replacement, options, author, report);
                index++;
            }
        }

        return report;
    }

    private static void ReplaceInParagraph(MarkupNode p, SearchUnit unit, int index, TextSearch search, string replacement, DocumentReplaceOptions options, RevisionAuthor? author, ReplaceReport report)
    {
        string text = RevisionAuthor.CurrentText(p);
        IReadOnlyList<TextMatch> matches = search.Find(text);
        if (matches.Count > 0)
        {
            var expanded = new string[matches.Count];
            for (int i = 0; i < matches.Count; i++)
            {
                expanded[i] = search.Expand(matches[i], replacement);
                Rev.WordXml.RequireXmlText(expanded[i], nameof(replacement));
            }

            var done = new List<ReplacementItem>();
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                TextMatch m = matches[i];
                var loc = new TextLocation(unit.Part, unit.Story, index, m.Index);
                try
                {
                    if (author is not null)
                    {
                        author.ReplaceText(p, m.Index, m.Length, expanded[i]);
                    }
                    else
                    {
                        RevisionAuthor.ReplacePlain(p, m.Index, m.Length, expanded[i], options.RunPropertiesXml);
                    }

                    done.Add(new ReplacementItem(loc, m.Value, expanded[i], author is not null));
                }
                catch (ArgumentException ex)
                {
                    report.Add(new ReplaceIssue("markup-unsupported", "The markup around the text cannot be edited: " + ex.Message, loc));
                }
            }

            for (int i = done.Count - 1; i >= 0; i--)
            {
                report.Add(done[i]);
            }
        }

        if (!options.IncludeFieldCodes)
        {
            return;
        }

        foreach ((MarkupNode node, string instruction) in FieldInstructions(p))
        {
            IReadOnlyList<TextMatch> fm = search.Find(instruction);
            if (fm.Count == 0)
            {
                continue;
            }

            if (author is not null)
            {
                foreach (TextMatch m in fm)
                {
                    report.Add(new ReplaceIssue("markup-unsupported", "Field instructions are not changed as tracked changes.", new TextLocation(unit.Part, "field-code", index, m.Index)));
                }

                continue;
            }

            string updated = instruction;
            var items = new List<ReplacementItem>();
            for (int i = fm.Count - 1; i >= 0; i--)
            {
                string r = search.Expand(fm[i], replacement);
                Rev.WordXml.RequireXmlText(r, nameof(replacement));
                updated = string.Concat(updated.AsSpan(0, fm[i].Index), r, updated.AsSpan(fm[i].Index + fm[i].Length));
                items.Add(new ReplacementItem(new TextLocation(unit.Part, "field-code", index, fm[i].Index), fm[i].Value, r, false));
            }

            SetInstruction(node, updated);
            for (int i = items.Count - 1; i >= 0; i--)
            {
                report.Add(items[i]);
            }
        }
    }

    // ECMA-376 Part 1 §17.16.13 (fldSimple) holds the instruction in the w:instr attribute; §17.16.23 (instrText) in the element's text.
    private static List<(MarkupNode Node, string Text)> FieldInstructions(MarkupNode paragraph)
    {
        var list = new List<(MarkupNode, string)>();
        Walk(paragraph, 0);
        return list;

        void Walk(MarkupNode n, int depth)
        {
            if (depth > 64)
            {
                return;
            }

            foreach (MarkupNode c in n.ChildElements)
            {
                if (!Rev.WordXml.InMain(c))
                {
                    continue;
                }

                if (Rev.WordXml.Is(c, "instrText"))
                {
                    list.Add((c, c.InnerText));
                }
                else if (Rev.WordXml.Is(c, "fldSimple"))
                {
                    if (Rev.WordXml.Attr(c, "instr") is { Length: > 0 } instr)
                    {
                        list.Add((c, instr));
                    }

                    Walk(c, depth + 1);
                }
                else if (Rev.WordXml.IsAny(c, "r", "ins", "del", "moveTo", "moveFrom", "hyperlink", "smartTag", "customXml", "sdt", "sdtContent", "dir", "bdo"))
                {
                    Walk(c, depth + 1);
                }
            }
        }
    }

    private static void SetInstruction(MarkupNode node, string text)
    {
        if (Rev.WordXml.Is(node, "fldSimple"))
        {
            node.SetAttribute(node.TryGetPrefixInScope(Rev.WordXml.Main, out string p) ? p + ":instr" : "w:instr", text);
            return;
        }

        node.SetText(text);
        if (node.GetAttribute("http://www.w3.org/XML/1998/namespace", "space") is null && (text.StartsWith(' ') || text.EndsWith(' ')))
        {
            node.SetAttribute("xml:space", "preserve");
        }
    }

    private sealed record SearchUnit(string Part, string Story, List<MarkupNode> Paragraphs);

    private List<SearchUnit> SearchUnits(DocumentStories stories)
    {
        var units = new List<SearchUnit>();
        var parts = new List<(string Name, string Story, MarkupDocument Doc, DocumentStories Flag)>();
        if (stories.HasFlag(DocumentStories.Body) || stories.HasFlag(DocumentStories.TextBoxes))
        {
            parts.Add((MainPartName, "body", MainMarkup, DocumentStories.Body));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MainPartName };
        foreach ((string rel, string story, DocumentStories flag) in new[]
        {
            ("header", "header", DocumentStories.HeadersFooters), ("footer", "footer", DocumentStories.HeadersFooters),
            ("footnotes", "footnotes", DocumentStories.Notes), ("endnotes", "endnotes", DocumentStories.Notes), ("comments", "comments", DocumentStories.Comments),
        })
        {
            if (!stories.HasFlag(flag) && !stories.HasFlag(DocumentStories.TextBoxes))
            {
                continue;
            }

            foreach (Relationship r in Package.GetRelationships(MainPartName))
            {
                if (r.TargetMode != RelationshipTargetMode.Internal || !RelationshipTypes.Is(r.Type, rel)
                    || !PartUri.TryResolve(new PartUri(MainPartName), r.Target, out PartUri uri) || !seen.Add(uri.Value))
                {
                    continue;
                }

                if (Package.FindPart(uri.Value) is { } part && Parts.Load(part.Name, required: false) is { } doc)
                {
                    parts.Add((part.Name, story, doc, flag));
                }
            }
        }

        foreach ((string name, string story, MarkupDocument doc, DocumentStories flag) in parts)
        {
            if (doc.Root.IsNull)
            {
                continue;
            }

            if (stories.HasFlag(flag))
            {
                var ps = new List<MarkupNode>();
                CollectParagraphs(doc.Root, ps, 0);
                if (ps.Count > 0)
                {
                    units.Add(new SearchUnit(name, story, ps));
                }
            }

            if (stories.HasFlag(DocumentStories.TextBoxes))
            {
                var boxed = new List<MarkupNode>();
                CollectBoxParagraphs(TextBoxScanner.In(this, doc.Root), boxed, 0);
                if (boxed.Count > 0)
                {
                    units.Add(new SearchUnit(name, "textbox", boxed));
                }
            }
        }

        return units;
    }

    private void CollectBoxParagraphs(List<TextBox> boxes, List<MarkupNode> into, int depth)
    {
        if (depth > Context.Limits.MaxRecursionDepth)
        {
            return;
        }

        foreach (TextBox box in boxes)
        {
            foreach (Paragraph p in box.Paragraphs())
            {
                into.Add(p.Markup.Node);
            }

            CollectBoxParagraphs(TextBoxScanner.In(this, box.Container.Node), into, depth + 1);
        }
    }

    // Paragraphs in document order. Alternate content and text box content are left to the text box pass, so no text is seen twice.
    private static void CollectParagraphs(MarkupNode node, List<MarkupNode> into, int depth)
    {
        if (depth > 256)
        {
            return;
        }

        foreach (MarkupNode c in node.ChildElements)
        {
            if (Rev.WordXml.Is(c, "p"))
            {
                into.Add(c);
            }
            else if (MarkupCompatibility.IsAlternateContent(c) || Rev.WordXml.Is(c, "txbxContent"))
            {
                continue;
            }
            else
            {
                CollectParagraphs(c, into, depth + 1);
            }
        }
    }
}
