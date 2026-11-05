using System.Globalization;
using RecursiveAI.OneOffice.Calc;
using RecursiveAI.OneOffice.NumberFormats;
using Xunit;

namespace RecursiveAI.OneOffice.Tests.Calc;

public class FinancialFunctionTests
{
    private static int D(int y, int m, int d)
    {
        Assert.True(ExcelDate.TryToSerial(y, m, d, DateSystem.Date1900, out int s));
        return s;
    }

    private static double N(string formula, FakeResolver? res = null)
    {
        CalcValue v = CalcTestHelper.Eval(formula, res);
        Assert.True(v.Kind == ValueKind.Number, formula + " -> " + CalcTestHelper.Show(v));
        return v.Number;
    }

    private static string S(string formula, FakeResolver? res = null) => CalcTestHelper.Show(CalcTestHelper.Eval(formula, res));

    private static void Near(double expected, string formula, double tolerance = 1e-6, FakeResolver? res = null) =>
        Assert.True(Math.Abs(N(formula, res) - expected) <= tolerance * Math.Max(1, Math.Abs(expected)), $"{formula}: expected {expected}, got {N(formula, res)}");

    [Fact]
    public void TimeValueOfMoney()
    {
        Near(-1037.0320893591, "PMT(0.08/12,10,10000)");
        Near(2581.4033740601, "FV(0.06/12,10,-200,-500,1)");
        Near(-59777.145851188, "PV(0.08/12,12*20,500,,0)");
        Near(59.6738656742946, "NPER(0.12/12,-100,-1000,10000,1)");
        Near(0.00770147248820137, "RATE(4*12,-200,8000)", 1e-8);
        Near(-66.6666666666667, "IPMT(0.1/12,1,3*12,8000)");
        Near(-75.6231860083663, "PPMT(0.1/12,1,2*12,2000)");
        Assert.Equal(-64814.8148148148, N("ISPMT(0.1/12,1,36,8000000)"), 6);
        Near(0.053542667, "EFFECT(0.0525,4)", 1e-8);
        Near(0.052500319, "NOMINAL(0.053543,4)", 1e-8);
        Near(1.33089, "FVSCHEDULE(1,{0.09,0.11,0.1})");
        Near(0.0009933, "RRI(96,10000,11000)", 1e-3);
        Near(3.85986, "PDURATION(0.025,2000,2200)", 1e-5);
    }

    [Fact]
    public void ZeroRateAndErrors()
    {
        Assert.Equal(-1000, N("PMT(0,10,10000)"));
        Assert.Equal("#NUM!", S("PMT(0.1,0,100)"));
        Assert.Equal("#NUM!", S("IPMT(0.1,0,3,100)"));
        Assert.Equal("#NUM!", S("RATE(10,-10,1000,0,0,-1)"));
        Assert.Equal("#VALUE!", S("PMT(\"x\",10,100)"));
        Assert.Equal("#DIV/0!", S("ISPMT(0.1,1,0,100)"));
    }

    [Fact]
    public void ExpandsOverArrays()
    {
        Assert.Equal("{-1000,-500}", S("PMT(0,{10,20},10000)"));
    }

    [Fact]
    public void PaymentsSplitIntoInterestAndPrincipal()
    {
        double total = 0;
        for (int p = 1; p <= 12; p++)
        {
            total += N($"IPMT(0.05/12,{p},12,1000)") + N($"PPMT(0.05/12,{p},12,1000)");
        }

        Assert.Equal(12 * N("PMT(0.05/12,12,1000)"), total, 8);
        Assert.Equal(0, N("IPMT(0.05,1,12,1000,0,1)"));
        Near(N("PMT(0.05,12,1000,0,1)"), "IPMT(0.05,5,12,1000,0,1)+PPMT(0.05,5,12,1000,0,1)", 1e-9);
    }

    [Fact]
    public void CumulativeInterestAndPrincipal()
    {
        Near(-11135.23213, "CUMIPMT(0.09/12,30*12,125000,13,24,0)", 1e-7);
        Near(-934.1071234, "CUMPRINC(0.09/12,30*12,125000,13,24,0)", 1e-7);
        Assert.Equal("#NUM!", S("CUMIPMT(0.09/12,360,125000,0,24,0)"));
        Assert.Equal("#NUM!", S("CUMIPMT(0.09/12,360,125000,13,24,2)"));
    }

