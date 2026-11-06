using System.Linq;
using System.Text;
using RecursiveAI.OneArchive;
using RecursiveAI.OneArchive.Zip;
using RecursiveAI.OneCompress;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Vba;

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

        // Comments, extra fields and names in the zip headers are outside every entry.
        foreach (ZipMetadata.Region region in ZipMetadata.Regions(bytes))
        {
            RawLiteral(bytes.AsSpan(region.Start, region.Length).ToArray(), prefix + "(" + region.What + ")", xml: false);
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

        if (!image)
        {
            TextForms(bytes, location);
        }

        CompoundStreams(bytes, location, depth);
        Deflated(bytes, location, depth, image);
    }

    // Text parts that are not XML (an included HTML page, a mail archive, RTF) store characters as references, escapes or quoted-printable;
    // the secret is read after the reader decodes them.
    private void TextForms(byte[] bytes, string location)
    {
        // Only parts that are text: decoding the bytes of a movie as text finds words that are not there.
        if (bytes.Length < 4 || BinaryText.IsZip(bytes) || CompoundFileMap.IsCompoundFile(bytes) || !EncodedText.LooksLikeText(bytes))
        {
            return;
        }

        if (bytes.Length >= 5 && bytes[0] == (byte)'{' && bytes[1] == (byte)'\\' && bytes[2] == (byte)'r' && bytes[3] == (byte)'t' && bytes[4] == (byte)'f')
        {
            Report(Survivor(RecursiveAI.OneDocument.Rtf.RtfHtmlConverter.Convert(bytes).Text), location, "rtf text");
            return;
        }

        string text = Encoding.UTF8.GetString(bytes);
        if (text.Contains('&', StringComparison.Ordinal))
        {
            Report(Survivor(System.Net.WebUtility.HtmlDecode(text)), location, "html text");
        }

        if (text.Contains('=', StringComparison.Ordinal))
        {
            Report(Survivor(DecodeQuotedPrintable(text)), location, "quoted-printable text");
        }
    }

    // RFC 2045 §6.7: =XX is a byte, and = at the end of a line joins it to the next.
    private static string DecodeQuotedPrintable(string text)
    {
        var bytes = new List<byte>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '=' && i + 2 < text.Length && char.IsAsciiHexDigit(text[i + 1]) && char.IsAsciiHexDigit(text[i + 2]))
            {
                bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
                i += 2;
            }
            else if (text[i] == '=' && i + 1 < text.Length && text[i + 1] is '\r' or '\n')
            {
                i += i + 2 < text.Length && text[i + 1] == '\r' && text[i + 2] == '\n' ? 2 : 1;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(text[i].ToString()));
            }
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }

    private const int MaxCandidates = 2000;

    // A compound file's streams as the reader sees them (a string across sectors that are not neighbours), what a macro project holds once its
    // source is decompressed, and packages stored inside a stream.
    private void CompoundStreams(byte[] bytes, string location, int depth)
    {
        if (!CompoundFileMap.TryRead(bytes, out List<CompoundFileMap.StreamMap> streams))
        {
            return;
        }

        foreach (CompoundFileMap.StreamMap stream in streams)
        {
            string at = location + "[" + stream.Path + "]";
            byte[] data = stream.Read(bytes);
            Entry(data, at, depth + 1, false);
            int tried = 0;
            for (int i = 0; i + 3 < data.Length && tried < MaxCandidates; i++)
            {
                // [MS-OVBA] 2.4.1.1: a compressed container starts with 0x01 and chunk headers whose signature bits are 0b011.
                if (data[i] == 1 && (data[i + 2] & 0x70) == 0x30)
                {
                    tried++;
                    if (VbaCompression.TryDecompress(data.AsSpan(i), _ctx.Limits.MaxPartSize, out byte[] source, out _) && source.Length > 0)
                    {
                        Entry(source, at + "[macro source]", depth + 1, false);
                    }
                }
            }

            int pk = data.Length > 1 ? data.AsSpan(1).IndexOf(new byte[] { 0x50, 0x4B, 0x03, 0x04 }) : -1;
            if (pk >= 0 && !BinaryText.IsZip(data))
            {
                try
                {
                    ScanZip(data.AsSpan(pk + 1).ToArray(), at + "!", depth + 1);
                }
                catch (OfficeException)
                {
                    // Not a package after all; its bytes were searched above.
                }
            }
        }
    }

    // Deflated data inside a binary entry (a gzip file, a text chunk in a picture, an object header) is read as the reader would after inflating it.
    private void Deflated(byte[] bytes, string location, int depth, bool image)
    {
        if (bytes.Length > 20 && bytes[0] == 0x1F && bytes[1] == 0x8B)
        {
            Inflated(() => new System.IO.Compression.GZipStream(new MemoryStream(bytes), System.IO.Compression.CompressionMode.Decompress), location + "[gzip]", depth, image);
        }

        int tried = 0;
        for (int i = 0; i + 8 < bytes.Length && tried < MaxCandidates; i++)
        {
            // RFC 1950: CMF 0x78 (deflate, 32K window) and a header checksum that makes CMF*256+FLG a multiple of 31.
            if (bytes[i] == 0x78 && bytes[i + 1] is 0x01 or 0x5E or 0x9C or 0xDA && ((bytes[i] << 8) | bytes[i + 1]) % 31 == 0)
            {
                tried++;
                int at = i;
                Inflated(() => new System.IO.Compression.ZLibStream(new MemoryStream(bytes, at, bytes.Length - at), System.IO.Compression.CompressionMode.Decompress), location + "[deflate@" + at + "]", depth, image);
            }
        }
    }

    private void Inflated(Func<Stream> open, string location, int depth, bool image)
    {
        byte[] inflated;
        try
        {
            using Stream z = open();
            using var ms = new MemoryStream();
            var buffer = new byte[8192];
            int n;
            while ((n = z.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, n);
                if (ms.Length > _ctx.Limits.MaxPartSize)
                {
                    return;
                }
            }

            inflated = ms.ToArray();
        }
        catch (InvalidDataException)
        {
            return;
        }

        if (inflated.Length < 4 || depth > _ctx.Limits.MaxRecursionDepth)
        {
            return;
        }

        // Pixel data is not text: only a literal counts there, as in the picture itself.
        if (image)
        {
            RawLiteral(inflated, location, xml: false);
        }
        else
        {
            Entry(inflated, location, depth + 1, false);
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

                if (!InsideMark(text, s, l))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    // A match is the redaction's own doing only when it lies within a mark that is written there: a pattern such as ".+" matches its marks, and
    // so does a word that is part of an identifier mark. The same letters elsewhere in the text are still a survivor.
    private bool InsideMark(string text, int start, int length)
    {
        foreach (string mark in _ctx.Marks)
        {
            int at = text.IndexOf(mark, StringComparison.Ordinal);
            while (at >= 0)
            {
                if (start >= at && start + length <= at + mark.Length)
                {
                    return true;
                }

                at = text.IndexOf(mark, at + 1, StringComparison.Ordinal);
            }
        }

        return false;
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

                    string text = c.Kind == MarkupNodeKind.Text ? c.InnerText : Encoding.UTF8.GetString(c.RawContent);
                    Report(Survivor(text), location, "text");
                    Report(Encoded(text, OoxmlText.IsSmlNode(parent)), location, "text");
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
            Report(Encoded(value, OoxmlText.IsSmlNode(el)), location, "attribute");
            string level = value;
            for (int i = 0; i < 4 && level.Contains('%', StringComparison.Ordinal); i++)
            {
                try
                {
                    level = Uri.UnescapeDataString(level);
                }
                catch (UriFormatException)
                {
                    break;
                }

                Report(Survivor(level), location, "attribute");
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

    // The value as a reader decodes it: SpreadsheetML _xHHHH_ escapes, or base64 that holds bytes.
    private int Encoded(string text, bool sml)
    {
        if (sml)
        {
            string decoded = EncodedText.DecodeX(text);
            if (!string.Equals(decoded, text, StringComparison.Ordinal) && Survivor(decoded) is var hit and >= 0)
            {
                return hit;
            }
        }

        if (!EncodedText.TryDecodeBase64(text, out byte[] bytes))
        {
            return -1;
        }

        for (int i = 0; i < _ctx.Plan.Matches.Count; i++)
        {
            RedactionMatch m = _ctx.Plan.Matches[i];
            if (!m.IsRegex && BinaryText.Needles(m.PatternText).Any(n => BinaryText.Find(bytes, n.Needle).Any()))
            {
                return i;
            }
        }

        if (EncodedText.LooksLikeText(bytes))
        {
            foreach (BinaryText.Run run in BinaryText.Runs(bytes))
            {
                int found = Survivor(run.Text);
                if (found >= 0)
                {
                    return found;
                }
            }
        }

        return -1;
    }

    private void Report(int match, string location, string where)
    {
        if (match >= 0)
        {
            _ctx.Report.Survivor($"{location} ({where})", match);
        }
    }
}
