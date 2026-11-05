using RecursiveAI.OneOffice.Calc;
using static RecursiveAI.OneOffice.Tests.Calc.CalcTestHelper;

namespace RecursiveAI.OneOffice.Tests.Calc;

public class EvaluatorTests
{
    [Theory]
    [InlineData("1+2", "3")]
    [InlineData("\"3\"+1", "4")]
    [InlineData("TRUE+1", "2")]
    [InlineData("\"a\"+1", "#VALUE!")]
    [InlineData("\"\"+1", "#VALUE!")]
    [InlineData("1/0", "#DIV/0!")]
    [InlineData("0^0", "#NUM!")]
    [InlineData("(-8)^(1/3)", "-2")]
    [InlineData("(-8)^0.5", "#NUM!")]
    [InlineData("0^-1", "#DIV/0!")]
    [InlineData("1E308*10", "#NUM!")]
    [InlineData("#N/A+\"a\"", "#N/A")]
    [InlineData("\"a\"+#N/A", "#VALUE!")]
    [InlineData("#REF!+#N/A", "#REF!")]
    [InlineData("1&2", "\"12\"")]
    [InlineData("TRUE&\"x\"", "\"TRUEx\"")]
    [InlineData("0.1+0.2", "0.30000000000000004")]
    [InlineData("(0.1+0.2)=0.3", "TRUE")]
    [InlineData("1.1-1-0.1", "0")]
    [InlineData("\"a\"<\"B\"", "TRUE")]
    [InlineData("\"a\"=\"A\"", "TRUE")]
    [InlineData("1<\"a\"", "TRUE")]
    [InlineData("\"a\"<TRUE", "TRUE")]
    [InlineData("100>TRUE", "FALSE")]
    [InlineData("10%+1", "1.1")]
    [InlineData("-\"2\"", "-2")]
    [InlineData("2-\"1,000\"", "-998")]
    [InlineData("\"50%\"*2", "1")]
    [InlineData("\"1/2/2020\"-\"1/1/2020\"", "1")]
    [InlineData("\"12:00\"*2", "1")]
    public void OperatorsAndCoercion(string formula, string expected) => Assert.Equal(expected, Show(Eval(formula)));

    [Theory]
    [InlineData("\"_\"<\"a\"", "TRUE")]
    [InlineData("\"a-b\"<\"ab\"", "FALSE")]
    [InlineData("\"é\"<\"f\"", "TRUE")]
    [InlineData("\"ä\"<\"b\"", "TRUE")]
    [InlineData("\"ß\"=\"ss\"", "TRUE")]
    [InlineData("\"é\"=\"e\"", "FALSE")]
    [InlineData("\"a\"=\"A\"", "TRUE")]
    [InlineData("\"10\"<\"9\"", "TRUE")]
    [InlineData("123456789.123456789&\"\"", "\"123456789.123456\"")]
    [InlineData("12345678901234567890&\"\"", "\"12345678901234500000\"")]
    [InlineData("OR(\"x\",TRUE)", "TRUE")]
    [InlineData("INDEX({1,2,3;4,5,6},-1,1)", "#VALUE!")]
    [InlineData("MAP({1;2},{10,20},LAMBDA(a,b,a+b))", "{11,#N/A;#N/A,#N/A}")]
    [InlineData("MAKEARRAY(2,2,LAMBDA(r,1))", "#VALUE!")]
    [InlineData("IFS({TRUE,FALSE},1,TRUE,2)", "{1,2}")]
    [InlineData("CHOOSE({1,2},\"a\",\"b\")", "{\"a\",\"b\"}")]
    [InlineData("@{1,2,3}", "1")]
    [InlineData("TEXT(1,\"\")", "\"\"")]
    // Snap: below 8 units in the last place of the larger operand's binade a sum or difference is zero; at 8 it is kept (Excel 16 grid).
    [InlineData("1+1.55431223447521E-15-1", "0")]
    [InlineData("1+1.77635683940025E-15-1", "1.7763568394002505E-15")]
    [InlineData("100+9.9475983006414E-14-100", "0")]
    [InlineData("1000+9.09494701772928E-13-1000", "9.094947017729282E-13")]
    [InlineData("SUM(0.1,0.2,-0.3)", "0")]
    [InlineData("SUM(1,1.99840144432528E-15,-1)", "1.9984014443252818E-15")]
    [InlineData("(-1)^2147483649", "-1")]
    [InlineData("(-1)^4294967296", "#NUM!")]
    [InlineData("1E-18&\"\"", "\"0.000000000000000001\"")]
    [InlineData("1E-19&\"\"", "\"1E-19\"")]
    [InlineData("1.23456789012345E-5&\"\"", "\"1.23456789012345E-05\"")]
    [InlineData("1.2345678901234E-5&\"\"", "\"0.000012345678901234\"")]
    public void BehavioursSettledAgainstExcel(string formula, string expected) => Assert.Equal(expected, Show(Eval(formula)));