    [Fact]
    public void NetPresentValueAndReturns()
    {
        Near(1188.4434123352, "NPV(0.1,-10000,3000,4200,6800)", 1e-8);
        Near(-0.0212448482733, "IRR({-70000,12000,15000,18000,21000})", 1e-6);
        Near(0.0866309480365, "IRR({-70000,12000,15000,18000,21000,26000})", 1e-6);
        Near(0.126094, "MIRR({-120000,39000,30000,21000,37000,46000},0.1,0.12)", 1e-5);
        Assert.Equal("#NUM!", S("IRR({1,2,3})"));
        Assert.Equal("#DIV/0!", S("MIRR({1,2,3},0.1,0.1)"));
        Assert.Equal("#DIV/0!", S("NPV(-1,1,2)"));
        var res = new FakeResolver().Set("A1", -10000).Set("A2", 2750).Set("A3", 4250).Set("A4", 3250).Set("A5", 2750)
            .Set("B1", D(2008, 1, 1)).Set("B2", D(2008, 3, 1)).Set("B3", D(2008, 10, 30)).Set("B4", D(2009, 2, 15)).Set("B5", D(2009, 4, 1));
        Near(2086.6476020315, "XNPV(0.09,A1:A5,B1:B5)", 1e-8, res);
        Near(0.373362535, "XIRR(A1:A5,B1:B5,0.1)", 1e-6, res);
        Assert.Equal("#NUM!", S("XNPV(0.09,A1:A4,B1:B5)", res));
    }

    [Fact]
    public void DollarFractions()
    {
        Assert.Equal(1.125, N("DOLLARDE(1.02,16)"), 12);
        Assert.Equal(1.02, N("DOLLARFR(1.125,16)"), 12);
        Assert.Equal(1.1, N("DOLLARDE(1.1,10)"), 12);
        Assert.Equal("#DIV/0!", S("DOLLARDE(1.02,0)"));
        Assert.Equal("#NUM!", S("DOLLARDE(1.02,-1)"));
    }

    [Fact]
    public void Depreciation()
    {
        Assert.Equal(2250, N("SLN(30000,7500,10)"));
        Near(4090.90909, "SYD(30000,7500,10,1)");
        Near(186083.333333, "DB(1000000,100000,6,1,7)", 1e-8);
        Near(259639.416667, "DB(1000000,100000,6,2,7)", 1e-8);
        Near(1.31506849, "DDB(2400,300,10*365,1)", 1e-7);
        Near(22.1225472, "DDB(2400,300,10,10)", 1e-6);
        Near(480, "DDB(2400,300,10,1)");
        Near(306, "DDB(2400,300,10,2,1.5)", 1e-9);
        Assert.Equal("#NUM!", S("DDB(2400,300,10,11)"));
        Near(480, "VDB(2400,300,10,0,1)");
        Near(864, "VDB(2400,300,10,0,2)");
        Assert.Equal("#NUM!", S("VDB(2400,300,10,5,3)"));
    }

    [Fact]
    public void FrenchDepreciation()
    {
        string args = $"2400,{D(2008, 8, 19)},{D(2008, 12, 31)},300";
        Assert.Equal(776, N($"AMORDEGRC({args},1,0.15,1)"));
        Assert.Equal(360, N($"AMORLINC({args},1,0.15,1)"), 6);
        Assert.Equal("#NUM!", S($"AMORLINC({args},1,0.15,2)"));
    }

    [Fact]
    public void CouponDates()
    {
        int s = D(2011, 1, 25), m = D(2011, 11, 15);
        Assert.Equal(71, N($"COUPDAYBS({s},{m},2,1)"));
        Assert.Equal(181, N($"COUPDAYS({s},{m},2,1)"));
        Assert.Equal(110, N($"COUPDAYSNC({s},{m},2,1)"));
        Assert.Equal(D(2011, 5, 15), N($"COUPNCD({s},{m},2,1)"));
        Assert.Equal(2, N($"COUPNUM({s},{m},2,1)"));
        Assert.Equal(D(2010, 11, 15), N($"COUPPCD({s},{m},2,1)"));
        Assert.Equal("#NUM!", S($"COUPNUM({m},{s},2,1)"));
        Assert.Equal("#NUM!", S($"COUPNUM({s},{m},3,1)"));
        // A month-end maturity keeps month-end coupon dates.
        Assert.Equal(D(2010, 2, 28), N($"COUPPCD({D(2010, 3, 10)},{D(2012, 8, 31)},2,0)"));
    }

