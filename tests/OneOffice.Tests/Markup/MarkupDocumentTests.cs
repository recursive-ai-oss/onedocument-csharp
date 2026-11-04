using System.Buffers;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Markup.Xml;

namespace RecursiveAI.OneOffice.Tests.Markup;

public class MarkupDocumentTests
{
    internal const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    internal const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    internal const string Sample =
        "\uFEFF<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
        "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:mc='http://schemas.openxmlformats.org/markup-compatibility/2006' " +
        "xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" mc:Ignorable=\"w14 x\" xmlns:x=\"urn:x\">\r\n" +
        "  <w:body>\r\n" +
        "    <w:p w14:paraId='0001' w:rsidR=\"00AB12\"><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t xml:space=\"preserve\">Hello &amp; welcome </w:t></w:r><w:r/></w:p>\r\n" +
        "    <mc:AlternateContent><mc:Choice Requires=\"wps\"><w:r><w:t>new</w:t></w:r></mc:Choice><mc:Fallback><w:r><w:t>old</w:t></w:r></mc:Fallback></mc:AlternateContent>\r\n" +
        "    <!-- a comment --><?proc keep me?>\r\n" +
        "    <x:custom a = 'v' ><![CDATA[raw <data>]]></x:custom>\r\n" +
        "    <w:sectPr><w:extLst><w:ext uri=\"{X}\"><w14:thing/></w:ext></w:extLst></w:sectPr>\r\n" +
        "  </w:body>\r\n" +
        "</w:document>\r\n";

    internal static byte[] U8(string s) => Encoding.UTF8.GetBytes(s);

    internal static string Text(MarkupDocument d) => Encoding.UTF8.GetString(d.ToArray());

    [Fact]
    public void Unedited_tree_writes_the_original_bytes()
    {
        byte[] data = U8(Sample);
        MarkupDocument d = MarkupDocument.Parse(data);
        Assert.False(d.IsModified);
        Assert.Equal(data, d.ToArray());
        Assert.True(d.HasByteOrderMark);
        // Navigation does not change it either.
        _ = d.Root.InnerText;
        _ = d.Root.OuterXml;
        Assert.Equal(data, d.ToArray());
    }

    [Fact]
    public void Tree_navigation_reads_names_namespaces_attributes_and_text()
    {
        MarkupDocument d = MarkupDocument.Parse(U8(Sample));
        MarkupNode root = d.Root;
        Assert.Equal("w:document", root.Name);
        Assert.Equal(Word, root.NamespaceUri);
        Assert.True(root.IsElement(Word, "document"));
        Assert.False(root.IsElement(Word, "body"));
        Assert.Equal("w14 x", root.GetAttribute(Mc, "Ignorable"));
        MarkupNode body = root.ChildElements.First();
        Assert.Equal("body", body.LocalNameString);
        MarkupNode p = body.ChildElements.First();
        Assert.Equal("0001", p.GetAttribute("http://schemas.microsoft.com/office/word/2010/wordml", "paraId"));
        Assert.Null(p.GetAttribute("paraId"));
        Assert.Equal("00AB12", p.GetAttribute(Word, "rsidR"));
        Assert.Equal("Hello & welcome ", p.InnerText);
        MarkupNode custom = body.ChildElements.First(e => e.LocalNameString == "custom");
        Assert.Equal("urn:x", custom.NamespaceUri);
        Assert.Equal("v", custom.GetAttribute("a"));
        Assert.Equal("raw <data>", custom.InnerText);
        Assert.Contains(body.Children, n => n.Kind == MarkupNodeKind.Comment && n.InnerText == " a comment ");
        Assert.Contains(body.Children, n => n.Kind == MarkupNodeKind.ProcessingInstruction && n.InnerText == "keep me");
        Assert.True(p.ChildElements.Last().IsSelfClosing);
        Assert.Equal(MarkupNodeKind.Document, root.Parent.Kind);
        Assert.True(root.Parent.Parent.IsNull);
        var names = new List<string>();
        foreach (MarkupAttributeRef a in root.Attributes)
        {
            names.Add(Encoding.UTF8.GetString(a.RawName));
        }

        Assert.Equal(["xmlns:w", "xmlns:mc", "xmlns:w14", "xmlns:wps", "mc:Ignorable", "xmlns:x"], names);
    }