    [Fact]
    public void ParenthesisedDifferenceIsNotSnapped()
    {
        Assert.NotEqual("0", Show(Eval("(1.1-1-0.1)")));
        Assert.Equal("0", Show(Eval("1.1-1-0.1")));
    }

    [Fact]
    public void BlankComparesAsZeroEmptyOrFalse()
    {
        var r = new FakeResolver();
        Assert.Equal("TRUE", Show(Eval("A1=0", r)));
        Assert.Equal("TRUE", Show(Eval("A1=\"\"", r)));
        Assert.Equal("TRUE", Show(Eval("A1=FALSE", r)));
        Assert.Equal("TRUE", Show(Eval("A1=A2", r)));
        Assert.Equal("0", Show(Eval("A1+A2", r)));
    }

    [Fact]
    public void ReferencesReadCellsAndRanges()
    {
        var r = new FakeResolver().Set("A1", 1).Set("A2", 2).Set("A3", "x").Set("A4", 4).Set("B1", true);
        Assert.Equal("7", Show(Eval("SUM(A1:A4)", r)));
        Assert.Equal("9", Show(Eval("SUM(A1:A4,\"2\")", r)));
        Assert.Equal("#VALUE!", Show(Eval("SUM(A1,\"x\")", r)));
        Assert.Equal("1", Show(Eval("SUM(A1,B1)", r)));
        Assert.Equal("4", Show(Eval("ROWS(A1:A4)", r)));
        Assert.Equal("2", Show(Eval("COLUMNS(A1:B1)", r)));
        Assert.Equal("1", Show(Eval("COLUMNS(A1)", r)));
        Assert.Equal("4", Show(Eval("INDEX(A1:A4,4)", r)));
        Assert.Equal("\"x\"", Show(Eval("INDEX(A1:A4,3,1)", r)));
        Assert.Equal("#REF!", Show(Eval("INDEX(A1:A4,5)", r)));
        Assert.Equal("{1;2;\"x\";4}", Show(Eval("INDEX(A1:A4,0)", r)));
        Assert.Equal("3", Show(Eval("SUM(INDEX(A1:A4,1):INDEX(A1:A4,2))", r)));
        Assert.Equal("#NULL!", Show(Eval("A1:A2 B1:B2", r)));
        Assert.Equal("2", Show(Eval("SUM(A1:A2 A2:A3)", r)));
        Assert.Equal("7", Show(Eval("SUM((A1:A2,A4))", r)));
    }

    [Fact]
    public void ArraysBroadcastAndLift()
    {
        var r = new FakeResolver().Set("A1", 1).Set("A2", 2).Set("A3", 3);
        Assert.Equal("{2;3;4}", Show(Eval("A1:A3+1", r)));
        Assert.Equal("{1,2;2,4}", Show(Eval("{1;2}*{1,2}")));
        Assert.Equal("{1,2;3,4}", Show(Eval("{1,2;3,4}*{1,1;1,1}")));
        Assert.Equal("{1,2,3}", Show(Eval("LEN({1,22,333})")));
        Assert.Equal("{\"a1\";\"a2\";\"a3\"}", Show(Eval("\"a\"&A1:A3", r)));
        Assert.Equal("{\"a\",\"b\"}", Show(Eval("IF({TRUE,FALSE},\"a\",\"b\")")));
        Assert.Equal("{TRUE,FALSE}", Show(Eval("NOT({FALSE,TRUE})")));
        Assert.Equal("{FALSE,TRUE}", Show(Eval("ISERROR({1,#N/A})")));
        Assert.Equal("{-1,-2}", Show(Eval("-{1,2}")));
        Assert.Equal("#N/A", Show(Eval("{1,2}+{1,2,3}").Array![0, 2]));
    }

