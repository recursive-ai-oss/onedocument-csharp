using RecursiveAI.OneOffice.Calc;
using RecursiveAI.OneOffice.Diagnostics;

namespace RecursiveAI.OneOffice.Tests.Calc;

public class FormulaParserTests
{
    public static TheoryData<string> FileForms => new()
    {
        "1+2",
        "IFERROR(SUM(D!D.Data.WSF.MSG : D!D.Data.WSF.CDS),0)",
        "SUM(Name1 : Name2)",
        "SUM(A1 : B2)",
        "SUM(A1:B2)",
        "SUM( A1 , B2 )",
        "$A$1+B$2*$C3",
        "Sheet1!A1",
        "'My Sheet'!$A$1:$B$2",
        "'It''s'!A1",
        "Sheet1:Sheet3!A1:B2",
        "[1]Sheet1!A1",
        "[1]!Total",
        "A:A",
        "$A:$C",
        "1:1",
        "$2:$5",
        "Sheet2!A:A",
        "-A1^2",
        "2^-2",
        "A1%",
        "50%*A1",
        "\"a\"\"b\"&A1",
        "IF(A1>0,\"yes\",\"no\")",
        "IF(A1,,2)",
        "{1,2;3,4}",
        "{\"a\",TRUE;#N/A,-1.5}",
        "(A1,B1)",
        "SUM((A1:A3,C1:C3))",
        "A1:B2 B1:C3",
        "_xlfn.SEQUENCE(3,2)",
        "_xlfn._xlws.FILTER(A1:A9,B1:B9>0)",
        "_xlfn.LET(_xlpm.x,5,_xlpm.x*2)",
        "_xlfn.LAMBDA(_xlpm.x,_xlpm.x+1)(3)",
        "_xlfn.ANCHORARRAY(A1)",
        "_xlfn.SINGLE(A1:A3)",
        "Table1[Col]",
        "Table1[[#This Row],[Col One]]",
        "Table1[@Col]",
        "Table1[[Col1]:[Col2]]",
        "Table1[#Headers]",
        "[@Col]",
        "#REF!+1",
        "Sheet1!#REF!",
        "SUM(A1:INDEX(B:B,3))",
        "1E+10+1.5e-3+.5",
        "TRUE",
        "true",
        "F()",
        "F( )",
        "A1 + B1",
        "a1:b2",
        "STDEV.S(A1:A3)",
        "MyName",
        "Sales.2020*2",
        "A1<>B1",
        "A1<=B1",
        "NOT(A1>=1)",
        "(1+2)*3",
        "((A1))",
        "--A1",
        "+A1",
        "'Sheet 1'!A1:'Sheet 1'!B2",
        "SUM(Sheet1!A1:Sheet1!B2)",
        "\n A1\n",
    };

    [Theory]
    [MemberData(nameof(FileForms))]
    public void FileFormRoundTripsByteForByte(string formula)
    {
        FormulaNode n = FormulaParser.Parse(formula);
        Assert.Equal(formula, FormulaPrinter.Print(n));
    }

    [Fact]
    public void PrecedenceMatchesExcel()
    {
        // Unary minus binds tighter than ^ in Excel, so -2^2 is 4; ^ is left associative; % is postfix.
        Assert.Equal("4", CalcTestHelper.Show(CalcTestHelper.Eval("-2^2")));
        Assert.Equal("64", CalcTestHelper.Show(CalcTestHelper.Eval("2^3^2")));
        Assert.Equal("0.25", CalcTestHelper.Show(CalcTestHelper.Eval("2^-2")));
        Assert.Equal("7", CalcTestHelper.Show(CalcTestHelper.Eval("1+2*3")));
        Assert.Equal("\"13\"", CalcTestHelper.Show(CalcTestHelper.Eval("1&3")));
        Assert.Equal("TRUE", CalcTestHelper.Show(CalcTestHelper.Eval("1+1=2")));
        Assert.Equal("0.5", CalcTestHelper.Show(CalcTestHelper.Eval("50%")));
        Assert.Equal("\"31\"", CalcTestHelper.Show(CalcTestHelper.Eval("1+2&1")));
    }