    [Fact]
    public void Setting_an_attribute_rewrites_only_that_value()
    {
        string before = Sample;
        MarkupDocument d = MarkupDocument.Parse(U8(before));
        MarkupNode p = d.Root.ChildElements.First().ChildElements.First();
        p.SetAttribute(Word, "rsidR", "00FFFF & <");
        Assert.True(d.IsModified);
        string after = Text(d);
        string expected = before.Replace("w:rsidR=\"00AB12\"", "w:rsidR=\"00FFFF &amp; &lt;\"", StringComparison.Ordinal);
        Assert.Equal(expected, after);
        Assert.Equal("00FFFF & <", p.GetAttribute(Word, "rsidR"));
        MarkupDocument reparsed = MarkupDocument.Parse(d.ToArray());
        Assert.Equal(d.ToArray(), reparsed.ToArray());
    }

    [Fact]
    public void Single_quoted_value_keeps_its_quote_and_spacing_and_new_attributes_are_added_last()
    {
        MarkupDocument d = MarkupDocument.Parse(U8(Sample));
        MarkupNode custom = d.Root.ChildElements.First().ChildElements.First(e => e.LocalNameString == "custom");
        custom.SetAttribute("a", "it's");
        Assert.Contains("<x:custom a = 'it&apos;s' >", Text(d));
        custom.SetAttribute("b", "2");
        Assert.Contains("<x:custom a = 'it&apos;s' b=\"2\" >", Text(d));
        Assert.True(custom.RemoveAttribute("a"));
        Assert.Contains("<x:custom b=\"2\" >", Text(d));
        Assert.False(custom.RemoveAttribute("zzz"));
    }

    [Fact]
    public void Adding_a_namespaced_attribute_declares_a_prefix_only_when_needed()
    {
        MarkupDocument d = MarkupDocument.Parse(U8("<a xmlns:p='urn:p'><b/><c xmlns:q='urn:q'/></a>"));
        MarkupNode b = d.Root.ChildElements.First();
        b.SetAttribute("urn:p", "x", "1");
        b.SetAttribute("urn:new", "y", "2");
        Assert.Equal("<a xmlns:p='urn:p'><b p:x=\"1\" xmlns:ns1=\"urn:new\" ns1:y=\"2\"/><c xmlns:q='urn:q'/></a>", Text(d));
        Assert.Throws<ArgumentException>(() => b.SetAttribute("zz:k", "v"));
        Assert.Throws<ArgumentException>(() => b.SetAttribute("1bad", "v"));
        Assert.Throws<ArgumentException>(() => b.SetAttribute("k", "bad \u0001"));
    }

    [Fact]
    public void Insert_remove_replace_and_set_text_change_only_their_regions()
    {
        MarkupDocument d = MarkupDocument.Parse(U8("<r xmlns:w='urn:w'>\n  <w:a/>\n  <w:b>old</w:b>\n  <w:c><w:d/></w:c>\n</r>"));
        MarkupNode r = d.Root;
        MarkupNode a = r.ChildElements.First();
        MarkupNode b = r.ChildElements.ElementAt(1);
        MarkupNode c = r.ChildElements.ElementAt(2);
        a.AppendXml("<w:x>1</w:x><!--n-->"u8);
        Assert.Equal("<r xmlns:w='urn:w'>\n  <w:a><w:x>1</w:x><!--n--></w:a>\n  <w:b>old</w:b>\n  <w:c><w:d/></w:c>\n</r>", Text(d));
        b.SetText("new <&>");
        c.FirstChild.Remove();
        c.PrependXml("<w:e/>"u8);
        a.InsertXmlAfter("<w:f/>"u8);
        b.ReplaceWithXml("<w:g/>"u8);
        Assert.Equal("<r xmlns:w='urn:w'>\n  <w:a><w:x>1</w:x><!--n--></w:a><w:f/>\n  <w:g/>\n  <w:c><w:e/></w:c>\n</r>", Text(d));
        Assert.Equal(["a", "f", "g", "c"], r.ChildElements.Select(e => e.LocalNameString).ToArray());
        Assert.Equal(d.ToArray(), MarkupDocument.Parse(d.ToArray()).ToArray());
    }

