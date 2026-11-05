using RecursiveAI.OneOffice.Calc;

namespace RecursiveAI.OneOffice.Tests.Calc;

/// <summary>Lookup, reference, dynamic-array and LAMBDA-helper functions against the behaviour Microsoft's function reference documents.</summary>
public class LookupFunctionTests
{
    private static FakeResolver Grid()
    {
        var g = new FakeResolver();
        // A1:C4: a small table; D1:D3 a descending column; F1:F3 a column with a gap.
        string[] names = ["apple", "banana", "cherry", "date"];
        for (int i = 0; i < 4; i++)
        {
            g.Set($"A{i + 1}", names[i]);
            g.Set($"B{i + 1}", (i + 1) * 10.0);
            g.Set($"C{i + 1}", (i % 2) == 0);
        }

        g.Set("D1", 30.0).Set("D2", 20.0).Set("D3", 10.0);
        g.Set("F1", "x").Set("F3", "z");
        return g;
    }

    [Theory]
    // MATCH
    [InlineData("MATCH(3,{1,2,3,4},0)", "3")]
    [InlineData("MATCH(3.5,{1,2,3,4})", "3")]
    [InlineData("MATCH(0,{1,2,3,4})", "#N/A")]
    [InlineData("MATCH(25,{40,30,20,10},-1)", "2")]
    [InlineData("MATCH(\"b*\",{\"apple\",\"banana\"},0)", "2")]
    [InlineData("MATCH(\"B?NANA\",{\"apple\",\"banana\"},0)", "2")]
    [InlineData("MATCH(\"~*\",{\"a\",\"*\"},0)", "2")]
    [InlineData("MATCH(\"1\",{1,2},0)", "#N/A")]
    [InlineData("MATCH(1,{1,2;3,4},0)", "#N/A")]
    [InlineData("MATCH(2,{5;2;2},0)", "2")]
    [InlineData("MATCH(B2,B1:B4,0)", "2")]
    // VLOOKUP / HLOOKUP
    [InlineData("VLOOKUP(\"cherry\",A1:C4,2,FALSE)", "30")]
    [InlineData("VLOOKUP(\"Cherry\",A1:C4,3,0)", "TRUE")]
    [InlineData("VLOOKUP(\"cherry\",A1:C4,4,FALSE)", "#REF!")]
    [InlineData("VLOOKUP(\"cherry\",A1:C4,0,FALSE)", "#VALUE!")]
    [InlineData("VLOOKUP(\"zzz\",A1:C4,2,FALSE)", "#N/A")]
    [InlineData("VLOOKUP(\"c\",A1:C4,2)", "20")]
    [InlineData("VLOOKUP(\"a\",A1:C4,2)", "#N/A")]
    [InlineData("VLOOKUP(\"*e\",A1:C4,2,FALSE)", "10")]
    [InlineData("HLOOKUP(2,{1,2,3;\"a\",\"b\",\"c\"},2,FALSE)", "\"b\"")]
    [InlineData("HLOOKUP(2.5,{1,2,3;\"a\",\"b\",\"c\"},2)", "\"b\"")]
    // LOOKUP
    [InlineData("LOOKUP(2.5,{1,2,3},{\"a\",\"b\",\"c\"})", "\"b\"")]
    [InlineData("LOOKUP(0,{1,2,3},{\"a\",\"b\",\"c\"})", "#N/A")]
    [InlineData("LOOKUP(\"b\",{\"a\",\"b\",\"c\";1,2,3})", "2")]
    [InlineData("LOOKUP(3,{1,2,3;\"a\",\"b\",\"c\"})", "\"c\"")]
    // XMATCH
    [InlineData("XMATCH(3,{1,2,3,4})", "3")]
    [InlineData("XMATCH(3,{3,3,3},0,-1)", "3")]
    [InlineData("XMATCH(3,{3,3,3},0,1)", "1")]
    [InlineData("XMATCH(3.5,{1,2,3,4},-1)", "3")]
    [InlineData("XMATCH(3.5,{1,2,3,4},1)", "4")]
    [InlineData("XMATCH(5,{1,2,3,4},1)", "#N/A")]
    [InlineData("XMATCH(\"a*\",{\"b\",\"abc\"},2)", "2")]
    [InlineData("XMATCH(\"a*\",{\"b\",\"abc\"},0)", "#N/A")]
    [InlineData("XMATCH(3,{1,2,3,4,5},0,2)", "3")]
    [InlineData("XMATCH(3,{5,4,3,2,1},0,-2)", "3")]
    [InlineData("XMATCH(3.5,{1,2,3,4,5},-1,2)", "3")]
    [InlineData("XMATCH(3.5,{1,2,3,4,5},1,2)", "4")]
    [InlineData("XMATCH(3,{1,2},3)", "#VALUE!")]
    [InlineData("XMATCH(3,{1,2},0,3)", "#VALUE!")]
    [InlineData("XMATCH(3,{1,2;3,4})", "#VALUE!")]
    [InlineData("XMATCH({2,4},{1,2,3,4})", "{2,4}")]
    // XLOOKUP
    [InlineData("XLOOKUP(2,{1,2,3},{\"a\",\"b\",\"c\"})", "\"b\"")]
    [InlineData("XLOOKUP(9,{1,2,3},{\"a\",\"b\",\"c\"})", "#N/A")]
    [InlineData("XLOOKUP(9,{1,2,3},{\"a\",\"b\",\"c\"},\"none\")", "\"none\"")]
    [InlineData("XLOOKUP(2.5,{1,2,3},{\"a\",\"b\",\"c\"},,-1)", "\"b\"")]
    [InlineData("XLOOKUP(2.5,{1,2,3},{\"a\",\"b\",\"c\"},,1)", "\"c\"")]
    [InlineData("XLOOKUP(2,{1,2,3},{\"a\",\"b\"})", "#VALUE!")]
    [InlineData("XLOOKUP(2,{1;2;3},{10,20;30,40;50,60})", "{30,40}")]
    [InlineData("XLOOKUP(2,{1,2,3},{10,20,30;40,50,60})", "{20;50}")]
    [InlineData("XLOOKUP(2,{1,2,3},{10,20,30;40,50,60;70,80,90})", "{20;50;80}")]
    [InlineData("XLOOKUP(\"BANANA\",A1:A4,B1:B4)", "20")]
    [InlineData("XLOOKUP(\"c*\",A1:A4,B1:B4,,2)", "30")]
    [InlineData("SUM(XLOOKUP(\"banana\",A1:A4,B1:B4):B4)", "90")]
    // CHOOSECOLS / CHOOSEROWS
    [InlineData("CHOOSECOLS({1,2,3;4,5,6},1,3)", "{1,3;4,6}")]
    [InlineData("CHOOSECOLS({1,2,3;4,5,6},-1)", "{3;6}")]
    [InlineData("CHOOSECOLS({1,2,3;4,5,6},4)", "#VALUE!")]
    [InlineData("CHOOSECOLS({1,2,3;4,5,6},0)", "#VALUE!")]
    [InlineData("CHOOSEROWS({1,2;3,4;5,6},{1,3})", "{1,2;5,6}")]
    [InlineData("CHOOSEROWS({1,2;3,4;5,6},-1,1)", "{5,6;1,2}")]
    // reference functions
    [InlineData("ROW(B3)", "3")]
    [InlineData("ROW(B3:B5)", "{3;4;5}")]
    [InlineData("COLUMN(C1:E1)", "{3,4,5}")]
    [InlineData("COLUMN(C1:E3)", "{3,4,5}")]
    [InlineData("ROW()", "1")]
    [InlineData("COLUMN()", "1")]
    [InlineData("ROW({1,2})", "#VALUE!")]
    [InlineData("AREAS(A1:B2)", "1")]
    [InlineData("AREAS((A1:B2,D1))", "2")]
    [InlineData("AREAS(5)", "#VALUE!")]
    [InlineData("ADDRESS(2,3)", "\"$C$2\"")]
    [InlineData("ADDRESS(2,3,4)", "\"C2\"")]
    [InlineData("ADDRESS(2,3,2)", "\"C$2\"")]
    [InlineData("ADDRESS(2,3,3)", "\"$C2\"")]
    [InlineData("ADDRESS(2,3,1,FALSE)", "\"R2C3\"")]
    [InlineData("ADDRESS(2,3,4,FALSE)", "\"R[2]C[3]\"")]
    [InlineData("ADDRESS(2,3,2,FALSE)", "\"R2C[3]\"")]
    [InlineData("ADDRESS(2,3,1,TRUE,\"Sheet2\")", "\"Sheet2!$C$2\"")]
    [InlineData("ADDRESS(2,3,1,TRUE,\"My Sheet\")", "\"'My Sheet'!$C$2\"")]
    [InlineData("ADDRESS(16384,1048576)", "#VALUE!")]
    [InlineData("ADDRESS(1048576,16384)", "\"$XFD$1048576\"")]
    [InlineData("ADDRESS(0,1)", "#VALUE!")]
    [InlineData("ADDRESS(1,1,5)", "#VALUE!")]
    [InlineData("SUM(OFFSET(B1,1,0,2,1))", "50")]
    [InlineData("OFFSET(B1,1,1)", "FALSE")]
    [InlineData("OFFSET(B1,-1,0)", "#REF!")]
    [InlineData("OFFSET(B1,0,0,0,1)", "#REF!")]
    [InlineData("OFFSET(5,1,1)", "#VALUE!")]
    [InlineData("SUM(OFFSET(B3,0,0,-2,1))", "50")]
    [InlineData("SUM(OFFSET(B1:B2,1,0))", "50")]
    [InlineData("INDIRECT(\"B2\")", "20")]
    [InlineData("SUM(INDIRECT(\"B1:B3\"))", "60")]
    [InlineData("INDIRECT(\"R2C2\",FALSE)", "20")]
    [InlineData("INDIRECT(\"R[1]C[1]\",FALSE)", "20")]
    [InlineData("INDIRECT(\"Sheet1!A1\")", "\"apple\"")]
    [InlineData("INDIRECT(\"nonsense(\")", "#REF!")]
    [InlineData("INDIRECT(\"\")", "#REF!")]
    [InlineData("INDIRECT(\"5\")", "#REF!")]
    [InlineData("INDIRECT(\"B2+1\")", "#REF!")]
    [InlineData("TRANSPOSE({1,2,3})", "{1;2;3}")]
    [InlineData("TRANSPOSE({1,2;3,4;5,6})", "{1,3,5;2,4,6}")]
    [InlineData("TRANSPOSE(7)", "7")]
    [InlineData("HYPERLINK(\"http://x\",\"click\")", "\"click\"")]
    [InlineData("HYPERLINK(\"http://x\")", "\"http://x\"")]
    [InlineData("GETPIVOTDATA(\"Sales\",A1)", "#REF!")]
    [InlineData("ROWS(TRIMRANGE(F1:F5))", "3")]
    [InlineData("ROWS(TRIMRANGE(F1:F5,1))", "5")]
    [InlineData("ROWS(TRIMRANGE(F1:F5,2))", "3")]
    [InlineData("ROWS(TRIMRANGE(F1:F5,0))", "5")]
    public void LookupAndReference(string formula, string expected)
    {
        Assert.Equal(expected, CalcTestHelper.Show(CalcTestHelper.Eval(formula, Grid())));
    }

