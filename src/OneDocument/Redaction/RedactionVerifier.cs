using System.Linq;
using System.Text;
using RecursiveAI.OneArchive;
using RecursiveAI.OneArchive.Zip;
using RecursiveAI.OneCompress;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Redaction;

// Re-opens the written file and looks for any match in every zip entry (parts, relationships, content types and entries that are not
// parts), reading what a reader would: decoded text nodes, attribute values, paragraph text across runs, comments, raw bytes, printable
// runs of binary parts and the contents of embedded packages.
internal sealed class RedactionVerifier
{
    private readonly RedactionContext _ctx;
    private readonly List<TextSegment> _segments = [];

    public RedactionVerifier(RedactionContext ctx) => _ctx = ctx;

    public void Verify(byte[] output)
    {
        _ctx.Report.VerificationRan = true;
        ScanZip(output, string.Empty, 0);
    }

    private void ScanZip(byte[] bytes, string prefix, int depth)
    {
        if (depth > _ctx.Limits.MaxRecursionDepth)
        {
            _ctx.Report.Gap("redaction.depth", prefix, "Embedded packages nest deeper than the recursion limit and were not verified.");
            return;
        }

        // Content types say which entries are pictures; a zip that is not a package has none.
        _ = OpcPackage.TryOpen(ByteSource.FromMemory(bytes), out OpcPackage? typed, new OpenContext(_ctx.Limits));
        using OpcPackage? typedScope = typed;
        try
        {
            var options = new ZipReaderOptions { Limits = _ctx.Limits.ToArchiveLimits() };
            using var reader = new ZipReader(new MemoryStream(bytes, writable: false), options, leaveOpen: false);
            while (reader.GetNextEntry() is { } entry)
            {
                string location = prefix + entry.Name;
                Name(entry.Name, location);
                if (entry.Kind == ArchiveEntryKind.Directory)
                {
                    continue;
                }

                using var ms = new MemoryStream();
                using (Stream s = reader.OpenEntryStream())
                {
                    s.CopyTo(ms);
                }

                if (ms.Length > _ctx.Limits.MaxPartSize)
                {
                    throw new OfficeLimitException(nameof(OfficeLimits.MaxPartSize), _ctx.Limits.MaxPartSize, ms.Length);
                }

                Entry(ms.ToArray(), location, depth, typed?.FindPart("/" + entry.Name) is { } typedPart && PackageRedactor.IsImagePart(entry.Name, typedPart.ContentType) || BinaryText.IsImageName(entry.Name));
            }
        }
        catch (CompressionException ex)
        {
            throw ArchiveErrors.Translate(ex);
        }
    }

    private void Name(string name, string location)
    {
        int hit = Survivor(name, literalOnly: true);
        if (hit >= 0)
        {
            _ctx.Report.Survivor(location + " (name)", hit);
        }
    }

    private void Entry(byte[] bytes, string location, int depth, bool image)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        if (BinaryText.IsZip(bytes))
        {
            try
            {
                ScanZip(bytes, location + "!", depth + 1);
                return;
            }
            catch (OfficeFormatException)
            {
                // Not a readable zip after all: searched as raw bytes below, as the redaction did.
            }
        }

        if (MarkupDocument.TryParse(bytes, out MarkupDocument? doc, new OpenContext(_ctx.Limits), location) && doc is not null)
        {
            Xml(doc, location);
            RawLiteral(bytes, location, xml: true);
            return;
        }

