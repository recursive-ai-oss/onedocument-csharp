using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Fields;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class FieldTests
{
    private static string Run(string inner) => "<w:r>" + inner + "</w:r>";

    private static string Begin() => Run("<w:fldChar w:fldCharType=\"begin\"/>");

    private static string Sep() => Run("<w:fldChar w:fldCharType=\"separate\"/>");

    private static string End() => Run("<w:fldChar w:fldCharType=\"end\"/>");

    private static string Instr(string text) => Run("<w:instrText xml:space=\"preserve\">" + text + "</w:instrText>");

    private static string Text(string text) => Run("<w:t>" + text + "</w:t>");

    private static string Para(string inner) => "<w:p>" + inner + "</w:p>";

    [Fact]
    public void Instruction_SplitsTypeArgumentsAndSwitches()
    {
        FieldInstruction toc = FieldInstruction.Parse(" TOC \\o \"1-3\" \\h \\z \\u ");
        Assert.Equal("TOC", toc.Type);
        Assert.Empty(toc.Arguments);
        Assert.Equal("1-3", toc.Switch("o")!.Value.Value);
        Assert.True(toc.HasSwitch("h") && toc.HasSwitch("z") && toc.HasSwitch("u"));
        Assert.Null(toc.Switch("h")!.Value.Value);

        FieldInstruction reference = FieldInstruction.Parse("pageref _Toc123 \\h \\* MERGEFORMAT");
        Assert.Equal("PAGEREF", reference.Type);
        Assert.Equal(["_Toc123"], reference.Arguments);
        Assert.Equal("MERGEFORMAT", reference.Switch("*")!.Value.Value);

        FieldInstruction date = FieldInstruction.Parse("DATE \\@ \"dddd, d MMMM yyyy\"");
        Assert.Equal("dddd, d MMMM yyyy", date.Switch("@")!.Value.Value);

        FieldInstruction formula = FieldInstruction.Parse("= SUM(ABOVE) \\# \"0.00\"");
        Assert.Equal("=", formula.Type);
        Assert.Equal(["SUM(ABOVE)"], formula.Arguments);

        FieldInstruction link = FieldInstruction.Parse("HYPERLINK \"http://example.org/a b\" \\l \"top\" \\n");
        Assert.Equal(["http://example.org/a b"], link.Arguments);
        Assert.Equal("top", link.Switch("l")!.Value.Value);
        Assert.True(link.HasSwitch("n"));
    }

    [Fact]
    public void Instruction_ToleratesGarbage()
    {
        Assert.Equal(string.Empty, FieldInstruction.Parse("   ").Type);
        FieldInstruction open = FieldInstruction.Parse("REF \"unclosed");
        Assert.False(open.IsWellFormed);
        Assert.Equal(["unclosed"], open.Arguments);
        Assert.Equal("a\"b", FieldInstruction.Parse("X \"a\\\"b\"").Arguments[0]);
        FieldInstruction lone = FieldInstruction.Parse("X \\");
        Assert.Equal(["\\"], lone.Arguments);
    }

    [Fact]
    public void ComplexField_SpanningParagraphs_IsOneField()
    {
        string body = Para(Text("before ") + Begin() + Instr(" TOC \\o \"1-3\" ") + Sep() + Text("Heading one\t3")) + Para(Text("Heading two\t4") + End() + Text(" after"));
        using Document d = TestDocs.Open(body);
        Field f = Assert.Single(d.Fields());
        Assert.Equal(FieldKind.Complex, f.Kind);
        Assert.True(f.IsComplete);
        Assert.Equal("TOC", f.Type);
        Assert.Equal("Heading one\t3Heading two\t4", f.ResultText);
        Assert.Equal(0, f.Depth);
    }

    [Fact]
    public void NestedFields_InCodeAndInResult()
    {
        // IF { PAGE } > 1 "{ NUMPAGES }" "x"
        string code = Begin() + Instr(" IF ") + Begin() + Instr(" PAGE ") + Sep() + Text("2") + End() + Instr(" > 1 ") + Sep() +
            Begin() + Instr(" NUMPAGES ") + Sep() + Text("7") + End() + End();
        using Document d = TestDocs.Open(Para(code));
        IReadOnlyList<Field> fields = d.Fields();
        Assert.Equal(["IF", "PAGE", "NUMPAGES"], fields.Select(f => f.Type).ToArray());
        Assert.Equal([0, 1, 1], fields.Select(f => f.Depth).ToArray());
        Assert.Equal(" IF 2 > 1 ", fields[0].Instruction);
        Assert.Equal("7", fields[0].ResultText);
        Assert.Same(fields[0], fields[1].Parent);
        Assert.Equal(2, fields[0].Children.Count);
    }

    [Fact]
    public void SimpleField_IsReadWithItsAttributes()
    {
        using Document d = TestDocs.Open(Para("<w:fldSimple w:instr=\" DATE \\@ &quot;yyyy&quot; \" w:dirty=\"true\">" + Text("2025") + "</w:fldSimple>"));
        Field f = Assert.Single(d.Fields());
        Assert.Equal(FieldKind.Simple, f.Kind);
        Assert.True(f.IsDirty);
        Assert.False(f.IsLocked);
        Assert.Equal("DATE", f.Type);
        Assert.Equal("yyyy", f.Parsed.Switch("@")!.Value.Value);
        Assert.Equal("2025", f.ResultText);
    }

    [Fact]
    public void Unbalanced_ReportsDefects()
    {
        var defects = new DefectCollection();
        using Document d = TestDocs.Open(Para(Begin() + Instr(" PAGE ") + Text("x")), defects: defects);
        Field f = Assert.Single(d.Fields());
        Assert.False(f.IsComplete);
        Assert.Contains(defects, x => x.Code == "docx.field-unbalanced");
        Assert.Equal(1, defects.Count(x => x.Code == "docx.field-unbalanced"));

        var stray = new DefectCollection();
        using Document d2 = TestDocs.Open(Para(Text("a") + End()), defects: stray);
        Assert.Empty(d2.Fields());
        Defect defect = Assert.Single(stray, x => x.Code == "docx.field-unbalanced");
        Assert.True(defect.Location.Offset > 0);
    }

    [Fact]
    public void TooDeep_IsReported_AndReadAsPlainContent()
    {
        var defects = new DefectCollection();
        string open = string.Concat(Enumerable.Repeat(Begin() + Instr(" X ") + Sep(), 5)) + Text("core") + string.Concat(Enumerable.Repeat(End(), 5));
        InMemoryPackage p = TestDocs.Package(Para(open));
        using var d = Document.FromPackage(p, new OpenContext(new OfficeLimits { MaxRecursionDepth = 3 }, defects));
        Assert.Equal(3, d.Fields().Count);
        Assert.Single(defects, x => x.Code == "docx.field-too-deep");
    }

    [Fact]
    public void FallbackBranch_IsNotCountedTwice()
    {
        string mc = "<mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"><mc:Choice Requires=\"w14\">" + Begin() + Instr(" PAGE ") + End() + "</mc:Choice><mc:Fallback>" + Begin() + Instr(" PAGE ") + End() + "</mc:Fallback></mc:AlternateContent>";
        using Document d = TestDocs.Open(Para(mc));
        Assert.Single(d.Fields());
    }

    [Fact]
    public void SetInstruction_Simple_AndComplex()
    {
        using Document d = TestDocs.Open(Para("<w:fldSimple w:instr=\" PAGE \">" + Text("1") + "</w:fldSimple>") + Para(Begin() + Instr(" REF a") + Instr(" \\h ") + Sep() + Text("v") + End()));
        d.Fields()[0].SetInstruction(" NUMPAGES ");
        Field complex = d.Fields()[1];
        complex.SetInstruction(" REF b \\h ");
        IReadOnlyList<Field> again = d.Fields();
        Assert.Equal(" NUMPAGES ", again[0].Instruction);
        Assert.Equal(" REF b \\h ", again[1].Instruction);
        Assert.Equal("v", again[1].ResultText);
        Assert.Equal(1, Regex(d.Markup.Node.OuterXml, "<w:instrText"));
    }

    [Fact]
    public void SetResult_ReplacesRunsAcrossParagraphs_KeepingFormatting()
    {
        string first = "<w:r><w:rPr><w:b/></w:rPr><w:t>Old one</w:t></w:r>";
        using Document d = TestDocs.Open(Para(Begin() + Instr(" TOC ") + Sep() + first) + Para(Text("Old two") + End() + Text("tail")));
        Field f = d.Fields().Single();
        f.SetResult("New\tvalue");
        Field g = d.Fields().Single();
        Assert.Equal("New\tvalue", g.ResultText);
        string xml = d.Markup.Node.OuterXml;
        Assert.Contains("<w:rPr><w:b/></w:rPr><w:t xml:space=\"preserve\">New</w:t><w:tab/><w:t xml:space=\"preserve\">value</w:t>", xml);
        Assert.DoesNotContain("Old", xml);
        Assert.Contains("tail", xml);
        Assert.Equal("tail", d.Body.Paragraphs().Last().Text);
    }

    [Fact]
    public void SetResult_AddsSeparator_WhenThereIsNone_AndReplacesNestedFields()
    {
        using Document d = TestDocs.Open(Para("<w:r><w:rPr><w:i/></w:rPr><w:fldChar w:fldCharType=\"begin\"/></w:r>" + Instr(" AUTHOR ") + End()));
        d.Fields().Single().SetResult("Ann");
        Field f = d.Fields().Single();
        Assert.True(f.HasResult);
        Assert.Equal("Ann", f.ResultText);
        Assert.Contains("<w:r><w:rPr><w:i/></w:rPr><w:t xml:space=\"preserve\">Ann</w:t></w:r>", d.Markup.Node.OuterXml);

        string nested = Begin() + Instr(" IF 1 = 1 ") + Sep() + Text("a ") + Begin() + Instr(" PAGE ") + Sep() + Text("9") + End() + Text(" b") + End();
        using Document d2 = TestDocs.Open(Para(nested));
        d2.Fields()[0].SetResult("plain");
        IReadOnlyList<Field> after = d2.Fields();
        Assert.Single(after);
        Assert.Equal("plain", after[0].ResultText);
        Assert.Equal("plain", d2.Body.Paragraphs().Single().Text);
    }

    [Fact]
    public void SetResult_OnSimpleField_AndOnUnclosedField()
    {
        using Document d = TestDocs.Open(Para("<w:fldSimple w:instr=\" PAGE \"/>") + Para(Begin() + Instr(" X ")));
        d.Fields()[0].SetResult("4");
        Assert.Equal("4", d.Fields()[0].ResultText);
        Assert.Throws<InvalidOperationException>(() => d.Fields()[1].SetResult("no"));
        Assert.Throws<ArgumentException>(() => d.Fields()[0].SetResult("bad\u0001"));
    }

    [Fact]
    public void Edit_ChangesOnlyTheMainPart()
    {
        InMemoryPackage p = TestDocs.Package(Para("<w:fldSimple w:instr=\" PAGE \">" + Text("1") + "</w:fldSimple>"), "<w:style w:type=\"paragraph\" w:styleId=\"A\"/>");
        byte[] styles = p.FindPart("/word/styles.xml")!.GetBytes().ToArray();
        using var d = Document.FromPackage(p);
        d.Fields().Single().SetResult("2");
        d.CommitChanges();
        Assert.Equal(styles, p.FindPart("/word/styles.xml")!.GetBytes().ToArray());
        Assert.Contains("<w:t xml:space=\"preserve\">2</w:t>", Encoding.UTF8.GetString(p.FindPart("/word/document.xml")!.GetBytes().Span));
    }

    private static int Regex(string text, string find)
    {
        int n = 0;
        for (int i = text.IndexOf(find, StringComparison.Ordinal); i >= 0; i = text.IndexOf(find, i + 1, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
