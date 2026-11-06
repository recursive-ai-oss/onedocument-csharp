using System.Linq;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Layout;

/// <summary>A field whose layout-time result was not written back, and why.</summary>
/// <param name="Code">A stable code: <c>fields.per-page</c> (a header or footer field that shows a different result on different pages), <c>fields.not-written</c> (the field could not take a result).</param>
/// <param name="FieldType">The field type, for example <c>PAGE</c>.</param>
/// <param name="Message">A sentence for people.</param>
public sealed record FieldUpdateSkip(string Code, string FieldType, string Message);

/// <summary>What <see cref="DocumentFieldUpdate.UpdateFields"/> did.</summary>
public sealed class FieldUpdateReport
{
    internal FieldUpdateReport(int tablesBuilt, int updated, int unchanged, IReadOnlyList<FieldUpdateSkip> skipped, int passes, IReadOnlyList<LayoutNote> notes, bool flagCleared)
    {
        TablesBuilt = tablesBuilt;
        FieldsUpdated = updated;
        FieldsUnchanged = unchanged;
        Skipped = skipped;
        LayoutPasses = passes;
        Notes = notes;
        UpdateFieldsFlagCleared = flagCleared;
    }

    /// <summary>Gets how many tables of contents, indexes and tables of authorities were built or rebuilt.</summary>
    public int TablesBuilt { get; }

    /// <summary>Gets how many other fields got a new stored result.</summary>
    public int FieldsUpdated { get; }

    /// <summary>Gets how many fields were computed and already showed that result.</summary>
    public int FieldsUnchanged { get; }

    /// <summary>Gets the fields whose computed result was not written, each with the reason.</summary>
    public IReadOnlyList<FieldUpdateSkip> Skipped { get; }

    /// <summary>Gets how many layout passes the page-dependent fields took to settle.</summary>
    public int LayoutPasses { get; }

    /// <summary>Gets what the layout could not do exactly (for example <c>layout.field-skipped</c>, <c>layout.fields-unsettled</c>).</summary>
    public IReadOnlyList<LayoutNote> Notes { get; }

    /// <summary>Gets a value indicating whether <c>w:updateFields</c> was present in the settings and removed.</summary>
    public bool UpdateFieldsFlagCleared { get; }
}

/// <summary>Writes the results of fields that depend on the layout back into the document.</summary>
public static class DocumentFieldUpdate
{
    /// <summary>
    /// Updates the fields of the document as an update of all fields does, and stores the results in the file: the table of
    /// contents, index and table of authorities are rebuilt as paragraphs, and <c>PAGE</c>, <c>NUMPAGES</c>, <c>SECTIONPAGES</c>,
    /// <c>SECTION</c>, <c>PAGEREF</c>, <c>NOTEREF</c>, <c>REF</c>, <c>STYLEREF</c>, <c>SEQ</c> and the date, property, <c>IF</c> and <c>=</c> fields
    /// get the result the layout computed (see <c>docs/layout-fields.md</c>). A saved file then shows the same results without Word
    /// updating fields; <c>w:updateFields</c> is removed from the settings.
    /// </summary>
    /// <param name="document">The document. It is edited: the fields' result runs and the entries of rebuilt tables change; nothing else does.</param>
    /// <param name="options">Layout options (fonts, clock for <c>DATE</c> and <c>TIME</c>, pass limit); null for the defaults. <see cref="LayoutOptions.UpdateFields"/> is always on for this call.</param>
    /// <param name="cancellationToken">Cancels between paragraphs of the layout.</param>
    /// <returns>What was built, written and left alone.</returns>
    /// <remarks>
    /// <para>The layout runs on the document, so every field that depends on pages reads the facts of the previous pass and the stored
    /// results are those of the final pass: laying the saved file out again moves no line. A field in a header or footer that shows a different
    /// result on different pages (<c>PAGE</c>) keeps its stored result and is listed in <see cref="FieldUpdateReport.Skipped"/>. A field with
    /// <c>w:fldLock</c> is never changed. Index and authorities entries hold their page numbers as plain text, and the
    /// temporary bookmarks the build uses are removed again.</para>
    /// <para>Fields that need the outside world (<c>INCLUDETEXT</c>, <c>HYPERLINK</c> targets, <c>MERGEFIELD</c>) keep their stored results. Nothing is read from the system clock unless <see cref="LayoutOptions.Clock"/> is given.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static FieldUpdateReport UpdateFields(this Document document, LayoutOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        LayoutOptions opt = (options ?? new LayoutOptions()).WithUpdateFields();
        var notes = new List<LayoutNote>();

        FieldModel model = FieldModel.Build(document);
        int built = FieldUpdater.NeedsBuild(model, opt) ? FieldUpdater.ApplyInPlace(document, model, opt, notes) : 0;

        // The tables now exist in the document, so the layout reads them as they are.
        var sink = new FieldResultSink();
        opt.ResultSink = sink;
        opt.SkipFieldBuild = true;
        LayoutDocument layout = LayoutEngine.Layout(document, opt, cancellationToken);
        notes.AddRange(layout.Notes);

