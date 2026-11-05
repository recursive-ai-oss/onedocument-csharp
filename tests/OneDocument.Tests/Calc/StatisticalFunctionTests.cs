using RecursiveAI.OneDocument.Calc;
using static RecursiveAI.OneDocument.Tests.Calc.CalcTestHelper;

namespace RecursiveAI.OneDocument.Tests.Calc;

/// <summary>The statistical functions against the values printed on Microsoft's function reference pages.</summary>
public class StatisticalFunctionTests
{
    private static double Num(string formula, FakeResolver? res = null)
    {
        CalcValue v = Eval(formula, res);
        Assert.True(v.Kind == ValueKind.Number, formula + " gave " + Show(v));
        return v.Number;
    }

    private static FakeResolver Column(string col, params double[] values)
    {
        var r = new FakeResolver();
        for (int i = 0; i < values.Length; i++)
        {
            r.Set(col + (i + 1), values[i]);
        }

        return r;
    }

    // The numbers are the examples on the function pages (six or seven digits as printed there).
    [Theory]
    [InlineData("AVERAGE(10,7,9,27,2)", 11, 1e-12)]
    [InlineData("AVERAGEA({10,7,9,27,2,\"x\",TRUE})", 56.0 / 7, 1e-12)]
    [InlineData("AVERAGE({10,7,9,27,2,\"x\",TRUE})", 11, 1e-12)]
    [InlineData("MEDIAN(1,2,3,4,5,6)", 3.5, 0)]
    [InlineData("MODE.SNGL({5.6,4,4,3,2,4})", 4, 0)]
    [InlineData("LARGE({3,4,5,2,3,4,5,6,4,7},3)", 5, 0)]
    [InlineData("SMALL({3,4,5,2,3,4,5,6,4,7},4)", 4, 0)]
    [InlineData("PERCENTILE.INC({1,2,3,4},0.3)", 1.9, 1e-12)]
    [InlineData("PERCENTILE.EXC({1,2,3,4},0.3)", 1.5, 1e-12)]
    [InlineData("QUARTILE.INC({1,2,4,7,8,9,10,12},1)", 3.5, 1e-12)]
    [InlineData("QUARTILE.EXC({1,2,4,7,8,9,10,12},1)", 2.5, 1e-12)]
    [InlineData("PERCENTRANK.INC({13,12,11,8,4,3,2,1,1,1},2)", 0.333, 0)]
    [InlineData("PERCENTRANK.EXC({1,2,3,6,6,6,7,8,9},8)", 0.8, 0)]
    [InlineData("RANK.EQ(3.5,{7,3.5,3.5,1,2},1)", 3, 0)]
    [InlineData("RANK.AVG(3.5,{7,3.5,3.5,1,2},1)", 3.5, 0)]
    [InlineData("STDEV.S({1345,1301,1368,1322,1310,1370,1318,1350,1303,1299})", 27.46391572, 1e-8)]
    [InlineData("STDEV.P({1345,1301,1368,1322,1310,1370,1318,1350,1303,1299})", 26.05455814, 1e-8)]
    [InlineData("VAR.S({1345,1301,1368,1322,1310,1370,1318,1350,1303,1299})", 754.2666667, 1e-6)]
    [InlineData("VAR.P({1345,1301,1368,1322,1310,1370,1318,1350,1303,1299})", 678.84, 1e-9)]
    [InlineData("DEVSQ({4,5,8,7,11,4,3})", 48, 1e-12)]
    [InlineData("AVEDEV({4,5,6,7,5,4,3})", 1.020408163, 1e-8)]
    [InlineData("GEOMEAN({4,5,8,7,11,4,3})", 5.476987, 1e-6)]
    [InlineData("HARMEAN({4,5,8,7,11,4,3})", 5.028376, 1e-6)]
    [InlineData("TRIMMEAN({4,5,6,7,2,3,4,5,1,2,3},0.2)", 3.777777778, 1e-8)]
    [InlineData("KURT({3,4,5,2,3,4,5,6,4,7})", -0.151799637, 1e-8)]
    [InlineData("SKEW({3,4,5,2,3,4,5,6,4,7})", 0.359543071, 1e-8)]
    [InlineData("SKEW.P({3,4,5,2,3,4,5,6,4,7})", 0.303193339, 1e-8)]
    [InlineData("STANDARDIZE(42,40,1.5)", 1.333333333, 1e-8)]
    [InlineData("COUNT(1,\"a\",TRUE,{1,\"x\"},\"5\")", 4, 0)]
    [InlineData("COUNTA(1,\"a\",{1,\"x\"})", 4, 0)]
    [InlineData("MAX({1,5,3})", 5, 0)]
    [InlineData("MIN({1,5,3})", 1, 0)]
    [InlineData("MAXA({1,TRUE,\"x\"})", 1, 0)]
    [InlineData("MINA({4,TRUE,\"x\"})", 0, 0)]
    public void Descriptive(string formula, double expected, double tolerance) => Assert.Equal(expected, Num(formula), tolerance == 0 ? 1e-12 : tolerance);

