using RecursiveAI.OneDocument.Calc;
using Xunit;

namespace RecursiveAI.OneDocument.Tests.Calc;

public class EngineeringFunctionTests
{
    private static string S(string formula, FakeResolver? res = null)
    {
        CalcValue v = CalcTestHelper.Eval(formula, res);
        return v.Kind == ValueKind.Text ? v.Text : v.Kind == ValueKind.Number ? CalcConvert.NumberToText(v.Number) : CalcTestHelper.Show(v);
    }

    private static double N(string formula)
    {
        CalcValue v = CalcTestHelper.Eval(formula);
        Assert.True(v.Kind == ValueKind.Number, formula + " -> " + CalcTestHelper.Show(v));
        return v.Number;
    }

    private static void Near(double expected, string formula, double tol = 1e-8) =>
        Assert.True(Math.Abs(N(formula) - expected) <= tol * Math.Max(1, Math.Abs(expected)), $"{formula}: expected {expected}, got {N(formula)}");

    [Fact]
    public void BaseConversions()
    {
        Assert.Equal("100", S("BIN2DEC(1100100)"));
        Assert.Equal("-1", S("BIN2DEC(1111111111)"));
        Assert.Equal("00FB", S("BIN2HEX(11111011,4)"));
        Assert.Equal("011", S("BIN2OCT(1001,3)"));
        Assert.Equal("1001", S("DEC2BIN(9,4)"));
        Assert.Equal("1110011100", S("DEC2BIN(-100)"));
        Assert.Equal("FF", S("DEC2HEX(255)"));
        Assert.Equal("FFFFFFFFCA", S("DEC2HEX(-54)"));
        Assert.Equal("072", S("DEC2OCT(58,3)"));
        Assert.Equal("00001111", S("HEX2BIN(\"F\",8)"));
        Assert.Equal("-165", S("HEX2DEC(\"FFFFFFFF5B\")"));
        Assert.Equal("-165", S("OCT2DEC(7777777533)"));
        Assert.Equal("7777777777", S("HEX2OCT(\"FFFFFFFFFF\")"));
        Assert.Equal("255", S("HEX2DEC(\"ff\")"));
    }

    [Fact]
    public void BaseConversionErrors()
    {
        Assert.Equal("#NUM!", S("BIN2DEC(102)"));
        Assert.Equal("#NUM!", S("BIN2DEC(\"11111111111\")"));
        Assert.Equal("#NUM!", S("DEC2BIN(512)"));
        Assert.Equal("#NUM!", S("DEC2BIN(-513)"));
        Assert.Equal("#NUM!", S("DEC2BIN(9,2)"));
        Assert.Equal("#NUM!", S("DEC2BIN(9,11)"));
        Assert.Equal("#VALUE!", S("DEC2BIN(\"x\")"));
        Assert.Equal("#VALUE!", S("BIN2DEC(TRUE)"));
        Assert.Equal("11111111", S("HEX2BIN(\"FF\")"));
        Assert.Equal("#NUM!", S("HEX2BIN(\"200\")"));
        Assert.Equal("#NUM!", S("HEX2BIN(\"FFFFFFFDFF\")"));
        Assert.Equal("1111111111", S("HEX2BIN(\"FFFFFFFFFF\")"));
        Assert.Equal("3", S("DEC2HEX(3.9)"));
    }

    [Fact]
    public void BitOperations()
    {
        Assert.Equal("1", S("BITAND(1,5)"));
        Assert.Equal("31", S("BITOR(23,10)"));
        Assert.Equal("6", S("BITXOR(5,3)"));
        Assert.Equal("16", S("BITLSHIFT(4,2)"));
        Assert.Equal("3", S("BITRSHIFT(13,2)"));
        Assert.Equal("3", S("BITLSHIFT(13,-2)"));
        Assert.Equal("#NUM!", S("BITAND(-1,5)"));
        Assert.Equal("#NUM!", S("BITAND(1.5,5)"));
        Assert.Equal("#NUM!", S("BITLSHIFT(1,48)"));
        Assert.Equal("#NUM!", S("BITLSHIFT(1,54)"));
        Assert.Equal("#NUM!", S("BITAND(281474976710656,1)"));
        Assert.Equal("140737488355328", S("BITLSHIFT(1,47)"));
    }

