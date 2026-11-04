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
                    NumberWriter.AppendPadded(ref w, e.N == 2 ? t.Year % 100 : t.Year, e.N);
                    break;
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
                    NumberWriter.AppendPadded(ref w, e.N == 2 ? (t.Year + 543) % 100 : t.Year + 543, e.N);
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
        string full = am ? c.AmDesignator : c.PmDesignator;
        switch (style)
        {
            case 0:
            case 4:
                w.Append(full);
                break;
            case 1:
                foreach (char ch in full)
                {
                    w.Append(char.ToLowerInvariant(ch));
                }

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

        return "AD";
    }

    private static bool OnOrAfter(ExcelDateTime t, int y, int m, int d) =>
        t.Year != y ? t.Year > y : t.Month != m ? t.Month > m : t.Day >= d;
}

// [DBNum1], [DBNum2] and [DBNum3]: the digits of the displayed number written as CJK numerals.
// Whole numbers become positional numerals (一百二十三); digits after a decimal point, and every digit of a date or time, are written one by one.
internal static class DbNumbers
{
    internal const int MaxChars = 256;

    internal static void Apply(scoped ReadOnlySpan<char> src, ref Writer w, NumberFormatSection sec, NumberFormatCulture culture, FormatOptions o)
    {
        int len = src.Length;
        NumberFormatCulture c = sec.LocaleId != 0 ? NumberFormatCulture.Get(sec.LocaleId) : (o.Culture ?? culture);
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

            bool afterPoint = i > 0 && src[i - 1] == '.';
            ReadOnlySpan<char> run = src.Slice(i, j - i);
            if ((!dateLike || run.Length <= 2) && !afterPoint && !(sec.DbNum == 3 && c.Lcid is 0x0411 or 0x0412) && run.Length <= 16 && !(run.Length > 1 && run[0] == '0'))
            {
                Positional(ref w, run, sec.DbNum, c);
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

    private static string Set(int style, NumberFormatCulture c, out string units, out string big)
    {
        bool tw = c.Lcid == 0x0404;
        bool jp = c.Lcid == 0x0411;
        big = tw || jp ? "萬億兆" : "万亿兆";
        if (jp)
        {
            big = "万億兆";
        }

        switch (style)
        {
            case 2:
                units = jp ? "拾百千" : "拾佰仟";
                return jp ? "零壱弐参四五六七八九" : tw ? "零壹貳參肆伍陸柒捌玖" : "零壹贰叁肆伍陆柒捌玖";
            case 3:
                units = "十百千";
                return "0123456789";
            default:
                units = "十百千";
                return "〇一二三四五六七八九";
        }
    }

    private static char Digit(int v, int style, NumberFormatCulture c)
    {
        if (style == 3)
        {
            // Full-width digits for Japanese and Korean; the Chinese locales keep Arabic digits.
            return c.Lcid is 0x0411 or 0x0412 ? (char)('０' + v) : (char)('0' + v);
        }

        return Set(style, c, out _, out _)[v];
    }

    private static void Positional(ref Writer w, scoped ReadOnlySpan<char> digits, int style, NumberFormatCulture c)
    {
        string set = Set(style, c, out string units, out string big);
        string zero = "零"; // inside a numeral; a lone zero is written with the digit set
        int len = digits.Length;
        bool allZero = true;
        foreach (char ch in digits)
        {
            allZero &= ch == '0';
        }

        if (allZero)
        {
            w.Append(Digit(0, style, c));
            return;
        }

        bool pendingZero = false;
        bool started = false;
        for (int pos = len - 1; pos >= 0; pos--)
        {
            int idx = len - 1 - pos;
            int d = digits[idx] - '0';
            int inGroup = pos % 4;
            int group = pos / 4;
            if (d == 0)
            {
                pendingZero = started;
            }
            else
            {
                if (pendingZero)
                {
                    w.Append(style == 3 ? '0' : zero[0]);
                    pendingZero = false;
                }

                if (style == 3)
                {
                    w.Append(Digit(d, 3, c));
                }
                else
                {
                    w.Append(set[d]);
                }

                if (inGroup > 0)
                {
                    w.Append(units[inGroup - 1]);
                }

                started = true;
            }

            if (inGroup == 0 && group > 0 && AnyNonZero(digits, idx - 3, idx))
            {
                w.Append(big[group - 1]);
                pendingZero = false;
            }
        }
    }

    private static bool AnyNonZero(ReadOnlySpan<char> digits, int from, int to)
    {
        for (int i = Math.Max(0, from); i <= to; i++)
        {
            if (digits[i] != '0')
            {
                return true;
            }
        }

        return false;
    }
}
