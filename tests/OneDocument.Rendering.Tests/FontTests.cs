using System.Text;
using OneFont;
using OneRender.Fonts;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering.Tests;

public sealed class FontTests
{
    private static LayoutDocument Lay(string family, FontLibrary? fonts = null)
    {
        string body = "<w:p><w:r><w:rPr><w:rFonts w:ascii=\"" + family + "\" w:hAnsi=\"" + family + "\"/></w:rPr><w:t>Hello</w:t></w:r></w:p>";
        using Document d = TestDocx.Open(body);
        return LayoutEngine.Layout(d, new LayoutOptions { Fonts = fonts ?? FontLibrary.CreateDefault() });
    }

    [Theory]
    [InlineData("Calibri", "Carlito", FontMatchKind.MetricCompatible)]
    [InlineData("Cambria", "Caladea", FontMatchKind.MetricCompatible)]
    [InlineData("Arial", "Liberation Sans", FontMatchKind.MetricCompatible)]
    [InlineData("Times New Roman", "Liberation Serif", FontMatchKind.MetricCompatible)]
    [InlineData("Courier New", "Liberation Mono", FontMatchKind.MetricCompatible)]
    [InlineData("Aptos", "Carlito", FontMatchKind.Approximate)]
    [InlineData("Segoe UI", "Carlito", FontMatchKind.Approximate)]
    [InlineData("Some Unknown Face", "Liberation Sans", FontMatchKind.Generic)]
    public void SubstitutionsAreReportedWithTheirKind(string asked, string used, FontMatchKind kind)
    {
        LayoutDocument l = Lay(asked);
        FontSubstitutionNote note = Assert.Single(l.FontNotes, n => n.Requested == asked);
        Assert.Equal(used, note.Actual);
        Assert.Equal(kind, note.Kind);
        Assert.Contains(asked, note.Message, StringComparison.Ordinal);
        LayoutRun run = l.Pages[0].Paragraphs.Single().Lines.Single().Runs.Single();
        Assert.Equal(used, run.Font!.ActualFamily);
        Assert.Equal(asked, run.Font.RequestedFamily);
    }

    [Fact]
    public void ExactFamiliesProduceNoNote()
    {
        Assert.Empty(Lay("Liberation Serif").FontNotes);
    }

    [Fact]
    public void FamilyClassAndPanoseChooseTheGenericFace()
    {
        var fonts = FontLibrary.CreateDefault();
        Assert.Equal("Liberation Serif", fonts.Resolve(new FontRequest("Nope", FamilyClass: "roman"))!.ActualFamily);
        Assert.Equal("Liberation Mono", fonts.Resolve(new FontRequest("Nope", FamilyClass: "modern"))!.ActualFamily);
        Assert.Equal("Liberation Sans", fonts.Resolve(new FontRequest("Nope", FamilyClass: "swiss"))!.ActualFamily);
        Assert.Equal("Liberation Serif", fonts.Resolve(new FontRequest("Nope", Panose: "02020603050405020304"))!.ActualFamily);
        Assert.Equal("Liberation Mono", fonts.Resolve(new FontRequest("Nope", Panose: "02070309020205020404"))!.ActualFamily);
        Assert.Equal("Liberation Sans", fonts.Resolve(new FontRequest("Nope", Panose: "020B0604020202020204"))!.ActualFamily);
    }

    [Fact]
    public void BoldAndItalicPickTheMatchingFace()
    {
        var fonts = FontLibrary.CreateDefault();
        FontFace f = fonts.Resolve(new FontRequest("Carlito", Bold: true, Italic: true))!.Face;
        Assert.True(f.IsBold);
        Assert.True(f.IsItalic);
    }

    [Fact]
    public void ObfuscationIsItsOwnInverse()
    {
        byte[] original = Enumerable.Range(0, 100).Select(i => (byte)(i * 7)).ToArray();
        byte[] data = (byte[])original.Clone();
        const string key = "{1B2C3D4E-5F60-7182-93A4-B5C6D7E8F901}";
        Assert.True(DocumentFonts.Deobfuscate(data, key));
        Assert.NotEqual(original.AsSpan(0, 32).ToArray(), data.AsSpan(0, 32).ToArray());
        Assert.Equal(original.AsSpan(32).ToArray(), data.AsSpan(32).ToArray());
        // The key bytes are the GUID's hex pairs read from the end of the string.
        Assert.Equal((byte)(original[0] ^ 0x01), data[0]);
        Assert.Equal((byte)(original[1] ^ 0xF9), data[1]);
        Assert.Equal((byte)(original[16] ^ 0x01), data[16]);
        Assert.True(DocumentFonts.Deobfuscate(data, key));
        Assert.Equal(original, data);
        Assert.False(DocumentFonts.Deobfuscate(data, "not a guid"));
    }