    [Fact]
    public void ParsesStructureOfReferences()
    {
        var cell = (CellReferenceNode)FormulaParser.Parse("$B$3");
        Assert.Equal(new RefCoord(3, true), cell.Row);
        Assert.Equal(new RefCoord(2, true), cell.Column);

        var area = (AreaReferenceNode)FormulaParser.Parse("'My Sheet'!A1:C9");
        Assert.Equal("My Sheet", area.Prefix!.Sheet);
        Assert.Equal(AreaKind.Cells, area.AreaKind);
        Assert.Equal(9, area.Row2.Index);

        var three = (CellReferenceNode)FormulaParser.Parse("Sheet1:Sheet3!A1");
        Assert.True(three.Prefix!.Is3D);
        Assert.Equal("Sheet3", three.Prefix.LastSheet);

        var ext = (CellReferenceNode)FormulaParser.Parse("[1]Sheet1!A1");
        Assert.Equal("1", ext.Prefix!.Workbook);

        var st = (StructuredReferenceNode)FormulaParser.Parse("Table1[[#This Row],[Col One]]");
        Assert.Equal("Table1", st.Table);
        Assert.Equal(["#This Row", "Col One"], st.GetItems());
        Assert.Equal(["#This Row", "Col"], ((StructuredReferenceNode)FormulaParser.Parse("[@Col]")).GetItems());
    }

    [Fact]
    public void LOG10IsAFunctionNotACell()
    {
        Assert.IsType<FunctionCallNode>(FormulaParser.Parse("LOG10(100)"));
        Assert.IsType<NameNode>(FormulaParser.Parse("ABCD1"));
        Assert.IsType<NameNode>(FormulaParser.Parse("A1B"));
        Assert.IsType<CellReferenceNode>(FormulaParser.Parse("XFD1048576"));
        Assert.IsType<NameNode>(FormulaParser.Parse("XFE1"));
    }

    [Theory]
    [InlineData("", "formula.unexpected-end")]
    [InlineData("1+", "formula.unexpected-end")]
    [InlineData("SUM(1", "formula.unbalanced-parenthesis")]
    [InlineData("1)", "formula.unbalanced-parenthesis")]
    [InlineData("\"abc", "formula.unterminated-string")]
    [InlineData("{1,2;3}", "formula.array-ragged")]
    [InlineData("{1,A1}", "formula.bad-array-element")]
    [InlineData("#BOGUS", "formula.bad-error-literal")]
    [InlineData("1 2", "formula.unexpected-character")]
    [InlineData("A1 @", "formula.unexpected-character")]
    [InlineData("1A", "formula.bad-number")]
    [InlineData("1E999", "formula.bad-number")]
    [InlineData("Sheet1!", "formula.bad-reference")]
    [InlineData("{{1}}", "formula.nested-array")]
    [InlineData("{1,2", "formula.unbalanced-brace")]
    [InlineData("Table1[Col", "formula.unbalanced-bracket")]
    public void BadTextIsLocated(string formula, string code)
    {
        Assert.False(FormulaParser.TryParse(formula, out FormulaNode? n, out FormulaSyntaxError err));
        Assert.Null(n);
        Assert.Equal(code, err.Code);
        Assert.InRange(err.Offset, 0, formula.Length);
        var ex = Assert.Throws<FormulaParseException>(() => FormulaParser.Parse(formula));
        Assert.Equal(DefectKind.Formula, ex.Defect.Kind);
        Assert.Equal(code, ex.Defect.Code);
    }

    [Fact]
    public void ErrorOffsetsPointAtTheProblem()
    {
        Assert.False(FormulaParser.TryParse("1+2+#BOGUS", out _, out FormulaSyntaxError e));
        Assert.Equal(4, e.Offset);
        Assert.False(FormulaParser.TryParse("SUM(1,2,\"x", out _, out e));
        Assert.Equal(8, e.Offset);
    }

    [Fact]
    public void LimitsAreTyped()
    {
        string deep = new string('(', 100) + "1" + new string(')', 100);
        Assert.Throws<OfficeLimitException>(() => FormulaParser.Parse(deep));
        Assert.False(FormulaParser.TryParse(deep, out _, out FormulaSyntaxError e));
        Assert.Equal("formula.too-deep", e.Code);
        string longText = "1" + string.Concat(Enumerable.Repeat("+1", 5000));
        Assert.Throws<OfficeLimitException>(() => FormulaParser.Parse(longText));
        Assert.False(FormulaParser.TryParse(longText, out _, out e));
        Assert.Equal("formula.too-long", e.Code);
    }

