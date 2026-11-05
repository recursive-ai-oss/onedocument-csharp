using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Tests.NumberFormats;

// Expected strings come from Microsoft's "Number format codes" and "Review guidelines for customizing a number format" articles
// (cited per case) or from the ECMA-376 Part 1 §18.8.30/§18.8.31 rules; the rest are cases of the documented rules whose exact
// answer is confirmed by the Excel grid (tools/NumFmtGrid).
public class NumberFormatTests
{
    private static string F(string code, double v, FormatOptions o = default) => NumberFormat.Parse(code).Format(v, o);

    // "Number format codes" / "Review guidelines": decimal places table.
    [Theory]
    [InlineData("####.#", 1234.59, "1234.6")]
    [InlineData("#.000", 8.9, "8.900")]
    [InlineData("0.#", 0.631, "0.6")]
    [InlineData("#.0#", 12, "12.0")]
    [InlineData("#.0#", 1234.568, "1234.57")]
    [InlineData("???.???", 44.398, " 44.398")]
    [InlineData("???.???", 102.65, "102.65 ")]
    [InlineData("???.???", 2.8, "  2.8  ")]
    [InlineData("0.0?", 8.9, "8.9 ")]
    [InlineData("0.0?", 88.99, "88.99")]
    [InlineData("#.##", 8.9, "8.9")]
    [InlineData("#.00", 0.47, ".47")]
    public void DecimalPlaces(string code, double v, string expected) => Assert.Equal(expected, F(code, v));

    // Thousands separator and scaling rows.
    [Theory]
    [InlineData("#,###", 12000, "12,000")]
    [InlineData("#,", 12000, "12")]
    [InlineData("0.0,,", 12200000, "12.2")]
    [InlineData("0.0,,\"M\"", 12200000, "12.2M")]
    [InlineData("#.0,", 12200000, "12200.0")]
    [InlineData("#,##0", 1234567.891, "1,234,568")]
    [InlineData("#,##0", 5, "5")]
    [InlineData("0,000", 5, "0,005")]
    [InlineData("#,##0.00", -1234.5, "-1,234.50")]
    [InlineData("#,##0,", 1234567, "1,235")]
    public void ThousandsAndScaling(string code, double v, string expected) => Assert.Equal(expected, F(code, v));

    // Leading and trailing zeros table.
    [Theory]
    [InlineData("00000", 12, "00012")]
    [InlineData("00000", 123, "00123")]
    [InlineData("\"000\"#", 12, "00012")]
    [InlineData("\"000\"#", 123, "000123")]
    [InlineData("\"0\"#", 123, "0123")]
    [InlineData("000-0000", 5551234, "555-1234")]
    [InlineData("(###) ###-####", 5551234567, "(555) 123-4567")]
    public void Zeros(string code, double v, string expected) => Assert.Equal(expected, F(code, v));

    // Scientific rows: 12,200,000 shows 1.22E+07 with 0.00E+00 and 12.2E+6 with #0.0E+0.
    [Theory]
    [InlineData("0.00E+00", 12200000, "1.22E+07")]
    [InlineData("#0.0E+0", 12200000, "12.2E+6")]
    [InlineData("0.00E+00", 0.000123, "1.23E-04")]
    [InlineData("0.00E-00", 12200000, "1.22E07")]
    [InlineData("0.00E+00", 0, "0.00E+00")]
    [InlineData("##0.0E+0", 12345, "12.3E+3")]
    [InlineData("0.0E+00", 9.96, "1.0E+01")]

    public void Scientific(string code, double v, string expected) => Assert.Equal(expected, F(code, v));

    [Theory]
    [InlineData("0%", 0.08, "8%")]
    [InlineData("0%", 2.8, "280%")]
    [InlineData("0.00%", 0.12345, "12.35%")]
    [InlineData("0.0%", 0.0005, "0.1%")]
    public void Percent(string code, double v, string expected) => Assert.Equal(expected, F(code, v));

