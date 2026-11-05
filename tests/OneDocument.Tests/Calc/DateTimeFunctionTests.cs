using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.NumberFormats;
using static RecursiveAI.OneDocument.Tests.Calc.CalcTestHelper;

namespace RecursiveAI.OneDocument.Tests.Calc;

/// <summary>The date and time functions against the examples on Microsoft's function reference pages.</summary>
public class DateTimeFunctionTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static string Run(string formula, DateSystem system = DateSystem.Date1900) =>
        Show(Eval(formula, options: new EvaluatorOptions { DateSystem = system, Clock = new FixedClock(new DateTimeOffset(2024, 3, 9, 15, 30, 0, TimeSpan.Zero)) }));

    [Theory]
    [InlineData("DATE(2008,1,1)", "39448")]
    [InlineData("DATE(2008,14,2)", "39846")]
    [InlineData("DATE(2008,1,-15)", "39432")]
    [InlineData("DATE(108,1,2)", "39449")]
    [InlineData("DATE(1900,1,1)", "1")]
    [InlineData("DATE(1900,2,29)", "60")]
    [InlineData("DATE(1900,3,1)", "61")]
    [InlineData("DATE(1900,1,0)", "0")]
    [InlineData("DATE(1900,1,-1)", "#NUM!")]
    [InlineData("DATE(10000,1,1)", "#NUM!")]
    [InlineData("DATE(9999,12,31)", "2958465")]
    [InlineData("DATE(9999,12,32)", "#NUM!")]
    [InlineData("TIME(12,0,0)", "0.5")]
    [InlineData("TIME(27,0,0)", "0.125")]
    [InlineData("TIME(0,750,0)", "0.520833333333333")]
    [InlineData("TIME(-1,0,0)", "#NUM!")]
    [InlineData("DATEVALUE(\"8/22/2011\")", "40777")]
    [InlineData("DATEVALUE(\"22-AUG-2011\")", "40777")]
    [InlineData("DATEVALUE(\"2011-02-23\")", "40597")]
    [InlineData("DATEVALUE(\"August 22, 2011\")", "40777")]
    [InlineData("DATEVALUE(\"22 Aug 2011 6:35 AM\")", "40777")]
    [InlineData("DATEVALUE(\"1/1/1899\")", "#VALUE!")]
    [InlineData("DATEVALUE(\"13/40/2011\")", "#VALUE!")]
    [InlineData("DATEVALUE(5)", "#VALUE!")]
    [InlineData("DATEVALUE(\"3/9\")", "45360")]
    [InlineData("TIMEVALUE(\"2:24 AM\")", "0.1")]
    [InlineData("TIMEVALUE(\"6:35 AM\")", "0.274305555555556")]
    [InlineData("TIMEVALUE(\"6:35\")", "0.274305555555556")]
    [InlineData("TIMEVALUE(\"12:00 PM\")", "0.5")]
    [InlineData("TIMEVALUE(\"25:00\")", "#VALUE!")]
    public void ConstructAndParse(string formula, string expected)
    {
        string got = Run(formula);
        if (double.TryParse(expected, System.Globalization.CultureInfo.InvariantCulture, out double e) && double.TryParse(got, System.Globalization.CultureInfo.InvariantCulture, out double g))
        {
            Assert.Equal(e, g, 12);
        }
        else
        {
            Assert.Equal(expected, got);
        }
    }

    [Theory]
    [InlineData("YEAR(DATE(2011,8,22))", "2011")]
    [InlineData("MONTH(DATE(2011,8,22))", "8")]
    [InlineData("DAY(DATE(2011,8,22))", "22")]
    [InlineData("DAY(0)", "0")]
    [InlineData("YEAR(0)", "1900")]
    [InlineData("YEAR(\"2011-08-22\")", "2011")]
    [InlineData("YEAR(-1)", "#NUM!")]
    [InlineData("HOUR(0.75)", "18")]
    [InlineData("HOUR(\"3:30:30 AM\")", "3")]
    [InlineData("MINUTE(\"4:48:18 PM\")", "48")]
    [InlineData("SECOND(\"4:48:18 PM\")", "18")]
    [InlineData("SECOND(0.5)", "0")]
    [InlineData("WEEKDAY(DATE(2008,2,14))", "5")]
    [InlineData("WEEKDAY(DATE(2008,2,14),2)", "4")]
    [InlineData("WEEKDAY(DATE(2008,2,14),3)", "3")]
    [InlineData("WEEKDAY(DATE(2008,2,14),11)", "4")]
    [InlineData("WEEKDAY(DATE(2008,2,14),17)", "5")]
    [InlineData("WEEKDAY(DATE(2008,2,14),4)", "#NUM!")]
    [InlineData("WEEKDAY(1)", "1")]
    [InlineData("WEEKDAY(0)", "7")]
    [InlineData("WEEKNUM(DATE(2012,3,9))", "10")]
    [InlineData("WEEKNUM(DATE(2012,3,9),2)", "11")]
    [InlineData("WEEKNUM(DATE(2012,3,9),21)", "10")]
    [InlineData("WEEKNUM(DATE(2021,1,1),21)", "53")]
    [InlineData("ISOWEEKNUM(DATE(2012,3,9))", "10")]
    [InlineData("ISOWEEKNUM(DATE(2021,1,3))", "53")]
    [InlineData("ISOWEEKNUM(DATE(2019,12,30))", "1")]
    [InlineData("WEEKNUM(DATE(2021,1,1))", "1")]
    [InlineData("WEEKNUM(DATE(2021,1,3))", "2")]
    [InlineData("WEEKNUM(DATE(2021,1,3),2)", "1")]
    [InlineData("WEEKNUM(DATE(2021,1,1),5)", "#NUM!")]
    public void Parts(string formula, string expected) => Assert.Equal(expected, Run(formula));

    [Theory]
    [InlineData("EDATE(DATE(2011,1,15),1)", "40589")]
    [InlineData("EDATE(DATE(2011,1,31),1)", "40602")]
    [InlineData("EDATE(DATE(2011,1,15),-1)", "40527")]
    [InlineData("EOMONTH(DATE(2011,1,1),1)", "40602")]
    [InlineData("EOMONTH(DATE(2011,1,1),-3)", "40482")]
    [InlineData("EOMONTH(DATE(1900,1,15),1)", "60")]
    [InlineData("EOMONTH(DATE(2011,1,1),0)", "40574")]
    [InlineData("EDATE(DATE(2011,1,15),-100000)", "#NUM!")]
    [InlineData("DAYS(\"3/15/2021\",\"2/1/2021\")", "42")]
    [InlineData("DAYS(DATE(2011,1,1),DATE(2011,12,31))", "-364")]
    [InlineData("DAYS360(DATE(2011,1,1),DATE(2011,12,31))", "360")]
    [InlineData("DAYS360(DATE(2011,1,1),DATE(2011,1,31))", "30")]
    [InlineData("DAYS360(DATE(2011,1,30),DATE(2011,2,1))", "1")]
    [InlineData("DAYS360(DATE(2011,1,1),DATE(2011,12,31),TRUE)", "359")]
    [InlineData("DATEDIF(DATE(2001,1,1),DATE(2003,1,1),\"Y\")", "2")]
    [InlineData("DATEDIF(DATE(2001,1,1),DATE(2003,1,1),\"D\")", "730")]
    [InlineData("DATEDIF(DATE(2001,6,1),DATE(2002,8,15),\"MD\")", "14")]
    [InlineData("DATEDIF(DATE(2001,6,1),DATE(2002,8,15),\"YM\")", "2")]
    [InlineData("DATEDIF(DATE(2001,6,1),DATE(2002,8,15),\"YD\")", "75")]
    [InlineData("DATEDIF(DATE(2001,6,1),DATE(2002,8,15),\"M\")", "14")]
    [InlineData("DATEDIF(DATE(2003,1,1),DATE(2001,1,1),\"Y\")", "#NUM!")]
    [InlineData("DATEDIF(DATE(2001,1,1),DATE(2003,1,1),\"Q\")", "#NUM!")]
    public void Shifts(string formula, string expected) => Assert.Equal(expected, Run(formula));

    [Theory]
    [InlineData("YEARFRAC(DATE(2012,1,1),DATE(2012,7,30))", 0.580555555555556)]
    [InlineData("YEARFRAC(DATE(2012,1,1),DATE(2012,7,30),1)", 0.576502732240437)]
    [InlineData("YEARFRAC(DATE(2012,1,1),DATE(2012,7,30),2)", 0.586111111111111)]
    [InlineData("YEARFRAC(DATE(2012,1,1),DATE(2012,7,30),3)", 0.57808219178082)]
    [InlineData("YEARFRAC(DATE(2012,1,1),DATE(2012,7,30),4)", 0.580555555555556)]
    [InlineData("YEARFRAC(DATE(2012,7,30),DATE(2012,1,1))", 0.580555555555556)]
    public void YearFrac(string formula, double expected) => Assert.Equal(expected, Eval(formula, options: new EvaluatorOptions()).Number, 12);

    [Theory]
    [InlineData("NETWORKDAYS(DATE(2012,10,1),DATE(2013,3,1))", "110")]
    [InlineData("NETWORKDAYS(DATE(2012,10,1),DATE(2013,3,1),{41235,41247,41295})", "107")]
    [InlineData("NETWORKDAYS(DATE(2013,3,1),DATE(2012,10,1))", "-110")]
    [InlineData("NETWORKDAYS.INTL(DATE(2012,10,1),DATE(2013,3,1),\"0000011\")", "110")]
    [InlineData("NETWORKDAYS.INTL(DATE(2012,10,1),DATE(2012,10,7),11)", "6")]
    [InlineData("NETWORKDAYS.INTL(DATE(2012,10,1),DATE(2012,10,7),\"1111111\")", "#VALUE!")]
    [InlineData("NETWORKDAYS.INTL(DATE(2012,10,1),DATE(2012,10,7),18)", "#NUM!")]
    [InlineData("NETWORKDAYS.INTL(DATE(2012,10,1),DATE(2012,10,7),\"01\")", "#VALUE!")]
    [InlineData("WORKDAY(DATE(2012,1,6),1)", "40917")]
    [InlineData("WORKDAY(DATE(2012,1,9),-1)", "40914")]
    [InlineData("WORKDAY(DATE(2012,1,6),5,{40917})", "40924")]
    [InlineData("WORKDAY(DATE(2012,1,6),0)", "40914")]
    [InlineData("WORKDAY.INTL(DATE(2012,1,6),1,\"0000011\")", "40917")]
    [InlineData("WORKDAY.INTL(DATE(2012,1,6),1,11)", "40915")]
    [InlineData("WORKDAY.INTL(DATE(2012,1,6),1,\"1111111\")", "#VALUE!")]
    [InlineData("WORKDAY(DATE(2012,1,6),-100000)", "#NUM!")]
    public void WorkingDays(string formula, string expected) => Assert.Equal(expected, Run(formula));

    [Fact]
    public void ClockAndVolatility()
    {
        Assert.Equal("45360", Run("TODAY()"));
        Assert.Equal(45360 + (15.5 / 24), Eval("NOW()", options: new EvaluatorOptions { Clock = new FixedClock(new DateTimeOffset(2024, 3, 9, 15, 30, 0, TimeSpan.Zero)) }).Number, 9);
        var ev = new Evaluator(new FakeResolver(), options: new EvaluatorOptions { Clock = new FixedClock(new DateTimeOffset(2024, 3, 9, 15, 30, 0, TimeSpan.Zero)) });
        ev.Evaluate(FormulaParser.Parse("TODAY()"), new CalcCell("Sheet1", 1, 1));
        Assert.True(ev.UsedVolatile);
    }

    [Fact]
    public void System1904()
    {
        Assert.Equal("0", Run("DATE(1904,1,1)", DateSystem.Date1904));
        Assert.Equal("#NUM!", Run("DATE(1903,12,31)", DateSystem.Date1904));
        Assert.Equal("1461", Run("DATE(1908,1,1)", DateSystem.Date1904));
        Assert.Equal("6", Run("WEEKDAY(0)", DateSystem.Date1904));
        Assert.Equal("1904", Run("YEAR(0)", DateSystem.Date1904));
        Assert.Equal("0", Run("DATEVALUE(\"1/1/1904\")", DateSystem.Date1904));
        Assert.Equal("1", Run("DAY(0)", DateSystem.Date1904));
        Assert.Equal("43898", Run("TODAY()", DateSystem.Date1904));
    }
}
