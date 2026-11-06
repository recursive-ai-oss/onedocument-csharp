using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>The parts of a presentation a search covers.</summary>
[Flags]
public enum PresentationParts
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The slides (hidden ones included): text in shapes, groups and tables.</summary>
    Slides = 1,

    /// <summary>The speaker notes of the slides.</summary>
    Notes = 2,

    /// <summary>The slide layouts.</summary>
    Layouts = 4,

    /// <summary>The slide masters and the notes master.</summary>
    Masters = 8,

    /// <summary>Slides and their notes: what a reader of the presentation sees or presents.</summary>
    Default = Slides | Notes,

    /// <summary>Everything above.</summary>
    All = Slides | Notes | Layouts | Masters,
}

/// <summary>What <see cref="Presentation.Find(string, PresentationFindOptions?)"/> searches and how.</summary>
public class PresentationFindOptions
{
    /// <summary>Gets how the pattern is read. Default: literal, case-insensitive.</summary>
    public SearchOptions Search { get; init; } = new();

    /// <summary>Gets the parts searched. Default <see cref="PresentationParts.Default"/> (slides and notes).</summary>
    public PresentationParts Parts { get; init; } = PresentationParts.Default;
}

/// <summary>What <see cref="Presentation.Replace(string, string, PresentationReplaceOptions?)"/> does; currently the same choices as finding.</summary>
public sealed class PresentationReplaceOptions : PresentationFindOptions
{
}

public sealed partial class Presentation
{
    /// <summary>
    /// Finds text as a reader sees it: the text of each paragraph, joining its runs (so a match can cross a change of formatting, a
    /// field's result or a hyperlinked run). A line break counts as one character, U+000B. A match never crosses a paragraph. Text of charts,
    /// SmartArt and comments lives in other parts and is not searched.
    /// </summary>
    /// <param name="pattern">The text, or the regular expression when <see cref="SearchOptions.UseRegex"/> is set.</param>
    /// <param name="options">What to search; null searches slides and notes with a literal, case-insensitive pattern.</param>
    /// <returns>The hits in slide order; the location names the part and the paragraph's index in it.</returns>
    /// <exception cref="ArgumentException">The pattern is empty or not a valid regular expression.</exception>
    /// <exception cref="OfficeLimitException">A regular expression ran past <see cref="OfficeLimits.RegexMatchTimeoutMilliseconds"/>.</exception>
    public IReadOnlyList<TextHit> Find(string pattern, PresentationFindOptions? options = null)
    {
        options ??= new PresentationFindOptions();
        TextSearch search = TextSearch.Create(pattern, options.Search, Context.Limits);
        var hits = new List<TextHit>();
        foreach (SearchUnit unit in Units(options.Parts))
        {
            for (int i = 0; i < unit.Paragraphs.Count; i++)
            {
                string text = ParagraphText.Read(unit.Paragraphs[i], out _);
                foreach (TextMatch m in search.Find(text))
                {
                    hits.Add(new TextHit(new TextLocation(unit.PartName, unit.Story, i, m.Index), m.Value));
                }
            }
        }

        return hits;
    }

    /// <summary>
    /// Replaces every match. Within a paragraph the runs are edited in place: the new text goes into the run where the match began, so it
    /// keeps that run's character formatting; later runs lose only the matched part of their text and a run left empty is removed.
    /// A line feed or U+000B in the replacement becomes a line break (<c>a:br</c>), which splits the run (not possible inside a field, which
    /// is then reported as <c>markup-unsupported</c>). Paragraph properties, bullets, hyperlinks on other runs and unknown markup stay as they are.
    /// </summary>
    /// <param name="pattern">The text, or the regular expression when <see cref="SearchOptions.UseRegex"/> is set.</param>
    /// <param name="replacement">The new text. For a regular-expression search <c>$1</c> and <c>${name}</c> insert groups.</param>
    /// <param name="options">What to search; null replaces in slides and notes.</param>
    /// <returns>Every replacement with its location, and every match left alone with the reason (<c>markup-unsupported</c>).</returns>
    /// <exception cref="ArgumentException">The pattern is empty or invalid, or the replacement holds a character XML cannot carry.</exception>
    /// <exception cref="OfficeLimitException">A regular expression ran past <see cref="OfficeLimits.RegexMatchTimeoutMilliseconds"/>.</exception>
    public ReplaceReport Replace(string pattern, string replacement, PresentationReplaceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        options ??= new PresentationReplaceOptions();
        TextSearch search = TextSearch.Create(pattern, options.Search, Context.Limits);
        var report = new ReplaceReport();
        foreach (SearchUnit unit in Units(options.Parts))
        {
            for (int i = 0; i < unit.Paragraphs.Count; i++)
            {
                MarkupNode p = unit.Paragraphs[i];
                string text = ParagraphText.Read(p, out bool escapes);
                IReadOnlyList<TextMatch> matches = search.Find(text);
                if (matches.Count == 0)
                {
                    continue;
                }

                var expanded = new string[matches.Count];
                for (int k = 0; k < expanded.Length; k++)
                {
                    expanded[k] = search.Expand(matches[k], replacement).Replace("\r\n", "\v", StringComparison.Ordinal).Replace('\n', '\v').Replace('\r', '\v');
                    ParagraphText.RequireXmlText(expanded[k], nameof(replacement));
                }

                var done = new List<ReplacementItem>();
                for (int k = matches.Count - 1; k >= 0; k--)
                {
                    var loc = new TextLocation(unit.PartName, unit.Story, i, matches[k].Index);
                    string? why = null;
                    if (escapes || !ParagraphText.TryReplace(p, matches[k].Index, matches[k].Length, expanded[k], out why))
                    {
                        report.Add(new ReplaceIssue("markup-unsupported", escapes ? "The paragraph's text has _xHHHH_ escapes; it was not changed." : why!, loc));
                        continue;
                    }

                    done.Add(new ReplacementItem(loc, matches[k].Value, expanded[k], false));
                }

                for (int k = done.Count - 1; k >= 0; k--)
                {
                    report.Add(done[k]);
                }
            }
        }

        return report;
    }