    [Fact]
    public void R1C1ConvertsBothWays()
    {
        var o = new FormulaPrintOptions { Syntax = FormulaSyntax.R1C1, OriginRow = 5, OriginColumn = 3 };
        FormulaNode a1 = FormulaParser.Parse("SUM(A1:B2)+$C$4+D5+C$1");
        Assert.Equal("SUM(R[-4]C[-2]:R[-3]C[-1])+R4C3+RC[1]+R1C", FormulaPrinter.Print(a1, o));

        FormulaNode back = FormulaParser.Parse("SUM(R[-4]C[-2]:R[-3]C[-1])+R4C3+RC[1]+R1C", new FormulaParserOptions { Syntax = FormulaSyntax.R1C1, OriginRow = 5, OriginColumn = 3 });
        Assert.Equal("SUM(A1:B2)+$C$4+D5+C$1", FormulaPrinter.Print(back));

        Assert.Equal("SUM(R1:R3,C[-1]:C[1])", FormulaPrinter.Print(FormulaParser.Parse("SUM($1:$3,B:D)"), o));
        Assert.Equal("$1:$3", FormulaPrinter.Print(FormulaParser.Parse("R1:R3", new FormulaParserOptions { Syntax = FormulaSyntax.R1C1 })));
        Assert.Equal("Sheet1!R1C1", FormulaPrinter.Print(FormulaParser.Parse("Sheet1!$A$1"), new FormulaPrintOptions { Syntax = FormulaSyntax.R1C1 }));
    }

    [Fact]
    public void DisplayFormShowsOperatorsNotPrefixes()
    {
        FormulaNode n = FormulaParser.Parse("_xlfn.SINGLE(_xlfn.ANCHORARRAY(A1))+_xlfn.STDEV.S(B1:B3)");
        var d = new FormulaPrintOptions { Form = FormulaForm.Display };
        Assert.Equal("@A1#+STDEV.S(B1:B3)", FormulaPrinter.Print(n, d));

        FormulaNode typed = FormulaParser.Parse("@A1#+SEQUENCE(2)+FILTER(A1:A3,B1:B3)", new FormulaParserOptions { FileForm = false });
        Assert.Equal("_xlfn.SINGLE(A1#)+_xlfn.SEQUENCE(2)+_xlfn._xlws.FILTER(A1:A3,B1:B3)".Replace("A1#", "_xlfn.ANCHORARRAY(A1)", StringComparison.Ordinal), FormulaPrinter.Print(typed));
    }

    [Fact]
    public void LocalSeparatorsAndDecimals()
    {
        var de = FormulaLocale.ForDecimalSeparator(",");
        FormulaNode n = FormulaParser.Parse("SUM(1,5;2;{1.2;3.4})", new FormulaParserOptions { Locale = de });
        Assert.Equal("SUM(1.5,2,{1,2;3,4})", FormulaPrinter.Print(n));
        Assert.Equal("SUM(1,5;2;{1.2;3.4})", FormulaPrinter.Print(n, new FormulaPrintOptions { Form = FormulaForm.Display, Locale = de }));
    }

    [Fact]
    public void SharedFormulaTranslationMovesRelativeParts()
    {
        FormulaNode n = FormulaParser.Parse("A1+$B$2+C$3+$D4+SUM(E5:F6)");
        FormulaNode moved = FormulaTranslator.Translate(n, 2, 3);
        Assert.Equal("D3+$B$2+F$3+$D6+SUM(H7:I8)", FormulaPrinter.Print(moved));
        Assert.Equal("#REF!+1", FormulaPrinter.Print(FormulaTranslator.Translate(FormulaParser.Parse("A1+1"), -1, 0)));
        Assert.Equal("A1+1", FormulaPrinter.Print(FormulaTranslator.Translate(FormulaParser.Parse("A1+1"), 0, 0)));
    }

    [Fact]
    public void TemplateExtensionIsOffByDefault()
    {
        Assert.IsType<NameNode>(FormulaParser.Parse("item.price"));
        var m = Assert.IsType<MemberPathNode>(FormulaParser.Parse("item.price", new FormulaParserOptions { TemplateExtensions = true }));
        Assert.Equal(["item", "price"], m.Segments);
        Assert.Equal("item.price*2", FormulaPrinter.Print(FormulaParser.Parse("item.price*2", new FormulaParserOptions { TemplateExtensions = true })));
    }

    [Fact]
    public void NodesCarryOffsets()
    {
        var b = (BinaryNode)FormulaParser.Parse("A1 + SUM(B1)");
        Assert.Equal(0, b.Left.Offset);
        Assert.Equal(5, b.Right.Offset);
        Assert.Equal(7, b.Right.Length);
    }
}