    [Fact]
    public void LegacyImplicitIntersection()
    {
        var r = new FakeResolver().Set("A1", 10).Set("A2", 20).Set("A3", 30);
        var legacy = new EvaluatorOptions { LegacyImplicitIntersection = true };
        Assert.Equal("20", Show(Eval("A1:A3", r, legacy, row: 2, col: 5)));
        Assert.Equal("21", Show(Eval("A1:A3+1", r, legacy, row: 2, col: 5)));
        Assert.Equal("#VALUE!", Show(Eval("A1:A3+1", r, legacy, row: 9, col: 5)));
        Assert.Equal("20", Show(Eval("@A1:A3", r, row: 2, col: 5)));
        Assert.Equal("{10;20;30}", Show(Eval("A1:A3", r, row: 2, col: 5)));
    }

    [Fact]
    public void SequenceMakeArrayMap()
    {
        Assert.Equal("{1,2,3;4,5,6}", Show(Eval("SEQUENCE(2,3)")));
        Assert.Equal("{10;20;30}", Show(Eval("SEQUENCE(3,,10,10)")));
        Assert.Equal("#CALC!", Show(Eval("SEQUENCE(0)")));
        Assert.Equal("{1,2;2,4}", Show(Eval("MAKEARRAY(2,2,LAMBDA(r,c,r*c))")));
        Assert.Equal("{2,4,6}", Show(Eval("MAP({1,2,3},LAMBDA(x,x*2))")));
        Assert.Equal("{11,22}", Show(Eval("MAP({1,2},{10,20},LAMBDA(a,b,a+b))")));
        Assert.Equal("{1;4;9}", Show(Eval("MAP(SEQUENCE(3),LAMBDA(x,x^2))")));
        Assert.Equal("#CALC!", Show(Eval("MAKEARRAY(1,1,LAMBDA(r,c,{1,2}))")));
    }

    [Fact]
    public void LetAndLambda()
    {
        Assert.Equal("12", Show(Eval("LET(x,5,y,x+1,x+y+1)")));
        Assert.Equal("4", Show(Eval("LAMBDA(x,x+1)(3)")));
        Assert.Equal("7", Show(Eval("LET(f,LAMBDA(a,b,a*b),f(2,3)+1)")));
        Assert.Equal("5", Show(Eval("LET(n,5,LAMBDA(x,x+n)(0))")));
        Assert.Equal(ValueKind.Lambda, Eval("LET(f,LAMBDA(x,x),f)").Kind);
        Assert.Equal("120", Show(Eval("LET(fa,LAMBDA(self,n,IF(n<=1,1,n*self(self,n-1))),fa(fa,5))")));
        Assert.Equal("#VALUE!", Show(Eval("LAMBDA(x,x)(1,2)")));
        Assert.Equal("#NAME?", Show(Eval("LET(x,1,y)")));
        Assert.Equal("6", Show(Eval("LET(r,{1,2,3},SUM(r))")));
    }

    [Fact]
    public void RecursionStopsAtTheLimit()
    {
        var ev = new Evaluator(new FakeResolver(), limits: new OfficeLimits { MaxRecursionDepth = 50 });
        FormulaNode n = FormulaParser.Parse("LET(f,LAMBDA(self,n,self(self,n+1)),f(f,1))", new FormulaParserOptions { FileForm = false });
        CalcValue v = ev.Evaluate(n, new CalcCell("Sheet1", 1, 1));
        Assert.Equal("#NUM!", Show(v));
        Assert.True(ev.RecursionLimitHit);
    }

    [Fact]
    public void HugeArraysAreRefusedTyped()
    {
        Assert.Throws<OfficeLimitException>(() => Eval("SEQUENCE(100000,100000)"));
        Assert.Equal("#VALUE!", Show(Eval("SEQUENCE(-1)")));
    }