    private sealed record SearchUnit(string PartName, string Story, List<MarkupNode> Paragraphs);

    private List<SearchUnit> Units(PresentationParts parts)
    {
        var units = new List<SearchUnit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(PresentationPart part, string story)
        {
            if (!seen.Add(part.PartName))
            {
                return;
            }

            MarkupRef root = part.Markup;
            if (!root.IsValid)
            {
                return;
            }

            var ps = new List<MarkupNode>();
            ParagraphText.Collect(root.Node, ps, 0);
            if (ps.Count > 0)
            {
                units.Add(new SearchUnit(part.PartName, story, ps));
            }
        }

        if (parts.HasFlag(PresentationParts.Slides) || parts.HasFlag(PresentationParts.Notes))
        {
            foreach (Slide s in Slides)
            {
                if (parts.HasFlag(PresentationParts.Slides))
                {
                    Add(s, "slide");
                }

                if (parts.HasFlag(PresentationParts.Notes) && s.Notes is { } n)
                {
                    Add(n, "notes");
                }
            }
        }

        if (parts.HasFlag(PresentationParts.Layouts) || parts.HasFlag(PresentationParts.Masters))
        {
            foreach (SlideMaster m in Masters)
            {
                if (parts.HasFlag(PresentationParts.Masters))
                {
                    Add(m, "master");
                }

                if (parts.HasFlag(PresentationParts.Layouts))
                {
                    foreach (SlideLayout l in m.Layouts)
                    {
                        Add(l, "layout");
                    }
                }
            }
        }

        if (parts.HasFlag(PresentationParts.Masters) && NotesMaster is { } nm)
        {
            Add(nm, "notes-master");
        }

        return units;
    }
}

// The paragraphs of DrawingML text (a:p) and the edits that keep a paragraph's runs.
internal static class ParagraphText
{
    private readonly record struct Atom(MarkupNode Run, MarkupNode Node, bool IsBreak, int Start, int Length);

