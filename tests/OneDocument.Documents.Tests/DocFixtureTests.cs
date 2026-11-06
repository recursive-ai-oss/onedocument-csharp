using System.Runtime.CompilerServices;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>
/// Word 97-2003 files written by a word processor (LibreOffice, pinned in testdata/doc/oracle-version.txt) from the DOCX inputs of
/// tools/DocFixtures, read through the reader. They are real files the reader did not write for itself.
/// </summary>
public sealed class DocFixtureTests
{
    static DocFixtureTests() => DocumentFormats.Register(FormatRegistry.Default);

    private static string Path([CallerFilePath] string here = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "../../testdata/doc"));

    private static Document Open(string name, DefectCollection? defects = null) =>
        Document.Open(System.IO.Path.Combine(Path(), name + ".doc"), new OpenContext(defects: defects));

    private static string Xml(Document d) => d.MainMarkup.Root.OuterXml;

    private static string Part(Document d, string name) => Encoding.UTF8.GetString(d.Package.FindPart(name)!.GetBytes().Span);

    [Fact]
    public void Basic_text_and_character_formatting()
    {
        using Document d = Open("basic");
        Assert.Equal(OfficeFormat.Doc, d.SourceFormat);
        Assert.Equal(
            "Quarterly report\nPlain, bold, italic, underlined and large red.\nCentered paragraph\nIndented with spacing\nItem\t42\nVisit the site.\nBefore the break\nAfter the break\n",
            Office.ExtractText(System.IO.Path.Combine(Path(), "basic.doc")));
        string xml = Xml(d);
        Assert.Contains("<w:b/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:i/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:u w:val=\"single\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:color w:val=\"FF0000\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:sz w:val=\"28\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:jc w:val=\"center\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("w:val=\"right\" w:pos=\"7200\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:pageBreakBefore/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:ind w:left=\"720\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:lineRule=\"auto\"", xml, StringComparison.Ordinal);
        // Heading 1 is a style with its own properties.
        Assert.Contains("w:styleId=\"Heading1\"", Part(d, "/word/styles.xml"), StringComparison.Ordinal);
        Assert.Contains("<w:pStyle w:val=\"Heading1\"/>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Table_with_header_row_merges_and_shading()
    {
        using Document d = Open("table");
        string xml = Xml(d);
        Assert.Equal("Before\nName\tQty\tPrice\nApples\t3\t1.50\nMerged across two\t9.99\nTall\tx\ty\n\tz\tw\nAfter\n", d.ExtractText());
        Assert.Equal(5, Count(xml, "<w:tr>"));
        Assert.Contains("<w:tblHeader/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:gridSpan w:val=\"2\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:vMerge w:val=\"restart\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:vMerge/>", xml, StringComparison.Ordinal);
        Assert.Contains("w:fill=\"DDDDDD\"", xml, StringComparison.Ordinal);
        Assert.Equal(3, Count(xml, "<w:gridCol "));
    }

    [Fact]
    public void Numbered_and_bulleted_lists()
    {
        using Document d = Open("lists");
        string numbering = Part(d, "/word/numbering.xml");
        Assert.Contains("w:numFmt w:val=\"decimal\"", numbering, StringComparison.Ordinal);
        Assert.Contains("w:numFmt w:val=\"lowerLetter\"", numbering, StringComparison.Ordinal);
        Assert.Contains("w:numFmt w:val=\"bullet\"", numbering, StringComparison.Ordinal);
        Assert.Contains("w:lvlText w:val=\"%1.\"", numbering, StringComparison.Ordinal);
        string xml = Xml(d);
        Assert.Equal(5, Count(xml, "<w:numPr>"));
        Assert.Contains("<w:ilvl w:val=\"1\"/>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Footnote_endnote_and_comment()
    {
        using Document d = Open("notes");
        Assert.Contains("<w:footnoteReference w:id=\"1\"/>", Xml(d), StringComparison.Ordinal);
        Assert.Contains("<w:endnoteReference w:id=\"1\"/>", Xml(d), StringComparison.Ordinal);
        Assert.Contains("<w:commentReference w:id=\"0\"/>", Xml(d), StringComparison.Ordinal);
        Assert.Contains("The footnote text.", Part(d, "/word/footnotes.xml"), StringComparison.Ordinal);
        Assert.Contains("<w:footnoteRef/>", Part(d, "/word/footnotes.xml"), StringComparison.Ordinal);
        Assert.Contains("The endnote text.", Part(d, "/word/endnotes.xml"), StringComparison.Ordinal);
        string comments = Part(d, "/word/comments.xml");
        Assert.Contains("Check this sentence.", comments, StringComparison.Ordinal);
        Assert.Contains("w:author=\"Reviewer\"", comments, StringComparison.Ordinal);
        Assert.Contains("w:initials=\"RV\"", comments, StringComparison.Ordinal);
        // The text extractor and the model agree on every part's text.
        string text = Office.ExtractText(System.IO.Path.Combine(Path(), "notes.doc"));
        Assert.Equal(1, Count(text, "The footnote text."));
        Assert.Equal(1, Count(text, "The endnote text."));
    }

    [Fact]
    public void Two_sections_with_header_footer_orientation_and_columns()
    {
        using Document d = Open("sections");
        string xml = Xml(d);
        Assert.Equal(2, Count(xml, "<w:sectPr>"));
        Assert.Contains("w:orient=\"landscape\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:cols w:num=\"2\"", xml, StringComparison.Ordinal);
        Assert.Contains("Running header", Part(d, "/word/header1.xml"), StringComparison.Ordinal);
        string footer = Part(d, "/word/footer1.xml");
        Assert.Contains("PAGE", footer, StringComparison.Ordinal);
        Assert.Contains("w:fldCharType=\"separate\"", footer, StringComparison.Ordinal);
        // Only the odd-page header and footer of each section are written: no odd/even setting, no title page.
        Assert.Null(d.Package.FindPart("/word/header3.xml"));
        string text = Office.ExtractText(System.IO.Path.Combine(Path(), "sections.doc"));
        Assert.Equal(2, Count(text, "Running header"));
    }

    [Fact]
    public void Tracked_insertion_and_deletion_keep_author_and_date()
    {
        using Document d = Open("revisions");
        string xml = Xml(d);
        Assert.Contains("<w:ins ", xml, StringComparison.Ordinal);
        Assert.Contains("w:author=\"Ann\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:date=\"2020-03-04T10:20:00Z\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:del ", xml, StringComparison.Ordinal);
        Assert.Contains("w:author=\"Bob\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:delText", xml, StringComparison.Ordinal);
        Assert.Equal("Kept inserted and the end.\n", Office.ExtractText(System.IO.Path.Combine(Path(), "revisions.doc")));
    }

    [Fact]
    public void Inline_picture_is_kept_as_media()
    {
        using Document d = Open("picture");
        Assert.Contains("<wp:inline", Xml(d), StringComparison.Ordinal);
        IPackagePartAlias png = new(d);
        Assert.True(png.StartsWithPngSignature);
    }

    [Fact]
    public void Every_fixture_converts_to_a_docx_that_reopens()
    {
        foreach (string file in Directory.GetFiles(Path(), "*.doc"))
        {
            var defects = new DefectCollection();
            using Document d = Document.Open(file, new OpenContext(defects: defects));
            using var ms = new MemoryStream();
            d.SaveAs(ms, OfficeFormat.Docx);
            using Document again = Document.Open(new MemoryStream(ms.ToArray()), new OpenContext(), leaveOpen: false);
            Assert.Equal(d.ExtractText(), again.ExtractText());
            Assert.DoesNotContain(defects, x => x.Severity == DefectSeverity.Fatal);
        }
    }

    private static int Count(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }

    private sealed class IPackagePartAlias
    {
        internal IPackagePartAlias(Document d)
        {
            foreach (RecursiveAI.OneDocument.Packaging.IPackagePart p in d.Package.Parts)
            {
                if (p.Name.StartsWith("/word/media/", StringComparison.Ordinal))
                {
                    ReadOnlySpan<byte> b = p.GetBytes().Span;
                    StartsWithPngSignature = b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
                }
            }
        }

        internal bool StartsWithPngSignature { get; }
    }
}
