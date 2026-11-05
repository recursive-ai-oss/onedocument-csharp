using System.Text;
using RecursiveAI.OneOffice.Markup.Xml;

namespace RecursiveAI.OneOffice.Tests.Markup;

public class XmlTokenizerTests
{
    private static byte[] U8(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>Reads every token and checks that the raw tokens tile the input.</summary>
    internal static List<string> Tokens(byte[] data)
    {
        var list = new List<string>();
        var tk = new XmlTokenizer(data);
        int expectedStart = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
        while (tk.Read())
        {
            Assert.Equal(expectedStart, tk.Info.Start);
            expectedStart = tk.Info.End;
            string ns = tk.NamespaceUri.Length > 0 ? "{" + Encoding.UTF8.GetString(tk.NamespaceUri) + "}" : "";
            list.Add(tk.Kind switch
            {
                XmlTokenKind.StartElement => "<" + ns + Encoding.UTF8.GetString(tk.LocalName) + Attrs(tk) + ">",
                XmlTokenKind.EmptyElement => "<" + ns + Encoding.UTF8.GetString(tk.LocalName) + Attrs(tk) + "/>",
                XmlTokenKind.EndElement => "</" + ns + Encoding.UTF8.GetString(tk.LocalName) + ">",
                XmlTokenKind.Text => "T:" + Encoding.UTF8.GetString(tk.Content),
                XmlTokenKind.Whitespace => "W",
                XmlTokenKind.CData => "C:" + Encoding.UTF8.GetString(tk.Content),
                XmlTokenKind.Comment => "#:" + Encoding.UTF8.GetString(tk.Content),
                XmlTokenKind.ProcessingInstruction => "?" + Encoding.UTF8.GetString(tk.RawName) + ":" + Encoding.UTF8.GetString(tk.Content),
                _ => "D",
            });
        }

        Assert.True(tk.EndOfDocument);
        Assert.Equal(data.Length, expectedStart);
        return list;
    }

    private static string Attrs(XmlTokenizer tk)
    {
        var sb = new StringBuilder();
        foreach (XmlAttributeToken a in tk.Attributes)
        {
            string ns = a.NamespaceUri.Length > 0 ? "{" + Encoding.UTF8.GetString(a.NamespaceUri) + "}" : "";
            sb.Append(' ').Append(ns).Append(Encoding.UTF8.GetString(a.RawName)).Append('=').Append(Encoding.UTF8.GetString(a.RawValue));
        }

        return sb.ToString();
    }

    private static XmlSyntaxException Fails(string xml, XmlLimits limits = default)
    {
        byte[] data = U8(xml);
        return Assert.Throws<XmlSyntaxException>(() =>
        {
            var tk = new XmlTokenizer(data, limits);
            while (tk.Read())
            {
            }
        });
    }

    [Fact]
    public void Tokens_cover_every_byte_and_resolve_namespaces()
    {
        string xml = "\uFEFF<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<w:document xmlns:w='urn:w' xmlns='urn:d' w:a=\"1\" b=\"2\"><!-- c -->\r\n  <w:p/><x>t &amp; u<![CDATA[<>]]></x><?pi data?></w:document>\r\n";
        List<string> t = Tokens(U8(xml));
        Assert.Equal(
            [
                "D", "W", "<{urn:w}document xmlns:w=urn:w xmlns=urn:d {urn:w}w:a=1 b=2>", "#: c ", "W", "<{urn:w}p/>", "<{urn:d}x>", "T:t &amp; u", "C:<>", "</{urn:d}x>", "?pi:data", "</{urn:w}document>", "W",
            ],
            t);
    }

    [Fact]
    public void Prefixes_stay_as_written_and_default_namespace_can_be_undeclared()
    {
        List<string> t = Tokens(U8("<a xmlns='urn:1'><b xmlns=''><c/></b></a>"));
        Assert.Equal(["<{urn:1}a xmlns=urn:1>", "<b xmlns=>", "<c/>", "</b>", "</{urn:1}a>"], t);
        var tk = new XmlTokenizer(U8("<odd:Name xmlns:odd='urn:o'/>"));
        Assert.True(tk.Read());
        Assert.Equal("odd:Name", Encoding.UTF8.GetString(tk.RawName));
        Assert.Equal("odd", Encoding.UTF8.GetString(tk.Prefix));
        Assert.Equal("Name", Encoding.UTF8.GetString(tk.LocalName));
    }

    [Fact]
    public void Namespace_declared_after_the_attribute_that_uses_it_resolves()
    {
        List<string> t = Tokens(U8("<a p:x='1' xmlns:p='urn:p'/>"));
        Assert.Equal(["<a {urn:p}p:x=1 xmlns:p=urn:p/>"], t);
    }

    [Fact]
    public void Entity_and_character_references_decode_on_demand()
    {
        Assert.Equal("a<b>&\"'\u00e9\U0001F600", XmlText.GetString(U8("a&lt;b&gt;&amp;&quot;&apos;&#xE9;&#128512;")));
        Assert.Equal("l1\nl2\nl3", XmlText.GetString(U8("l1\r\nl2\rl3")));
        Assert.Equal("a b c d", XmlText.GetString(U8("a\tb\r\nc\nd"), isAttributeValue: true));
        Assert.Equal("a\nb", XmlText.GetString(U8("a&#10;b"), isAttributeValue: true));
        Assert.False(XmlText.NeedsDecoding(U8("plain")));
        Assert.True(XmlText.NeedsDecoding(U8("a&amp;b")));
    }

    [Theory]
    [InlineData("<a>", XmlErrorCode.UnexpectedEnd)]
    [InlineData("<a", XmlErrorCode.UnexpectedEnd)]
    [InlineData("<a b='1", XmlErrorCode.UnexpectedEnd)]
    [InlineData("<a><!-- x", XmlErrorCode.UnexpectedEnd)]
    [InlineData("<!DOCTYPE a><a/>", XmlErrorCode.DtdRefused)]
    [InlineData("<!DOCTYPE a [<!ENTITY x \"y\">]><a>&x;</a>", XmlErrorCode.DtdRefused)]
    [InlineData("<a>\u0001</a>", XmlErrorCode.InvalidCharacter)]
    [InlineData("<a b='<'/>", XmlErrorCode.InvalidCharacter)]
    [InlineData("<a>]]></a>", XmlErrorCode.InvalidCharacter)]
    [InlineData("<1a/>", XmlErrorCode.InvalidName)]
    [InlineData("<a:b:c xmlns:a='u'/>", XmlErrorCode.InvalidName)]
    [InlineData("<a b/>", XmlErrorCode.Syntax)]
    [InlineData("<a b=1/>", XmlErrorCode.Syntax)]
    [InlineData("<a b='1'c='2'/>", XmlErrorCode.Syntax)]
    [InlineData("<a><!-- -- --></a>", XmlErrorCode.Syntax)]
    [InlineData("<a></b>", XmlErrorCode.MismatchedEndTag)]
    [InlineData("</a>", XmlErrorCode.MismatchedEndTag)]
    [InlineData("<a b='1' b='2'/>", XmlErrorCode.DuplicateAttribute)]
    [InlineData("<a xmlns:p='u' xmlns:q='u' p:x='1' q:x='2'/>", XmlErrorCode.DuplicateAttribute)]
    [InlineData("<p:a/>", XmlErrorCode.UnboundPrefix)]
    [InlineData("<a p:x='1'/>", XmlErrorCode.UnboundPrefix)]
    [InlineData("<a xmlns:xmlns='u'/>", XmlErrorCode.InvalidNamespaceDeclaration)]
    [InlineData("<a xmlns:xml='urn:x'/>", XmlErrorCode.InvalidNamespaceDeclaration)]
    [InlineData("<a xmlns:p=''/>", XmlErrorCode.InvalidNamespaceDeclaration)]
    [InlineData("<a>&nbsp;</a>", XmlErrorCode.InvalidReference)]
    [InlineData("<a>&#0;</a>", XmlErrorCode.InvalidReference)]
    [InlineData("<a>&amp</a>", XmlErrorCode.InvalidReference)]
    [InlineData("<a/><b/>", XmlErrorCode.MisplacedContent)]
    [InlineData("text<a/>", XmlErrorCode.MisplacedContent)]
    [InlineData("<a/>text", XmlErrorCode.MisplacedContent)]
    [InlineData("", XmlErrorCode.NoRoot)]
    [InlineData("<!-- only -->", XmlErrorCode.NoRoot)]
    [InlineData(" <?xml version='1.0'?><a/>", XmlErrorCode.InvalidDeclaration)]
    [InlineData("<?xml version='2.0'?><a/>", XmlErrorCode.InvalidDeclaration)]
    [InlineData("<?xml encoding='UTF-8'?><a/>", XmlErrorCode.InvalidDeclaration)]
    [InlineData("<a/><?xml version='1.0'?>", XmlErrorCode.InvalidDeclaration)]
    [InlineData("<?XML x?><a/>", XmlErrorCode.InvalidName)]
    public void Malformed_input_is_a_typed_error(string xml, XmlErrorCode code)
    {
        XmlSyntaxException ex = Fails(xml);
        Assert.Equal(code, ex.Code);
        Assert.True(ex.Offset >= 0 && ex.Offset <= xml.Length + 4, $"offset {ex.Offset}");
    }

    [Fact]
    public void Offsets_point_at_the_problem()
    {
        Assert.Equal(7, Fails("<a><b/></c>").Offset);
        Assert.Equal(3, Fails("<a>\u0001</a>").Offset);
    }

    [Fact]
    public void Invalid_utf8_and_utf16_input_are_typed_errors()
    {
        var bad = new byte[] { (byte)'<', (byte)'a', (byte)'>', 0xC3, 0x28, (byte)'<', (byte)'/', (byte)'a', (byte)'>' };
        var ex = Assert.Throws<XmlSyntaxException>(() => { var tk = new XmlTokenizer(bad); while (tk.Read()) { } });
        Assert.Equal(XmlErrorCode.InvalidEncoding, ex.Code);
        Assert.Equal(3, ex.Offset);
        var u16 = Encoding.Unicode.GetBytes("\uFEFF<a/>");
        Assert.Equal(XmlErrorCode.UnsupportedEncoding, Assert.Throws<XmlSyntaxException>(() => new XmlTokenizer(u16).Read()).Code);
    }

    [Fact]
    public void Limits_are_enforced_with_the_limit_named()
    {
        XmlSyntaxException d = Fails("<a><a><a></a></a></a>", new XmlLimits { MaxDepth = 2 });
        Assert.Equal("MaxDepth", d.LimitName);
        Assert.Equal(XmlErrorCode.LimitExceeded, d.Code);
        XmlSyntaxException n = Fails("<abcdefgh/>", new XmlLimits { MaxNameLength = 4 });
        Assert.Equal("MaxNameLength", n.LimitName);
        XmlSyntaxException c = Fails("<a x='1' y='2' z='3'/>", new XmlLimits { MaxAttributeCount = 2 });
        Assert.Equal("MaxAttributeCount", c.LimitName);
    }

    [Fact]
    public void Hostile_nesting_and_entity_bombs_stop_early()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 100_000; i++)
        {
            sb.Append("<a>");
        }