    [Fact]
    public void DeltaAndStep()
    {
        Assert.Equal("1", S("DELTA(5,5)"));
        Assert.Equal("0", S("DELTA(5,4)"));
        Assert.Equal("1", S("DELTA(0)"));
        Assert.Equal("1", S("GESTEP(5,4)"));
        Assert.Equal("0", S("GESTEP(-1)"));
        Assert.Equal("#VALUE!", S("DELTA(\"a\")"));
    }

    [Fact]
    public void ErrorFunction()
    {
        Near(0.842700792949715, "ERF(1)", 1e-14);
        Near(0.842700792949715, "ERF(0,1)", 1e-14);
        Near(0.157299207050285, "ERFC(1)", 1e-14);
        Near(-0.842700792949715, "ERF.PRECISE(-1)", 1e-14);
        Near(1.84270079294971, "ERFC.PRECISE(-1)", 1e-14);
        Near(1.5374597944280349e-12, "ERFC(5)", 1e-13);
        Near(0.9953222650189527, "ERF(2)", 1e-14);
        Near(0.0046777349810472658, "ERFC(2)", 1e-14);
        Assert.Equal(0, N("ERF(0)"));
        Assert.Equal(1, N("ERF(7)"));
    }

    [Fact]
    public void BesselFunctions()
    {
        Near(0.981666428, "BESSELI(1.5,1)", 1e-8);
        Near(0.329925829, "BESSELJ(1.9,2)", 1e-6); // the reference prints 0.329925829; the exact value is 0.3299257277
        Near(0.277387804, "BESSELK(1.5,1)", 1e-8);
        Near(0.145918138, "BESSELY(2.5,1)", 1e-8);
        Near(0.7651976865579666, "BESSELJ(1,0)", 1e-14);
        Near(-0.2459357644513483, "BESSELJ(10,0)", 1e-14);
        Near(0.0434727461688614, "BESSELJ(10,1)", 1e-14);
        Near(0.0, "BESSELJ(2.404825557695773,0)", 1e-9);
        Near(0.08825696421567697, "BESSELY(1,0)", 1e-12);
        Near(-0.7812128213002887, "BESSELY(1,1)", 1e-12);
        Near(0.4210244382407083, "BESSELK(1,0)", 1e-13);
        Near(1.2660658777520082, "BESSELI(1,0)", 1e-14);
        Near(0.33905895852593, "BESSELJ(3,1)", 1e-12);
        Near(-0.33905895852593, "BESSELJ(-3,1)", 1e-12);
        Assert.Equal("#NUM!", S("BESSELK(0,1)"));
        Assert.Equal("#NUM!", S("BESSELY(-1,1)"));
        Assert.Equal("#NUM!", S("BESSELJ(1,-1)"));
        Assert.Equal(-N("BESSELJ(2,1)"), N("BESSELJ(-2,1)"), 12);
        Assert.Equal(1, N("BESSELJ(0,0)"));
        Assert.Equal(0, N("BESSELJ(0,3)"));
    }