    [Theory]
    [InlineData("NORM.DIST(42,40,1.5,TRUE)", 0.908788780, 1e-6)]
    [InlineData("NORM.DIST(42,40,1.5,FALSE)", 0.109340050, 1e-6)]
    [InlineData("NORM.S.DIST(1.333333,TRUE)", 0.908788726, 1e-6)]
    [InlineData("NORM.INV(0.908789,40,1.5)", 42.00000201, 1e-6)]
    [InlineData("NORM.S.INV(0.975)", 1.959963985, 1e-9)]
    [InlineData("NORM.S.INV(0.001)", -3.090232306, 1e-8)]
    [InlineData("NORM.S.INV(1E-10)", -6.361340902, 1e-8)]
    [InlineData("T.DIST(60,1,TRUE)", 0.994695326, 1e-8)]
    [InlineData("T.DIST(8,3,FALSE)", 0.000736913, 1e-8)]
    [InlineData("T.DIST.2T(1.959999998,60)", 0.054645, 1e-6)]
    [InlineData("T.DIST.RT(1.959999998,60)", 0.027322, 1e-6)]
    [InlineData("T.INV(0.75,2)", 0.816496581, 1e-8)]
    [InlineData("T.INV.2T(0.546449,60)", 0.606533, 1e-6)]
    [InlineData("CHISQ.DIST(0.5,1,TRUE)", 0.520499878, 1e-8)]
    [InlineData("CHISQ.DIST(2,3,FALSE)", 0.207553749, 1e-8)]
    [InlineData("CHISQ.DIST.RT(18.307,10)", 0.0500006, 1e-7)]
    [InlineData("CHISQ.INV(0.93,1)", 3.283020287, 1e-8)]
    [InlineData("CHISQ.INV.RT(0.050001,10)", 18.30697, 1e-5)]
    [InlineData("F.DIST(15.2069,6,4,TRUE)", 0.99, 1e-6)]
    [InlineData("F.DIST(15.2069,6,4,FALSE)", 0.0012238, 1e-7)]
    [InlineData("F.DIST.RT(15.2069,6,4)", 0.01, 1e-6)]
    [InlineData("F.INV(0.01,6,4)", 0.109309908, 1e-8)]
    [InlineData("F.INV.RT(0.01,6,4)", 15.20686, 1e-5)]
    [InlineData("BETA.DIST(2,8,10,TRUE,1,3)", 0.685470581, 1e-6)]
    [InlineData("BETA.DIST(2,8,10,FALSE,1,3)", 1.483764648, 1e-6)]
    [InlineData("BETA.INV(0.685470581,8,10,1,3)", 2, 1e-6)]
    [InlineData("GAMMA.DIST(10.00001131,9,2,TRUE)", 0.068094, 1e-6)]
    [InlineData("GAMMA.DIST(10.00001131,9,2,FALSE)", 0.0326391, 1e-6)]
    [InlineData("GAMMA.INV(0.068094,9,2)", 10.00001119, 1e-5)]
    [InlineData("GAMMALN(4)", 1.791759469, 1e-9)]
    [InlineData("GAMMA(2.5)", 1.329340388, 1e-9)]
    [InlineData("GAMMA(-3.75)", 0.267866129, 1e-8)]
    [InlineData("GAMMA(5)", 24, 0)]
    [InlineData("BINOM.DIST(6,10,0.5,FALSE)", 0.205078125, 1e-12)]
    [InlineData("BINOM.DIST(6,10,0.5,TRUE)", 0.828125, 1e-12)]
    [InlineData("BINOM.DIST.RANGE(60,0.75,48)", 0.084, 1e-3)]
    [InlineData("BINOM.DIST.RANGE(60,0.75,45,50)", 0.524, 1e-3)]
    [InlineData("BINOM.INV(6,0.5,0.75)", 4, 0)]
    [InlineData("POISSON.DIST(2,5,FALSE)", 0.084224337, 1e-6)]
    [InlineData("POISSON.DIST(2,5,TRUE)", 0.124652019, 1e-6)]
    [InlineData("EXPON.DIST(0.2,10,TRUE)", 0.864664717, 1e-6)]
    [InlineData("EXPON.DIST(0.2,10,FALSE)", 1.353352832, 1e-9)]
    [InlineData("WEIBULL.DIST(105,20,100,TRUE)", 0.929581390, 1e-6)]
    [InlineData("WEIBULL.DIST(105,20,100,FALSE)", 0.035588864, 1e-6)]
    [InlineData("LOGNORM.DIST(4,3.5,1.2,TRUE)", 0.039083556, 1e-6)]
    [InlineData("LOGNORM.DIST(4,3.5,1.2,FALSE)", 0.017617597, 1e-6)]
    [InlineData("LOGNORM.INV(0.039084,3.5,1.2)", 4.0000252, 1e-5)]
    [InlineData("HYPGEOM.DIST(1,4,8,20,FALSE)", 0.363261094, 1e-8)]
    [InlineData("HYPGEOM.DIST(1,4,8,20,TRUE)", 0.465428277, 1e-8)]
    [InlineData("NEGBINOM.DIST(10,5,0.25,FALSE)", 0.055049, 1e-6)]
    [InlineData("NEGBINOM.DIST(10,5,0.25,TRUE)", 0.3135141, 1e-6)]
    [InlineData("CONFIDENCE.NORM(0.05,2.5,50)", 0.692952, 1e-6)]
    [InlineData("CONFIDENCE.T(0.05,1,50)", 0.284196855, 1e-8)]
    [InlineData("FISHER(0.75)", 0.972955075, 1e-6)]
    [InlineData("FISHERINV(0.972955)", 0.75, 1e-6)]
    [InlineData("PHI(0.75)", 0.301137432, 1e-6)]
    [InlineData("GAUSS(2)", 0.477249868, 1e-6)]
    public void Distributions(string formula, double expected, double tolerance) => Assert.Equal(expected, Num(formula), tolerance == 0 ? 1e-12 : tolerance);