        Assert.Equal("MaxDepth", Fails(sb.ToString()).LimitName);

        const string laughs = "<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY lol \"lol\"><!ENTITY lol2 \"&lol;&lol;&lol;&lol;\">]><lolz>&lol2;</lolz>";
        Assert.Equal(XmlErrorCode.DtdRefused, Fails(laughs).Code);
        Assert.Equal(XmlErrorCode.InvalidReference, Fails("<a>&lol;</a>").Code);
    }

    [Fact]
    public void Very_long_attribute_values_and_text_are_fine()
    {
        string big = new('x', 5_000_000);
        List<string> t = Tokens(U8($"<a v=\"{big}\">{big}</a>"));
        Assert.Equal(3, t.Count);
    }

    [Fact]
    public void Chunked_reading_gives_the_same_tokens_at_every_block_size()
    {
        string xml = "\uFEFF<?xml version=\"1.0\"?>\r\n<r xmlns:a='urn:a'><a:x a:y=\"v &amp; w\">text &lt;<![CDATA[cd]]><!--c--></a:x><?p d?><e/></r>\n";
        byte[] data = U8(xml);
        var whole = new List<string>();
        var all = new XmlTokenizer(data);
        while (all.Read())
        {
            whole.Add(all.Kind + ":" + Encoding.UTF8.GetString(all.Raw) + "|" + Encoding.UTF8.GetString(all.NamespaceUri));
        }

        foreach (int size in new[] { 1, 2, 3, 5, 7, 11, 64 })
        {
            var state = new XmlTokenizerState();
            var got = new List<string>();
            int head = 0;
            int end = 0;
            while (true)
            {
                end = Math.Min(data.Length, end + size);
                var tk = new XmlTokenizer(data.AsSpan(head, end - head), end == data.Length, state);
                while (tk.Read())
                {
                    got.Add(tk.Kind + ":" + Encoding.UTF8.GetString(tk.Raw) + "|" + Encoding.UTF8.GetString(tk.NamespaceUri));
                }

                head += tk.BytesConsumed;
                if (tk.EndOfDocument)
                {
                    break;
                }
            }

            Assert.Equal(whole, got);
        }
    }

    [Fact]
    public void Skip_moves_past_the_end_tag()
    {
        var tk = new XmlTokenizer(U8("<r><a><b/><b>x</b></a><c/></r>"));
        Assert.True(tk.Read());
        Assert.True(tk.Read());
        Assert.Equal("a", Encoding.UTF8.GetString(tk.RawName));
        Assert.True(tk.Skip());
        Assert.True(tk.Read());
        Assert.Equal("c", Encoding.UTF8.GetString(tk.RawName));
    }

    [Fact]
    public void Tokenizing_allocates_nothing_per_token()
    {
        static byte[] Doc(int paragraphs)
        {
            var sb = new StringBuilder("<root xmlns:w='urn:w' xmlns='urn:d'>");
            for (int i = 0; i < paragraphs; i++)
            {
                sb.Append("<w:p w:rsid='00A1' w:x=\"y\"><w:r><w:t>text &amp; more</w:t></w:r></w:p>\r\n");
            }

            sb.Append("</root>");
            return U8(sb.ToString());
        }

        static long Run(byte[] data)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var tk = new XmlTokenizer(data);
            while (tk.Read())
            {
                _ = tk.NamespaceUri.Length;
                foreach (XmlAttributeToken a in tk.Attributes)
                {
                    _ = a.NamespaceUri.Length + a.RawValue.Length;
                }
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        byte[] small = Doc(1_000);
        byte[] large = Doc(40_000);
        Run(small);
        long a = Run(small);
        long b = Run(large);
        // The fixed cost is the state's few arrays (just over 8 KiB once other tests have warmed shared pools); the gate is that
        // ten times the tokens cost nothing more.
        Assert.True(a < 16384, $"allocated {a} bytes");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Writer_output_is_read_back_and_prefixes_and_order_are_kept()
    {
        var buf = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new XmlWriter(buf);
        w.WriteXmlDeclaration();
        w.WriteStartElement("w:document");
        w.WriteNamespaceDeclaration("w", "urn:w");
        w.WriteAttribute("z", "1 < 2 & \"3\"\t\n");
        w.WriteAttribute("a", "x");
        w.WriteStartElement("w:p");
        w.WriteEndElement();
        w.WriteText("a<b&c>\r");
        w.WriteCData("x]]>y");
        w.WriteComment("note");
        w.WriteProcessingInstruction("pi", "data");
        w.WriteRaw("<raw/>"u8);
        w.WriteEndElement();
        w.Complete();
        string text = Encoding.UTF8.GetString(buf.WrittenSpan);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<w:document xmlns:w=\"urn:w\" z=\"1 &lt; 2 &amp; &quot;3&quot;&#9;&#10;\" a=\"x\"><w:p/>a&lt;b&amp;c&gt;&#13;<![CDATA[x]]]]><![CDATA[>y]]><!--note--><?pi data?><raw/></w:document>", text);
        List<string> t = Tokens(buf.WrittenSpan.ToArray());
        Assert.Contains("T:a&lt;b&amp;c&gt;&#13;", t);
        Assert.Throws<ArgumentException>(() => new XmlWriter(new System.Buffers.ArrayBufferWriter<byte>()).WriteStartElement("1bad"));
        Assert.Throws<ArgumentException>(() =>
        {
            var x = new XmlWriter(new System.Buffers.ArrayBufferWriter<byte>());
            x.WriteStartElement("a");
            x.WriteAttribute("b", "1");
            x.WriteAttribute("b", "2");
        });
        Assert.Throws<ArgumentException>(() =>
        {
            var x = new XmlWriter(new System.Buffers.ArrayBufferWriter<byte>());
            x.WriteStartElement("a");
            x.WriteText("bad \u0001");
        });
    }

    [Fact]
    public void Utf16_is_detected_and_transcoded_both_ways()
    {
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?><a>h\u00e9llo \U0001F600</a>";
        foreach (bool be in new[] { false, true })
        {
            foreach (bool bom in new[] { true, false })
            {
                var enc = new UnicodeEncoding(be, bom);
                byte[] data = [.. enc.GetPreamble(), .. enc.GetBytes(xml)];
                Assert.True(XmlEncoding.TryDetect(data, out XmlEncodingKind kind, out int bomLength));
                Assert.Equal(be ? XmlEncodingKind.Utf16BigEndian : XmlEncodingKind.Utf16LittleEndian, kind);
                Assert.Equal(bom ? 2 : 0, bomLength);
                byte[] utf8 = XmlEncoding.Utf16ToUtf8(data.AsSpan(bomLength), be);
                Assert.Equal(xml, Encoding.UTF8.GetString(utf8));
                var back = new System.Buffers.ArrayBufferWriter<byte>();
                XmlEncoding.Utf8ToUtf16(utf8, be, bom, back);
                Assert.Equal(data, back.WrittenSpan.ToArray());
            }
        }

        Assert.False(XmlEncoding.TryDetect([0xFF, 0xFE, 0, 0, 0x3C, 0, 0, 0], out _, out _));
    }

    [Fact]
    public void Tokenizer_namespace_depends_on_nothing_else_in_the_library()
    {
        string dir = Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "..", "..", "src", "OneOffice", "Markup", "Xml");
        Assert.True(Directory.Exists(dir), dir);
        foreach (string file in Directory.GetFiles(dir, "*.cs"))
        {
            string text = File.ReadAllText(file);
            foreach (string line in text.Split('\n'))
            {
                string t = line.Trim();
                if (t.StartsWith("using ", StringComparison.Ordinal) && !t.StartsWith("using (", StringComparison.Ordinal))
                {
                    Assert.False(t.Contains("RecursiveAI", StringComparison.Ordinal) && !t.Contains("RecursiveAI.OneOffice.Markup.Xml", StringComparison.Ordinal), $"{file}: {t}");
                }
            }

            Assert.DoesNotContain("RecursiveAI.OneOffice.Diagnostics", text, StringComparison.Ordinal);
            Assert.DoesNotContain("OfficeLimits", text.Replace("OfficeLimits", "OfficeLimits"), StringComparison.Ordinal);
            Assert.DoesNotContain("OfficeException", text, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Xml", text, StringComparison.Ordinal);
        }
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