    // The fractions table: 5.25 and 5.3 show 5 1/4 and 5 3/10 with "# ???/???"; the numerators line up.
    [Theory]
    [InlineData("# ???/???", 5.25, "5   1/4  ")]
    [InlineData("# ???/???", 5.3, "5   3/10 ")]
    [InlineData("# ?/?", 0.5, " 1/2")]
    [InlineData("# ?/?", 2, "2    ")]
    [InlineData("?/?", 0.75, "3/4")]
    [InlineData("?/?", 1.5, "3/2")]
    [InlineData("# ?/8", 2.5, "2 4/8")]
    [InlineData("# ?/?", 0.9999, "1    ")]
    public void Fractions(string code, double v, string expected) => Assert.Equal(expected, F(code, v));

    // Rounding is half away from zero on the 15-digit decimal text, not on the binary value.
    [Theory]
    [InlineData("0.00", 0.285, "0.29")]
    [InlineData("0.00", 1.005, "1.01")]
    [InlineData("0.0", 0.25, "0.3")]
    [InlineData("0", 2.5, "3")]
    [InlineData("0", -2.5, "-3")]
    [InlineData("0.00", 0.1 + 0.2, "0.30")]
    [InlineData("0", 1234567890123456789.0, "1234567890123460000")]
    [InlineData("0.000000000000000000", 0.1, "0.100000000000000000")]
    [InlineData("0", 0.49999999999999994, "1")] // 15 significant digits: 0.500000000000000
    [InlineData("0.00", -0.001, "0.00")] // observed in Excel: no minus when the result rounds to zero
    [InlineData("0", -0.0, "0")]
    [InlineData("0.0", 99.95, "100.0")]
    [InlineData("#,##0", 999999.5, "1,000,000")]
    public void Rounding(string code, double v, string expected) => Assert.Equal(expected, F(code, v));

    [Fact]
    public void ExtremeNumbers()
    {
        Assert.Equal("1" + new string('0', 300), F("0", 1e300));
        Assert.Equal("0", F("0", 1e-300));
        Assert.Equal("1.00E+300", F("0.00E+00", 1e300));
        Assert.Equal("1.00E-300", F("0.00E+00", 1e-300));
    }

    // Sections: positive;negative;zero;text and conditions.
    [Fact]
    public void Sections()
    {
        // "Review guidelines": [Blue]#,##0.00_);[Red](#,##0.00);0.00;"sales "@
        NumberFormat f = NumberFormat.Parse("[Blue]#,##0.00_);[Red](#,##0.00);0.00;\"sales \"@");
        Assert.Equal("1,234.50 ", f.Format(1234.5));
        Assert.Equal("(5.00)", f.Format(-5));
        Assert.Equal("0.00", f.Format(0));
        Assert.Equal("sales x", f.FormatText("x"));
        Assert.Equal("Blue", f.TryFormat(1, new char[16]).Color!.Value.Name);
        Assert.Equal(0xFF0000, f.TryFormat(-1, new char[16]).Color!.Value.Rgb);
        Assert.Equal(4, f.Sections.Count);

        // Two sections: the second is for negative numbers, without a minus sign.
        Assert.Equal("5;5", F("0;0", 5) + ";" + F("0;0", -5));
        Assert.Equal("-5", F("0", -5));

        // "$0.00" Surplus";$-0.00" Shortage"" from the guidelines.
        NumberFormat g = NumberFormat.Parse("$0.00\" Surplus\";$-0.00\" Shortage\"");
        Assert.Equal("$125.74 Surplus", g.Format(125.74));
        Assert.Equal("$-125.74 Shortage", g.Format(-125.74));

        // Hide zeros / hide everything.
        Assert.Equal(string.Empty, F("0;-0;;@", 0));
        Assert.Equal("5", F("0;-0;;@", 5));
        Assert.Equal(string.Empty, F(";;;", 5));
        Assert.Equal(string.Empty, NumberFormat.Parse(";;;").FormatText("abc"));

        // Conditions: [Red][<=100];[Blue][>100]
        NumberFormat c = NumberFormat.Parse("[Red][ <=100];[Blue][>100]");
        Assert.Equal("Red", c.TryFormat(50, new char[8]).Color!.Value.Name);
        Assert.Equal("Blue", c.TryFormat(500, new char[8]).Color!.Value.Name);
        NumberFormat d = NumberFormat.Parse("[>100]\"big\";[<=100]0.0;0.00");
        Assert.Equal("big", d.Format(500));
        Assert.Equal("5.0", d.Format(5));
        Assert.Equal("-5.0", d.Format(-5));
    }

