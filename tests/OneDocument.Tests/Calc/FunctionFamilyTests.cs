using System.Globalization;
using RecursiveAI.OneDocument.Calc;
using static RecursiveAI.OneDocument.Tests.Calc.CalcTestHelper;

namespace RecursiveAI.OneDocument.Tests.Calc;

/// <summary>Behaviour that Microsoft's function reference states for the math, logical, information and text families (examples and error rules).</summary>
public class FunctionFamilyTests
{
    private static void Check(string formula, string expected, FakeResolver? res = null)
    {
        CalcValue v = Eval(formula, res);
        string actual = Show(v);
        if (v.Kind == ValueKind.Number && double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out double want))
        {
            Assert.True(Math.Abs(v.Number - want) <= Math.Abs(want) * 1e-13, $"{formula} gave {actual}, wanted {expected}");
            return;
        }

        Assert.True(expected == actual, $"{formula} gave {actual}, wanted {expected}");
    }

    [Theory]
    [InlineData("ROUND(2.15,1)", "2.2")]
    [InlineData("ROUND(2.149,1)", "2.1")]
    [InlineData("ROUND(-1.475,2)", "-1.48")]
    [InlineData("ROUND(21.5,-1)", "20")]
    [InlineData("ROUND(626.3,-3)", "1000")]
    [InlineData("ROUND(1.98,-1)", "0")]
    [InlineData("ROUND(-50.55,-2)", "-100")]
    [InlineData("ROUND(2.675,2)", "2.68")]
    [InlineData("ROUNDDOWN(3.2,0)", "3")]
    [InlineData("ROUNDDOWN(76.9,0)", "76")]
    [InlineData("ROUNDDOWN(3.14159,3)", "3.141")]
    [InlineData("ROUNDDOWN(-3.14159,1)", "-3.1")]
    [InlineData("ROUNDDOWN(31415.92654,-2)", "31400")]
    [InlineData("ROUNDUP(3.2,0)", "4")]
    [InlineData("ROUNDUP(76.9,0)", "77")]
    [InlineData("ROUNDUP(3.14159,3)", "3.142")]
    [InlineData("ROUNDUP(-3.14159,1)", "-3.2")]
    [InlineData("ROUNDUP(31415.92654,-2)", "31500")]
    [InlineData("TRUNC(8.9)", "8")]
    [InlineData("TRUNC(-8.9)", "-8")]
    [InlineData("TRUNC(0.45)", "0")]
    [InlineData("TRUNC(3.14159,2)", "3.14")]
    [InlineData("INT(8.9)", "8")]
    [InlineData("INT(-8.9)", "-9")]
    [InlineData("MOD(3,2)", "1")]
    [InlineData("MOD(-3,2)", "1")]
    [InlineData("MOD(3,-2)", "-1")]
    [InlineData("MOD(-3,-2)", "-1")]
    [InlineData("MOD(3,0)", "#DIV/0!")]
    [InlineData("QUOTIENT(5,2)", "2")]
    [InlineData("QUOTIENT(4.5,3.1)", "1")]
    [InlineData("QUOTIENT(-10,3)", "-3")]
    [InlineData("QUOTIENT(1,0)", "#DIV/0!")]
    [InlineData("MROUND(10,3)", "9")]
    [InlineData("MROUND(-10,-3)", "-9")]
    [InlineData("MROUND(1.3,0.2)", "1.4")]
    [InlineData("MROUND(5,-2)", "#NUM!")]
    [InlineData("CEILING(2.5,1)", "3")]
    [InlineData("CEILING(-2.5,-2)", "-4")]
    [InlineData("CEILING(-2.5,2)", "-2")]
    [InlineData("CEILING(1.5,0.1)", "1.5")]
    [InlineData("CEILING(0.234,0.01)", "0.24")]
    [InlineData("CEILING(2,-1)", "#NUM!")]
    [InlineData("FLOOR(3.7,2)", "2")]
    [InlineData("FLOOR(-2.5,-2)", "-2")]
    [InlineData("FLOOR(2.5,-2)", "#NUM!")]
    [InlineData("FLOOR(1.58,0.1)", "1.5")]
    [InlineData("FLOOR(0.234,0.01)", "0.23")]
    [InlineData("FLOOR(3,0)", "#DIV/0!")]
    [InlineData("CEILING.MATH(6.3)", "7")]
    [InlineData("CEILING.MATH(-5.5,2,0)", "-4")]
    [InlineData("CEILING.MATH(-5.5,2,-1)", "-6")]
    [InlineData("CEILING.MATH(24.3,5)", "25")]
    [InlineData("FLOOR.MATH(24.3,5)", "20")]
    [InlineData("FLOOR.MATH(6.7)", "6")]
    [InlineData("FLOOR.MATH(-8.1,2)", "-10")]
    [InlineData("FLOOR.MATH(-5.5,2,-1)", "-4")]
    [InlineData("CEILING.PRECISE(4.3)", "5")]
    [InlineData("CEILING.PRECISE(-4.3)", "-4")]
    [InlineData("CEILING.PRECISE(4.3,-2)", "6")]
    [InlineData("CEILING.PRECISE(-4.3,2)", "-4")]
    [InlineData("ISO.CEILING(4.3,-2)", "6")]
    [InlineData("FLOOR.PRECISE(-3.2,-1)", "-4")]
    [InlineData("FLOOR.PRECISE(3.2,1)", "3")]
    [InlineData("FLOOR.PRECISE(3.2,-1)", "3")]
    [InlineData("EVEN(1.5)", "2")]
    [InlineData("EVEN(3)", "4")]
    [InlineData("EVEN(2)", "2")]
    [InlineData("EVEN(-1)", "-2")]
    [InlineData("ODD(1.5)", "3")]
    [InlineData("ODD(3)", "3")]
    [InlineData("ODD(2)", "3")]
    [InlineData("ODD(-1)", "-1")]
    [InlineData("ODD(-2)", "-3")]
    [InlineData("ODD(0)", "1")]
    public void RoundingFamily(string formula, string expected) => Check(formula, expected);

    [Theory]
    [InlineData("ABS(-4)", "4")]
    [InlineData("SIGN(10)", "1")]
    [InlineData("SIGN(4-4)", "0")]
    [InlineData("SIGN(-0.00001)", "-1")]
    [InlineData("SQRT(16)", "4")]
    [InlineData("SQRT(-16)", "#NUM!")]
    [InlineData("SQRT(ABS(-16))", "4")]
    [InlineData("SQRTPI(1)", "1.7724538509055159")]
    [InlineData("SQRTPI(2)", "2.5066282746310002")]
    [InlineData("POWER(5,2)", "25")]
    [InlineData("POWER(98.6,3.2)", "2401077.2220695773")]
    [InlineData("POWER(4,5/4)", "5.656854249492381")]
    [InlineData("POWER(0,0)", "#NUM!")]
    [InlineData("EXP(1)", "2.718281828459045")]
    [InlineData("EXP(2)", "7.38905609893065")]
    [InlineData("LN(86)", "4.454347296253507")]
    [InlineData("LN(0)", "#NUM!")]
    [InlineData("LOG(10)", "1")]
    [InlineData("LOG(8,2)", "3")]
    [InlineData("LOG(86,EXP(1))", "4.454347296253507")]
    [InlineData("LOG(5,1)", "#DIV/0!")]
    [InlineData("LOG10(86)", "1.9344984512435677")]
    [InlineData("LOG10(10)", "1")]
    [InlineData("LOG10(-1)", "#NUM!")]
    [InlineData("PI()", "3.141592653589793")]
    [InlineData("DEGREES(PI())", "180")]
    [InlineData("RADIANS(270)", "4.71238898038469")]
    [InlineData("ACOS(-0.5)", "2.0943951023931957")]
    [InlineData("ACOS(2)", "#NUM!")]
    [InlineData("ASIN(-0.5)", "-0.5235987755982989")]
    [InlineData("ASIN(2)", "#NUM!")]
    [InlineData("ATAN(1)", "0.7853981633974483")]
    [InlineData("ATAN2(1,1)", "0.7853981633974483")]
    [InlineData("ATAN2(-1,-1)", "-2.356194490192345")]
    [InlineData("ATAN2(0,0)", "#DIV/0!")]
    [InlineData("ASINH(10)", "2.99822295029797")]
    [InlineData("ACOSH(1)", "0")]
    [InlineData("ACOSH(0.5)", "#NUM!")]
    [InlineData("ATANH(-0.1)", "-0.10033534773107562")]
    [InlineData("ATANH(1)", "#NUM!")]
    [InlineData("ACOT(2)", "0.4636476090008061")]
    [InlineData("ACOT(0)", "1.5707963267948966")]
    [InlineData("ACOTH(6)", "0.16823611831060645")]
    [InlineData("ACOTH(0.5)", "#NUM!")]
    [InlineData("COTH(2)", "1.0373147207275482")]
    [InlineData("COT(0)", "#DIV/0!")]
    [InlineData("CSC(15)", "1.5377805615408537")]
    [InlineData("CSCH(1.5)", "0.46964244059522")]
    [InlineData("SEC(45)", "1.9035944074044246")]
    [InlineData("SECH(1)", "0.6480542736638855")]
    [InlineData("SIN(PI()/2)", "1")]
    [InlineData("COS(0)", "1")]
    [InlineData("TAN(0.785398163)", "0.9999999992051033")]
    [InlineData("SINH(1)", "1.1752011936438014")]
    [InlineData("COSH(4)", "27.308232836016487")]
    [InlineData("TANH(0.5)", "0.46211715726000974")]
    [InlineData("\"3\"+SIN(0)", "3")]
    [InlineData("SQRT(\"x\")", "#VALUE!")]
    public void ArithmeticPowersAndTrig(string formula, string expected) => Check(formula, expected);

    [Theory]
    [InlineData("FACT(5)", "120")]
    [InlineData("FACT(1.9)", "1")]
    [InlineData("FACT(0)", "1")]
    [InlineData("FACT(-1)", "#NUM!")]
    [InlineData("FACT(171)", "#NUM!")]
    [InlineData("FACT(170)", "7.257415615307994E+306")]
    [InlineData("FACTDOUBLE(6)", "48")]
    [InlineData("FACTDOUBLE(7)", "105")]
    [InlineData("FACTDOUBLE(-1)", "1")]
    [InlineData("FACTDOUBLE(-2)", "#NUM!")]
    [InlineData("COMBIN(8,2)", "28")]
    [InlineData("COMBIN(5,0)", "1")]
    [InlineData("COMBIN(3,5)", "#NUM!")]
    [InlineData("COMBINA(4,3)", "20")]
    [InlineData("COMBINA(10,3)", "220")]
    [InlineData("PERMUT(100,3)", "970200")]
    [InlineData("PERMUT(3,2)", "6")]
    [InlineData("PERMUT(2,3)", "#NUM!")]
    [InlineData("PERMUTATIONA(3,2)", "9")]
    [InlineData("PERMUTATIONA(2,2)", "4")]
    [InlineData("MULTINOMIAL(2,3,4)", "1260")]
    [InlineData("GCD(5,2)", "1")]
    [InlineData("GCD(24,36)", "12")]
    [InlineData("GCD(7,1)", "1")]
    [InlineData("GCD(5,0)", "5")]
    [InlineData("GCD(-1,2)", "#NUM!")]
    [InlineData("LCM(5,2)", "10")]
    [InlineData("LCM(24,36)", "72")]
    [InlineData("LCM(5,0)", "0")]
    [InlineData("ROMAN(499,0)", "\"CDXCIX\"")]
    [InlineData("ROMAN(499,1)", "\"LDVLIV\"")]
    [InlineData("ROMAN(499,2)", "\"XDIX\"")]
    [InlineData("ROMAN(499,3)", "\"VDIV\"")]
    [InlineData("ROMAN(499,4)", "\"ID\"")]
    [InlineData("ROMAN(2013)", "\"MMXIII\"")]
    [InlineData("ROMAN(0)", "\"\"")]
    [InlineData("ROMAN(4000)", "#VALUE!")]
    [InlineData("ROMAN(-1)", "#VALUE!")]
    [InlineData("ARABIC(\"LVII\")", "57")]
    [InlineData("ARABIC(\"mcmxii\")", "1912")]
    [InlineData("ARABIC(\"\")", "0")]
    [InlineData("ARABIC(\"-IV\")", "-4")]
    [InlineData("ARABIC(\"Q\")", "#VALUE!")]
    [InlineData("BASE(7,2)", "\"111\"")]
    [InlineData("BASE(100,16)", "\"64\"")]
    [InlineData("BASE(15,2,10)", "\"0000001111\"")]
    [InlineData("BASE(-1,2)", "#NUM!")]
    [InlineData("BASE(5,1)", "#NUM!")]
    [InlineData("DECIMAL(\"FF\",16)", "255")]
    [InlineData("DECIMAL(\"111\",2)", "7")]
    [InlineData("DECIMAL(\"zap\",36)", "45745")]
    [InlineData("DECIMAL(\"2\",2)", "#NUM!")]
    public void NumberTheoryAndConversions(string formula, string expected) => Check(formula, expected);

    [Theory]
    [InlineData("SUMSQ(3,4)", "25")]
    [InlineData("PRODUCT(5,15,30)", "2250")]
    [InlineData("PRODUCT(\"2\",TRUE)", "2")]
    [InlineData("PRODUCT({2,3},4)", "24")]
    [InlineData("SUMPRODUCT({1,2,3},{4,5,6})", "32")]
    [InlineData("SUMPRODUCT({1,2},{\"a\",3})", "6")]
    [InlineData("SUMPRODUCT({1,2},{1,2,3})", "#VALUE!")]
    [InlineData("SUMX2MY2({2,3,9,1,8,7,5},{6,5,11,7,5,4,4})", "-55")]
    [InlineData("SUMX2PY2({2,3,9,1,8,7,5},{6,5,11,7,5,4,4})", "521")]
    [InlineData("SUMXMY2({2,3,9,1,8,7,5},{6,5,11,7,5,4,4})", "79")]
    [InlineData("SUMXMY2({1,2},{1,2,3})", "#N/A")]
    [InlineData("SERIESSUM(2,0,1,{1,2,3})", "17")]
    [InlineData("MDETERM({3,6,1;1,1,0;3,10,2})", "1")]
    [InlineData("MDETERM({1,3,8,5;1,3,6,1;1,1,1,0;7,3,10,2})", "88")]
    [InlineData("MDETERM({1,2,3})", "#VALUE!")]
    [InlineData("MINVERSE({4,-1;2,0})", "{0,0.5;-1,2}")]
    [InlineData("MINVERSE({1,2;2,4})", "#NUM!")]
    [InlineData("MMULT({1,2;3,4},{5,6;7,8})", "{19,22;43,50}")]
    [InlineData("MMULT({1,2},{1,2})", "#VALUE!")]
    [InlineData("MUNIT(2)", "{1,0;0,1}")]
    [InlineData("MUNIT(0)", "#VALUE!")]
    [InlineData("SEQUENCE(2)+ROUND({1.5;2.5},0)", "{3;5}")]
    public void AggregatesAndMatrices(string formula, string expected) => Check(formula, expected);

    [Fact]
    public void CriteriaSumIfAndSumIfs()
    {
        FakeResolver res = new FakeResolver()
            .Set("A1", 100000).Set("A2", 200000).Set("A3", 300000).Set("A4", 400000)
            .Set("B1", 7000).Set("B2", 14000).Set("B3", 21000).Set("B4", 28000)
            .Set("C1", "apple").Set("C2", "Apricot").Set("C3", "banana").Set("C4", "a*b")
            .Set("D1", 1).Set("D2", 2).Set("D3", 3).Set("D4", 4);
        Check("SUMIF(A1:A4,\">160000\",B1:B4)", "63000", res);
        Check("SUMIF(A1:A4,300000,B1:B4)", "21000", res);
        Check("SUMIF(A1:A4,\">160000\")", "900000", res);
        Check("SUMIF(C1:C4,\"a*\",D1:D4)", "7", res);
        Check("SUMIF(C1:C4,\"A?ple\",D1:D4)", "1", res);
        Check("SUMIF(C1:C4,\"a~*b\",D1:D4)", "4", res);
        Check("SUMIF(C1:C4,\"<>apple\",D1:D4)", "9", res);
        Check("SUMIF(C1:C4,\"=banana\",D1:D4)", "3", res);
        Check("SUMIF(A1:A4,\"<=200000\",D1:D4)", "3", res);
        Check("SUMIF({1,2},1)", "#VALUE!", res);
        Check("SUMIFS(D1:D4,A1:A4,\">100000\",C1:C4,\"a*\")", "6", res);
        Check("SUMIFS(D1:D4,A1:A3,\">0\")", "#VALUE!", res);
        Check("SUMIF(A1:A4,{\">150000\",\"<250000\"},D1:D4)", "{9,3}", res);
    }

    [Fact]
    public void CriteriaHelper()
    {
        Assert.True(CalcCriteria.Parse(CalcValue.FromText(">=5")).Matches(CalcValue.FromNumber(5)));
        Assert.False(CalcCriteria.Parse(CalcValue.FromText(">5")).Matches(CalcValue.FromText("6")));
        Assert.True(CalcCriteria.Parse(CalcValue.FromText("<>a")).Matches(CalcValue.FromNumber(3)));
        Assert.True(CalcCriteria.Parse(CalcValue.FromText("")).Matches(CalcValue.Blank));
        Assert.False(CalcCriteria.Parse(CalcValue.FromText("=")).Matches(CalcValue.EmptyText));
        Assert.True(CalcCriteria.Parse(CalcValue.FromText("TRUE")).Matches(CalcValue.True));
        Assert.False(CalcCriteria.Parse(CalcValue.FromText("TRUE")).Matches(CalcValue.FromNumber(1)));
        Assert.True(CalcCriteria.Parse(CalcValue.FromText("#N/A")).Matches(CalcValue.FromError(CalcError.NA)));
        Assert.True(CalcCriteria.Parse(CalcValue.FromText("1")).Matches(CalcValue.FromText("1")));
        Assert.True(CalcCriteria.Parse(CalcValue.FromText("A*C")).Matches(CalcValue.FromText("abXYc")));
        Assert.False(CalcCriteria.Parse(CalcValue.FromText("A?C")).Matches(CalcValue.FromText("abbc")));
        Assert.True(CalcCriteria.Parse(CalcValue.FromText("~?")).Matches(CalcValue.FromText("?")));
        Assert.False(CalcCriteria.Parse(CalcValue.FromText("~?")).Matches(CalcValue.FromText("x")));
    }

    private sealed class Sheet : ICalcResolver
    {
        private readonly FakeResolver _cells = new();

        public Sheet Set(string a1, CalcValue v)
        {
            _cells.Set(a1, v);
            return this;
        }

        public HashSet<int> Hidden { get; } = [];

        public HashSet<int> Filtered { get; } = [];

        public HashSet<(int, int)> Subtotals { get; } = [];

        public HashSet<(int, int)> Formulas { get; } = [];

        public CalcValue GetCell(string? sheet, int row, int column) => ((ICalcResolver)_cells).GetCell(sheet, row, column);

        public RowVisibility GetRowVisibility(string? sheet, int row) =>
            Filtered.Contains(row) ? RowVisibility.Filtered : Hidden.Contains(row) ? RowVisibility.Hidden : RowVisibility.Visible;

        public bool IsSubtotalCell(string? sheet, int row, int column) => Subtotals.Contains((row, column));

        public bool IsFormula(string? sheet, int row, int column) => Formulas.Contains((row, column));

        public string? GetFormulaText(string? sheet, int row, int column) => Formulas.Contains((row, column)) ? "=1+1" : null;

        public int GetSheetIndex(string? sheet) => sheet == "Sheet1" ? 1 : sheet == "Other" ? 2 : 0;

        public int GetSheetCount() => 2;
    }

    private static CalcValue EvalOn(ICalcResolver r, string formula) =>
        new Evaluator(r).Evaluate(FormulaParser.Parse(formula, new FormulaParserOptions { FileForm = false }), new CalcCell("Sheet1", 1, 1));

    [Fact]
    public void SubtotalAndAggregateSkipHiddenFilteredAndNestedRows()
    {
        var s = new Sheet();
        for (int i = 1; i <= 6; i++)
        {
            s.Set("A" + i, CalcValue.FromNumber(i * 10));
        }

        s.Hidden.Add(2);
        s.Filtered.Add(3);
        s.Subtotals.Add((4, 1));
        Assert.Equal("140", Show(EvalOn(s, "SUBTOTAL(9,A1:A6)"))); // 10 + 20 (hidden by hand, kept) + 50 + 60; the filtered row and the subtotal row are skipped
        Assert.Equal("120", Show(EvalOn(s, "SUBTOTAL(109,A1:A6)")));
        Assert.Equal("4", Show(EvalOn(s, "SUBTOTAL(2,A1:A6)")));
        Assert.Equal("#VALUE!", Show(EvalOn(s, "SUBTOTAL(12,A1:A6)")));
        Assert.Equal("60", Show(EvalOn(s, "AGGREGATE(4,0,A1:A6)")));
        Assert.Equal("170", Show(EvalOn(s, "AGGREGATE(9,0,A1:A6)")));
        Assert.Equal("160", Show(EvalOn(s, "AGGREGATE(9,5,A1:A6)")));
        Assert.Equal("210", Show(EvalOn(s, "AGGREGATE(9,4,A1:A6)")));
        s.Set("A7", CalcValue.FromError(CalcError.Div0));
        Assert.Equal("#DIV/0!", Show(EvalOn(s, "AGGREGATE(9,4,A1:A7)")));
        Assert.Equal("170", Show(EvalOn(s, "AGGREGATE(9,2,A1:A7)")));
        Assert.Equal("50", Show(EvalOn(s, "AGGREGATE(14,6,A1:A7,2)")));
        Assert.Equal("#NUM!", Show(EvalOn(s, "AGGREGATE(14,6,A1:A7,9)")));
        Assert.Equal("35", Show(EvalOn(s, "AGGREGATE(12,6,A1:A7)")));
    }

    [Fact]
    public void InformationThroughTheResolver()
    {
        var s = new Sheet().Set("A1", CalcValue.FromNumber(3)).Set("A2", CalcValue.FromText("x"));
        s.Formulas.Add((1, 1));
        Assert.Equal("TRUE", Show(EvalOn(s, "ISFORMULA(A1)")));
        Assert.Equal("FALSE", Show(EvalOn(s, "ISFORMULA(A2)")));
        Assert.Equal("#VALUE!", Show(EvalOn(s, "ISFORMULA(5)")));
        Assert.Equal("\"=1+1\"", Show(EvalOn(s, "FORMULATEXT(A1)")));
        Assert.Equal("#N/A", Show(EvalOn(s, "FORMULATEXT(A2)")));
        Assert.Equal("1", Show(EvalOn(s, "SHEET()")));
        Assert.Equal("2", Show(EvalOn(s, "SHEET(\"Other\")")));
        Assert.Equal("#N/A", Show(EvalOn(s, "SHEET(\"Nope\")")));
        Assert.Equal("2", Show(EvalOn(s, "SHEETS()")));
        Assert.Equal("1", Show(EvalOn(s, "SHEETS(A1)")));
        Assert.Equal("TRUE", Show(EvalOn(s, "ISREF(A1)")));
        Assert.Equal("FALSE", Show(EvalOn(s, "ISREF(5)")));
        Assert.Equal("3", Show(EvalOn(s, "CELL(\"row\",B3)")));
        Assert.Equal("2", Show(EvalOn(s, "CELL(\"col\",B3)")));
        Assert.Equal("\"$B$3\"", Show(EvalOn(s, "CELL(\"address\",B3)")));
        Assert.Equal("\"v\"", Show(EvalOn(s, "CELL(\"type\",A1)")));
        Assert.Equal("\"l\"", Show(EvalOn(s, "CELL(\"type\",A2)")));
        Assert.Equal("\"b\"", Show(EvalOn(s, "CELL(\"type\",A9)")));
        Assert.Equal("3", Show(EvalOn(s, "CELL(\"contents\",A1)")));
        Assert.Equal("\"G\"", Show(EvalOn(s, "CELL(\"format\",A1)")));
        Assert.Equal("1", Show(EvalOn(s, "CELL(\"protect\",A1)")));
        Assert.Equal("\"16.0\"", Show(EvalOn(s, "INFO(\"release\")")));
    }

    [Theory]
    [InlineData("XOR(3>0,2<9)", "FALSE")]
    [InlineData("XOR(3>12,4>6)", "FALSE")]
    [InlineData("XOR(3>0,2>9)", "TRUE")]
    [InlineData("XOR(TRUE,TRUE,TRUE)", "TRUE")]
    [InlineData("XOR({TRUE,\"x\",FALSE})", "TRUE")]
    [InlineData("XOR(\"x\")", "#VALUE!")]
    [InlineData("SWITCH(2,1,\"Sunday\",2,\"Monday\",3,\"Tuesday\",\"No match\")", "\"Monday\"")]
    [InlineData("SWITCH(99,1,\"a\",2,\"b\",\"none\")", "\"none\"")]
    [InlineData("SWITCH(99,1,\"a\")", "#N/A")]
    [InlineData("SWITCH(\"B\",\"a\",1,\"b\",2)", "2")]
    [InlineData("SWITCH(#N/A,1,2)", "#N/A")]
    [InlineData("SWITCH({1,2},1,\"x\",2,\"y\")", "{\"x\",\"y\"}")]
    [InlineData("TRUE()", "TRUE")]
    [InlineData("FALSE()", "FALSE")]
    [InlineData("IF(TRUE,1,1/0)", "1")]
    [InlineData("IFS(FALSE,1,TRUE,2)", "2")]
    [InlineData("IFERROR(1/0,\"e\")", "\"e\"")]
    [InlineData("IFNA(NA(),5)", "5")]
    [InlineData("IFNA(1/0,5)", "#DIV/0!")]
    public void Logical(string formula, string expected) => Check(formula, expected);

    [Theory]
    [InlineData("ISNUMBER(4)", "TRUE")]
    [InlineData("ISNUMBER(\"4\")", "FALSE")]
    [InlineData("ISTEXT(\"a\")", "TRUE")]
    [InlineData("ISTEXT(1)", "FALSE")]
    [InlineData("ISNONTEXT(1)", "TRUE")]
    [InlineData("ISNONTEXT(\"a\")", "FALSE")]
    [InlineData("ISLOGICAL(TRUE)", "TRUE")]
    [InlineData("ISLOGICAL(1)", "FALSE")]
    [InlineData("ISBLANK(Z9)", "TRUE")]
    [InlineData("ISBLANK(\"\")", "FALSE")]
    [InlineData("ISERR(#N/A)", "FALSE")]
    [InlineData("ISERR(#VALUE!)", "TRUE")]
    [InlineData("ISNA(#N/A)", "TRUE")]
    [InlineData("ISNA(#REF!)", "FALSE")]
    [InlineData("ISEVEN(-1)", "FALSE")]
    [InlineData("ISEVEN(2.5)", "TRUE")]
    [InlineData("ISEVEN(5)", "FALSE")]
    [InlineData("ISEVEN(0)", "TRUE")]
    [InlineData("ISODD(3)", "TRUE")]
    [InlineData("ISODD(-1)", "TRUE")]
    [InlineData("ISODD(2)", "FALSE")]
    [InlineData("ISEVEN(\"a\")", "#VALUE!")]
    [InlineData("ISNUMBER({1,\"a\"})", "{TRUE,FALSE}")]
    [InlineData("N(7)", "7")]
    [InlineData("N(TRUE)", "1")]
    [InlineData("N(FALSE)", "0")]
    [InlineData("N(\"7\")", "0")]
    [InlineData("N(#N/A)", "#N/A")]
    [InlineData("T(\"Rainfall\")", "\"Rainfall\"")]
    [InlineData("T(19)", "\"\"")]
    [InlineData("T(TRUE)", "\"\"")]
    [InlineData("NA()", "#N/A")]
    [InlineData("TYPE(19)", "1")]
    [InlineData("TYPE(\"a\")", "2")]
    [InlineData("TYPE(TRUE)", "4")]
    [InlineData("TYPE(#N/A)", "16")]
    [InlineData("TYPE({1,2})", "64")]
    [InlineData("TYPE(Z9)", "1")]
    [InlineData("ERROR.TYPE(#NULL!)", "1")]
    [InlineData("ERROR.TYPE(#DIV/0!)", "2")]
    [InlineData("ERROR.TYPE(#VALUE!)", "3")]
    [InlineData("ERROR.TYPE(#REF!)", "4")]
    [InlineData("ERROR.TYPE(#NAME?)", "5")]
    [InlineData("ERROR.TYPE(#NUM!)", "6")]
    [InlineData("ERROR.TYPE(#N/A)", "7")]
    [InlineData("ERROR.TYPE(#SPILL!)", "9")]
    [InlineData("ERROR.TYPE(#CALC!)", "14")]
    [InlineData("ERROR.TYPE(1)", "#N/A")]
    public void Information(string formula, string expected) => Check(formula, expected);

    [Fact]
    public void EveryFunctionIsRegisteredOnce()
    {
        var r = FunctionRegistry.CreateDefault();
        Assert.Empty(r.ReplacedNames);
        Assert.True(r.TryGet("PERMUT", out _));
        Assert.True(r.TryGet("PERMUTATIONA", out _));
    }

    [Fact]
    public void IsOmittedSeesAMissingParameter()
    {
        Assert.Equal("FALSE", Show(Eval("LET(f,LAMBDA(x,ISOMITTED(x)),f(1))")));
    }

    [Theory]
    [InlineData("LEFT(\"Sale Price\",4)", "\"Sale\"")]
    [InlineData("LEFT(\"Sweden\")", "\"S\"")]
    [InlineData("LEFT(\"abc\",10)", "\"abc\"")]
    [InlineData("LEFT(\"abc\",-1)", "#VALUE!")]
    [InlineData("LEFT(12345,2)", "\"12\"")]
    [InlineData("RIGHT(\"Sale Price\",5)", "\"Price\"")]
    [InlineData("RIGHT(\"Stock Number\")", "\"r\"")]
    [InlineData("RIGHT(\"abc\",0)", "\"\"")]
    [InlineData("MID(\"Fluid Flow\",1,5)", "\"Fluid\"")]
    [InlineData("MID(\"Fluid Flow\",7,20)", "\"Flow\"")]
    [InlineData("MID(\"Fluid Flow\",20,5)", "\"\"")]
    [InlineData("MID(\"abc\",0,1)", "#VALUE!")]
    [InlineData("LENB(\"abc\")", "3")]
    [InlineData("LEFTB(\"abc\",2)", "\"ab\"")]
    [InlineData("FIND(\"M\",\"Miriam McGovern\")", "1")]
    [InlineData("FIND(\"m\",\"Miriam McGovern\")", "6")]
    [InlineData("FIND(\"M\",\"Miriam McGovern\",3)", "8")]
    [InlineData("FIND(\"x\",\"abc\")", "#VALUE!")]
    [InlineData("FIND(\"a\",\"abc\",0)", "#VALUE!")]
    [InlineData("FIND(\"\",\"abc\")", "1")]
    [InlineData("SEARCH(\"e\",\"Statements\",6)", "7")]
    [InlineData("SEARCH(\"margin\",\"Profit Margin\")", "8")]
    [InlineData("SEARCH(\"?o\",\"Profit\")", "2")]
    [InlineData("SEARCH(\"p*t\",\"Profit\")", "1")]
    [InlineData("SEARCH(\"~?\",\"a?b\")", "2")]
    [InlineData("SUBSTITUTE(\"Sales Data\",\"Sales\",\"Cost\")", "\"Cost Data\"")]
    [InlineData("SUBSTITUTE(\"Quarter 1, 2008\",\"1\",\"2\",1)", "\"Quarter 2, 2008\"")]
    [InlineData("SUBSTITUTE(\"Quarter 1, 2011\",\"1\",\"2\",3)", "\"Quarter 1, 2012\"")]
    [InlineData("SUBSTITUTE(\"abc\",\"\",\"x\")", "\"abc\"")]
    [InlineData("SUBSTITUTE(\"abc\",\"B\",\"x\")", "\"abc\"")]
    [InlineData("REPLACE(\"abcdefghijk\",6,5,\"*\")", "\"abcde*k\"")]
    [InlineData("REPLACE(\"2009\",3,2,\"10\")", "\"2010\"")]
    [InlineData("REPLACE(\"123456\",1,3,\"@\")", "\"@456\"")]
    [InlineData("REPLACE(\"abc\",9,1,\"z\")", "\"abcz\"")]
    [InlineData("REPT(\"*-\",3)", "\"*-*-*-\"")]
    [InlineData("REPT(\"x\",-1)", "#VALUE!")]
    [InlineData("REPT(\"x\",0)", "\"\"")]
    [InlineData("TRIM(\" First   Quarter  Earnings \")", "\"First Quarter Earnings\"")]
    [InlineData("CLEAN(CHAR(9)&\"Monthly report\"&CHAR(10))", "\"Monthly report\"")]
    [InlineData("UPPER(\"total\")", "\"TOTAL\"")]
    [InlineData("LOWER(\"E. E. Cummings\")", "\"e. e. cummings\"")]
    [InlineData("PROPER(\"this is a TITLE\")", "\"This Is A Title\"")]
    [InlineData("PROPER(\"2-cent's worth\")", "\"2-Cent'S Worth\"")]
    [InlineData("PROPER(\"76BudGet\")", "\"76Budget\"")]
    [InlineData("EXACT(\"word\",\"word\")", "TRUE")]
    [InlineData("EXACT(\"Word\",\"word\")", "FALSE")]
    [InlineData("EXACT(1,\"1\")", "TRUE")]
    [InlineData("CHAR(65)", "\"A\"")]
    [InlineData("CHAR(33)", "\"!\"")]
    [InlineData("CHAR(128)", "\"\u20AC\"")]
    [InlineData("CHAR(0)", "#VALUE!")]
    [InlineData("CHAR(256)", "#VALUE!")]
    [InlineData("CODE(\"A\")", "65")]
    [InlineData("CODE(\"Alphabet\")", "65")]
    [InlineData("CODE(\"\u20AC\")", "128")]
    [InlineData("CODE(\"\")", "#VALUE!")]
    [InlineData("UNICHAR(66)", "\"B\"")]
    [InlineData("UNICHAR(0)", "#VALUE!")]
    [InlineData("UNICHAR(55296)", "#VALUE!")]
    [InlineData("UNICODE(\" \")", "32")]
    [InlineData("UNICODE(\"B\")", "66")]
    [InlineData("UNICODE(UNICHAR(128512))", "128512")]
    [InlineData("LEN(UNICHAR(128512))", "2")]
    public void TextBasics(string formula, string expected) => Check(formula, expected);

    [Theory]
    [InlineData("CONCATENATE(\"Stream population for \",\"brook trout\",\" species is \",32,\"/mile.\")", "\"Stream population for brook trout species is 32/mile.\"")]
    [InlineData("CONCATENATE({\"a\",\"b\"},\"x\")", "{\"ax\",\"bx\"}")]
    [InlineData("TEXTJOIN(\", \",TRUE,\"a\",\"\",\"c\")", "\"a, c\"")]
    [InlineData("TEXTJOIN(\", \",FALSE,\"a\",\"\",\"c\")", "\"a, , c\"")]
    [InlineData("TEXTJOIN(\"-\",TRUE,{\"a\",\"b\";\"c\",\"d\"})", "\"a-b-c-d\"")]
    [InlineData("TEXTJOIN({\"-\",\"+\"},TRUE,\"a\",\"b\",\"c\")", "\"a-b+c\"")]
    [InlineData("TEXTJOIN(\",\",TRUE,\"a\",#N/A)", "#N/A")]
    [InlineData("ARRAYTOTEXT({1,\"a\";TRUE,2})", "\"1, a, TRUE, 2\"")]
    [InlineData("ARRAYTOTEXT({1,\"a\";TRUE,2},1)", "\"{1,\"a\";TRUE,2}\"")]
    [InlineData("VALUETOTEXT(\"a\")", "\"a\"")]
    [InlineData("VALUETOTEXT(\"a\",1)", "\"\"a\"\"")]
    [InlineData("VALUETOTEXT(5,1)", "\"5\"")]
    [InlineData("VALUETOTEXT(1,2)", "#VALUE!")]
    [InlineData("TEXTBEFORE(\"a-b-c\",\"-\")", "\"a\"")]
    [InlineData("TEXTBEFORE(\"a-b-c\",\"-\",2)", "\"a-b\"")]
    [InlineData("TEXTBEFORE(\"a-b-c\",\"-\",-1)", "\"a-b\"")]
    [InlineData("TEXTBEFORE(\"abc\",\"x\")", "#N/A")]
    [InlineData("TEXTBEFORE(\"abc\",\"x\",1,0,0,\"none\")", "\"none\"")]
    [InlineData("TEXTBEFORE(\"a-b\",\"-\",0)", "#VALUE!")]
    [InlineData("TEXTBEFORE(\"a-b\",\"-\",3)", "#N/A")]
    [InlineData("TEXTBEFORE(\"a-b\",\"-\",2,0,1)", "\"a-b\"")]
    [InlineData("TEXTBEFORE(\"aXb\",\"x\",1,1)", "\"a\"")]
    [InlineData("TEXTAFTER(\"a-b-c\",\"-\")", "\"b-c\"")]
    [InlineData("TEXTAFTER(\"a-b-c\",\"-\",-1)", "\"c\"")]
    [InlineData("TEXTAFTER(\"a-b-c\",\"-\",2)", "\"c\"")]
    [InlineData("TEXTAFTER(\"a-b-c\",{\"-\",\"b\"})", "\"b-c\"")]
    [InlineData("TEXTAFTER(\"a-b\",\"-\",2,0,1)", "\"\"")]
    [InlineData("TEXTSPLIT(\"a,b;c,d\",\",\",\";\")", "{\"a\",\"b\";\"c\",\"d\"}")]
    [InlineData("TEXTSPLIT(\"1,2,,3\",\",\")", "{\"1\",\"2\",\"\",\"3\"}")]
    [InlineData("TEXTSPLIT(\"1,2,,3\",\",\",,TRUE)", "{\"1\",\"2\",\"3\"}")]
    [InlineData("TEXTSPLIT(\"a b c\",\" \")", "{\"a\",\"b\",\"c\"}")]
    [InlineData("TEXTSPLIT(\"a,b;c\",\",\",\";\",FALSE,0,\"-\")", "{\"a\",\"b\";\"c\",\"-\"}")]
    [InlineData("TEXTSPLIT(\"a,b;c\",\",\",\";\")", "{\"a\",\"b\";\"c\",#N/A}")]
    [InlineData("TEXTSPLIT(\"A1b\",\"B\",,,1)", "{\"A1\",\"\"}")]
    public void TextJoinSplit(string formula, string expected) => Check(formula, expected);

    [Theory]
    [InlineData("VALUE(\"123\")", "123")]
    [InlineData("VALUE(\"$1,000\")", "1000")]
    [InlineData("VALUE(\"x\")", "#VALUE!")]
    [InlineData("VALUE(5)", "5")]
    [InlineData("VALUE(TRUE)", "#VALUE!")]
    [InlineData("NUMBERVALUE(\"2.500,27\",\",\",\".\")", "2500.27")]
    [InlineData("NUMBERVALUE(\"3.5%\")", "0.035")]
    [InlineData("NUMBERVALUE(\"9%%\")", "0.0009")]
    [InlineData("NUMBERVALUE(\" 1 234.5 \")", "1234.5")]
    [InlineData("NUMBERVALUE(\"\")", "0")]
    [InlineData("NUMBERVALUE(\"1.2.3\")", "#VALUE!")]
    [InlineData("NUMBERVALUE(\"1\",\".\",\".\")", "#VALUE!")]
    [InlineData("FIXED(1234.567,1)", "\"1,234.6\"")]
    [InlineData("FIXED(1234.567,-1)", "\"1,230\"")]
    [InlineData("FIXED(-1234.567,-1,TRUE)", "\"-1230\"")]
    [InlineData("FIXED(44.332)", "\"44.33\"")]
    [InlineData("FIXED(1234.567,0,TRUE)", "\"1235\"")]
    [InlineData("FIXED(-0.4,0)", "\"0\"")]
    [InlineData("DOLLAR(1234.567)", "\"$1,234.57\"")]
    [InlineData("DOLLAR(1234.567,-2)", "\"$1,200\"")]
    [InlineData("DOLLAR(-1234.567,-2)", "\"($1,200)\"")]
    [InlineData("DOLLAR(-0.123,4)", "\"($0.1230)\"")]
    [InlineData("DOLLAR(99.888)", "\"$99.89\"")]
    [InlineData("BAHTTEXT(1234)", "\"หนึ่งพันสองร้อยสามสิบสี่บาทถ้วน\"")]
    [InlineData("BAHTTEXT(0)", "\"ศูนย์บาทถ้วน\"")]
    [InlineData("BAHTTEXT(21.5)", "\"ยี่สิบเอ็ดบาทห้าสิบสตางค์\"")]
    [InlineData("ASC(\"ＡＢＣ１２３\")", "\"ABC123\"")]
    [InlineData("DBCS(\"ABC\")", "\"ＡＢＣ\"")]
    [InlineData("ASC(\"ガ\")", "\"ｶﾞ\"")]
    [InlineData("DBCS(\"ｶﾞｷ\")", "\"ガキ\"")]
    [InlineData("TRANSLATE(\"hi\",\"fr\")", "#CONNECT!")]
    public void TextConversions(string formula, string expected) => Check(formula, expected);

    [Theory]
    [InlineData("REGEXTEST(\"abc123\",\"[0-9]+\")", "TRUE")]
    [InlineData("REGEXTEST(\"abc\",\"[0-9]+\")", "FALSE")]
    [InlineData("REGEXTEST(\"ABC\",\"abc\")", "FALSE")]
    [InlineData("REGEXTEST(\"ABC\",\"abc\",1)", "TRUE")]
    [InlineData("REGEXTEST(\"x\",\"(\")", "#VALUE!")]
    [InlineData("REGEXTEST(\"x\",\"a++\")", "#VALUE!")]
    [InlineData("REGEXTEST(\"\u0663\",\"\\d\")", "FALSE")]
    [InlineData("REGEXTEST(\"ab\",\"^[[:alpha:]]+$\")", "TRUE")]
    [InlineData("REGEXEXTRACT(\"abc123def456\",\"[0-9]+\")", "\"123\"")]
    [InlineData("REGEXEXTRACT(\"abc123def456\",\"[0-9]+\",1)", "{\"123\";\"456\"}")]
    [InlineData("REGEXEXTRACT(\"ab12\",\"([a-z]+)([0-9]+)\",2)", "{\"ab\",\"12\"}")]
    [InlineData("REGEXEXTRACT(\"abc\",\"[0-9]+\")", "#N/A")]
    [InlineData("REGEXREPLACE(\"a1b2\",\"[0-9]\",\"#\")", "\"a#b#\"")]
    [InlineData("REGEXREPLACE(\"abc\",\"(b)\",\"[$1]\")", "\"a[b]c\"")]
    [InlineData("REGEXREPLACE(\"a1b2c3\",\"[0-9]\",\"#\",2)", "\"a1b#c3\"")]
    [InlineData("REGEXREPLACE(\"a1b2c3\",\"[0-9]\",\"#\",-1)", "\"a1b2c#\"")]
    public void Regex(string formula, string expected) => Check(formula, expected);

    [Fact]
    public void RandomFunctionsAreVolatileAndSeedable()
    {
        var ev = new Evaluator(new FakeResolver(), options: new EvaluatorOptions { RandomSeed = 7 });
        CalcValue r = ev.Evaluate(FormulaParser.Parse("RAND()", new FormulaParserOptions { FileForm = false }), new CalcCell("Sheet1", 1, 1));
        Assert.True(ev.UsedVolatile);
        Assert.InRange(r.Number, 0, 1);
        var ev2 = new Evaluator(new FakeResolver(), options: new EvaluatorOptions { RandomSeed = 7 });
        Assert.Equal(r, ev2.Evaluate(FormulaParser.Parse("RAND()", new FormulaParserOptions { FileForm = false }), new CalcCell("Sheet1", 1, 1)));
        CalcValue b = Eval("RANDBETWEEN(5,5)");
        Assert.Equal("5", Show(b));
        Assert.Equal("#NUM!", Show(Eval("RANDBETWEEN(6,5)")));
        CalcValue arr = Eval("RANDARRAY(2,3,1,10,TRUE)");
        Assert.Equal(2, arr.Array!.Rows);
        Assert.Equal(3, arr.Array.Columns);
        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                Assert.InRange(arr.Array[i, j].Number, 1, 10);
                Assert.Equal(Math.Floor(arr.Array[i, j].Number), arr.Array[i, j].Number);
            }
        }

        Assert.Equal("#VALUE!", Show(Eval("RANDARRAY(1,1,5,1)")));
    }

    [Fact]
    public void ScalarMathDoesNotAllocate()
    {
        var ev = new Evaluator(new FakeResolver());
        FormulaNode[] nodes =
        [
            FormulaParser.Parse("ROUND(2.675,2)+ABS(-3)+MOD(7,3)+SIN(1)", new FormulaParserOptions { FileForm = false }),
            FormulaParser.Parse("LOG(100,10)+POWER(2,3)+INT(2.5)+SQRT(9)", new FormulaParserOptions { FileForm = false }),
        ];
        var cell = new CalcCell("Sheet1", 1, 1);
        foreach (FormulaNode n in nodes)
        {
            // Let the runtime promote the hot code to its final tier before counting.
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < 2000; i++)
                {
                    ev.Evaluate(n, cell);
                }

                Thread.Sleep(120);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                ev.Evaluate(n, cell);
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