        // The model covers the body; the fields of headers, footers, notes and comments are found in their own parts.
        FieldModel after = FieldModel.Build(document);
        var stories = new Dictionary<MarkupNode, Documents.Fields.Field>();
        foreach ((string name, MarkupDocument part) in OpsXml.StoryParts(document))
        {
            foreach (Documents.Fields.Field f in Documents.Fields.FieldScanner.Scan(document, new Schema.MarkupRef(part.Root), name))
            {
                stories[f.Markup.Node] = f;
            }
        }

        int updated = 0;
        int unchanged = 0;
        var skipped = new List<FieldUpdateSkip>();
        foreach (KeyValuePair<MarkupNode, FieldResultSink.Entry> kv in sink.Entries.ToList())
        {
            FieldResultSink.Entry e = kv.Value;
            if (e.Ambiguous)
            {
                skipped.Add(new FieldUpdateSkip("fields.per-page", e.Type, "The " + e.Type + " field shows a different result on different pages, so its stored result was kept."));
                continue;
            }

            if (e.Value == e.Cached)
            {
                unchanged++;
                continue;
            }

            Documents.Fields.Field? field = after.FieldAt(kv.Key)?.Field ?? stories.GetValueOrDefault(kv.Key);
            if (field is null || field.IsLocked)
            {
                continue;
            }

            try
            {
                field.SetResult(e.Value);
                updated++;
            }
            catch (InvalidOperationException)
            {
                skipped.Add(new FieldUpdateSkip("fields.not-written", e.Type, "The " + e.Type + " field is not closed, so it has nowhere to keep a result."));
            }
        }

        FinishIndexFields(document);
        bool cleared = ClearUpdateFieldsFlag(document);
        return new FieldUpdateReport(built, updated, unchanged, skipped, layout.FieldPasses, notes, cleared);
    }

    // Index and authorities entries carry their page numbers as plain text; the page-list fields and the bookmarks the build placed at
    // XE and TA fields only existed to compute them.
    private static void FinishIndexFields(Document document)
    {
        FieldModel model = FieldModel.Build(document);
        foreach (FieldInfo fi in model.Fields.Where(f => f.Parsed.Type == IndexPages.FieldName).ToList())
        {
            Documents.Fields.Field f = fi.Field;
            var structural = new List<MarkupNode> { f.Markup.Node };
            if (f.SeparatorRun is { } sep)
            {
                structural.Add(sep.Node);
            }

            if (f.EndRun is { } end)
            {
                structural.Add(end.Node);
            }

            foreach (MarkupNode instr in f.InstructionNodes)
            {
                structural.Add(instr.Parent);
            }

            foreach (MarkupNode run in structural.Distinct())
            {
                if (!run.IsNull && OnlyFieldMachinery(run))
                {
                    run.Remove();
                }
            }
        }

        foreach (MarkupNode n in OpsXmlRendering.Elements(document.MainMarkup.Root).Where(IsOdXeBookmark).ToList())
        {
            n.Remove();
        }
    }

    private static bool OnlyFieldMachinery(MarkupNode run)
    {
        if (run.LocalNameString != "r")
        {
            return false;
        }

        for (MarkupNode c = run.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == MarkupNodeKind.Element && c.LocalNameString is not ("rPr" or "fldChar" or "instrText"))
            {
                return false;
            }
        }

        return true;
    }

    // The builder names the bookmarks it places at XE and TA fields _OdXe000000; the end element carries only the id, so the ids are collected first.
    private static bool IsOdXeBookmark(MarkupNode n)
    {
        if (n.Kind != MarkupNodeKind.Element || n.LocalNameString is not ("bookmarkStart" or "bookmarkEnd"))
        {
            return false;
        }

        if (n.LocalNameString == "bookmarkStart")
        {
            return n.GetAttribute(n.NamespaceUri, "name") is { } name && name.StartsWith("_OdXe", StringComparison.Ordinal);
        }

        // A bookmarkEnd is removed when a start with the same id and an _OdXe name sits right before it (the builder writes them as a pair).
        MarkupNode prev = n.PreviousSibling;
        return !prev.IsNull && prev.Kind == MarkupNodeKind.Element && prev.LocalNameString == "bookmarkStart"
            && prev.GetAttribute(prev.NamespaceUri, "name") is { } pn && pn.StartsWith("_OdXe", StringComparison.Ordinal);
    }

    private static bool ClearUpdateFieldsFlag(Document document)
    {
        if (document.LoadRelatedByType(document.OfficeRelationship("settings"), out _) is not { } settings)
        {
            return false;
        }

        bool found = false;
        for (MarkupNode c = settings.Root.FirstChild; !c.IsNull;)
        {
            MarkupNode next = c.NextSibling;
            if (c.Kind == MarkupNodeKind.Element && c.LocalNameString == "updateFields" && HubXml.IsWordNamespace(c.NamespaceUri))
            {
                c.Remove();
                found = true;
            }

            c = next;
        }

        return found;
    }
}

// Element enumeration over a part, for the few places in Rendering that edit the document tree directly.
internal static class OpsXmlRendering
{
    public static IEnumerable<MarkupNode> Elements(MarkupNode root)
    {
        var stack = new Stack<MarkupNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            MarkupNode n = stack.Pop();
            if (n.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            yield return n;
            for (MarkupNode c = n.LastChild; !c.IsNull; c = c.PreviousSibling)
            {
                stack.Push(c);
            }
        }
    }
}