    [Fact]
    public void TextAndLiterals()
    {
        Assert.Equal("abc", NumberFormat.Parse("0.00").FormatText("abc")); // no text section: unchanged
        Assert.Equal("x: abc", NumberFormat.Parse("0;0;0;\"x: \"@").FormatText("abc"));
        Assert.Equal("x", NumberFormat.Parse("0;0;0;\"x\"").FormatText("abc")); // a text section without @ shows its literals, not the text
        Assert.Equal("gross receipts for abc", NumberFormat.Parse("\"gross receipts for \"@").FormatText("abc"));
        Assert.Equal("32\"", F("#\\\"", 32));
        Assert.Equal("5 x", F("0\\ \\x", 5));
        Assert.Equal("5", F("@", 5));
    }

    // _ pads one space, * is a hint, not output.
    [Fact]
    public void PaddingAndFill()
    {
        NumberFormat f = NumberFormat.Parse("_(* #,##0_);_(* \\(#,##0\\);_(* \"-\"_);_(@_)");
        char[] buf = new char[64];
        FormatResult r = f.TryFormat(1234, buf);
        Assert.Equal(" 1,234 ", new string(buf, 0, r.Length));
        Assert.Equal(1, r.FillPosition);
        Assert.Equal(' ', r.FillCharacter);
        Assert.Equal(NumberFormatCategory.Accounting, f.Category);
        Assert.Equal(" - ", f.Format(0));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(-1, "-1")]
    [InlineData(1234567.891, "1234567.891")]
    [InlineData(123456789012, "1.23457E+11")]
    [InlineData(12345678901, "12345678901")]
    [InlineData(0.1 + 0.2, "0.3")]
    [InlineData(1.0 / 3, "0.333333333")]
    [InlineData(100.0 / 3, "33.33333333")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(0.00001, "0.00001")] // observed in Excel: the plain form stays when it keeps as many digits
    [InlineData(1.2345e-8, "1.2345E-08")]
    [InlineData(1e300, "1E+300")]
    [InlineData(0.000123456789, "0.000123457")]
    [InlineData(123456.7891234, "123456.7891")]
    public void General(double v, string expected) => Assert.Equal(expected, F("General", v));

    [Fact]
    public void GeneralWidthIsAParameter()
    {
        Assert.Equal("0.33333", NumberFormat.General.Format(1.0 / 3, new FormatOptions(GeneralWidth: 7)));
        Assert.Equal("5 apples", F("General\" apples\"", 5));
    }

    [Fact]
    public void MaxWidthReportsDoesNotFit()
    {
        NumberFormat f = NumberFormat.Parse("#,##0.00");
        FormatResult r = f.TryFormat(1234567.891, new char[32], new FormatOptions(MaxWidth: 6));
        Assert.Equal(FormatStatus.DoesNotFit, r.Status);
        Assert.Equal("######", f.Format(1234567.891, new FormatOptions(MaxWidth: 6)));
        Assert.Equal("1,234,567.89", f.Format(1234567.891));
    }

    [Fact]
    public void DestinationTooSmallIsReported()
    {
        FormatResult r = NumberFormat.Parse("#,##0.00").TryFormat(1234567.891, new char[4]);
        Assert.Equal(FormatStatus.DestinationTooSmall, r.Status);
        Assert.Equal(301, NumberFormat.Parse("0").Format(1e300).Length);
    }

    [Fact]
    public void NanAndInfinity()
    {
        Assert.Equal(FormatStatus.InvalidNumber, NumberFormat.General.TryFormat(double.NaN, new char[16]).Status);
        Assert.Equal("#NUM!", NumberFormat.General.Format(double.PositiveInfinity));
    }