        RawLiteral(bytes, location, xml: false);
        foreach (BinaryText.Run run in BinaryText.Runs(bytes, image ? BinaryText.ImageMinimumRun : 4))
        {
            int hit = Survivor(run.Text, false, image ? BinaryText.ImageMinimumMatch : 0);
            if (hit >= 0)
            {
                _ctx.Report.Survivor(location + " (raw)", hit);
            }
        }
    }

    private void RawLiteral(byte[] bytes, string location, bool xml)
    {
        for (int i = 0; i < _ctx.Plan.Matches.Count; i++)
        {
            RedactionMatch m = _ctx.Plan.Matches[i];
            if (m.IsRegex)
            {
                continue;
            }

            foreach ((byte[] needle, int _) in BinaryText.Needles(m.PatternText))
            {
                if (BinaryText.Find(bytes, needle).Any())
                {
                    _ctx.Report.Survivor(location + (xml ? " (raw bytes)" : " (raw)"), i);
                    break;
                }
            }
        }
    }

    private int Survivor(string text, bool literalOnly = false, int minimumMatch = 0)
    {
        var spans = new List<(int Start, int Length)>();
        for (int i = 0; i < _ctx.Plan.Matches.Count; i++)
        {
            spans.Clear();
            if (literalOnly && _ctx.Plan.Matches[i].IsRegex)
            {
                continue;
            }

            _ctx.Plan.Matches[i].FindAll(text, spans, _ctx.Limits, "verify");
            foreach ((int s, int l) in spans)
            {
                if (l < minimumMatch && _ctx.Plan.Matches[i].IsRegex)
                {
                    continue;
                }

                string hit = text.Substring(s, l);
                bool inMark = false;
                foreach (string mark in _ctx.Marks)
                {
                    if (mark.Contains(hit, StringComparison.Ordinal))
                    {
                        inMark = true;
                        break;
                    }
                }

                if (!inMark)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private void Xml(MarkupDocument doc, string location)
    {
        Walk(doc.DocumentNode, location);
    }

    private void Walk(MarkupNode parent, string location)
    {
        for (MarkupNode c = parent.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            switch (c.Kind)
            {
                case MarkupNodeKind.Element:
                    Element(c, location);
                    Walk(c, location);
                    break;
                case MarkupNodeKind.Text:
                case MarkupNodeKind.CData:
                    if (IsRunText(parent))
                    {
                        break; // matched as part of its paragraph, which is checked as a whole
                    }

                    Report(Survivor(c.Kind == MarkupNodeKind.Text ? c.InnerText : Encoding.UTF8.GetString(c.RawContent)), location, "text");
                    break;
                case MarkupNodeKind.Comment:
                case MarkupNodeKind.ProcessingInstruction:
                    Report(Survivor(Encoding.UTF8.GetString(c.RawContent)), location, "comment");
                    break;
            }
        }
    }

    // The text of a run belongs to the paragraph-like container it is in: matching is defined over the joined text, so a word split across runs
    // is not a match when one run is looked at alone.
    private static bool IsRunText(MarkupNode leaf)
    {
        if (OoxmlText.Classify(leaf, out _) is not (TextRole.Leaf or TextRole.CodeLeaf))
        {
            return false;
        }

        for (MarkupNode n = leaf.Parent; !n.IsNull; n = n.Parent)
        {
            if (n.Kind == MarkupNodeKind.Element && OoxmlText.Classify(n, out _) == TextRole.Container)
            {
                return true;
            }
        }

        return false;
    }

    private void Element(MarkupNode el, string location)
    {
        Report(Survivor(Encoding.UTF8.GetString(el.RawName), true), location, "name");
        foreach (MarkupAttributeRef a in el.Attributes)
        {
            if (a.IsNamespaceDeclaration)
            {
                // A namespace URI is not content; a literal planted in one is still a leak.
                Report(Survivor(a.Value, true), location, "attribute");
                continue;
            }

            string value = a.Value;
            Report(Survivor(Encoding.UTF8.GetString(a.RawName), true), location, "name");
            Report(Survivor(value), location, "attribute");
            if (value.Contains('%', StringComparison.Ordinal))
            {
                try
                {
                    Report(Survivor(Uri.UnescapeDataString(value)), location, "attribute");
                }
                catch (UriFormatException)
                {
                }
            }
        }

        if (OoxmlText.Classify(el, out _) == TextRole.Container)
        {
            _segments.Clear();
            OoxmlText.Collect(el, _segments);
            var visible = new StringBuilder();
            var code = new StringBuilder();
            foreach (TextSegment s in _segments)
            {
                (s.Code ? code : visible).Append(s.Text);
            }

            Report(Survivor(visible.ToString()), location, "text");
            Report(Survivor(code.ToString()), location, "text");
        }
    }

    private void Report(int match, string location, string where)
    {
        if (match >= 0)
        {
            _ctx.Report.Survivor($"{location} ({where})", match);
        }
    }
}