    [Fact]
    public void BondPricing()
    {
        string ds = $"{D(2008, 2, 15)},{D(2017, 11, 15)}";
        Near(94.6343616213, $"PRICE({ds},0.0575,0.065,100,2,0)", 1e-8);
        Near(0.065, $"YIELD({D(2008, 2, 15)},{D(2016, 11, 15)},0.0575,{N($"PRICE({D(2008, 2, 15)},{D(2016, 11, 15)},0.0575,0.065,100,2,0)")},100,2,0)", 1e-9);
        Near(10.9191453, $"DURATION({D(2018, 7, 1)},{D(2048, 1, 1)},0.08,0.09,2,1)", 1e-6);
        double dur = N($"DURATION({D(2008, 1, 1)},{D(2016, 1, 1)},0.08,0.09,2,1)");
        Near(dur / (1 + (0.09 / 2)), $"MDURATION({D(2008, 1, 1)},{D(2016, 1, 1)},0.08,0.09,2,1)", 1e-12);
        Near(5.73567, $"MDURATION({D(2008, 1, 1)},{D(2016, 1, 1)},0.08,0.09,2,1)", 1e-5);
        Assert.Equal("#NUM!", S($"PRICE({ds},-0.1,0.065,100,2,0)"));
    }

    [Fact]
    public void DiscountedAndMaturitySecurities()
    {
        Near(0.052420213, $"DISC({D(2007, 1, 25)},{D(2007, 6, 15)},97.975,100,1)", 1e-7);
        Near(0.05768, $"INTRATE({D(2008, 2, 15)},{D(2008, 5, 15)},1000000,1014420,2)", 1e-5);
        Near(1014584.65, $"RECEIVED({D(2008, 2, 15)},{D(2008, 5, 15)},1000000,0.0575,2)", 1e-8);
        Near(99.7958333, $"PRICEDISC({D(2008, 2, 16)},{D(2008, 3, 1)},0.0525,100,2)", 1e-8);
        Near(0.05282257, $"YIELDDISC({D(2008, 2, 16)},{D(2008, 3, 1)},99.795,100,2)", 1e-7);
        Near(99.98449888, $"PRICEMAT({D(2008, 2, 15)},{D(2008, 4, 13)},{D(2007, 11, 11)},0.061,0.061,0)", 1e-8);
        Near(0.060954, $"YIELDMAT({D(2008, 3, 15)},{D(2008, 11, 3)},{D(2007, 11, 8)},0.0625,100.0123,0)", 1e-5);
        Near(20.54794521, $"ACCRINTM({D(2008, 4, 1)},{D(2008, 6, 15)},0.1,1000,3)", 1e-8);
        Near(16.6666667, $"ACCRINT({D(2008, 3, 1)},{D(2008, 8, 31)},{D(2008, 5, 1)},0.1,1000,2,0)", 1e-7);
    }

    [Fact]
    public void TreasuryBills()
    {
        Near(98.45, $"TBILLPRICE({D(2008, 3, 31)},{D(2008, 6, 1)},0.09)", 1e-9);
        Near(0.0914169, $"TBILLYIELD({D(2008, 3, 31)},{D(2008, 6, 1)},98.45)", 1e-6);
        Near(0.094151, $"TBILLEQ({D(2008, 3, 31)},{D(2008, 6, 1)},0.0914)", 1e-5);
        Assert.Equal("#NUM!", S($"TBILLPRICE({D(2008, 3, 31)},{D(2009, 6, 1)},0.09)"));
    }

    [Fact]
    public void OddPeriodSecurities()
    {
        Near(113.597717, $"ODDFPRICE({D(2008, 11, 11)},{D(2021, 3, 1)},{D(2008, 10, 15)},{D(2009, 3, 1)},0.0785,0.0625,100,2,1)", 1e-6);
        Near(0.0772, $"ODDFYIELD({D(2008, 11, 11)},{D(2021, 3, 1)},{D(2008, 10, 15)},{D(2009, 3, 1)},0.0575,84.5,100,2,0)", 1e-4);
        Near(99.878286, $"ODDLPRICE({D(2008, 2, 7)},{D(2008, 6, 15)},{D(2007, 10, 15)},0.0375,0.0405,100,2,0)", 1e-6);
        Near(0.0451922, $"ODDLYIELD({D(2008, 4, 20)},{D(2008, 6, 15)},{D(2007, 12, 24)},0.0375,99.875,100,2,0)", 1e-6);
        Assert.Equal("#NUM!", S($"ODDLPRICE({D(2008, 2, 7)},{D(2008, 6, 15)},{D(2008, 3, 15)},0.0375,0.0405,100,2,0)"));
    }

    [Fact]
    public void OddYieldInvertsOddPrice()
    {
        string head = $"{D(2008, 11, 11)},{D(2021, 3, 1)},{D(2008, 10, 15)},{D(2009, 3, 1)},0.0575";
        double price = N($"ODDFPRICE({head},0.07,100,2,0)");
        Near(0.07, $"ODDFYIELD({head},{price.ToString("R", CultureInfo.InvariantCulture)},100,2,0)", 1e-9);
    }
}