    [Fact]
    public void CultureSeparators()
    {
        var de = new FormatOptions(Culture: NumberFormatCulture.Get(0x0407));
        Assert.Equal("1.234.567,89", F("#,##0.00", 1234567.891, de));
        Assert.Equal("0,5", F("General", 0.5, de));
        var fr = new FormatOptions(Culture: NumberFormatCulture.Get(0x040C));
        Assert.Equal("1\u202F234,50", F("#,##0.00", 1234.5, fr));
    }

    // Dates: the 1900 system's serials (the leap-year quirk and serial 0).
    [Theory]
    [InlineData(0, "1900-01-00 Saturday")]
    [InlineData(1, "1900-01-01 Sunday")]
    [InlineData(59, "1900-02-28 Tuesday")]
    [InlineData(60, "1900-02-29 Wednesday")]
    [InlineData(61, "1900-03-01 Thursday")]
    [InlineData(36526, "2000-01-01 Saturday")]
    [InlineData(43831, "2020-01-01 Wednesday")]
    [InlineData(45000, "2023-03-15 Wednesday")]
    [InlineData(2958465, "9999-12-31 Friday")]
    public void Serial1900(double serial, string expected) =>
        Assert.Equal(expected, F("yyyy-mm-dd dddd", serial));

    [Fact]
    public void Serial1904()
    {
        var o = new FormatOptions(DateSystem.Date1904);
        Assert.Equal("1904-01-01 Friday", F("yyyy-mm-dd dddd", 0, o));
        Assert.Equal("2023-03-15", F("yyyy-mm-dd", 45000 - 1462, o));
        Assert.True(ExcelDate.TryToSerial(2023, 3, 15, DateSystem.Date1904, out int s));
        Assert.Equal(45000 - 1462, s);
        Assert.False(ExcelDate.TryToSerial(1900, 2, 29, DateSystem.Date1904, out _));
        Assert.True(ExcelDate.TryToSerial(1900, 2, 29, DateSystem.Date1900, out int s60));
        Assert.Equal(60, s60);
        Assert.False(ExcelDate.TryToSerial(1900, 2, 30, DateSystem.Date1900, out _));
        Assert.True(ExcelDate.TryToSerial(9999, 12, 31, DateSystem.Date1900, out int mx));
        Assert.Equal(2958465, mx);
        Assert.True(ExcelDate.TryToSerial(9999, 12, 31, DateSystem.Date1904, out int mx4));
        Assert.Equal(2957003, mx4);
    }

    [Fact]
    public void DateRange()
    {
        NumberFormat f = NumberFormat.Parse("yyyy-mm-dd");
        Assert.Equal(FormatStatus.DateOutOfRange, f.TryFormat(-1, new char[32]).Status);
        Assert.Equal(FormatStatus.DateOutOfRange, f.TryFormat(2958466, new char[32]).Status);
        Assert.Equal(FormatStatus.Ok, f.TryFormat(2958465.4, new char[32]).Status);
        Assert.Equal(FormatStatus.DateOutOfRange, f.TryFormat(2958465.9999999999, new char[32]).Status);
        Assert.Equal("########", f.Format(-1));
        Assert.Equal("####", f.Format(-1, new FormatOptions(MaxWidth: 4)));
        Assert.False(ExcelDate.TryFromSerial(double.NaN, DateSystem.Date1900, out _));
    }

    // Codes from the dates-and-times table.
    [Theory]
    [InlineData("yy", 45000, "23")]
    [InlineData("yyyy", 45000, "2023")]
    [InlineData("m", 45000, "3")]
    [InlineData("mm", 45000, "03")]
    [InlineData("mmm", 45000, "Mar")]
    [InlineData("mmmm", 45000, "March")]
    [InlineData("mmmmm", 45000, "M")]
    [InlineData("d", 45000, "15")]
    [InlineData("dd", 45005, "20")]
    [InlineData("d", 45005 - 17, "3")]
    [InlineData("dd", 45005 - 17, "03")]
    [InlineData("ddd", 45000, "Wed")]
    [InlineData("dddd", 45000, "Wednesday")]
    [InlineData("m/d/yyyy", 45000, "3/15/2023")]
    [InlineData("d-mmm-yy", 45000, "15-Mar-23")]
    [InlineData("mmm-yy", 45000, "Mar-23")]
    public void DateCodes(string code, double v, string expected) => Assert.Equal(expected, F(code, v));