    [Fact]
    public void Inserting_xml_checks_well_formedness_and_prefixes_and_rolls_back()
    {
        MarkupDocument d = MarkupDocument.Parse(U8("<r xmlns:w='urn:w'><a/></r>"));
        byte[] before = d.ToArray();
        MarkupNode a = d.Root.ChildElements.First();
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => a.AppendXml("<w:x></w:y>"u8));
        Assert.Equal("xml.mismatched-end-tag", ex.Defect.Code);
        Assert.Throws<ArgumentException>(() => a.AppendXml("<q:x/>"u8));
        Assert.Throws<ArgumentException>(() => d.DocumentNode.AppendXml("<second/>"u8));
        Assert.Throws<InvalidOperationException>(() => d.Root.Remove());
        Assert.Throws<InvalidOperationException>(() => d.Root.ReplaceWithXml("<z/>"u8));
        Assert.Equal(before, d.ToArray());
        a.AppendXml("<q:x xmlns:q='urn:q'/>"u8);
        Assert.Equal("<r xmlns:w='urn:w'><a><q:x xmlns:q='urn:q'/></a></r>", Text(d));
        d.DocumentNode.AppendXml("<!--tail-->"u8);
        Assert.EndsWith("</r><!--tail-->", Text(d), StringComparison.Ordinal);
    }

    [Fact]
    public void Edited_document_keeps_everything_it_did_not_touch()
    {
        MarkupDocument d = MarkupDocument.Parse(U8(Sample));
        MarkupNode root = d.Root;
        root.SetAttribute("xmlns:extra", "urn:extra");
        string after = Text(d);
        string expected = Sample.Replace("mc:Ignorable=\"w14 x\" xmlns:x=\"urn:x\">", "mc:Ignorable=\"w14 x\" xmlns:x=\"urn:x\" xmlns:extra=\"urn:extra\">", StringComparison.Ordinal);
        Assert.Equal(expected, after);
        foreach (string keep in new[] { "<mc:AlternateContent>", "<w:extLst>", "<![CDATA[raw <data>]]>", "<?proc keep me?>", "<!-- a comment -->", "<w:r/>", "w14:paraId='0001'", "\r\n  <w:body>" })
        {
            Assert.Contains(keep, after, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Edits_on_a_self_closing_element_add_an_end_tag()
    {
        MarkupDocument d = MarkupDocument.Parse(U8("<a><b x='1' /><c/></a>"));
        MarkupNode b = d.Root.ChildElements.First();
        b.SetText("t");
        Assert.Equal("<a><b x='1'>t</b><c/></a>", Text(d));
        MarkupNode c = d.Root.ChildElements.Last();
        c.SetText(string.Empty);
        c.SetAttribute("k", "v");
        Assert.Equal("<a><b x='1'>t</b><c k=\"v\"/></a>", Text(d));
    }

    [Fact]
    public void Utf16_documents_round_trip_and_edit_in_their_encoding()
    {
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n<a b=\"1\">h\u00e9llo</a>";
        foreach (bool be in new[] { false, true })
        {
            var enc = new UnicodeEncoding(be, true);
            byte[] data = [.. enc.GetPreamble(), .. enc.GetBytes(xml)];
            MarkupDocument d = MarkupDocument.Parse(data);
            Assert.Equal(data, d.ToArray());
            Assert.Equal(be ? XmlEncodingKind.Utf16BigEndian : XmlEncodingKind.Utf16LittleEndian, d.SourceEncoding);
            Assert.Equal("h\u00e9llo", d.Root.InnerText);
            d.Root.SetAttribute("b", "2");
            byte[] edited = d.ToArray();
            Assert.Equal([.. enc.GetPreamble(), .. enc.GetBytes(xml.Replace("\"1\"", "\"2\"", StringComparison.Ordinal))], edited);
        }

        // A UTF-16 error is located in the original bytes.
        var bad = Encoding.Unicode.GetBytes("\uFEFF<a>\u0001</a>");
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => MarkupDocument.Parse(bad, partUri: "/p.xml"));
        Assert.Equal("xml.invalid-character", ex.Defect.Code);
        Assert.Equal(2 + (3 * 2), ex.Location.Offset);
        Assert.Equal("/p.xml", ex.Location.Container);
    }

    [Theory]
    [InlineData("<a>", "xml.unexpected-end")]
    [InlineData("<!DOCTYPE a><a/>", "xml.dtd-refused")]
    [InlineData("<a>\u0001</a>", "xml.invalid-character")]
    [InlineData("\u00ff\u00fe", "xml.invalid-encoding")]
    [InlineData("<1/>", "xml.invalid-name")]
    [InlineData("<a b/>", "xml.syntax")]
    [InlineData("<a></b>", "xml.mismatched-end-tag")]
    [InlineData("<a b='1' b='2'/>", "xml.duplicate-attribute")]
    [InlineData("<p:a/>", "xml.unbound-prefix")]
    [InlineData("<a xmlns:xmlns='u'/>", "xml.invalid-namespace-declaration")]
    [InlineData("<a>&x;</a>", "xml.invalid-reference")]
    [InlineData("<a/><b/>", "xml.misplaced-content")]
    [InlineData("", "xml.no-root")]
    [InlineData("<a/><?xml version='1.0'?>", "xml.invalid-declaration")]
    public void Parse_errors_are_typed_with_a_part_location(string xml, string code)
    {
        byte[] data = xml == "\u00ff\u00fe" ? [0xFF, 0xFE, 0, 0, 0x3C, 0, 0, 0] : U8(xml);
        if (code == "xml.invalid-encoding")
        {
            // UTF-32 is not accepted at all.
            code = "xml.unsupported-encoding";
        }

        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => MarkupDocument.Parse(data, partUri: "/word/document.xml"));
        Assert.Equal(code, ex.Defect.Code);
        Assert.Equal(DefectKind.Markup, ex.Defect.Kind);
        Assert.Equal(DefectLocationKind.Part, ex.Location.Kind);
        Assert.Equal("/word/document.xml", ex.Location.Container);
        Assert.False(MarkupDocument.TryParse(data, out MarkupDocument? doc));
        Assert.Null(doc);
    }

    [Fact]
    public void Invalid_utf8_is_located()
    {
        byte[] data = [.. U8("<a>ok"), 0xC3, 0x28, .. U8("</a>")];
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => MarkupDocument.Parse(data, partUri: "/p.xml"));
        Assert.Equal("xml.invalid-encoding", ex.Defect.Code);
        Assert.Equal(5, ex.Location.Offset);
    }

    [Fact]
    public void Shared_limits_apply_and_name_the_property()
    {
        var ctx = new OpenContext(new OfficeLimits { MaxXmlDepth = 3, MaxXmlAttributeCount = 2, MaxXmlNameLength = 8, MaxPartSize = 64 });
        OfficeLimitException depth = Assert.Throws<OfficeLimitException>(() => MarkupDocument.Parse(U8("<a><b><c><d/></c></b></a>"), ctx));
        Assert.Equal("MaxXmlDepth", depth.LimitName);
        Assert.Equal("limit.max-xml-depth", depth.Defect.Code);
        Assert.Equal("MaxXmlAttributeCount", Assert.Throws<OfficeLimitException>(() => MarkupDocument.Parse(U8("<a x='1' y='2' z='3'/>"), ctx)).LimitName);
        Assert.Equal("MaxXmlNameLength", Assert.Throws<OfficeLimitException>(() => MarkupDocument.Parse(U8("<abcdefghijkl/>"), ctx)).LimitName);
        Assert.Equal("MaxPartSize", Assert.Throws<OfficeLimitException>(() => MarkupDocument.Parse(new byte[100], ctx)).LimitName);
        // Insertion is bounded by the same depth limit.
        MarkupDocument d = MarkupDocument.Parse(U8("<a><b/></a>"), ctx);
        Assert.Equal("MaxXmlDepth", Assert.Throws<OfficeLimitException>(() => d.Root.FirstChild.AppendXml("<c><d/></c>"u8)).LimitName);
    }

    [Fact]
    public void Nodes_are_cheap_and_a_large_document_parses_to_the_same_bytes()
    {
        var sb = new StringBuilder("<?xml version=\"1.0\"?>\n<w:document xmlns:w=\"urn:w\"><w:body>");
        for (int i = 0; i < 50_000; i++)
        {
            sb.Append("<w:p w:id=\"").Append(i).Append("\"><w:r><w:t>text ").Append(i).Append("</w:t></w:r></w:p>\n");
        }

        sb.Append("</w:body></w:document>");
        byte[] data = U8(sb.ToString());
        MarkupDocument d = MarkupDocument.Parse(data);
        Assert.Equal(data, d.ToArray());
        MarkupNode body = d.Root.FirstChild;
        Assert.Equal(100_000, body.Children.Count());
        d.Root.SetAttribute("xmlns:z", "urn:z");
        body.ChildElements.ElementAt(25_000).SetAttribute("w:id", "X");
        string text = Text(d);
        Assert.Equal(sb.ToString().Replace("xmlns:w=\"urn:w\">", "xmlns:w=\"urn:w\" xmlns:z=\"urn:z\">", StringComparison.Ordinal).Replace("w:id=\"25000\"", "w:id=\"X\"", StringComparison.Ordinal), text);
    }
}
