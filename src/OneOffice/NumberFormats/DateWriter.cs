using System;

namespace RecursiveAI.OneOffice.NumberFormats;

// Date, time and elapsed-time sections.
// Microsoft's "Number format codes": m or mm directly after h or before ss is minutes; AM/PM switches hours to the 12-hour clock;
// [h], [m] and [s] are elapsed time; ".0" after seconds shows fractions of a second.
internal static class DateWriter
{
    private static readonly (int Y, int M, int D, string Kanji, string Letter)[] JapaneseEras =
    [
        (2019, 5, 1, "令和", "R"),
        (1989, 1, 8, "平成", "H"),
        (1926, 12, 25, "昭和", "S"),
        (1912, 7, 30, "大正", "T"),
        (1868, 9, 8, "明治", "M"),
    ];

    internal static FormatStatus Emit(ref Writer w, NumberFormatSection sec, double value, FormatOptions o, NumberFormatCulture optionCulture)
    {
        NumberFormatCulture culture = sec.LocaleId != 0 ? NumberFormatCulture.Get(sec.LocaleId) : optionCulture;
        if (IsOnlyElapsed(sec))
        {
            return EmitOnlyElapsed(ref w, sec, value);
        }

        int k = sec.FractionSecondDigits;
        int unit = k >= 3 ? 1 : k == 2 ? 10 : k == 1 ? 100 : 1000;
        if (!ExcelDate.TryRoundToMilliseconds(value, unit, o.DateSystem, out long ms) || !ExcelDate.TryFromMilliseconds(ms, o.DateSystem, out ExcelDateTime t))
        {
            return FormatStatus.DateOutOfRange;
        }

        EraStyle era = sec.Era ?? culture.EraStyle;
        bool withDay = Array.Exists(sec.Elements, x => x.Kind == EK.Day && x.N <= 2);
        foreach (Element e in sec.Elements)
        {
            switch (e.Kind)
            {
                case EK.Literal:
                    w.Append(e.Text);
                    break;
                case EK.Fill:
                    w.SetFill(e.Ch);
                    break;
                case EK.Year:
                {
                    // Observed in Excel: a Buddhist calendar in the locale tag ([$-D07041E]) makes yyyy the Buddhist year.
                    int year = sec.CalendarId == 7 ? t.Year + 543 : t.Year;
                    NumberWriter.AppendPadded(ref w, e.N == 2 ? year % 100 : year, e.N);
                    break;
                }

                case EK.Month:
                    switch (e.N)
                    {
                        case 1:
                        case 2:
                            NumberWriter.AppendPadded(ref w, t.Month, e.N);
                            break;
                        case 3:
                            w.Append(culture.GetMonthName(t.Month, true));
                            break;
                        case 4:
                            w.Append(withDay ? culture.GetMonthNameWithDay(t.Month) : culture.GetMonthName(t.Month, false));
                            break;
                        default:
                            w.Append(FirstLetter(culture.GetMonthName(t.Month, false)));
                            break;
                    }

                    break;
                case EK.Day:
                    switch (e.N)
                    {
                        case 1:
                        case 2:
                            NumberWriter.AppendPadded(ref w, t.Day, e.N);
                            break;
                        case 3:
                            w.Append(culture.GetDayName(t.DayOfWeek, true));
                            break;
                        default:
                            w.Append(culture.GetDayName(t.DayOfWeek, false));
                            break;
                    }

                    break;
                case EK.Hour:
                {
                    int h = t.Hour;
                    if (sec.HasAmPm)
                    {
                        h %= 12;
                        if (h == 0)
                        {
                            h = 12;
                        }
                    }

                    NumberWriter.AppendPadded(ref w, h, e.N);
                    break;
                }

                case EK.Minute:
                    NumberWriter.AppendPadded(ref w, t.Minute, e.N);
                    break;
                case EK.Second:
                    NumberWriter.AppendPadded(ref w, t.Second, e.N);
                    break;
                case EK.ElapsedHour:
                    NumberWriter.AppendPadded(ref w, ms / 3_600_000, e.N);
                    break;
                case EK.ElapsedMinute:
                    NumberWriter.AppendPadded(ref w, ms / 60_000, e.N);
                    break;
                case EK.ElapsedSecond:
                    NumberWriter.AppendPadded(ref w, ms / 1000, e.N);
                    break;
                case EK.FractionSecond:
                {
                    w.Append(culture.DecimalSeparator);
                    int frac = t.Millisecond;
                    for (int i = 0; i < e.N; i++)
                    {
                        int div = i == 0 ? 100 : i == 1 ? 10 : i == 2 ? 1 : 0;
                        w.Append(div == 0 ? '0' : (char)('0' + (frac / div % 10)));
                    }

                    break;
                }

                case EK.AmPm:
                    AmPm(ref w, e.N, t.Hour < 12, culture);
                    break;
                case EK.EraYear:
                    NumberWriter.AppendPadded(ref w, EraYear(t, era), e.N == 2 ? 2 : (era == EraStyle.Gregorian ? 4 : 1));
                    break;
                case EK.EraName:
                    w.Append(EraName(t, era, e.N));
                    break;
                case EK.BuddhistYear:
                {
                    // Observed in Excel: b is the Buddhist year on its own, but with any locale tag that does not name the Buddhist
                    // calendar it is the Gregorian year.
                    int year = sec.LocaleId != 0 && sec.CalendarId != 7 ? t.Year : t.Year + 543;
                    NumberWriter.AppendPadded(ref w, e.N == 2 ? year % 100 : year, e.N);
                    break;
                }
            }
        }

        if (sec.NumeralSystem == 0x0D)
        {
            // Thai digits: the written date shows ๐-๙ for 0-9.
            Span<char> done = w.Written;
            for (int i = 0; i < done.Length; i++)
            {
                if (done[i] is >= '0' and <= '9')
                {
                    done[i] = (char)('\u0E50' + (done[i] - '0'));
                }
            }
        }

        return FormatStatus.Ok;
    }