    internal static void RequireXmlText(string s, string paramName)
    {
        foreach (char c in s)
        {
            bool ok = c is '\t' or '\n' or '\r' || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD) || char.IsSurrogate(c) || c == '\v';
            if (!ok)
            {
                throw new ArgumentException("The text has a character XML cannot carry (U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) + ").", paramName);
            }
        }
    }

    // Paragraphs in document order; the fallback of alternate content repeats the choice, so it is skipped.
    internal static void Collect(MarkupNode node, List<MarkupNode> into, int depth)
    {
        if (depth > 256)
        {
            return;
        }

        foreach (MarkupNode c in node.ChildElements)
        {
            if (IsDrawing(c) && c.LocalName.SequenceEqual("p"u8))
            {
                into.Add(c);
            }
            else if (MarkupCompatibility.IsFallback(c))
            {
                continue;
            }
            else
            {
                Collect(c, into, depth + 1);
            }
        }
    }

    private static bool IsDrawing(MarkupNode n) =>
        SchemaNamespaces.Resolve(n.NamespaceUri) == SchemaNamespace.Drawing;

    private static List<Atom> Atoms(MarkupNode p)
    {
        var list = new List<Atom>();
        int pos = 0;
        foreach (MarkupNode run in p.ChildElements)
        {
            if (!IsDrawing(run))
            {
                continue;
            }

            if (run.LocalName.SequenceEqual("r"u8) || run.LocalName.SequenceEqual("fld"u8))
            {
                foreach (MarkupNode t in run.ChildElements)
                {
                    if (IsDrawing(t) && t.LocalName.SequenceEqual("t"u8))
                    {
                        int len = t.InnerText.Length;
                        list.Add(new Atom(run, t, false, pos, len));
                        pos += len;
                        break;
                    }
                }
            }
            else if (run.LocalName.SequenceEqual("br"u8))
            {
                list.Add(new Atom(run, run, true, pos, 1));
                pos++;
            }
        }

        return list;
    }

    /// <summary>The paragraph's text: run and field text joined, a line break as U+000B. <paramref name="escapes"/> tells that some text holds <c>_xHHHH_</c> escapes (which this text keeps decoded).</summary>
    internal static string Read(MarkupNode p, out bool escapes)
    {
        escapes = false;
        var sb = new StringBuilder();
        foreach (Atom a in Atoms(p))
        {
            if (a.IsBreak)
            {
                sb.Append('\v');
                continue;
            }

            string raw = a.Node.InnerText;
            string decoded = TextParagraph.DecodeText(raw);
            if (decoded != raw)
            {
                escapes = true;
            }

            sb.Append(raw);
        }

        return sb.ToString();
    }

    private static string Prefixed(MarkupNode n, string local) => (n.Prefix.Length == 0 ? string.Empty : Encoding.UTF8.GetString(n.Prefix) + ":") + local;

    private static string RunProps(MarkupNode run)
    {
        foreach (MarkupNode c in run.ChildElements)
        {
            if (IsDrawing(c) && c.LocalName.SequenceEqual("rPr"u8))
            {
                return c.OuterXml;
            }
        }

        return string.Empty;
    }

    private static void SetText(MarkupNode t, string text)
    {
        t.SetText(text);
        bool edge = text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]));
        if (edge && t.GetAttribute("http://www.w3.org/XML/1998/namespace", "space") is null)
        {
            t.SetAttribute("xml:space", "preserve");
        }
    }

    /// <summary>Replaces a stretch of the paragraph's text; false (with the reason) when the markup cannot take it.</summary>
    internal static bool TryReplace(MarkupNode p, int start, int length, string replacement, out string? why)
    {
        why = null;
        List<Atom> atoms = Atoms(p);
        int end = start + length;
        var hit = new List<Atom>();
        foreach (Atom a in atoms)
        {
            if (a.Length > 0 && a.Start < end && a.Start + a.Length > start)
            {
                hit.Add(a);
            }
        }

        if (hit.Count == 0)
        {
            why = "The match is not in any run.";
            return false;
        }

        Atom first = hit[0];
        bool hasBreak = replacement.Contains('\v', StringComparison.Ordinal);
        bool firstIsField = first.Run.LocalName.SequenceEqual("fld"u8);
        if ((hasBreak && firstIsField) || (first.IsBreak && firstIsField))
        {
            why = "A line break cannot be put inside a field.";
            return false;
        }

        string pre = first.IsBreak ? string.Empty : first.Node.InnerText[..(start - first.Start)];
        Atom last = hit[^1];
        string post = last.IsBreak ? string.Empty : last.Node.InnerText[(end - last.Start)..];

        // Later runs first: they only lose text, and the handles of earlier nodes stay valid.
        for (int i = hit.Count - 1; i >= 1; i--)
        {
            Atom a = hit[i];
            if (a.IsBreak)
            {
                a.Run.Remove();
                continue;
            }

            string keep = i == hit.Count - 1 ? post : string.Empty;
            if (keep.Length == 0)
            {
                a.Run.Remove();
            }
            else
            {
                SetText(a.Node, keep);
            }
        }

        string endPart = hit.Count == 1 ? post : string.Empty;
        if (!first.IsBreak && !hasBreak)
        {
            SetText(first.Node, pre + replacement + endPart);
            return true;
        }

        // The replacement carries line breaks, or the match began on one: the run is rebuilt as runs and breaks with its own properties.
        string props = RunProps(first.IsBreak ? first.Node : first.Run);
        string runName = Prefixed(first.Run, "r");
        string tName = Prefixed(first.Run, "t");
        string brName = Prefixed(first.Run, "br");
        string[] segments = replacement.Split('\v');
        segments[0] = pre + segments[0];
        segments[^1] += endPart;
        var sb = new StringBuilder();
        for (int s = 0; s < segments.Length; s++)
        {
            if (s > 0)
            {
                sb.Append('<').Append(brName).Append('>').Append(props).Append("</").Append(brName).Append('>');
            }

            if (segments[s].Length > 0)
            {
                sb.Append('<').Append(runName).Append('>').Append(props).Append('<').Append(tName).Append(" xml:space=\"preserve\">")
                  .Append(Escape(segments[s])).Append("</").Append(tName).Append("></").Append(runName).Append('>');
            }
        }

        try
        {
            first.Run.InsertXmlBefore(Encoding.UTF8.GetBytes(sb.ToString()));
        }
        catch (ArgumentException ex)
        {
            why = "The markup around the run cannot be rewritten: " + ex.Message;
            return false;
        }

        first.Run.Remove();
        return true;
    }

    private static string Escape(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}