    [Theory]
    [InlineData("Z.TEST({3,6,7,8,6,5,4,2,1,9},4)", 0.090574, 1e-6)]
    [InlineData("Z.TEST({3,6,7,8,6,5,4,2,1,9},6)", 0.863043, 1e-6)]
    [InlineData("CORREL({3,2,4,5,6},{9,7,12,15,17})", 0.997054486, 1e-8)]
    [InlineData("SLOPE({2,3,9,1,8,7,5},{6,5,11,7,5,4,4})", 0.305555556, 1e-8)]
    [InlineData("INTERCEPT({2,3,9,1,8},{6,5,11,7,5})", 0.048387097, 1e-8)]
    [InlineData("RSQ({2,3,9,1,8,7,5},{6,5,11,7,5,4,4})", 0.05795, 1e-5)]
    [InlineData("STEYX({2,3,9,1,8,7,5},{6,5,11,7,5,4,4})", 3.305719, 1e-6)]
    [InlineData("COVARIANCE.P({3,2,4,5,6},{9,7,12,15,17})", 5.2, 1e-12)]
    [InlineData("COVARIANCE.S({3,2,4,5,6},{9,7,12,15,17})", 6.5, 1e-12)]
    [InlineData("FORECAST(30,{6,7,9,15,21},{20,28,31,38,40})", 10.60725, 1e-5)]
    [InlineData("T.TEST({3,4,5,8,9,1,2,4,5},{6,19,3,2,14,4,5,17,1},2,1)", 0.196016, 1e-6)]
    [InlineData("F.TEST({6,7,9,15,21},{20,28,31,38,40})", 0.648318, 1e-6)]
    [InlineData("CHISQ.TEST({58,35;11,25;10,23},{45.35,47.65;17.56,18.44;16.09,16.91})", 0.0003082, 1e-7)]
    [InlineData("INDEX(LINEST({1;9;5;7},{0;4;2;3}),1,1)", 2, 1e-9)]
    [InlineData("INDEX(LINEST({1;9;5;7},{0;4;2;3}),1,2)", 1, 1e-9)]
    [InlineData("INDEX(LINEST({1;9;5;7},{0;4;2;3},TRUE,TRUE),3,1)", 1, 1e-9)]
    [InlineData("INDEX(TREND({1;9;5;7},{0;4;2;3},{5}),1,1)", 11, 1e-9)]
    [InlineData("INDEX(GROWTH({1;2;4;8},{0;1;2;3},{4}),1,1)", 16, 1e-9)]
    public void Relationships(string formula, double expected, double tolerance) => Assert.Equal(expected, Num(formula), tolerance == 0 ? 1e-12 : tolerance);