    [Theory]
    [InlineData("FILTER({1,2;3,4;5,6},{TRUE;FALSE;TRUE})", "{1,2;5,6}")]
    [InlineData("FILTER({1,2,3;4,5,6},{TRUE,FALSE,TRUE})", "{1,3;4,6}")]
    [InlineData("FILTER({1;2;3},{1;0;2})", "{1;3}")]
    [InlineData("FILTER({1;2;3},{FALSE;FALSE;FALSE})", "#CALC!")]
    [InlineData("FILTER({1;2;3},{FALSE;FALSE;FALSE},\"none\")", "\"none\"")]
    [InlineData("FILTER({1;2;3},{TRUE;FALSE})", "#VALUE!")]
    [InlineData("FILTER({1;2;3},{\"a\";TRUE;TRUE})", "#VALUE!")]
    [InlineData("FILTER({1;2;3},{TRUE;#N/A;TRUE})", "#N/A")]
    [InlineData("SORT({3;1;2})", "{1;2;3}")]
    [InlineData("SORT({3;1;2},1,-1)", "{3;2;1}")]
    [InlineData("SORT({3,1,2},1,1,TRUE)", "{1,2,3}")]
    [InlineData("SORT({2,\"b\";1,\"a\";2,\"a\"},1)", "{1,\"a\";2,\"b\";2,\"a\"}")]
    [InlineData("SORT({2,\"b\";1,\"a\";2,\"a\"},{1,2},{1,1})", "{1,\"a\";2,\"a\";2,\"b\"}")]
    [InlineData("SORT({2,\"b\";1,\"a\";2,\"a\"},{1,2},{-1,1})", "{2,\"a\";2,\"b\";1,\"a\"}")]
    [InlineData("SORT({\"b\";TRUE;3;\"a\";FALSE;#N/A})", "{3;\"a\";\"b\";FALSE;TRUE;#N/A}")]
    [InlineData("SORT({\"b\";TRUE;3;\"a\";FALSE;#N/A},1,-1)", "{#N/A;TRUE;FALSE;\"b\";\"a\";3}")]
    [InlineData("SORT({\"B\";\"a\";\"C\"})", "{\"a\";\"B\";\"C\"}")]
    [InlineData("SORT({1;2},3)", "#VALUE!")]
    [InlineData("SORT({1;2},1,0)", "#VALUE!")]
    [InlineData("SORTBY({\"a\";\"b\";\"c\"},{3;1;2})", "{\"b\";\"c\";\"a\"}")]
    [InlineData("SORTBY({\"a\";\"b\";\"c\"},{3;1;2},-1)", "{\"a\";\"c\";\"b\"}")]
    [InlineData("SORTBY({\"a\";\"b\";\"c\"},{1;1;0},1,{3;2;1},1)", "{\"c\";\"b\";\"a\"}")]
    [InlineData("SORTBY({\"a\";\"b\";\"c\"},{3;1})", "#VALUE!")]
    [InlineData("SORTBY({\"a\",\"b\",\"c\"},{3,1,2})", "{\"b\",\"c\",\"a\"}")]
    [InlineData("UNIQUE({1;2;1;3;2})", "{1;2;3}")]
    [InlineData("UNIQUE({1;2;1;3;2},FALSE,TRUE)", "{3}")]
    [InlineData("UNIQUE({\"a\";\"A\";\"b\"})", "{\"a\";\"b\"}")]
    [InlineData("UNIQUE({1,2;1,2;3,4})", "{1,2;3,4}")]
    [InlineData("UNIQUE({1,1,2;3,3,4},TRUE)", "{1,2;3,4}")]
    [InlineData("UNIQUE({1;1},FALSE,TRUE)", "#CALC!")]
    [InlineData("UNIQUE({1;\"1\"})", "{1;\"1\"}")]
    [InlineData("TAKE({1,2,3;4,5,6;7,8,9},2)", "{1,2,3;4,5,6}")]
    [InlineData("TAKE({1,2,3;4,5,6;7,8,9},-1)", "{7,8,9}")]
    [InlineData("TAKE({1,2,3;4,5,6;7,8,9},,2)", "{1,2;4,5;7,8}")]
    [InlineData("TAKE({1,2,3;4,5,6;7,8,9},-2,-2)", "{5,6;8,9}")]
    [InlineData("TAKE({1,2;3,4},10)", "{1,2;3,4}")]
    [InlineData("TAKE({1,2;3,4},0)", "#CALC!")]
    [InlineData("DROP({1,2,3;4,5,6;7,8,9},1)", "{4,5,6;7,8,9}")]
    [InlineData("DROP({1,2,3;4,5,6;7,8,9},-1,1)", "{2,3;5,6}")]
    [InlineData("DROP({1,2;3,4},5)", "#CALC!")]
    [InlineData("EXPAND({1,2;3,4},3,3)", "{1,2,#N/A;3,4,#N/A;#N/A,#N/A,#N/A}")]
    [InlineData("EXPAND({1,2;3,4},3,,0)", "{1,2;3,4;0,0}")]
    [InlineData("EXPAND({1,2;3,4},1,2)", "#VALUE!")]
    [InlineData("HSTACK({1;2},{3;4})", "{1,3;2,4}")]
    [InlineData("HSTACK({1;2;3},{4,5})", "{1,4,5;2,#N/A,#N/A;3,#N/A,#N/A}")]
    [InlineData("VSTACK({1,2},{3})", "{1,2;3,#N/A}")]
    [InlineData("VSTACK({1,2},3,{4,5})", "{1,2;3,#N/A;4,5}")]
    [InlineData("VSTACK(#N/A,{1})", "#N/A")]
    [InlineData("TOCOL({1,2;3,4})", "{1;2;3;4}")]
    [InlineData("TOCOL({1,2;3,4},0,TRUE)", "{1;3;2;4}")]
    [InlineData("TOROW({1,2;3,4})", "{1,2,3,4}")]
    [InlineData("TOROW({1,#N/A;3,4},2)", "{1,3,4}")]
    [InlineData("TOROW({#N/A},2)", "#CALC!")]
    [InlineData("TOROW({1,2},5)", "#VALUE!")]
    [InlineData("WRAPROWS({1,2,3,4,5},2)", "{1,2;3,4;5,#N/A}")]
    [InlineData("WRAPROWS({1,2,3,4,5},2,0)", "{1,2;3,4;5,0}")]
    [InlineData("WRAPCOLS({1,2,3,4,5},2)", "{1,3,5;2,4,#N/A}")]
    [InlineData("WRAPCOLS({1;2;3},3)", "{1;2;3}")]
    [InlineData("WRAPROWS({1,2;3,4},2)", "#VALUE!")]
    [InlineData("WRAPROWS({1,2,3},0)", "#NUM!")]
    [InlineData("ROWS(RANDARRAY(3,2))", "3")]
    [InlineData("COLUMNS(RANDARRAY(3,2))", "2")]
    [InlineData("RANDARRAY(1,1,5,5,TRUE)", "{5}")]
    [InlineData("RANDARRAY(1,1,5,4)", "#VALUE!")]
    [InlineData("RANDARRAY(0)", "#VALUE!")]
    [InlineData("RANDARRAY(1,1,1.5,3,TRUE)", "#VALUE!")]
    public void DynamicArrays(string formula, string expected)
    {
        Assert.Equal(expected, CalcTestHelper.Show(CalcTestHelper.Eval(formula, Grid())));
    }