    [Fact]
    public void ShortCircuiting()
    {
        // The untaken branch must not evaluate: a lambda call with the wrong arity would be #VALUE! if it ran, a limit would throw.
        Assert.Equal("1", Show(Eval("IF(TRUE,1,SEQUENCE(1000000,1000000))")));
        Assert.Equal("1", Show(Eval("IFERROR(1,SEQUENCE(1000000,1000000))")));
        Assert.Equal("2", Show(Eval("IFERROR(1/0,2)")));
        Assert.Equal("#DIV/0!", Show(Eval("IFNA(1/0,2)")));
        Assert.Equal("2", Show(Eval("IFNA(NA(),2)".Replace("NA()", "#N/A", StringComparison.Ordinal))));
        Assert.Equal("\"b\"", Show(Eval("IFS(FALSE,\"a\",TRUE,\"b\",SEQUENCE(1000000,1000000),1)")));
        Assert.Equal("#N/A", Show(Eval("IFS(FALSE,1)")));
        Assert.Equal("30", Show(Eval("CHOOSE(3,10,20,30,SEQUENCE(1000000,1000000))")));
        Assert.Equal("FALSE", Show(Eval("IF(FALSE,1)")));
        Assert.Equal("0", Show(Eval("IF(TRUE,,1)")));
    }

    [Fact]
    public void LogicAndText()
    {
        Assert.Equal("TRUE", Show(Eval("AND(TRUE,1,\"true\")")));
        Assert.Equal("FALSE", Show(Eval("AND(TRUE,0)")));
        Assert.Equal("TRUE", Show(Eval("OR(FALSE,0,1)")));
        Assert.Equal("#VALUE!", Show(Eval("AND(\"x\")")));
        var r = new FakeResolver().Set("A1", "x").Set("A2", true);
        Assert.Equal("TRUE", Show(Eval("AND(A1:A2)", r)));
        Assert.Equal("#VALUE!", Show(Eval("AND(A1)", r)));
        Assert.Equal("FALSE", Show(Eval("NOT(1)")));
        Assert.Equal("#VALUE!", Show(Eval("NOT(\"x\")")));
        Assert.Equal("\"abc123TRUE\"", Show(Eval("CONCAT(\"abc\",{1,2,3},TRUE)")));
        Assert.Equal("3", Show(Eval("LEN(\"abc\")")));
        Assert.Equal("1", Show(Eval("LEN(5)")));
        Assert.Equal("#N/A", Show(Eval("LEN(#N/A)")));
        Assert.Equal("TRUE", Show(Eval("ISERROR(1/0)")));
        Assert.Equal("FALSE", Show(Eval("ISERROR(1)")));
        Assert.Equal("\"1,234.50\"", Show(Eval("TEXT(1234.5,\"#,##0.00\")")));
        Assert.Equal("\"2020-01-01\"", Show(Eval("TEXT(43831,\"yyyy-mm-dd\")")));
        Assert.Equal("\"50%\"", Show(Eval("TEXT(0.5,\"0%\")")));
        Assert.Equal("\"abc\"", Show(Eval("TEXT(\"abc\",\"0\")")));
    }

    [Fact]
    public void NumbersToTextUseFifteenDigits()
    {
        Assert.Equal("0.1", CalcConvert.NumberToText(0.1));
        Assert.Equal("0.3", CalcConvert.NumberToText(0.1 + 0.2));
        Assert.Equal("1E+20", CalcConvert.NumberToText(1e20));
        Assert.Equal("100000000000000", CalcConvert.NumberToText(1e14));
        Assert.Equal("1000000000000000", CalcConvert.NumberToText(1e15));
        Assert.Equal("123456789012346000", CalcConvert.NumberToText(123456789012345678));
        Assert.Equal("-1234.5", CalcConvert.NumberToText(-1234.5));
        Assert.Equal("0.000123", CalcConvert.NumberToText(0.000123));
        Assert.Equal("0.0000000001", CalcConvert.NumberToText(1e-10));
        Assert.Equal("0", CalcConvert.NumberToText(0));
    }

    [Fact]
    public void ErrorsAndUnknownFunctions()
    {
        Assert.Equal("#NAME?", Show(Eval("NOSUCH(1)")));
        Assert.Equal("#NAME?", Show(Eval("Unknown+1")));
        Assert.Equal("#VALUE!", Show(Eval("NOT()")));
        Assert.Equal("#REF!", Show(Eval("Table1[Col]")));
        Assert.Equal("#REF!", Show(Eval("[1]Sheet1!A1")));
        var r = new FakeResolver();
        r.Names["Rate"] = 0.5;
        Assert.Equal("1", Show(Eval("Rate*2", r)));
    }