    // Observed in Excel: a format whose only date or time part is one elapsed field ([h], [m] or [s]) is just a count, so it has no
    // date range and a negative value shows a minus sign; with any other date or time part Excel shows # for negative values.
    private static bool IsOnlyElapsed(NumberFormatSection sec)
    {
        int parts = 0;
        bool elapsed = false;
        foreach (Element e in sec.Elements)
        {
            if (e.Kind is EK.Year or EK.Month or EK.Day or EK.Hour or EK.Minute or EK.Second or EK.FractionSecond or EK.AmPm
                or EK.EraYear or EK.EraName or EK.BuddhistYear)
            {
                parts++;
            }
            else if (e.Kind is EK.ElapsedHour or EK.ElapsedMinute or EK.ElapsedSecond)
            {
                parts++;
                elapsed = true;
            }
        }

        return parts == 1 && elapsed;
    }

    private static FormatStatus EmitOnlyElapsed(ref Writer w, NumberFormatSection sec, double value)
    {
        double a = Math.Abs(value);
        double wholeSeconds = Math.Round(a * 86_400.0, MidpointRounding.AwayFromZero);
        bool wroteSign = false;
        foreach (Element e in sec.Elements)
        {
            switch (e.Kind)
            {
                case EK.Literal:
                    w.Append(e.Text);
                    break;
                case EK.Fill:
                    w.SetFill(e.Ch);
                    break;
                case EK.ElapsedHour or EK.ElapsedMinute or EK.ElapsedSecond:
                    // Observed in Excel: the time is first rounded to whole seconds, as for every time format, then cut to the unit.
                    double unitSeconds = e.Kind == EK.ElapsedHour ? 3600 : e.Kind == EK.ElapsedMinute ? 60 : 1;
                    double count = Math.Floor(wholeSeconds / unitSeconds);
                    if (value < 0 && count != 0 && !wroteSign)
                    {
                        w.Append('-');
                        wroteSign = true;
                    }

                    NumberWriter.AppendWholeNumber(ref w, count, e.N);
                    break;
            }
        }

        return FormatStatus.Ok;
    }

    private static string FirstLetter(string s)
    {
        // The first text element; names are BMP text in every table locale.
        return s.Length == 0 ? s : s.Substring(0, 1);
    }

    private static void AmPm(ref Writer w, int style, bool am, NumberFormatCulture c)
    {
        if (style >= 4)
        {
            // A written-out pair such as 上午/下午 shows its own half.
            string pair = FormatParser.AmPmPairs[style - 4];
            int slash = pair.IndexOf('/', StringComparison.Ordinal);
            w.Append(am ? pair.AsSpan(0, slash) : pair.AsSpan(slash + 1));
            return;
        }

        string full = am ? c.AmDesignator : c.PmDesignator;
        switch (style)
        {
            case 0:
                w.Append(full);
                break;
            case 2:
                w.Append(am ? 'A' : 'P');
                break;
            default:
                w.Append(am ? 'a' : 'p');
                break;
        }
    }

    private static int EraYear(ExcelDateTime t, EraStyle era)
    {
        switch (era)
        {
            case EraStyle.Minguo:
                return t.Year - 1911;
            case EraStyle.Buddhist:
                return t.Year + 543;
            case EraStyle.Japanese:
                foreach (var e in JapaneseEras)
                {
                    if (OnOrAfter(t, e.Y, e.M, e.D))
                    {
                        return t.Year - e.Y + 1;
                    }
                }

                return t.Year;
            default:
                return t.Year;
        }
    }

