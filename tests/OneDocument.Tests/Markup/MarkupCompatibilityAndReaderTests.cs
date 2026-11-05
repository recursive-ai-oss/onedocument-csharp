using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;

namespace RecursiveAI.OneDocument.Tests.Markup;

public class MarkupCompatibilityAndReaderTests
{
    private static MarkupNode AlternateContent(MarkupDocument d) =>
        d.Root.ChildElements.First().ChildElements.First(MarkupCompatibility.IsAlternateContent);

    [Fact]
    public void Ignorable_namespaces_come_from_prefixes_in_scope()
    {
        MarkupDocument d = MarkupDocument.Parse(MarkupDocumentTests.U8(MarkupDocumentTests.Sample));
        Assert.Equal(["http://schemas.microsoft.com/office/word/2010/wordml", "urn:x"], MarkupCompatibility.GetIgnorableNamespaces(d.Root.ChildElements.First()));
        Assert.True(MarkupCompatibility.IsNamespaceIgnorable(d.Root.FirstChild, "urn:x"));
        Assert.False(MarkupCompatibility.IsNamespaceIgnorable(d.Root, "http://schemas.microsoft.com/office/word/2010/wordprocessingShape"));
        MarkupNode custom = d.Root.ChildElements.First().ChildElements.First(e => e.LocalNameString == "custom");
        Assert.True(MarkupCompatibility.IsIgnored(custom, ns => ns == MarkupDocumentTests.Word));
        Assert.False(MarkupCompatibility.IsIgnored(custom, ns => ns == "urn:x"));
        Assert.False(MarkupCompatibility.IsIgnored(d.Root, _ => false));
    }

    [Fact]
    public void Alternate_content_picks_a_choice_or_the_fallback_and_keeps_all_branches()
    {
        MarkupDocument d = MarkupDocument.Parse(MarkupDocumentTests.U8(MarkupDocumentTests.Sample));
        MarkupNode ac = AlternateContent(d);
        MarkupNode choice = ac.ChildElements.First();
        Assert.Equal(["http://schemas.microsoft.com/office/word/2010/wordprocessingShape"], MarkupCompatibility.GetRequiredNamespaces(choice));
        MarkupNode picked = MarkupCompatibility.SelectAlternateContent(ac, ns => ns.EndsWith("wordprocessingShape", StringComparison.Ordinal));
        Assert.True(MarkupCompatibility.IsChoice(picked));
        Assert.Equal("new", picked.InnerText);
        MarkupNode fallback = MarkupCompatibility.SelectAlternateContent(ac, _ => false);
        Assert.True(MarkupCompatibility.IsFallback(fallback));
        Assert.Equal("old", fallback.InnerText);
        Assert.Equal(2, ac.ChildElements.Count());
    }

    [Fact]
    public void Process_content_and_must_understand_are_read()
    {
        string xml = "<a xmlns:mc='" + MarkupDocumentTests.Mc + "' xmlns:i='urn:i' xmlns:n='urn:n' mc:Ignorable='i' mc:ProcessContent='i:keep i:w:*' mc:MustUnderstand='n'><i:keep><b/></i:keep><i:drop/></a>";
        MarkupDocument d = MarkupDocument.Parse(MarkupDocumentTests.U8(xml));
        MarkupNode keep = d.Root.ChildElements.First();
        MarkupNode drop = d.Root.ChildElements.Last();
        Assert.True(MarkupCompatibility.IsIgnored(keep, _ => false));
        Assert.True(MarkupCompatibility.ProcessesContent(keep));
        Assert.False(MarkupCompatibility.ProcessesContent(drop));
        Assert.Equal(["urn:n"], MarkupCompatibility.GetMustUnderstandNamespaces(keep));
    }

    private static List<string> ReaderTokens(MarkupReader r)
    {
        var list = new List<string>();
        while (r.Read())
        {
            list.Add(r.Kind + ":" + Encoding.UTF8.GetString(r.Raw) + "|" + Encoding.UTF8.GetString(r.NamespaceUri) + "|" + r.TokenOffset + "|" + r.Depth);
        }

        return list;
    }