    [Theory]
    [InlineData("h AM/PM", 4.0 / 24, "4 AM")]
    [InlineData("h:mm AM/PM", 16.6 / 24, "4:36 PM")]
    [InlineData("h:mm:ss A/P", (16 + 36 / 60.0 + 3 / 3600.0) / 24, "4:36:03 P")]
    [InlineData("h:mm:ss.00", (4 + 36 / 60.0 + 3.75 / 3600.0) / 24, "4:36:03.75")]
    [InlineData("hh:mm:ss", 0.5, "12:00:00")]
    [InlineData("h:mm", 0.0, "0:00")]
    [InlineData("h AM/PM", 0.0, "12 AM")]
    [InlineData("h AM/PM", 0.5, "12 PM")]
    [InlineData("h:mm am/pm", 0.75, "6:00 PM")] // observed in Excel: lower-case am/pm still shows capitals
    [InlineData("h:mm a/p", 0.25, "6:00 a")]
    [InlineData("[h]:mm", 1.0 / 24 + 2.0 / 1440, "1:02")]
    [InlineData("[mm]:ss", 62.0 / 1440 + 16.0 / 86400, "62:16")]
    [InlineData("[ss].00", 3735.8 / 86400, "3735.80")]
    [InlineData("[h]:mm:ss", 1.5, "36:00:00")]
    [InlineData("[hh]", 1.0 / 24, "01")]
    [InlineData("mm:ss", 0.0006944444444444445, "01:00")]
    [InlineData("mm:ss.0", 0.0006944444444444445 + 0.5 / 86400, "01:00.5")]
    public void TimeCodes(string code, double v, string expected) => Assert.Equal(expected, F(code, v));

    // "m or mm directly after h or before ss is minutes, otherwise the month".
    [Theory]
    [InlineData("h:mm", 45000.5 + 5.0 / 1440, "12:05")]
    [InlineData("mm:ss", 45000 + 5.0 / 1440 + 9.0 / 86400, "05:09")]
    [InlineData("mm/dd", 45000, "03/15")]
    [InlineData("hh \"h\" mm \"min\"", 45000 + 5.0 / 1440, "00 h 05 min")]
    [InlineData("m/d/yy h:mm", 45000.75, "3/15/23 18:00")]
    [InlineData("yyyy-mm-dd hh:mm:ss", 45000.99999, "2023-03-15 23:59:59")]
    public void MinuteOrMonth(string code, double v, string expected) => Assert.Equal(expected, F(code, v));

    [Fact]
    public void TimeRoundsToMillisecondsAndCarries()
    {
        // 23:59:59.9996 rounds to the next day with whole-second formats; the fraction digits decide the rounding unit.
        Assert.Equal("2023-03-16 00:00:00", F("yyyy-mm-dd hh:mm:ss", 45000 + (86399.9996 / 86400)));
        Assert.Equal("23:59:59.999", F("hh:mm:ss.000", 45000 + (86399.9994 / 86400)));
        Assert.True(ExcelDate.TryFromSerial(45000 + (86399.9996 / 86400), DateSystem.Date1900, out ExcelDateTime t));
        Assert.Equal(16, t.Day);
        Assert.Equal(0, t.Hour);
        Assert.Equal("12:00:01", F("hh:mm:ss", 0.5 + (0.6 / 86400)));
    }

