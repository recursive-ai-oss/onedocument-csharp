using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>The formula parser, printer and evaluator under NativeAOT and WASM: no reflection, so the explicit function registry must be enough.</summary>
internal static class CalcSmoke
{
    private sealed class Grid : ICalcResolver
    {
        public CalcValue GetCell(string? sheet, int row, int column) => CalcValue.FromNumber((row * 10) + column);
    }

    public static List<string> Run()
    {
        var failures = new List<string>();
        void Check(string formula, string expected)
        {
            FormulaNode n = FormulaParser.Parse(formula, new FormulaParserOptions { FileForm = false });
            CalcValue v = new Evaluator(new Grid()).Evaluate(n, new CalcCell("Sheet1", 1, 1));
            string actual = v.Kind == ValueKind.Array ? $"array {v.Array!.Rows}x{v.Array.Columns}" : CalcConvert.ToText(v);
            if (actual != expected)
            {
                failures.Add($"calc {formula}: '{actual}' != '{expected}'");
            }
        }

        Check("SUM(A1:B2)", "66"); // 11 + 12 + 21 + 22
        Check("LET(x,2,x*LAMBDA(a,a+1)(3))", "8");
        Check("SUM(MAP(SEQUENCE(3),LAMBDA(v,v*v)))", "14");
        Check("SEQUENCE(2,3)", "array 2x3");
        Check("TEXT(1234.5,\"#,##0.00\")", "1,234.50");
        Check("IFERROR(1/0,\"e\")", "e");
        Check("ROUND(2.675,2)+MOD(-3,2)", "3.68");
        Check("SUMIF(A1:A3,\">11\")", "52"); // 21 + 31
        Check("TEXTJOIN(\"-\",TRUE,\"a\",\"\",\"b\")", "a-b");
        Check("REGEXREPLACE(\"a1b2\",\"[0-9]\",\"#\")", "a#b#");
        Check("MMULT({1,2;3,4},{5;6})", "array 2x1");
        Check("ROMAN(2013)&SUBSTITUTE(\"x\",\"x\",\"y\")", "MMXIIIy");
        Check("XLOOKUP(2,{1,2,3},{10,20,30})", "20");
        Check("SORT({3;1;2})", "array 3x1");
        Check("REDUCE(0,{1,2,3},SUM)", "6");
        Check("INDIRECT(\"B2\")", "22");
        Check("GROUPBY({\"a\";\"b\";\"a\"},{1;2;3},SUM,0,0)", "array 2x2");
        Check("ROUND(NORM.S.INV(0.975),6)", "1.959964");
        Check("ROUND(T.DIST.2T(2.228139,10),5)", "0.05");
        Check("ROUND(CHISQ.INV.RT(0.05,1),5)", "3.84146");
        Check("MEDIAN(A1:B2)+STDEV.S({1,2,3})", "17.5");
        Check("INDEX(LINEST({1;9;5;7},{0;4;2;3}),1,2)", "1");
        Check("DATE(2008,14,2)", "39846");
        Check("DATEDIF(DATE(2001,1,1),DATE(2003,1,1),\"Y\")&WEEKDAY(DATE(2008,2,14))", "25");
        Check("NETWORKDAYS(DATE(2012,10,1),DATE(2013,3,1))", "110");
        Check("DATEVALUE(\"22-AUG-2011\")", "40777");
        string printed = FormulaPrinter.Print(FormulaParser.Parse("_xlfn.SEQUENCE(2)+'S 1'!$A$1"));
        if (printed != "_xlfn.SEQUENCE(2)+'S 1'!$A$1")
        {
            failures.Add("calc print " + printed);
        }

        return failures;
    }
}