    [Fact]
    public void EmbeddedFontsBeatSubstitutes()
    {
        // Embed Liberation Mono under the name "Acme Mono Face1", obfuscated with a key, and check the document uses it exactly.
        byte[] font = DefaultFonts.Faces.First(f => f.Family == "Liberation Mono" && f.Weight == 400 && f.Slant == FontSlant.Normal).ReadAllBytes();
        byte[] renamed = Rename(font, "Liberation Mono", "Acme Mono Face1");
        const string key = "{1B2C3D4E-5F60-7182-93A4-B5C6D7E8F901}";
        byte[] obfuscated = (byte[])renamed.Clone();
        DocumentFonts.Deobfuscate(obfuscated, key);

        string fontTable = "<w:fonts xmlns:w=\"" + TestDocx.W + "\" xmlns:r=\"" + TestDocx.R + "\"><w:font w:name=\"Acme Mono Face1\"><w:family w:val=\"modern\"/><w:embedRegular r:id=\"rId1\" w:fontKey=\"" + key + "\"/></w:font></w:fonts>";
        byte[] docx = TestDocx.Bytes(
            "<w:p><w:r><w:rPr><w:rFonts w:ascii=\"Acme Mono Face1\" w:hAnsi=\"Acme Mono Face1\"/></w:rPr><w:t>Hello</w:t></w:r></w:p>",
            fontTable: fontTable,
            extraParts:
            [
                ("word/fonts/font1.odttf", obfuscated),
                ("word/_rels/fontTable.xml.rels", Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + TestDocx.R + "/font\" Target=\"fonts/font1.odttf\"/></Relationships>")),
            ]);
        using Document d = Document.Open(new MemoryStream(docx), leaveOpen: false);
        var library = FontLibrary.CreateDefault();
        Assert.Equal(1, DocumentFonts.AddEmbeddedFonts(d, library));
        LayoutDocument l = LayoutEngine.Layout(d, new LayoutOptions { Fonts = library });
        Assert.Empty(l.FontNotes);
        Assert.Equal("Acme Mono Face1", l.Pages[0].Paragraphs.Single().Lines.Single().Runs.Single().Font!.ActualFamily);

        // Without the library argument the layout reads the embedded fonts itself.
        LayoutDocument auto = LayoutEngine.Layout(d);
        Assert.Empty(auto.FontNotes);
    }

    // Changes the family name in the 'name' table by rewriting the UTF-16BE or ASCII records of equal length.
    private static byte[] Rename(byte[] font, string from, string to)
    {
        byte[] result = (byte[])font.Clone();
        Replace(result, Encoding.BigEndianUnicode.GetBytes(from), Encoding.BigEndianUnicode.GetBytes(to.PadRight(from.Length)));
        Replace(result, Encoding.ASCII.GetBytes(from), Encoding.ASCII.GetBytes(to.PadRight(from.Length)));
        return result;
    }

    private static void Replace(byte[] data, byte[] find, byte[] replace)
    {
        for (int i = 0; i + find.Length <= data.Length; i++)
        {
            if (data.AsSpan(i, find.Length).SequenceEqual(find))
            {
                replace.CopyTo(data, i);
                i += find.Length - 1;
            }
        }
    }

    [Fact]
    public void ScriptFallbackFindsAFaceForMissingCharacters()
    {
        var fonts = FontLibrary.CreateDefault();
        // The default sans lacks Hebrew; the registered Hebrew package covers it.
        OneRender.Fonts.Noto.NotoHebrewFonts.Register();
        fonts.AddOptionalPackages();
        FontFace? f = fonts.FindFallback(0x05D0, false, false);
        Assert.NotNull(f);
        Assert.True(f.HasGlyph(0x05D0));
        Assert.Null(fonts.FindFallback(0x10FFFD, false, false));
    }
}