    // Locale tags, currency tags and locale names.
    [Fact]
    public void LocaleTags()
    {
        Assert.Equal("15. März 2023", F("[$-407]d. mmmm yyyy", 45000));
        Assert.Equal("mercredi", F("[$-40C]dddd", 45000));
        Assert.Equal("15 mars 2023", F("[$-40C]d mmmm yyyy", 45000));
        Assert.Equal("€1,234.50", F("[$€-407]#,##0.00", 1234.5));
        Assert.Equal("USD 5", F("[$USD] 0", 5));
        Assert.Equal("Wednesday, March 15, 2023", F("[$-F800]", 45000));
        Assert.Equal("12:00:00 PM", F("[$-F400]", 0.5));
        Assert.Equal("2023年3月15日", F("[$-411]yyyy\"年\"m\"月\"d\"日\"", 45000));
        Assert.Equal("水曜日", F("[$-411]dddd", 45000));
        Assert.Equal("午前 9:00", F("[$-411]AM/PM h:mm", 0.375));
        Assert.Equal("令和5年", F("[$-411]ggge\"年\"", 45000));
        Assert.Equal("2023", F("[$-404]e", 45000)); // observed in Excel: the Taiwan tag alone keeps the Gregorian year
        Assert.Equal("2566", F("bbbb", 45000));
    }

    [Fact]
    public void DbNum()
    {
        // All of these were read back from Excel (cell display, zh-CN and ja-JP tags): digits one by one, place values only for
        // unpadded date parts, and no effect at all without a CJK locale.
        Assert.Equal("123", F("[DBNum1]0", 123));
        Assert.Equal("一二三", F("[DBNum1][$-804]0", 123));
        Assert.Equal("一○五", F("[DBNum1][$-804]0", 105));
        Assert.Equal("一〇五", F("[DBNum1][$-411]0", 105));
        Assert.Equal("○", F("[DBNum1][$-804]0", 0));
        Assert.Equal("壹贰叁", F("[DBNum2][$-804]0", 123));
        Assert.Equal("１２３", F("[$-411][DBNum3]0", 123));
        Assert.Equal("二○二三", F("[DBNum1][$-804]yyyy", 45000));
        Assert.Equal("十二月三十一日", F("[DBNum1][$-804]m\"月\"d\"日\"", 44926));
        Assert.Equal("壹拾贰月叁拾壹日", F("[DBNum2][$-804]m\"月\"d\"日\"", 44926));
    }

    [Fact]
    public void BuiltIns()
    {
        Assert.Equal("General", NumberFormat.GetBuiltInCode(0));
        Assert.Equal("0.00", NumberFormat.GetBuiltInCode(2));
        Assert.Equal("#,##0.00", NumberFormat.GetBuiltInCode(4));
        Assert.Equal("0%", NumberFormat.GetBuiltInCode(9));
        Assert.Equal("0.00E+00", NumberFormat.GetBuiltInCode(11));
        Assert.Equal("# ?/?", NumberFormat.GetBuiltInCode(12));
        Assert.Equal("m/d/yyyy", NumberFormat.GetBuiltInCode(14));
        Assert.Equal("dd.mm.yyyy", NumberFormat.GetBuiltInCode(14, NumberFormatCulture.Get(0x0407)));
        Assert.Equal("d-mmm-yy", NumberFormat.GetBuiltInCode(15));
        Assert.Equal("h:mm AM/PM", NumberFormat.GetBuiltInCode(18));
        Assert.Equal("m/d/yyyy h:mm", NumberFormat.GetBuiltInCode(22));
        Assert.Equal("\"$\"#,##0_);\\(\"$\"#,##0\\)", NumberFormat.GetBuiltInCode(5));
        Assert.Equal("#,##0\\ \"€\"_);\\(#,##0\\ \"€\"\\)", NumberFormat.GetBuiltInCode(5, NumberFormatCulture.Get(0x0407)));
        Assert.Equal("#,##0_);[Red](#,##0)", NumberFormat.GetBuiltInCode(38));
        Assert.Equal("[h]:mm:ss", NumberFormat.GetBuiltInCode(46));
        Assert.Equal("@", NumberFormat.GetBuiltInCode(49));
        Assert.Null(NumberFormat.GetBuiltInCode(23));
        Assert.Null(NumberFormat.GetBuiltInCode(164));
        Assert.Equal(2, NumberFormat.FindBuiltInId("0.00"));
        Assert.Equal(-1, NumberFormat.FindBuiltInId("0.000"));
        // ECMA-376 Part 1 §18.8.30: the East Asian IDs per locale.
        Assert.Equal("[$-411]ggge\"年\"m\"月\"d\"日\"", NumberFormat.GetBuiltInCode(28, NumberFormatCulture.Get(0x0411)));
        Assert.Equal("上午/下午hh\"時\"mm\"分\"", NumberFormat.GetBuiltInCode(34, NumberFormatCulture.Get(0x0404)));
        Assert.Equal("m\"月\"d\"日\"", NumberFormat.GetBuiltInCode(28, NumberFormatCulture.Get(0x0804)));
        Assert.Equal("mm-dd", NumberFormat.GetBuiltInCode(28, NumberFormatCulture.Get(0x0412)));
        Assert.Null(NumberFormat.GetBuiltInCode(28));
        foreach (NumberFormatCulture culture in NumberFormatCulture.Supported)
        {
            for (int id = 0; id <= 58; id++)
            {
                string? c = NumberFormat.GetBuiltInCode(id, culture);
                if (c is not null)
                {
                    Assert.True(NumberFormat.TryParse(c, out _, out NumberFormatDefect? d), $"{culture.Name} id {id}: {c}: {d}");
                }
            }
        }

        Assert.Equal("(1,234)", NumberFormat.GetBuiltIn(37)!.Format(-1234).Replace(" ", string.Empty));
        Assert.Equal("1:30:00", NumberFormat.GetBuiltIn(21)!.Format(1.5 / 24));
        Assert.Equal("$1,235 ", NumberFormat.GetBuiltIn(5)!.Format(1234.5).Replace("$ ", "$"));
    }