    [Theory]
    [InlineData("T.INV.2T(0.05,10)", 2.228138852, 1e-8)]
    [InlineData("T.INV.2T(0.05,1)", 12.7062047, 1e-6)]
    [InlineData("CHISQ.INV.RT(0.05,1)", 3.841458821, 1e-8)]
    [InlineData("CHISQ.INV.RT(0.01,10)", 23.20925116, 1e-7)]
    [InlineData("F.INV.RT(0.05,5,10)", 3.32583453, 1e-7)]
    [InlineData("NORM.S.INV(0.99)", 2.326347874, 1e-9)]
    [InlineData("GAMMALN(100)", 359.1342053695754, 1e-9)]
    [InlineData("GAMMALN(0.5)", 0.5723649429247001, 1e-12)]
    [InlineData("NORM.S.DIST(-8,TRUE)", 6.220960574e-16, 1e-24)]
    [InlineData("NORM.S.DIST(0,TRUE)", 0.5, 1e-15)]
    [InlineData("CHISQ.DIST.RT(100,3)", 1.5541594e-21, 1e-27)]
    [InlineData("T.DIST.RT(1,1)", 0.25, 1e-14)]
    [InlineData("T.DIST(0,5,TRUE)", 0.5, 1e-14)]
    [InlineData("BINOM.DIST(500,1000,0.5,TRUE)", 0.5126125, 1e-6)]
    public void Accuracy(string formula, double expected, double tolerance) => Assert.Equal(expected, Num(formula), tolerance);

    [Fact]
    public void InversesRoundTrip()
    {
        foreach (double p in new[] { 1e-9, 0.001, 0.05, 0.3, 0.5, 0.77, 0.95, 0.999999 })
        {
            string ps = p.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(p, Num("NORM.S.DIST(NORM.S.INV(" + ps + "),TRUE)"), p * 1e-11);
            Assert.Equal(p, Num("T.DIST(T.INV(" + ps + ",7),7,TRUE)"), p * 1e-11);
            Assert.Equal(p, Num("CHISQ.DIST(CHISQ.INV(" + ps + ",4),4,TRUE)"), p * 1e-11);
            Assert.Equal(p, Num("F.DIST(F.INV(" + ps + ",3,9),3,9,TRUE)"), p * 1e-11);
            Assert.Equal(p, Num("GAMMA.DIST(GAMMA.INV(" + ps + ",2.5,3),2.5,3,TRUE)"), p * 1e-11);
            Assert.Equal(p, Num("BETA.DIST(BETA.INV(" + ps + ",2,5),2,5,TRUE)"), p * 1e-11);
        }
    }

