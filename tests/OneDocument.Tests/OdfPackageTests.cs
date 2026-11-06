using System.Text;
using RecursiveAI.OneArchive;
using RecursiveAI.OneArchive.Zip;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;

namespace RecursiveAI.OneDocument.Tests;

public class OdfPackageTests
{
    private const string Office = OdfNamespaces.Office;
    private const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";

    private static string Root(string name, string children = "") =>
        $"{Decl}<office:{name} xmlns:office=\"{Office}\" xmlns:style=\"{OdfNamespaces.Style}\" xmlns:text=\"{OdfNamespaces.Text}\" xmlns:fo=\"{OdfNamespaces.Fo}\" xmlns:draw=\"{OdfNamespaces.Draw}\" xmlns:xlink=\"{OdfNamespaces.XLink}\" xmlns:dc=\"{OdfNamespaces.DublinCore}\" xmlns:meta=\"{OdfNamespaces.Meta}\" office:version=\"1.3\">{children}</office:{name}>";

    private static string Manifest(params string[] files) =>
        $"{Decl}<manifest:manifest xmlns:manifest=\"{OdfNamespaces.Manifest}\" manifest:version=\"1.3\"><manifest:file-entry manifest:full-path=\"/\" manifest:version=\"1.3\" manifest:media-type=\"application/vnd.oasis.opendocument.text\"/>" +
        string.Concat(files.Select(f => $"<manifest:file-entry manifest:full-path=\"{f}\" manifest:media-type=\"text/xml\"/>")) + "</manifest:manifest>";

    // Builds a zip: the entries are written in the order given, deflated unless the name is "mimetype" and stored is true.
    private static byte[] Zip(IEnumerable<(string Name, string Content)> entries, bool storeMimetype = true)
    {
        using var ms = new MemoryStream();
        using (var zw = new ZipWriter(ms, new ZipWriterOptions { Method = ZipMethod.Deflate }, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                if (name == "mimetype" && storeMimetype)
                {
                    var header = new ZipRawEntry { RawName = Encoding.ASCII.GetBytes(name), Method = ZipMethod.Store, Crc32 = Crc(bytes), CompressedSize = bytes.Length, UncompressedSize = bytes.Length };
                    zw.WriteRawEntry(header, new MemoryStream(bytes));
                }
                else
                {
                    zw.WriteEntry(new ZipEntry { Name = name, Size = bytes.Length }, bytes);
                }
            }

            zw.Finish();
        }

        return ms.ToArray();
    }

    private static uint Crc(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }

    private static (string, string)[] Minimal(string body = "<office:body><office:text><text:p>Hi</text:p></office:text></office:body>") =>
    [
        ("mimetype", OdfMediaTypes.Text),
        ("content.xml", Root("document-content", body)),
        ("styles.xml", Root("document-styles")),
        ("meta.xml", Root("document-meta", "<office:meta><dc:title>T</dc:title></office:meta>")),
        ("META-INF/manifest.xml", Manifest("content.xml", "styles.xml", "meta.xml")),
    ];

    private static OdfPackage Open(byte[] bytes, DefectCollection? defects = null) =>
        OdfPackage.Open(ByteSource.FromMemory(bytes), new OpenContext(defects: defects));

