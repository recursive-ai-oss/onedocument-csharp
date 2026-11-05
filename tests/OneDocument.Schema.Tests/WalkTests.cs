using System.IO.Compression;
using System.Text;

namespace RecursiveAI.OneDocument.Schema.Tests;

/// <summary>
/// Walks every WordprocessingML part of a folder of .docx files through the generated content models and counts what maps to a
/// view and what does not. Runs only when ONEDOCUMENT_SCHEMA_WALK names the folder; ONEDOCUMENT_SCHEMA_WALK_REPORT names the report file.
/// </summary>
public sealed class WalkTests
{
    private static readonly string[] PartPatterns =
    [
        "word/document.xml", "word/styles.xml", "word/numbering.xml", "word/footnotes.xml", "word/endnotes.xml", "word/comments", "word/header", "word/footer",
        "word/settings.xml", "word/fontTable.xml", "word/webSettings.xml", "word/commentsExtended.xml", "word/people.xml",
    ];

    private sealed class Counts
    {
        public long Seen, Typed, Simple, Unknown, InsideUnknown, McWrappers, UnknownAttributes;
        public SortedDictionary<string, long> UnknownByNamespace = new(StringComparer.Ordinal);
        public SortedDictionary<string, long> UnknownNames = new(StringComparer.Ordinal);
        public SortedDictionary<string, long> UnknownAttributesByNamespace = new(StringComparer.Ordinal);
    }

    private struct AttributeCounter : IViewVisitor
    {
        public Counts Counts;

        public void Visit<T>(T view) where T : struct, IElementView<T>
        {
            foreach (var a in UnknownAttributes.Of(view))
            {
                Counts.UnknownAttributes++;
                Counts.UnknownAttributesByNamespace[a.NamespaceUri] = Counts.UnknownAttributesByNamespace.GetValueOrDefault(a.NamespaceUri) + 1;
            }
        }
    }

    [Fact]
    public void EveryElementOfTheCorpusMapsToAViewOrIsCountedUnknown()
    {
        var dir = Environment.GetEnvironmentVariable("ONEDOCUMENT_SCHEMA_WALK");
        if (string.IsNullOrEmpty(dir)) { Assert.Skip("ONEDOCUMENT_SCHEMA_WALK is not set"); return; }
        var counts = new Counts();
        var files = 0;
        var parts = 0;
        var roundTripDiffs = 0;
        var badZips = 0;
        var badParts = 0;
        foreach (var path in Directory.EnumerateFiles(dir, "*.docx", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            files++;
            ZipArchive zip;
            try { zip = ZipFile.OpenRead(path); }
            catch (InvalidDataException) { badZips++; continue; } // truncated or damaged downloads in the crawl
            using var _ = zip;
            foreach (var entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                if (!entry.FullName.EndsWith(".xml", StringComparison.Ordinal) || !PartPatterns.Any(p => entry.FullName.StartsWith(p, StringComparison.Ordinal))) continue;
                using var reader = new StreamReader(entry.Open(), new UTF8Encoding(false), true);
                var text = reader.ReadToEnd();
                TextTree tree;
                try { tree = TextTree.Parse(text); }
                catch (OfficeException) { badParts++; continue; } // not well-formed XML: the markup layer refuses it with a typed exception
                parts++;
                if (tree.Serialize() != text) roundTripDiffs++;
                var root = tree.RootRef;
                var kind = ViewFactory.RootKind(root.Namespace, root.LocalName);
                Walk(root, kind, counts);
            }
        }
        var sb = new StringBuilder();
        sb.AppendLine($"docx files: {files}, parts walked: {parts}, tree re-serialisation differences: {roundTripDiffs}");
        sb.AppendLine($"unreadable zip files: {badZips}, parts refused as malformed XML: {badParts}");
        sb.AppendLine($"elements seen: {counts.Seen}");
        sb.AppendLine($"  typed (a generated view): {counts.Typed}");
        sb.AppendLine($"  declared with a simple type (no view): {counts.Simple}");
        sb.AppendLine($"  markup-compatibility wrappers passed through: {counts.McWrappers}");
        sb.AppendLine($"  unknown (top of an unknown subtree): {counts.Unknown}");
        sb.AppendLine($"  inside unknown subtrees: {counts.InsideUnknown}");
        sb.AppendLine($"unknown attributes on typed elements: {counts.UnknownAttributes}");
        foreach (var (k, v) in counts.UnknownAttributesByNamespace) sb.AppendLine($"  {k}: {v}");
        sb.AppendLine("unknown elements by namespace:");
        foreach (var (k, v) in counts.UnknownByNamespace) sb.AppendLine($"  {k}: {v}");
        sb.AppendLine("unknown element names (top 60 by count):");
        foreach (var (k, v) in counts.UnknownNames.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Take(60)) sb.AppendLine($"  {k}: {v}");
        var report = Environment.GetEnvironmentVariable("ONEDOCUMENT_SCHEMA_WALK_REPORT");
        if (!string.IsNullOrEmpty(report)) File.WriteAllText(report, sb.ToString());
        Assert.True(parts > 0, "no parts found");
        Assert.Equal(0, roundTripDiffs);
        Assert.Equal(counts.Seen, counts.Typed + counts.Simple + counts.McWrappers + counts.Unknown + counts.InsideUnknown);
    }

    private static void Walk(MarkupRef element, ViewKind kind, Counts counts)
    {
        counts.Seen++;
        if (kind == ViewKind.None) { counts.Unknown++; CountSubtree(element, counts); return; }
        if (kind == ViewKind.Simple) { counts.Simple++; CountSubtree(element, counts); return; }
        counts.Typed++;
        var visitor = new AttributeCounter { Counts = counts };
        ViewFactory.Dispatch(kind, element, ref visitor);
        WalkChildren(element, kind, counts);
    }

    private static void WalkChildren(MarkupRef element, ViewKind kind, Counts counts)
    {
        foreach (var child in element.Children)
        {
            var ns = child.Namespace;
            var local = child.LocalName;
            if (ns == SchemaNamespace.MarkupCompatibility)
            {
                // Alternate content is transparent: its Choice and Fallback hold content of the parent's own kind.
                counts.Seen++;
                counts.McWrappers++;
                if (local.SequenceEqual("AlternateContent"u8))
                    foreach (var branch in child.Children) { counts.Seen++; counts.McWrappers++; WalkChildren(branch, kind, counts); }
                continue;
            }
            var ck = ViewFactory.ChildKind(kind, ns, local, out _);
            if (ck == ViewKind.None)
            {
                var uri = child.NamespaceUriText;
                counts.UnknownByNamespace[uri] = counts.UnknownByNamespace.GetValueOrDefault(uri) + 1;
                var name = uri + " " + child.LocalNameText + " in " + kind;
                counts.UnknownNames[name] = counts.UnknownNames.GetValueOrDefault(name) + 1;
            }
            Walk(child, ck, counts);
        }
    }

    private static void CountSubtree(MarkupRef element, Counts counts)
    {
        foreach (var child in element.Children)
        {
            counts.Seen++;
            counts.InsideUnknown++;
            CountSubtree(child, counts);
        }
    }
}