    [Fact]
    public void FrequencyAndModeMult()
    {
        Assert.Equal("{1;2;4;2}", Show(Eval("FREQUENCY({79,85,78,85,50,81,95,88,97},{70,79,89})")));
        Assert.Equal("{2;3;4}", Show(Eval("MODE.MULT({1,2,2,3,3,4,4})")));
    }

    [Fact]
    public void Prob()
    {
        Assert.Equal(0.1, Num("PROB({0,1,2,3},{0.2,0.3,0.1,0.4},2)"), 12);
        Assert.Equal(0.8, Num("PROB({0,1,2,3},{0.2,0.3,0.1,0.4},1,3)"), 12);
        Assert.Equal("#NUM!", Show(Eval("PROB({0,1},{0.5,0.6},1)")));
    }

    [Theory]
    [InlineData("AVERAGE(\"x\")", "#VALUE!")]
    [InlineData("AVERAGE({\"x\"})", "#DIV/0!")]
    [InlineData("AVERAGE(#N/A,1)", "#N/A")]
    [InlineData("COUNT(#N/A,1)", "1")]
    [InlineData("MAX({\"x\"})", "0")]
    [InlineData("MEDIAN({\"x\"})", "#NUM!")]
    [InlineData("MODE.SNGL({1,2,3})", "#N/A")]
    [InlineData("LARGE({1,2},3)", "#NUM!")]
    [InlineData("PERCENTILE.INC({1,2},1.5)", "#NUM!")]
    [InlineData("PERCENTILE.EXC({1,2,3},0.1)", "#NUM!")]
    [InlineData("PERCENTRANK.INC({1,2,3},5)", "#N/A")]
    [InlineData("RANK.EQ(9,{1,2,3})", "#N/A")]
    [InlineData("STDEV.S({1})", "#DIV/0!")]
    [InlineData("VAR.P({1})", "0")]
    [InlineData("GEOMEAN({1,0})", "#NUM!")]
    [InlineData("TRIMMEAN({1,2,3},1)", "#NUM!")]
    [InlineData("SKEW({1,2})", "#DIV/0!")]
    [InlineData("NORM.DIST(1,0,0,TRUE)", "#NUM!")]
    [InlineData("NORM.S.INV(0)", "#NUM!")]
    [InlineData("NORM.S.INV(1)", "#NUM!")]
    [InlineData("T.DIST(1,0,TRUE)", "#NUM!")]
    [InlineData("T.DIST.2T(-1,5)", "#NUM!")]
    [InlineData("CHISQ.DIST(-1,2,TRUE)", "#NUM!")]
    [InlineData("BINOM.DIST(11,10,0.5,TRUE)", "#NUM!")]
    [InlineData("POISSON.DIST(-1,2,TRUE)", "#NUM!")]
    [InlineData("GAMMA(0)", "#NUM!")]
    [InlineData("GAMMALN(0)", "#NUM!")]
    [InlineData("FISHER(1)", "#NUM!")]
    [InlineData("CORREL({1,2},{1,2,3})", "#N/A")]
    [InlineData("CORREL({1,1},{1,2})", "#DIV/0!")]
    [InlineData("SLOPE({1,2,3},{4,4,4})", "#DIV/0!")]
    [InlineData("FORECAST.ETS(1,{1,2,3},{1,2,3})", "#N/A")]
    [InlineData("NORM.DIST(\"a\",0,1,TRUE)", "#VALUE!")]
    [InlineData("HYPGEOM.DIST(1E+300,1E+300,1E+300,1E+300,1E+300)", "#NUM!")]
    [InlineData("BINOM.INV(1E+300,0.5,0.5)", "#NUM!")]
    public void Errors(string formula, string expected) => Assert.Equal(expected, Show(Eval(formula)));