    [Fact]
    public void Convert()
    {
        Near(0.453592, "CONVERT(1,\"lbm\",\"kg\")", 1e-6);
        Near(20, "CONVERT(68,\"F\",\"C\")", 1e-12);
        Near(293.15, "CONVERT(68,\"F\",\"K\")", 1e-12);
        Near(3.785411784, "CONVERT(1,\"gal\",\"l\")", 1e-12);
        Near(1000, "CONVERT(1,\"km\",\"m\")", 1e-12);
        Near(1, "CONVERT(1000,\"mm\",\"m\")", 1e-12);
        Near(1e6, "CONVERT(1,\"km2\",\"m2\")", 1e-9);
        Near(1024, "CONVERT(1,\"kibyte\",\"byte\")", 1e-12);
        Near(8, "CONVERT(1,\"byte\",\"bit\")", 1e-12);
        Near(1.609344, "CONVERT(1,\"mi\",\"km\")", 1e-12);
        Near(60, "CONVERT(1,\"hr\",\"mn\")", 1e-12);
        Near(101.325, "CONVERT(1,\"atm\",\"kPa\")", 1e-12);
        Assert.Equal("#N/A", S("CONVERT(2.5,\"ft\",\"sec\")"));
        Assert.Equal("#N/A", S("CONVERT(1,\"xx\",\"m\")"));
        Assert.Equal("#N/A", S("CONVERT(1,\"M\",\"m\")"));
        Assert.Equal("#N/A", S("CONVERT(1,\"lbm\",\"KG\")"));
    }

    [Fact]
    public void ComplexNumbers()
    {
        Assert.Equal("3+4i", S("COMPLEX(3,4)"));
        Assert.Equal("3-4j", S("COMPLEX(3,-4,\"j\")"));
        Assert.Equal("i", S("COMPLEX(0,1)"));
        Assert.Equal("-i", S("COMPLEX(0,-1)"));
        Assert.Equal("3+i", S("COMPLEX(3,1)"));
        Assert.Equal("3", S("COMPLEX(3,0)"));
        Assert.Equal("0", S("COMPLEX(0,0)"));
        Assert.Equal("#VALUE!", S("COMPLEX(1,1,\"k\")"));
        Assert.Equal("13", S("IMABS(\"5+12i\")"));
        Assert.Equal("5", S("IMREAL(\"5+12i\")"));
        Assert.Equal("12", S("IMAGINARY(\"5+12i\")"));
        Assert.Equal("1", S("IMAGINARY(\"i\")"));
        Assert.Equal("-1", S("IMAGINARY(\"-j\")"));
        Assert.Equal("-4", S("IMAGINARY(\"-4i\")"));
        Assert.Equal("#NUM!", S("IMABS(\"1 + 2i\")"));
        Assert.Equal("#NUM!", S("IMABS(\"abc\")"));
        Assert.Equal("3-4i", S("IMCONJUGATE(\"3+4i\")"));
        Assert.Equal("8+i", S("IMSUM(\"3+4i\",\"5-3i\")"));
        Assert.Equal("-2+7i", S("IMSUB(\"13+4i\",\"15-3i\")"));
        Assert.Equal("27+11i", S("IMPRODUCT(\"3+4i\",\"5-3i\")"));
        Assert.Equal("5+12i", S("IMDIV(\"-238+240i\",\"10+24i\")"));
        Assert.Equal("#NUM!", S("IMDIV(\"1+i\",\"0\")"));
        Assert.Equal("#VALUE!", S("IMSUM(\"1+i\",\"1+j\")"));
        Assert.Equal("9+3j", S("IMSUM(\"4+j\",5,\"2j\")"));
        Assert.Equal("1.09868411346781+0.455089860562227i", S("IMSQRT(\"1+i\")"));
        Assert.Equal("1.46869393991589+2.28735528717884i", S("IMEXP(\"1+i\")"));
        Assert.Equal("#NUM!", S("IMLN(0)"));
        Assert.Equal("#DIV/0!", S("IMARGUMENT(0)"));
    }

    [Fact]
    public void ComplexTranscendentals()
    {
        Assert.Equal("0.346573590279973+0.785398163397448i", S("IMLN(\"1+i\")"));
        Assert.Equal("1.6094379124341+0.927295218001612i", S("IMLN(\"3+4i\")"));
        Assert.Equal("1.29845758141598+0.634963914784736i", S("IMSIN(\"1+i\")"));
        Assert.Equal("0.833730025131149-0.988897705762865i", S("IMCOS(\"1+i\")"));
    }