    private static byte[] Write(OdfPackage p)
    {
        using var ms = new MemoryStream();
        p.WriteTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void CreatedPackageHasAStoredMimetypeFirstAndAManifest()
    {
        using OdfPackage created = OdfPackage.Create(OdfMediaTypes.Text);
        byte[] bytes = Write(created);
        Assert.Equal("mimetype", Encoding.ASCII.GetString(bytes, 30, 8));
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 8));
        Assert.Equal(OdfMediaTypes.Text, Encoding.ASCII.GetString(bytes, 38, OdfMediaTypes.Text.Length));
        using OdfPackage back = Open(bytes);
        Assert.Equal(OdfMediaTypes.Text, back.MimeType);
        Assert.Equal("1.3", back.Version);
        Assert.True(back.Contains("META-INF/manifest.xml"));
        Assert.True(back.Manifest.TryGet("content.xml", out OdfManifestEntry e) && e.MediaType == "text/xml");
        Assert.False(OdfMarkup.Child(back.Body, Office, "text").IsNull);
    }

    [Fact]
    public void UntouchedPackageWritesTheSameBytesAgain()
    {
        byte[] source = Zip(Minimal());
        using OdfPackage p = Open(source);
        byte[] once = Write(p);
        using OdfPackage q = Open(once);
        Assert.Equal(once, Write(q));
        Assert.Equal(p.Entries.Select(e => e.Name).Order(), q.Entries.Select(e => e.Name).Order());
        foreach (OdfEntry e in p.Entries)
        {
            Assert.Equal(p.ReadEntry(e.Name).ToArray(), q.ReadEntry(e.Name).ToArray());
        }
    }

    [Fact]
    public void EditingOnePartChangesOnlyThatPartAndKeepsTheRestOfItsMarkup()
    {
        string content = Root("document-content", "<office:body><office:text><text:p text:style-name=\"P1\">Hi</text:p><!--keep--><foo:bar xmlns:foo=\"urn:x\" foo:a=\"1\"/></office:text></office:body>");
        var entries = Minimal().ToList();
        entries[1] = ("content.xml", content);
        byte[] source = Zip(entries);
        using OdfPackage p = Open(source);
        MarkupNode text = OdfMarkup.Child(p.Body, Office, "text");
        text.FirstChild.SetAttribute("text:style-name", "P9");
        byte[] written = Write(p);
        using OdfPackage back = Open(written);
        string xml = back.ContentRoot.OuterXml;
        Assert.Contains("text:style-name=\"P9\"", xml, StringComparison.Ordinal);
        Assert.Contains("<!--keep-->", xml, StringComparison.Ordinal);
        Assert.Contains("<foo:bar xmlns:foo=\"urn:x\" foo:a=\"1\"/>", xml, StringComparison.Ordinal);
        Assert.Equal(p.ReadEntry("styles.xml").ToArray(), back.ReadEntry("styles.xml").ToArray());
        Assert.Equal(p.ReadEntry("META-INF/manifest.xml").ToArray(), back.ReadEntry("META-INF/manifest.xml").ToArray());
    }

    [Fact]
    public void AddingAnEntryAddsItToTheManifest()
    {
        using OdfPackage p = Open(Zip(Minimal()));
        p.SetEntry("Pictures/a.png", new byte[] { 1, 2, 3 }, "image/png");
        using OdfPackage back = Open(Write(p));
        Assert.True(back.Manifest.TryGet("Pictures/a.png", out OdfManifestEntry e) && e.MediaType == "image/png");
        Assert.Equal(new byte[] { 1, 2, 3 }, back.ReadEntry("Pictures/a.png").ToArray());
        Assert.True(back.RemoveEntry("Pictures/a.png"));
        using OdfPackage again = Open(Write(back));
        Assert.False(again.Contains("Pictures/a.png"));
        Assert.False(again.Manifest.TryGet("Pictures/a.png", out _));
    }

    [Fact]
    public void ReadsTheMetadataAndTheStyleFamilies()
    {
        string styles = Root("document-styles",
            "<office:styles><style:default-style style:family=\"paragraph\"><style:text-properties fo:font-size=\"11pt\"/></style:default-style>" +
            "<style:style style:name=\"A\" style:family=\"paragraph\"><style:text-properties fo:font-weight=\"bold\"/></style:style>" +
            "<style:style style:name=\"B\" style:family=\"paragraph\" style:parent-style-name=\"A\"><style:text-properties fo:font-style=\"italic\"/></style:style>" +
            "<style:style style:name=\"B\" style:family=\"text\"/></office:styles>");
        var entries = Minimal().ToList();
        entries[2] = ("styles.xml", styles);
        using OdfPackage p = Open(Zip(entries));
        Assert.Equal("T", p.Metadata.Title);
        OdfStyle b = p.Styles.Find("paragraph", "B")!;
        Assert.Equal("text", p.Styles.Find("text", "B")!.Family);
        Assert.Equal(new[] { "B", "A", string.Empty }, p.Styles.Chain(b).Select(s => s.Name).ToArray());
        Assert.Equal("bold", p.Styles.Resolve(b, "text-properties", OdfNamespaces.Fo, "font-weight"));
        Assert.Equal("italic", p.Styles.Resolve(b, "text-properties", OdfNamespaces.Fo, "font-style"));
        Assert.Equal("11pt", p.Styles.Resolve(b, "text-properties", OdfNamespaces.Fo, "font-size"));
        Assert.Null(p.Styles.Resolve(b, "text-properties", OdfNamespaces.Fo, "color"));
    }

    [Fact]
    public void AStyleParentLoopIsCutAndReported()
    {
        string styles = Root("document-styles",
            "<office:styles><style:style style:name=\"A\" style:family=\"paragraph\" style:parent-style-name=\"B\"/><style:style style:name=\"B\" style:family=\"paragraph\" style:parent-style-name=\"A\"/></office:styles>");
        var entries = Minimal().ToList();
        entries[2] = ("styles.xml", styles);
        var defects = new DefectCollection();
        using OdfPackage p = Open(Zip(entries), defects);
        Assert.Equal(2, p.Styles.Chain(p.Styles.Find("paragraph", "A")!).Count());
        Assert.Contains(defects, d => d.Code == "odf.style-cycle");
    }

    [Fact]
    public void RepairsAreReportedWithTheirCodes()
    {
        // mimetype last and deflated; no manifest.
        var defects = new DefectCollection();
        var entries = Minimal().Where(e => e.Item1 != "mimetype" && e.Item1 != "META-INF/manifest.xml").Append(("mimetype", OdfMediaTypes.Text)).ToList();
        using (OdfPackage p = Open(Zip(entries, storeMimetype: false), defects))
        {
            Assert.Equal(OdfMediaTypes.Text, p.MimeType);
        }

        Assert.Contains(defects, d => d.Code == "packaging.odf-mimetype-not-first" && d.Severity == DefectSeverity.Repaired);
        Assert.Contains(defects, d => d.Code == "packaging.odf-no-manifest");

        // No mimetype entry: the type comes from the manifest, or from the body.
        defects = new DefectCollection();
        using (OdfPackage p = Open(Zip(Minimal().Where(e => e.Item1 != "mimetype")), defects))
        {
            Assert.Equal(OdfMediaTypes.Text, p.MimeType);
        }

        Assert.Contains(defects, d => d.Code == "packaging.odf-no-mimetype");

        // A deflated mimetype in first place.
        defects = new DefectCollection();
        using (OdfPackage p = Open(Zip(Minimal(), storeMimetype: false), defects))
        {
            Assert.Equal(OdfMediaTypes.Text, p.MimeType);
        }

        Assert.Contains(defects, d => d.Code == "packaging.odf-mimetype-compressed");

        // A manifest that lists a missing file, and one that is not XML.
        defects = new DefectCollection();
        var listed = Minimal().ToList();
        listed[4] = ("META-INF/manifest.xml", Manifest("content.xml", "styles.xml", "meta.xml", "Pictures/gone.png"));
        using (OdfPackage p = Open(Zip(listed), defects))
        {
            Assert.True(p.Manifest.TryGet("Pictures/gone.png", out _));
        }

        Assert.Contains(defects, d => d.Code == "packaging.odf-manifest-missing-entry");
        defects = new DefectCollection();
        var broken = Minimal().ToList();
        broken[4] = ("META-INF/manifest.xml", "<manifest");
        using (OdfPackage p = Open(Zip(broken), defects))
        {
            Assert.Empty(p.Manifest.Entries);
        }

        Assert.Contains(defects, d => d.Code == "packaging.odf-manifest-unreadable");
        defects = new DefectCollection();
        var noPath = Minimal().ToList();
        noPath[4] = ("META-INF/manifest.xml", $"{Decl}<manifest:manifest xmlns:manifest=\"{OdfNamespaces.Manifest}\"><manifest:file-entry manifest:media-type=\"x\"/></manifest:manifest>");
        using (Open(Zip(noPath), defects))
        {
        }

        Assert.Contains(defects, d => d.Code == "packaging.odf-manifest-entry" && d.Severity == DefectSeverity.Dropped);
    }

    [Fact]
    public void ABadRootIsFatalAndTyped()
    {
        var entries = Minimal().ToList();
        entries[1] = ("content.xml", Root("document-styles"));
        using OdfPackage p = Open(Zip(entries));
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => p.ContentRoot);
        Assert.Equal("packaging.odf-wrong-root", ex.Defect.Code);
        Assert.Throws<OfficeFormatException>(() => Open(Encoding.UTF8.GetBytes("<html xmlns=\"http://www.w3.org/1999/xhtml\"/>")));
        OfficeFormatException notOdf = Assert.Throws<OfficeFormatException>(() => Open(Encoding.UTF8.GetBytes("<a/>")));
        Assert.Equal("packaging.not-odf", notOdf.Defect.Code);
    }

    [Fact]
    public void EncryptedPackagesAreRefused()
    {
        var entries = Minimal().ToList();
        entries[4] = ("META-INF/manifest.xml",
            $"{Decl}<manifest:manifest xmlns:manifest=\"{OdfNamespaces.Manifest}\"><manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"><manifest:encryption-data manifest:checksum-type=\"x\"/></manifest:file-entry></manifest:manifest>");
        Assert.Throws<OfficeUnsupportedException>(() => Open(Zip(entries)));
    }

    [Fact]
    public void HostileEntryNamesAreDroppedOrRefusedAndLimitsHold()
    {
        var defects = new DefectCollection();
        var entries = Minimal().ToList();
        entries.Add(("../evil.txt", "x"));
        entries.Add(("/abs.txt", "x"));
        try
        {
            using OdfPackage p = Open(Zip(entries), defects);
            Assert.False(p.Contains("../evil.txt"));
            Assert.False(p.Contains("/abs.txt"));
        }
        catch (OfficeException)
        {
            // The archive layer may refuse such names outright; either is a typed outcome.
        }

        var many = Minimal().ToList();
        for (int i = 0; i < 40; i++)
        {
            many.Add(("f" + i, "x"));
        }

        var limits = new OfficeLimits { MaxPartCount = 20 };
        Assert.Throws<OfficeLimitException>(() => OdfPackage.Open(ByteSource.FromMemory(Zip(many)), new OpenContext(limits)));
        Assert.Throws<OfficeLimitException>(() =>
        {
            using OdfPackage p = OdfPackage.Open(ByteSource.FromMemory(Zip(Minimal())), new OpenContext(new OfficeLimits { MaxPartSize = 40 }));
            _ = p.ContentRoot;
        });
    }

    [Fact]
    public void FlatDocumentsOpenAndZipPackagesWriteAsFlatWithTheirPicturesInline()
    {
        string content = Root("document-content", "<office:body><office:text><text:p>Pic <draw:frame><draw:image xlink:href=\"Pictures/a.png\" xlink:type=\"simple\"/></draw:frame></text:p></office:text></office:body>");
        var entries = Minimal().ToList();
        entries[1] = ("content.xml", content);
        entries.Add(("Pictures/a.png", "PNGDATA"));
        using OdfPackage p = Open(Zip(entries));
        using var flat = new MemoryStream();
        p.WriteFlatTo(flat);
        string text = Encoding.UTF8.GetString(flat.ToArray());
        Assert.Contains("<office:document ", text, StringComparison.Ordinal);
        Assert.Contains("office:mimetype=\"" + OdfMediaTypes.Text + "\"", text, StringComparison.Ordinal);
        Assert.Contains("<office:binary-data>" + Convert.ToBase64String(Encoding.UTF8.GetBytes("PNGDATA")) + "</office:binary-data>", text, StringComparison.Ordinal);
        Assert.Contains("<dc:title>T</dc:title>", text, StringComparison.Ordinal);

        using OdfPackage f = Open(flat.ToArray());
        Assert.True(f.IsFlat);
        Assert.Equal(OdfMediaTypes.Text, f.MimeType);
        Assert.Equal("T", f.Metadata.Title);
        Assert.Same(f.ContentDocument, f.StylesDocument);
        Assert.False(f.Body.IsNull);
        Assert.Throws<OfficeUnsupportedException>(() => Write(f));
        using var again = new MemoryStream();
        f.WriteFlatTo(again);
        Assert.Equal(flat.ToArray(), again.ToArray());
    }

    [Fact]
    public void ATruncatedZipIsRecoveredOrRefusedWithATypedError()
    {
        byte[] bytes = Zip(Minimal());
        for (int cut = 60; cut < bytes.Length; cut += 97)
        {
            try
            {
                using OdfPackage p = Open(bytes[..cut]);
                _ = p.MimeType;
            }
            catch (OfficeException)
            {
            }
        }
    }
}