    [Fact]
    public void ReferencesAndCriteria()
    {
        FakeResolver r = Column("A", 10, 20, 30, 40);
        r.Set("B1", "x");
        r.Set("B2", "y");
        r.Set("B3", "x");
        r.Set("B4", "x");
        Assert.Equal(3, Num("COUNTIF(B1:B4,\"x\")", r));
        Assert.Equal(2, Num("COUNTIFS(B1:B4,\"x\",A1:A4,\">15\")", r));
        Assert.Equal(26.666666666666668, Num("AVERAGEIF(B1:B4,\"x\",A1:A4)", r), 9);
        Assert.Equal(35, Num("AVERAGEIFS(A1:A4,B1:B4,\"x\",A1:A4,\">15\")", r), 9);
        Assert.Equal(40, Num("MAXIFS(A1:A4,B1:B4,\"x\")", r), 9);
        Assert.Equal(10, Num("MINIFS(A1:A4,B1:B4,\"x\")", r), 9);
        Assert.Equal(0, Num("MAXIFS(A1:A4,B1:B4,\"zzz\")", r));
        Assert.Equal("#DIV/0!", Show(Eval("AVERAGEIF(B1:B4,\"zzz\",A1:A4)", r)));
        Assert.Equal("#VALUE!", Show(Eval("COUNTIFS(A1:A4,\">1\",B1:B3,\"x\")", r)));
        Assert.Equal(3, Num("COUNTBLANK(C1:C3)", r));
        Assert.Equal(25, Num("AVERAGE(A1:A4)", r));
        Assert.Equal(4, Num("COUNT(A1:A4)", r));
        Assert.Equal(5, Num("COUNTA(A1:B4,C1)", r) - 3);
    }

    // Every registered function against a spread of odd arguments: no exception but a typed one, no reference and no non-finite number.
    [Fact]
    public void EveryFunctionSurvivesOddArguments()
    {
        string[] atoms =
        [
            "0", "1", "-1", "0.5", "7", "1E+300", "2958465", "\"x\"", "\"2020-02-29\"", "\"Y\"", "\"0000011\"", "TRUE", "#N/A", "{1,2,3,4,5}", "{1,2;3,4}",
            "{1,\"a\",TRUE,#N/A}", "A1:A4", "B1:C3", "{0.2,0.3,0.1,0.4}", "C1:C4",
        ];
        FakeResolver res = Column("A", 3, 1, 4, 1);
        res.Set("B1", "t");
        res.Set("B2", 2);
        res.Set("C1", true);
        var registry = FunctionRegistry.CreateDefault();
        var options = new EvaluatorOptions { MaxSteps = 100_000, Clock = new FixedClock(new DateTimeOffset(2024, 3, 9, 15, 30, 0, TimeSpan.Zero)) };
        var limits = new OfficeLimits { MaxAllocationBytes = 4 << 20 };
        int calls = 0;
        foreach (string name in registry.Definitions.Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal))
        {
            if (name is "SEQUENCE" or "MAKEARRAY" or "LET" or "LAMBDA" or "MAP")
            {
                continue;
            }

            for (int argc = 0; argc <= 3; argc++)
            {
                int total = (int)System.Math.Pow(atoms.Length, argc);
                int step = argc == 3 ? 37 : 1;
                for (int k = 0; k < total; k += step)
                {
                    var args = new List<string>();
                    int rest = k;
                    for (int i = 0; i < argc; i++)
                    {
                        args.Add(atoms[rest % atoms.Length]);
                        rest /= atoms.Length;
                    }

                    string f = name + "(" + string.Join(",", args) + ")";
                    var ev = new Evaluator(res, registry, limits, options);
                    CalcValue v;
                    try
                    {
                        v = ev.Evaluate(FormulaParser.Parse(f, new FormulaParserOptions { FileForm = false }), new CalcCell("Sheet1", 6, 6));
                    }
                    catch (OfficeException)
                    {
                        continue;
                    }

                    calls++;
                    Assert.NotEqual(ValueKind.Reference, v.Kind);
                    if (v.Kind == ValueKind.Number)
                    {
                        Assert.True(double.IsFinite(v.Number), f);
                    }
                }
            }
        }

        Assert.True(calls > 10000);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