    [Theory]
    [InlineData("MAP({1,2,3},LAMBDA(x,x*2))", "{2,4,6}")]
    [InlineData("MAP({\"a\",\"bb\"},LEN)", "{1,2}")]
    [InlineData("MAP({1,2},{10,20},LAMBDA(a,b,a+b))", "{11,22}")]
    [InlineData("MAP({1,2},LAMBDA(x,{1,2}))", "#CALC!")]
    [InlineData("REDUCE(0,{1,2,3},LAMBDA(a,v,a+v))", "6")]
    [InlineData("REDUCE(,{1,2,3},LAMBDA(a,v,a+v))", "6")]
    [InlineData("REDUCE(10,{1,2,3},SUM)", "16")]
    [InlineData("REDUCE(\"\",{\"a\",\"b\"},LAMBDA(a,v,a&v))", "\"ab\"")]
    [InlineData("REDUCE(0,{1,2},LAMBDA(a,v,{1,2}))", "{1,2}")]
    [InlineData("REDUCE(0,{1,2},LAMBDA(a,v,1/0))", "#DIV/0!")]
    [InlineData("REDUCE(0,{1,2},LAMBDA(a,1))", "#VALUE!")]
    [InlineData("SCAN(0,{1,2,3},LAMBDA(a,v,a+v))", "{1,3,6}")]
    [InlineData("SCAN(,{1,2,3},LAMBDA(a,v,a+v))", "{1,3,6}")]
    [InlineData("SCAN(0,{1,2;3,4},LAMBDA(a,v,a+v))", "{1,3;6,10}")]
    [InlineData("SCAN(0,{1,2},LAMBDA(a,v,{1,2}))", "#CALC!")]
    [InlineData("BYROW({1,2;3,4},LAMBDA(r,SUM(r)))", "{3;7}")]
    [InlineData("BYCOL({1,2;3,4},LAMBDA(c,SUM(c)))", "{4,6}")]
    [InlineData("BYROW({1,2;3,4},SUM)", "{3;7}")]
    [InlineData("BYROW({1,2;3,4},LAMBDA(r,r))", "#CALC!")]
    [InlineData("BYROW({1,2},LAMBDA(a,b,1))", "#VALUE!")]
    [InlineData("MAKEARRAY(2,3,LAMBDA(r,c,r*10+c))", "{11,12,13;21,22,23}")]
    [InlineData("MAKEARRAY(0,1,LAMBDA(r,c,1))", "#VALUE!")]
    [InlineData("MAKEARRAY(1,1,5)", "#VALUE!")]
    [InlineData("LAMBDA(x,ISOMITTED(x))(1)", "FALSE")]
    [InlineData("GROUPBY({\"a\";\"b\";\"a\"},{1;2;3},SUM,0,0)", "{\"a\",4;\"b\",2}")]
    [InlineData("GROUPBY({\"a\";\"b\";\"a\"},{1;2;3},SUM,0,1)", "{\"a\",4;\"b\",2;\"Total\",6}")]
    [InlineData("GROUPBY({\"a\";\"b\";\"a\"},{1;2;3},SUM,0,-1)", "{\"Total\",6;\"a\",4;\"b\",2}")]
    [InlineData("GROUPBY({\"b\";\"a\";\"b\"},{1;2;3},SUM,0,0,-2)", "{\"b\",4;\"a\",2}")]
    [InlineData("GROUPBY({\"b\";\"a\";\"b\"},{1;2;3},LAMBDA(v,ROWS(v)),0,0)", "{\"a\",1;\"b\",2}")]
    [InlineData("GROUPBY({\"b\";\"a\";\"b\"},{1;2;3},LAMBDA(v,SUM(v)*2),0,0)", "{\"a\",4;\"b\",8}")]
    [InlineData("GROUPBY({\"b\";\"a\";\"b\"},{1;2;3},SUM,0,0,0,{TRUE;FALSE;TRUE})", "{\"b\",4}")]
    [InlineData("GROUPBY({\"k\";\"a\";\"b\"},{\"v\";2;3},SUM,3,0)", "{\"k\",\"v\";\"a\",2;\"b\",3}")]
    [InlineData("GROUPBY({\"a\";\"b\";\"a\"},{1;1;2},PERCENTOF,0,0)", "{\"a\",0.75;\"b\",0.25}")]
    [InlineData("PERCENTOF({1,2},{1,2,3,4})", "0.3")]
    [InlineData("PERCENTOF(1,0)", "#DIV/0!")]
    [InlineData("PIVOTBY({\"a\";\"b\";\"a\"},{\"x\";\"x\";\"y\"},{1;2;3},SUM,0)", "{\"\",\"x\",\"y\",\"Total\";\"a\",1,3,4;\"b\",2,\"\",2;\"Total\",3,3,6}")]
    public void LambdaHelpers(string formula, string expected)
    {
        Assert.Equal(expected, CalcTestHelper.Show(CalcTestHelper.Eval(formula, Grid())));
    }

