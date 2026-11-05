using System.Linq;

namespace RecursiveAI.OneOffice.Documents.Numbering;

/// <summary>The list labels of one running flow of paragraphs (see <see cref="NumberingDefinitions.LabelStories"/>).</summary>
/// <param name="Kind">The kind of story. Headers and footers share one flow and are listed one part at a time.</param>
/// <param name="PartName">The part the story is stored in; empty for the text boxes of a story, which are listed as one flow.</param>
/// <param name="Labels">The labels of the numbered paragraphs, in the order Word counts them.</param>
public sealed record StoryLabels(StoryKind Kind, string PartName, IReadOnlyList<ListLabel> Labels);

public sealed partial class NumberingDefinitions
{
    /// <summary>
    /// Computes the list labels of every story of the document the way Word counts them. Observed in Word 365 (16.0.20527) on generated
    /// documents: counters run per flow of stories and never carry from one flow into another.
    /// </summary>
    /// <remarks>
    /// <para>The flows are:</para>
    /// <list type="bullet">
    /// <item><description>the body;</description></item>
    /// <item><description>the headers and footers of all sections as one flow: section by section, and inside a section in the order
    /// even header, default header, even footer, default footer, first header, first footer (only stories the section itself names);</description></item>
    /// <item><description>the footnotes as one flow, across notes (separator notes hold no text);</description></item>
    /// <item><description>the endnotes as one flow;</description></item>
    /// <item><description>the comments as one flow, across comments;</description></item>
    /// <item><description>the text boxes of the body as one flow in document order (an outer box, then the boxes inside it); boxes anchored in a
    /// header or footer, a note or a comment form one flow per kind of host (not checked against Word).</description></item>
    /// </list>
    /// <para>Inside a flow, two numbering instances of one abstract definition continue one count, and an instance with a start override
    /// restarts, as for the body. A text box with a copy in <c>mc:Fallback</c> is counted once.</para>
    /// </remarks>
    /// <returns>One entry per body, per header or footer part, per notes or comments part and per text-box flow, in that order; stories without
    /// numbered paragraphs are left out.</returns>
    public IReadOnlyList<StoryLabels> LabelStories()
    {
        var result = new List<StoryLabels>();
        Add(result, StoryKind.Body, _owner.MainPartName, new ListLabeler(this), _owner.Body.Paragraphs());

        var headerFooter = new ListLabeler(this);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HeaderFooterKind[] kinds = [HeaderFooterKind.Header, HeaderFooterKind.Header, HeaderFooterKind.Footer, HeaderFooterKind.Footer, HeaderFooterKind.Header, HeaderFooterKind.Footer];
        HeaderFooterType[] types = [HeaderFooterType.Even, HeaderFooterType.Default, HeaderFooterType.Even, HeaderFooterType.Default, HeaderFooterType.First, HeaderFooterType.First];
        foreach (Section section in _owner.Sections)
        {
            for (int i = 0; i < kinds.Length; i++)
            {
                if (section.GetOwn(kinds[i], types[i]) is { } story && seen.Add(story.PartName))
                {
                    Add(result, kinds[i] == HeaderFooterKind.Header ? StoryKind.Header : StoryKind.Footer, story.PartName, headerFooter, story.Paragraphs());
                }
            }
        }

        AddFlow(result, StoryKind.Footnotes, _owner.Footnotes.Select(n => (BlockContainer)n));
        AddFlow(result, StoryKind.Endnotes, _owner.Endnotes.Select(n => (BlockContainer)n));
        AddFlow(result, StoryKind.Comments, _owner.Comments.Select(c => (BlockContainer)c));

        var flows = new Dictionary<StoryKind, ListLabeler>();
        var order = new List<StoryKind>();
        var perHost = new Dictionary<StoryKind, List<Paragraph>>();
        foreach (TextBox box in _owner.TextBoxes())
        {
            StoryKind host = box.HostStory is StoryKind.Footer ? StoryKind.Header : box.HostStory;
            if (!perHost.TryGetValue(host, out List<Paragraph>? list))
            {
                perHost[host] = list = [];
                order.Add(host);
            }

            CollectBox(box, list);
        }

        foreach (StoryKind host in order)
        {
            flows[host] = new ListLabeler(this);
            Add(result, StoryKind.TextBox, string.Empty, flows[host], perHost[host]);
        }

        return result;
    }

    private static void CollectBox(TextBox box, List<Paragraph> into)
    {
        into.AddRange(box.Paragraphs());
        foreach (TextBox inner in box.TextBoxes())
        {
            CollectBox(inner, into);
        }
    }

    private void AddFlow(List<StoryLabels> result, StoryKind kind, IEnumerable<BlockContainer> containers)
    {
        string part = kind switch
        {
            StoryKind.Footnotes => "footnotes",
            StoryKind.Endnotes => "endnotes",
            _ => "comments",
        };
        var paragraphs = new List<Paragraph>();
        string name = string.Empty;
        foreach (BlockContainer c in containers)
        {
            paragraphs.AddRange(c.Paragraphs());
            if (name.Length == 0 && c.Container.IsValid && _owner.Parts.PartNameOf(c.Container.Node) is { } n)
            {
                name = n;
            }
        }

        Add(result, kind, name.Length == 0 ? "/word/" + part + ".xml" : name, new ListLabeler(this), paragraphs);
    }

    private static void Add(List<StoryLabels> result, StoryKind kind, string partName, ListLabeler labeler, IEnumerable<Paragraph> paragraphs)
    {
        IReadOnlyList<ListLabel> labels = labeler.Run(paragraphs);
        if (labels.Count > 0)
        {
            result.Add(new StoryLabels(kind, partName, labels));
        }
    }
}