    [Fact]
    public void ComplexPower()
    {
        Assert.StartsWith("-46+9", S("IMPOWER(\"2+3i\",3)")); // polar form: the last digit may carry rounding noise
        Assert.Equal("#NUM!", S("IMPOWER(0,0)"));
        Assert.Equal("0", S("IMPOWER(0,2)"));
        Assert.Equal("0.25", S("IMPOWER(2,-2)"));
    }

    [Fact]
    public void ComplexFoldsRanges()
    {
        var res = new FakeResolver().Set("A1", "1+i").Set("A2", CalcValue.Blank).Set("A3", "2-3i");
        Assert.Equal("3-2i", S("IMSUM(A1:A3)", res));
        Assert.Equal("5-i", S("IMPRODUCT(A1:A3)", res));
    }
}

public class DatabaseFunctionTests
{
    private static FakeResolver Orchard()
    {
        // A1:E7 is the database; G1:H3 and G5:G7 hold criteria.
        var r = new FakeResolver();
        string[] header = ["Tree", "Height", "Age", "Yield", "Profit"];
        object[][] rows =
        [
            ["Apple", 18, 20, 14, 105.0],
            ["Pear", 12, 12, 10, 96.0],
            ["Cherry", 13, 14, 9, 105.0],
            ["Apple", 14, 15, 10, 75.0],
            ["Pear", 9, 8, 8, 76.8],
            ["Apple", 8, 9, 6, 45.0],
        ];
        for (int c = 0; c < 5; c++)
        {
            r.Set($"{(char)('A' + c)}1", header[c]);
        }

        for (int i = 0; i < rows.Length; i++)
        {
            for (int c = 0; c < 5; c++)
            {
                object o = rows[i][c];
                r.Set($"{(char)('A' + c)}{i + 2}", o is string s ? CalcValue.FromText(s) : CalcValue.FromNumber(Convert.ToDouble(o, System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        return r;
    }

    private static string S(string formula, FakeResolver r)
    {
        CalcValue v = CalcTestHelper.Eval(formula, r);
        return v.Kind == ValueKind.Text ? v.Text : v.Kind == ValueKind.Number ? CalcConvert.NumberToText(v.Number) : CalcTestHelper.Show(v);
    }

    [Fact]
    public void AndAcrossColumnsOrAcrossRows()
    {
        FakeResolver r = Orchard();
        r.Set("G1", "Tree").Set("H1", "Height").Set("G2", "Apple").Set("H2", ">10");
        Assert.Equal("2", S("DCOUNT(A1:E7,\"Yield\",G1:H2)", r)); // Apple and taller than 10: heights 18 and 14
        Assert.Equal("24", S("DSUM(A1:E7,\"Yield\",G1:H2)", r));
        r.Set("G3", "Pear").Set("H3", "<12");
        Assert.Equal("32", S("DSUM(A1:E7,4,G1:H3)", r)); // apple/>10 (14 + 10) or pear/<12 (8)
        Assert.Equal("10.6666666666667", S("DAVERAGE(A1:E7,\"Yield\",G1:H3)", r));
    }

    [Fact]
    public void TextCriteriaMatchPrefixes()
    {
        FakeResolver r = Orchard();
        r.Set("G1", "Tree").Set("G2", "Ap");
        Assert.Equal("3", S("DCOUNTA(A1:E7,\"Tree\",G1:G2)", r));
        r.Set("G2", "=Ap");
        Assert.Equal("0", S("DCOUNTA(A1:E7,\"Tree\",G1:G2)", r));
        r.Set("G2", "=Apple");
        Assert.Equal("3", S("DCOUNTA(A1:E7,\"Tree\",G1:G2)", r));
        r.Set("G2", "<>Apple");
        Assert.Equal("3", S("DCOUNT(A1:E7,\"Height\",G1:G2)", r));
        r.Set("G2", "?ear");
        Assert.Equal("2", S("DCOUNT(A1:E7,\"Height\",G1:G2)", r));
    }

    [Fact]
    public void Aggregates()
    {
        FakeResolver r = Orchard();
        r.Set("G1", "Tree").Set("G2", "Apple");
        Assert.Equal("18", S("DMAX(A1:E7,\"Height\",G1:G2)", r));
        Assert.Equal("8", S("DMIN(A1:E7,\"Height\",G1:G2)", r));
        Assert.Equal("840", S("DPRODUCT(A1:E7,\"Yield\",G1:G2)", r));
        Assert.Equal("4", S("DSTDEV(A1:E7,\"Yield\",G1:G2)", r));
        Assert.Equal("16", S("DVAR(A1:E7,\"Yield\",G1:G2)", r));
        Assert.Equal("10.6666666666667", S("DVARP(A1:E7,\"Yield\",G1:G2)", r));
        Assert.Equal("3.2659863237109", S("DSTDEVP(A1:E7,\"Yield\",G1:G2)", r));
    }

    [Fact]
    public void GetNeedsExactlyOne()
    {
        FakeResolver r = Orchard();
        r.Set("G1", "Tree").Set("G2", "Cherry");
        Assert.Equal("9", S("DGET(A1:E7,\"Yield\",G1:G2)", r));
        r.Set("G2", "Apple");
        Assert.Equal("#NUM!", S("DGET(A1:E7,\"Yield\",G1:G2)", r));
        r.Set("G2", "Plum");
        Assert.Equal("#VALUE!", S("DGET(A1:E7,\"Yield\",G1:G2)", r));
        Assert.Equal("Cherry", S("DGET(A1:E7,\"Tree\",G1:G2)", r.Set("G2", "Cherry")));
    }

    [Fact]
    public void FieldAndEdgeCases()
    {
        FakeResolver r = Orchard();
        r.Set("G1", "Tree").Set("G2", "Plum");
        Assert.Equal("#DIV/0!", S("DAVERAGE(A1:E7,\"Yield\",G1:G2)", r));
        Assert.Equal("0", S("DSUM(A1:E7,\"Yield\",G1:G2)", r));
        Assert.Equal("#VALUE!", S("DSUM(A1:E7,\"Nope\",G1:G2)", r));
        Assert.Equal("#VALUE!", S("DSUM(A1:E7,9,G1:G2)", r));
        Assert.Equal("#VALUE!", S("DSUM(A1:E7,0,G1:G2)", r));
        // Excel 16.0.20527: TRUE names the first column; a criteria block of one row, or a single value, is #VALUE!; DCOUNT and DCOUNTA may
        // leave the field out and count the matching records.
        Assert.Equal("0", S("DSUM(A1:E7,TRUE,G1:G2)", r));
        Assert.Equal("#VALUE!", S("DSUM(A1:E7,\"Yield\",G1:G1)", r));
        Assert.Equal("#VALUE!", S("DSUM(A1:E7,\"Yield\",5)", r));
        Assert.Equal("0", S("DCOUNT(A1:E7,,G1:G2)", r));
        r.Set("G2", "Apple");
        Assert.Equal("3", S("DCOUNTA(A1:E7,,G1:G2)", r));
        r.Set("G2", "Plum");
        // Field names compare ignoring case; an empty criteria row matches every record.
        r.Set("G2", CalcValue.Blank);
        Assert.Equal("6", S("DCOUNT(A1:E7,\"HEIGHT\",G1:G2)", r));
        // A computed criterion: a header that is not a field holds TRUE or FALSE.
        r.Set("H1", "check").Set("H2", CalcValue.True);
        Assert.Equal("6", S("DCOUNT(A1:E7,\"Height\",H1:H2)", r));
        r.Set("H2", CalcValue.False);
        Assert.Equal("0", S("DCOUNT(A1:E7,\"Height\",H1:H2)", r));
    }
}