    [Fact]
    public void RandArraySeedRepeats()
    {
        var a = new EvaluatorOptions { RandomSeed = 7 };
        string x = CalcTestHelper.Show(CalcTestHelper.Eval("RANDARRAY(2,2)", Grid(), a));
        string y = CalcTestHelper.Show(CalcTestHelper.Eval("RANDARRAY(2,2)", Grid(), a));
        string z = CalcTestHelper.Show(CalcTestHelper.Eval("RANDARRAY(2,2)", Grid(), new EvaluatorOptions { RandomSeed = 8 }));
        Assert.Equal(x, y);
        Assert.NotEqual(x, z);
    }

    [Fact]
    public void LookupAcrossOneMillionRowsDoesNotBoxPerElement()
    {
        var cells = new CalcValue[1_000_000];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = CalcValue.FromNumber(i);
        }

        var arr = new CalcArray(cells.Length, 1, cells);
        _ = arr;
        var res = new FakeResolver();
        res.Names["big"] = CalcValue.FromArray(arr);
        // Binary search touches ~20 cells: well under a kilobyte per call once warmed up.
        Assert.Equal("500001", CalcTestHelper.Show(CalcTestHelper.Eval("MATCH(500000,big,1)", res)));
        // Building the default registry allocates per function, so it stays outside the measured call.
        var registry = FunctionRegistry.CreateDefault();
        FormulaNode tree = FormulaParser.Parse("MATCH(500000,big,1)", new FormulaParserOptions { FileForm = false });
        var ev = new Evaluator(res, registry);
        _ = ev.Evaluate(tree, new CalcCell("Sheet1", 1, 1));
        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = ev.Evaluate(tree, new CalcCell("Sheet1", 1, 1));
        long used = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(used < 64 * 1024, $"allocated {used} bytes");
    }

    [Fact]
    public void ArrayFunctionsChargeForTheCellsTheyTouch()
    {
        var options = new EvaluatorOptions { MaxSteps = 5_000 };
        Assert.Throws<OfficeLimitException>(() => CalcTestHelper.Eval("SORT(SEQUENCE(1000,10))", options: options));
        Assert.Equal("{1;2;3}", CalcTestHelper.Show(CalcTestHelper.Eval("SORT({3;1;2})", options: options)));
    }
}