    [Theory]
    [InlineData("General", NumberFormatCategory.General)]
    [InlineData("0", NumberFormatCategory.Number)]
    [InlineData("#,##0.00", NumberFormatCategory.Number)]
    [InlineData("0%", NumberFormatCategory.Percentage)]
    [InlineData("0.00E+00", NumberFormatCategory.Scientific)]
    [InlineData("# ?/?", NumberFormatCategory.Fraction)]
    [InlineData("yyyy-mm-dd", NumberFormatCategory.Date)]
    [InlineData("h:mm:ss", NumberFormatCategory.Time)]
    [InlineData("[h]:mm", NumberFormatCategory.Time)]
    [InlineData("m/d/yy h:mm", NumberFormatCategory.DateTime)]
    [InlineData("@", NumberFormatCategory.Text)]
    [InlineData("\"$\"#,##0.00", NumberFormatCategory.Currency)]
    [InlineData("[$€-407]#,##0.00", NumberFormatCategory.Currency)]
    [InlineData("\"abc\"", NumberFormatCategory.Custom)]
    [InlineData("0.00;[Red]-0.00", NumberFormatCategory.Number)]
    public void Categories(string code, NumberFormatCategory expected) => Assert.Equal(expected, NumberFormat.Parse(code).Category);

    [Theory]
    [InlineData("General", "G")]
    [InlineData("0", "F0")]
    [InlineData("0.00", "F2")]
    [InlineData("#,##0", ",0")]
    [InlineData("#,##0.00", ",2")]
    [InlineData("0%", "P0")]
    [InlineData("0.00%", "P2")]
    [InlineData("0.00E+00", "S2")]
    [InlineData("d-mmm-yy", "D1")]
    [InlineData("d-mmm", "D2")]
    [InlineData("mmm-yy", "D3")]
    [InlineData("m/d/yy", "D4")]
    [InlineData("mm/dd", "D5")]
    [InlineData("h:mm:ss AM/PM", "D6")]
    [InlineData("h:mm AM/PM", "D7")]
    [InlineData("h:mm:ss", "D8")]
    [InlineData("h:mm", "D9")]
    [InlineData("0.00;[Red]0.00", "F2-")]
    [InlineData("0.00_);(0.00)", "F2()")]
    public void CellFormatCodes(string code, string expected) => Assert.Equal(expected, NumberFormat.Parse(code).GetCellFormatCode());