    [Fact]
    public void CustomFunctionsPlugIntoTheRegistry()
    {
        FunctionRegistry reg = FunctionRegistry.CreateDefault();
        reg.AddCustom("DOUBLEIT", 1, 1, a => CalcValue.FromNumber(a[0].Number * 2));
        reg.AddCustom("WHEN", 0, 0, _ => CalcValue.FromNumber(7), isVolatile: true);
        var ev = new Evaluator(new FakeResolver(), reg);
        FormulaNode n = FormulaParser.Parse("DOUBLEIT(4)+WHEN()+SUM(DOUBLEIT({1,2}))");
        Assert.Equal("21", Show(ev.Evaluate(n, new CalcCell("Sheet1", 1, 1))));
        Assert.True(ev.UsedVolatile);
        Assert.Equal("#N/A", Show(ev.Evaluate(FormulaParser.Parse("DOUBLEIT(#N/A)"), default)));
    }

    [Fact]
    public void RegistryFamiliesAddFunctionsWithoutTouchingTheEvaluator()
    {
        var reg = new FunctionRegistry();
        reg.Add(FunctionDefinition.Create("TWICE", 1, 1, [ArgKind.Value], (_, a) => CalcValue.FromNumber(a[0].Number * 2)));
        reg.Add(FunctionDefinition.CreateLazy("FIRSTOF", 1, int.MaxValue, (c, a) => c.Evaluate(a[0])));
        var ev = new Evaluator(new FakeResolver(), reg);
        Assert.Equal("{2,4}", Show(ev.Evaluate(FormulaParser.Parse("_xlfn.TWICE({1,2})"), default)));
        Assert.Equal("3", Show(ev.Evaluate(FormulaParser.Parse("FIRSTOF(3,4)"), default)));
        Assert.Equal("#NAME?", Show(ev.Evaluate(FormulaParser.Parse("SUM(1)"), default)));
    }

    [Fact]
    public void TemplateMembersLiftOverCollections()
    {
        var r = new FakeResolver();
        r.Members["items.price"] = CalcValue.FromArray(new CalcArray(3, 1, [1, 2, 3]));
        r.Members["items.qty"] = CalcValue.FromArray(new CalcArray(3, 1, [10, 20, 30]));
        var ev = new Evaluator(r);
        FormulaNode n = FormulaParser.Parse("SUM(items.price*items.qty)", new FormulaParserOptions { TemplateExtensions = true });
        Assert.Equal("140", Show(ev.Evaluate(n, new CalcCell(null, 1, 1))));
    }

    [Fact]
    public void SpillOperatorUsesTheResolver()
    {
        var r = new SpillResolver();
        var ev = new Evaluator(r);
        Assert.Equal("{5;6}", Show(ev.Evaluate(FormulaParser.Parse("A1#", new FormulaParserOptions { FileForm = false }), new CalcCell("Sheet1", 9, 9))));
        Assert.Equal("11", Show(ev.Evaluate(FormulaParser.Parse("SUM(_xlfn.ANCHORARRAY(A1))"), new CalcCell("Sheet1", 9, 9))));
        Assert.Equal("#REF!", Show(ev.Evaluate(FormulaParser.Parse("B1#", new FormulaParserOptions { FileForm = false }), new CalcCell("Sheet1", 9, 9))));
    }

    private sealed class SpillResolver : ICalcResolver
    {
        public CalcValue GetCell(string? sheet, int row, int column) => CalcValue.FromNumber(row == 1 ? 5 : row == 2 ? 6 : 0);

        public bool TryGetSpill(string? sheet, int row, int column, out CellArea area)
        {
            area = new CellArea(sheet, null, 1, 1, 2, 1);
            return column == 1;
        }
    }

    [Fact]
    public void ScalarArithmeticAllocatesNothing()
    {
        var ev = new Evaluator(new FakeResolver());
        FormulaNode n = FormulaParser.Parse("(1+2)*3-4/2+7^2>5");
        var origin = new CalcCell("Sheet1", 1, 1);
        for (int i = 0; i < 1000; i++)
        {
            _ = ev.Evaluate(n, origin);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        {
            _ = ev.Evaluate(n, origin);
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }
}