    [Fact]
    public void Reader_over_memory_and_over_tiny_buffers_agree_and_match_the_tokenizer()
    {
        byte[] data = MarkupDocumentTests.U8(MarkupDocumentTests.Sample);
        List<string> expected = ReaderTokens(new MarkupReader(data));
        Assert.True(expected.Count > 40);
        long total = 0;
        foreach (string t in expected)
        {
            total += Encoding.UTF8.GetByteCount(t.Split('|')[0][(t.IndexOf(':', StringComparison.Ordinal) + 1)..]);
        }

        Assert.Equal(data.Length - 3, total);
        foreach (int chunk in new[] { 1, 7, 100, 100_000 })
        {
            using var r = new MarkupReader(new OneByteStream(data, chunk), bufferSize: 1);
            Assert.Equal(expected, ReaderTokens(r));
        }
    }

    [Fact]
    public async Task Reader_reads_asynchronously_and_honours_cancellation()
    {
        byte[] data = MarkupDocumentTests.U8("<a><b>x</b></a>");
        using var r = new MarkupReader(new OneByteStream(data, 3));
        int n = 0;
        while (await r.ReadAsync(TestContext.Current.CancellationToken))
        {
            n++;
        }

        Assert.Equal(5, n);
        using var r2 = new MarkupReader(new OneByteStream(data, 3));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await r2.ReadAsync(cts.Token));
    }

    [Fact]
    public void Reader_memory_stays_bounded_by_the_largest_token()
    {
        var sb = new StringBuilder("<r>");
        for (int i = 0; i < 200_000; i++)
        {
            sb.Append("<c r=\"A").Append(i).Append("\"><v>").Append(i).Append("</v></c>");
        }

        sb.Append("</r>");
        byte[] data = MarkupDocumentTests.U8(sb.ToString());
        long Run()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            using var r = new MarkupReader(new OneByteStream(data, 4096));
            int tokens = 0;
            while (r.Read())
            {
                tokens++;
                foreach (XmlAttributeToken a in r.Attributes)
                {
                    _ = a.RawValue.Length;
                }
            }

            Assert.True(tokens > 800_000);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Run();
        Assert.True(Run() < 65_536, "the reader allocates per document, not per token");
    }

    [Fact]
    public void Reader_errors_are_located_and_utf16_streams_work()
    {
        using var r = new MarkupReader(new OneByteStream(MarkupDocumentTests.U8("<a><b></c></a>"), 2), partUri: "/x.xml");
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() =>
        {
            while (r.Read())
            {
            }
        });
        Assert.Equal("xml.mismatched-end-tag", ex.Defect.Code);
        Assert.Equal(6, ex.Location.Offset);
        byte[] u16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("<a>\u00e9</a>")];
        using var r16 = new MarkupReader(new OneByteStream(u16, 3));
        Assert.True(r16.Read());
        Assert.True(r16.Read());
        Assert.Equal("\u00e9", r16.GetText());
        using var deep = new MarkupReader(new OneByteStream(MarkupDocumentTests.U8("<a><a><a><a/></a></a></a>"), 5), new OpenContext(new OfficeLimits { MaxXmlDepth = 2 }));
        Assert.Throws<OfficeLimitException>(() =>
        {
            while (deep.Read())
            {
            }
        });
    }

    [Fact]
    public void Reader_skip_and_text()
    {
        using var r = new MarkupReader(MarkupDocumentTests.U8("<r><a><b/>x</a><c>t &amp; u</c></r>"));
        Assert.True(r.Read());
        Assert.True(r.Read());
        Assert.True(r.Skip());
        Assert.True(r.Read());
        Assert.Equal("c", Encoding.UTF8.GetString(r.RawName));
        Assert.True(r.Read());
        Assert.Equal("t & u", r.GetText());
    }

    /// <summary>A stream that returns at most <c>chunk</c> bytes per read.</summary>
    private sealed class OneByteStream(byte[] data, int chunk) : Stream
    {
        private int _pos;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => _pos; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(Math.Min(count, chunk), data.Length - _pos);
            Array.Copy(data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