    private static string EraName(ExcelDateTime t, EraStyle era, int n)
    {
        if (era == EraStyle.Minguo)
        {
            return "民國";
        }

        if (era == EraStyle.Japanese)
        {
            foreach (var e in JapaneseEras)
            {
                if (OnOrAfter(t, e.Y, e.M, e.D))
                {
                    return n == 1 ? e.Letter : n == 2 ? e.Kanji.Substring(0, 1) : e.Kanji;
                }
            }
        }

        // Observed in Excel: the Gregorian calendar has no era name in a format code.
        return string.Empty;
    }

    private static bool OnOrAfter(ExcelDateTime t, int y, int m, int d) =>
        t.Year != y ? t.Year > y : t.Month != m ? t.Month > m : t.Day >= d;
}

// [DBNum1], [DBNum2] and [DBNum3]: the digits of the displayed number written as CJK numerals.
// Observed in Excel (zh-CN, zh-TW, ja-JP and ko-KR tags, cell display): the digits of a number are written one by one (123 shows
// as 一二三, 105 as 一〇五), and so are the digits of a year, of a padded two-digit part and after a decimal point. A month, day, hour,
// minute or second written without a leading zero is a place-value numeral (12 is 十二, 31 is 三十一; DBNum2: 壹拾贰, 叁拾壹).
// DBNum3 writes full-width digits throughout. Without a CJK locale (in the tag or in the options) the codes change nothing.
internal static class DbNumbers
{
    internal const int MaxChars = 256;

    internal static void Apply(scoped ReadOnlySpan<char> src, ref Writer w, NumberFormatSection sec, NumberFormatCulture culture, FormatOptions o)
    {
        NumberFormatCulture c = sec.LocaleId != 0 ? NumberFormatCulture.Get(sec.LocaleId) : (o.Culture ?? culture);
        if (c.Lcid is not (0x0804 or 0x0404 or 0x0411 or 0x0412))
        {
            w.Append(src);
            return;
        }

        int len = src.Length;
        bool dateLike = sec.Kind == NumberFormatSectionKind.DateTime;
        int i = 0;
        while (i < len)
        {
            if (!char.IsAsciiDigit(src[i]))
            {
                w.Append(src[i]);
                i++;
                continue;
            }

            int j = i;
            while (j < len && char.IsAsciiDigit(src[j]))
            {
                j++;
            }

            ReadOnlySpan<char> run = src.Slice(i, j - i);
            bool afterPoint = i > 0 && src[i - 1] == '.';
            if (dateLike && sec.DbNum != 3 && !afterPoint && run.Length <= 2 && !(run.Length == 2 && run[0] == '0'))
            {
                PlaceValue(ref w, run, sec.DbNum, c);
            }
            else
            {
                foreach (char ch in run)
                {
                    w.Append(Digit(ch - '0', sec.DbNum, c));
                }
            }

            i = j;
        }
    }

    private static char Digit(int v, int style, NumberFormatCulture c)
    {
        if (style == 3)
        {
            return (char)('０' + v);
        }

        if (style == 2)
        {
            if (c.Lcid == 0x0411)
            {
                return "〇壱弐参四伍六七八九"[v];
            }

            if (c.Lcid == 0x0412 && v == 0)
            {
                // Excel writes the Korean zero with the compatibility ideograph U+F9B2.
                return '\uF9B2';
            }

            if (c.Lcid == 0x0412 && v == 3)
            {
                // Observed in Excel: the Korean three is the compatibility ideograph U+F96B.
                return '\uF96B';
            }

            if (c.Lcid == 0x0412 && v == 4)
            {
                // Observed in Excel: the Korean four is the plain 四.
                return '四';
            }

            return (c.Lcid == 0x0804 ? "零壹贰叁肆伍陆柒捌玖" : "零壹貳參肆伍陸柒捌玖")[v];
        }

        if (v == 0)
        {
            return c.Lcid switch { 0x0411 => '〇', 0x0412 => '０', _ => '○' };
        }

        return "〇一二三四五六七八九"[v];
    }

    // One or two digits without a leading zero as a place-value numeral: 10 is 十 (DBNum1) or 壹拾 (DBNum2), 25 is 二十五 or 贰拾伍.
    private static void PlaceValue(ref Writer w, scoped ReadOnlySpan<char> digits, int style, NumberFormatCulture c)
    {
        int v = 0;
        foreach (char ch in digits)
        {
            v = (v * 10) + (ch - '0');
        }

        if (v < 10)
        {
            w.Append(Digit(v, style, c));
            return;
        }

        int tens = v / 10;
        int ones = v % 10;
        if (style == 2)
        {
            w.Append(Digit(tens, 2, c));
            w.Append('拾');
        }
        else
        {
            if (tens > 1)
            {
                w.Append(Digit(tens, 1, c));
            }

            w.Append('十');
        }

        if (ones > 0)
        {
            w.Append(Digit(ones, style, c));
        }
    }
}