    // Invalid codes are typed defects, never other exceptions.
    [Theory]
    [InlineData("0.00\"abc", "numfmt.unterminated-string")]
    [InlineData("0.00[Red", "numfmt.unterminated-bracket")]
    [InlineData("0;0;0;0;0", "numfmt.too-many-sections")]
    [InlineData("0.0.0", "numfmt.multiple-decimal-points")]
    [InlineData("[Foo]0", "numfmt.unknown-bracket")]
    [InlineData("[>x]0", "numfmt.invalid-condition")]
    [InlineData("[=>5]0", "numfmt.invalid-condition")]
    [InlineData("0 yyyy", "numfmt.mixed-codes")]
    [InlineData("0.00E+", "numfmt.exponent-without-digits")]
    [InlineData("0 abc", "numfmt.unexpected-character")]
    [InlineData("0\\", "numfmt.dangling-escape")]
    [InlineData("mmmmmm", "numfmt.invalid-date-code")]
    [InlineData("0;0;0;[>5]@", "numfmt.condition-on-text-section")]
    [InlineData("0 ?/123456", "numfmt.fraction-denominator")]
    [InlineData("General0", "numfmt.mixed-codes")]
    [InlineData("0E+0E+0", "numfmt.multiple-exponents")]
    public void InvalidCodes(string code, string defect)
    {
        Assert.False(NumberFormat.TryParse(code, out NumberFormat? f, out NumberFormatDefect? d));
        Assert.Null(f);
        Assert.Equal(defect, d!.Code);
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => NumberFormat.Parse(code));
        Assert.Equal(defect, ex.Defect.Code);
    }

    [Fact]
    public void EmptyIsGeneral()
    {
        Assert.Equal("5", NumberFormat.Parse(string.Empty).Format(5));
        Assert.Equal("5", NumberFormat.Parse(null).Format(5));
    }

    [Fact]
    public void ParsedFormatIsReusableAcrossThreads()
    {
        NumberFormat f = NumberFormat.Parse("#,##0.00");
        Parallel.For(0, 64, i => Assert.Equal("1,234.00", f.Format(1234)));
    }

    [Fact]
    public void SpanPathDoesNotAllocate()
    {
        NumberFormat[] formats = [NumberFormat.Parse("#,##0.00"), NumberFormat.Parse("yyyy-mm-dd hh:mm:ss"), NumberFormat.Parse("0.00E+00"), NumberFormat.Parse("# ??/??"), NumberFormat.General, NumberFormat.Parse("0.0%;[Red]-0.0%")];
        char[] buf = new char[128];

        // Warm up with the measured loop itself, so tiered compilation has promoted these paths before the count starts.
        for (int i = 0; i < 2000; i++)
        {
            foreach (NumberFormat f in formats)
            {
                _ = f.TryFormat(12345.6789 + i, buf);
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
        {
            foreach (NumberFormat f in formats)
            {
                _ = f.TryFormat(12345.6789 + i, buf);
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void GeneralInsideASectionAndOtherDetails()
    {
        Assert.Equal("-x1", F("\"x\"General", -1));
        Assert.Equal("15 Март 2023", F("[$-419]d mmmm yyyy", 45000)); // observed in Excel: capitalised, nominative
        Assert.Equal("Март", F("[$-419]mmmm", 45000));
        Assert.Equal("0    ", F("# ?/?", 0));
        Assert.Equal("0 0/1", F("0 0/0", 0));
        Assert.Equal("2    ", F("# ?/8", 2)); // observed in Excel: a zero numerator over a fixed denominator is blank
        Assert.Equal("0.333333333333333", NumberFormat.General.Format(1.0 / 3, new FormatOptions(GeneralWidth: 30)));
        Assert.Equal("1.23456789012346E+17", NumberFormat.General.Format(123456789012345678.0, new FormatOptions(GeneralWidth: 30)));
        Assert.Equal("十二月三十一日", F("[DBNum1][$-804]m\"月\"d\"日\"", 44926));
    }
}
